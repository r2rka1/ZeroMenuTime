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
            // Changed in place: the sky and the weather hold on to this same object.
            game._realtimeSinceStartup = Time.realtimeSinceStartup;
            game.StatedRealDateTime = DateTime.UtcNow;
            game.StatedGameDateTime = Epoch.AddSeconds(seconds);
            _lastKnown = seconds;
        }

        /// <summary>At raid start: carry on from where the clock stood when the last raid ended.</summary>
        public static async void Begin()
        {
            _running = false;
            try
            {
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/hideout/list", "{}"));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("World clock not read: " + reply["Error"]);
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
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("World clock could not start: " + e);
            }
        }

        /// <summary>Moves the clock forward, as sleeping does.</summary>
        public static void Add(double gameSeconds)
        {
            if (!_running || gameSeconds <= 0)
            {
                return;
            }
            Set(Now + gameSeconds);
            Send("moved on");
        }

        /// <summary>Called every frame; tells the server the time about once a minute.</summary>
        public static void Tick()
        {
            if (!_running || Time.unscaledTime < _nextSend)
            {
                return;
            }
            _nextSend = Time.unscaledTime + 60f;
            HideTimer();
            if (Game() == null)
            {
                _running = false;
                return;
            }
            Send(null);
        }

        public static async void Send(string reason)
        {
            await SendAsync(reason);
        }

        public static async System.Threading.Tasks.Task SendAsync(string reason)
        {
            if (!_running)
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
}
