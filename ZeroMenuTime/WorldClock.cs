using System;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// The character's world clock: one count of game seconds that carries from raid to raid. It runs
    /// at the raid's own pace while a raid runs, jumps when the character sleeps, and stands still
    /// while the game is closed. The raid's time of day is set from it, and crafts in every hideout
    /// are timed against it. The server keeps the count between raids.
    /// </summary>
    public static class WorldClock
    {
        /// <summary>What the clock reads at zero: six in the morning of a summer day.</summary>
        private static readonly DateTime Epoch = new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc);

        private static bool _running;
        private static float _nextSend;
        private static double _lastKnown;

        // The weather the server sends is laid out along the raid's own calendar, which this clock
        // replaces. What has to be added to this clock to read that calendar again:
        private static GameDateTime _shiftFor;
        private static long _weatherShift;
        private static bool _hasShift;
        /// <summary>For the test harness: read the weather by this clock's own calendar, as versions before 2.1.2 did.</summary>
        public static bool WeatherByOwnCalendar;

        /// <summary>The moment the weather is to be read at, or null to leave it to the game.</summary>
        public static long? WeatherTicks
        {
            get
            {
                var game = Game();
                if (!_hasShift || WeatherByOwnCalendar || game == null || !ReferenceEquals(_shiftFor, game))
                {
                    return null;
                }
                return game.Calculate().Ticks + _weatherShift;
            }
        }

        public static string DescribeWeatherShift()
        {
            return _hasShift ? (_weatherShift / (double)TimeSpan.TicksPerDay).ToString("F2") + " days" : "none";
        }

        private static GameDateTime Game()
        {
            return Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.GameDateTime : null;
        }

        /// <summary>The clock now, in game seconds.</summary>
        public static double Now
        {
            get
            {
                var game = Game();
                if (_running && game != null)
                {
                    _lastKnown = (game.Calculate() - Epoch).TotalSeconds;
                }
                return _lastKnown;
            }
        }

        /// <summary>"Day 3, 14:05"</summary>
        public static string Describe(double seconds)
        {
            var at = Epoch.AddSeconds(seconds);
            return "day " + ((int)(at.Date - Epoch.Date).TotalDays + 1) + ", " + at.ToString("HH:mm");
        }

        /// <summary>"1:25" for a span of game seconds.</summary>
        public static string Span(double seconds)
        {
            seconds = Math.Max(0, seconds);
            return (int)(seconds / 3600) + ":" + ((int)(seconds % 3600 / 60)).ToString("00");
        }

        private static void Set(double seconds)
        {
            var game = Game();
            if (game == null)
            {
                return;
            }
            if (Net.IsHost && !ReferenceEquals(_shiftFor, game))
            {
                // First setting in this raid: the game still has the raid's own date. Kept, so the
                // weather goes on being read where the server meant it, and moves on with sleep.
                _shiftFor = game;
                _weatherShift = game.Calculate().Ticks - Epoch.AddSeconds(seconds).Ticks;
                _hasShift = true;
                Plugin.Log.LogInfo("World clock: the weather is read " + DescribeWeatherShift() + " from this clock, on the raid's own calendar");
            }
            // Changed in place: the sky and the weather hold on to this same object.
            game._realtimeSinceStartup = Time.realtimeSinceStartup;
            game.StatedRealDateTime = DateTime.UtcNow;
            game.StatedGameDateTime = Epoch.AddSeconds(seconds);
            _lastKnown = seconds;
            // On a co-op client the sky keeps a clock object of its own (alone, and on the host, it
            // is this same one): without this the client's sky stays at the hour it joined with.
            try
            {
                var sky = TODSkyProvider.Instance;
                var skyClock = sky != null && sky.CurrentTime != null ? sky.CurrentTime.GameDateTime : null;
                if (skyClock != null && !ReferenceEquals(skyClock, game))
                {
                    skyClock._realtimeSinceStartup = game._realtimeSinceStartup;
                    skyClock.StatedRealDateTime = game.StatedRealDateTime;
                    skyClock.StatedGameDateTime = game.StatedGameDateTime;
                    skyClock.TimeFactor = game.TimeFactor;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("World clock: the sky's own clock was not set: " + e.Message);
            }
        }

        /// <summary>At raid start: carry on from where the clock stood when the last raid ended.</summary>
        public static async void Begin()
        {
            _running = false;
            if (!Net.IsHost)
            {
                // In someone else's raid the clock is theirs: ask the host what it reads.
                Net.Current.Send("zmt.clock?", new byte[0], false);
                return;
            }
            try
            {
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/hideout/list", "{}"));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("World clock not read: " + reply["Error"]);
                    return;
                }
                if (Net.Role == Role.Client)
                {
                    // The role was not yet known when this began; the host's word counts.
                    return;
                }
                double seconds = (double?)reply["Clock"] ?? 0;
                if (Game() == null)
                {
                    return;
                }
                Set(seconds);
                _running = true;
                _nextSend = Time.unscaledTime + 5f;
                Plugin.Log.LogInfo("World clock: " + Describe(seconds) + " (" + seconds.ToString("F0") + " game seconds)");
                Tell();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("World clock could not start: " + e);
            }
        }

        /// <summary>Moves the clock forward, as sleeping does. Only where the world is decided.</summary>
        public static void Add(double gameSeconds)
        {
            if (!_running || gameSeconds <= 0)
            {
                return;
            }
            if (!Net.IsHost)
            {
                Plugin.Log.LogWarning("World clock: only the host moves the clock; not moved");
                return;
            }
            Set(Now + gameSeconds);
            Send("moved on");
            Tell();
        }

        /// <summary>On the host: says the time to every other machine in the raid.</summary>
        public static void Tell()
        {
            if (_running && Net.Coop && Net.IsHost)
            {
                // The time, then what the weather's calendar is off by (older versions read only the time).
                var body = new byte[16];
                BitConverter.GetBytes(Now).CopyTo(body, 0);
                BitConverter.GetBytes(_hasShift && ReferenceEquals(_shiftFor, Game()) ? _weatherShift : long.MinValue).CopyTo(body, 8);
                Net.Current.Send("zmt.clock", body, true);
            }
        }

        /// <summary>On the host: a machine asks the time (it has just come into the raid).</summary>
        public static void OnAsked(byte[] body, string from)
        {
            Tell();
        }

        /// <summary>On a client: the host says the time.</summary>
        public static void OnTold(byte[] body, string from)
        {
            if (Net.IsHost || body == null || body.Length < 8 || Game() == null)
            {
                return;
            }
            double seconds = BitConverter.ToDouble(body, 0);
            bool first = !_running;
            double off = _running ? seconds - Now : 0;
            Set(seconds);
            if (body.Length >= 16)
            {
                long shift = BitConverter.ToInt64(body, 8);
                _hasShift = shift != long.MinValue;
                _weatherShift = _hasShift ? shift : 0;
                _shiftFor = Game();
            }
            _running = true;
            if (first || Math.Abs(off) > 120)
            {
                Plugin.Log.LogInfo("World clock from the host: " + Describe(seconds) + (first ? "" : " (was " + off.ToString("F0") + " s off)"));
                HideTimer();
            }
        }

        /// <summary>Called every frame; tells the server the time about once a minute.</summary>
        public static void Tick()
        {
            if (!_running || Time.unscaledTime < _nextSend)
            {
                return;
            }
            _nextSend = Time.unscaledTime + 60f;
            if (!Net.Headless)
            {
                HideTimer();
            }
            if (Game() == null)
            {
                _running = false;
                return;
            }
            if (!Net.IsHost)
            {
                // The host keeps the time and tells the server.
                return;
            }
            Send(null);
            Tell();
        }

        public static async void Send(string reason)
        {
            await SendAsync(reason);
        }

        public static async System.Threading.Tasks.Task SendAsync(string reason)
        {
            if (!_running || !Net.IsHost)
            {
                return;
            }
            double seconds = Now;
            try
            {
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/clock", JsonConvert.SerializeObject(new { Seconds = seconds })));
                // The answer says how the hideout stands at that time: fuel burnt, crafts and
                // bottles finished. Inside the hideout that is what the windows must show.
                if (HideoutDoors.Inside != null && ((bool?)reply["Ok"] ?? false))
                {
                    HideoutPower.Read(reply);
                    HideoutCrafting.Read(reply);
                }
                if (reason != null)
                {
                    Plugin.Log.LogInfo("World clock sent (" + reason + "): " + Describe(seconds) + ", server ok: " + ((bool?)reply["Ok"] ?? false));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("World clock not sent: " + e.Message);
            }
        }

        /// <summary>
        /// A raid no longer runs out, so the countdown in the corner means nothing; it would show the
        /// weeks left with the days cut off. Switched off wherever it has appeared.
        /// </summary>
        public static void HideTimer()
        {
            try
            {
                foreach (var panel in UnityEngine.Object.FindObjectsOfType<EFT.UI.BattleTimer.MainTimerPanel>())
                {
                    if (panel.gameObject.activeSelf)
                    {
                        panel.gameObject.SetActive(false);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hiding the raid timer failed: " + e.Message);
            }
        }

        /// <summary>The raid is ending: send the time once more and stop counting.</summary>
        public static void End()
        {
            if (_running)
            {
                Send("raid ending");
            }
            _running = false;
        }
    }

    /// <summary>
    /// The weather is a curve along the raid's calendar, read at the game's date and time. The world
    /// clock puts its own date there, months away from the one the server laid the weather along,
    /// so the weather was read at some other point of the curve: not what the server (or a weather
    /// mod) meant for now. Here it is read on the raid's own calendar again.
    /// </summary>
    public class WeatherCalendarPatch : SPT.Reflection.Patching.ModulePatch
    {
        protected override System.Reflection.MethodBase GetTargetMethod()
        {
            return HarmonyLib.AccessTools.PropertyGetter(typeof(EFT.Weather.WeatherCurve), "Int64_0");
        }

        [SPT.Reflection.Patching.PatchPostfix]
        public static void Postfix(ref long __result)
        {
            long? at = WorldClock.WeatherTicks;
            if (at.HasValue)
            {
                __result = at.Value;
            }
        }
    }
}
