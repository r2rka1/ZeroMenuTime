using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BepInEx.Configuration;
using EFT;
using EFT.Bots;
using EFT.Communications;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Custom.Models;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// Removes the menus between raids. When a raid ends, the result screens are passed through
    /// automatically and the next raid starts by itself. If the raid ended at the hideout entrance,
    /// the hideout opens instead, and leaving the hideout starts a raid at its door.
    /// Loading screens remain; the clicking does not.
    /// </summary>
    public static class Seamless
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> Delay;
        public static ConfigEntry<KeyCode> StayKey;

        /// <summary>A raid just ended, or the hideout was just left: act when the menu next appears.</summary>
        public static bool Armed;
        /// <summary>The menu is appearing because the player walked out of the hideout.</summary>
        public static bool LeftHideout;
        /// <summary>Map of the raid that just ended; used when Vagabond cannot say where we are.</summary>
        public static string LastLocationId;

        /// <summary>The player chose "Save and go to menu" at a trader: skip the result screens, then stay in the menu.</summary>
        public static bool SaveAndMenuRequested;
        /// <summary>Pass through the result screens of the raid that just ended.</summary>
        public static bool SkipResults;

        /// <summary>The last raid ended in death: nothing continues automatically until the player starts a raid themself.</summary>
        public static bool Died;

        private static bool _running;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("No menus", "Enabled", true,
                "After a raid, skip the result screens and start the next raid (or open the hideout) automatically.");
            Delay = config.Bind("No menus", "Seconds before continuing", 4f,
                "How long the menu stays up before the next raid or the hideout loads.");
            StayKey = config.Bind("No menus", "Stay in menu key", KeyCode.Backspace,
                "Hold this while the countdown runs to stay in the main menu instead.");
        }

        public class VagabondState
        {
            public string CurrentMap;
            public bool HideoutAccessible;
        }

        public static async Task<VagabondState> FetchVagabondState()
        {
            try
            {
                string reply = await RequestHandler.PostJsonAsync("/vagabond/sync/state", "{\"InRaid\":false}");
                var json = JObject.Parse(reply);
                return new VagabondState
                {
                    CurrentMap = (string)json["CurrentMap"],
                    HideoutAccessible = (bool?)json["HideoutAccessible"] ?? false,
                };
            }
            catch (Exception e)
            {
                Plugin.Log.LogInfo("No Vagabond state (" + e.Message + "); using the last raid's map");
                return null;
            }
        }

        public static MainMenuShowOperation MenuOperation()
        {
            var app = ClientAppUtils.GetMainApp();
            return app == null ? null : AccessTools.Field(typeof(TarkovApplication), "_menuOperation").GetValue(app) as MainMenuShowOperation;
        }

        /// <summary>Continue, from the main menu: start a raid where the character is, at once.</summary>
        public static IEnumerator ContinueNow()
        {
            if (_running)
            {
                yield break;
            }
            _running = true;
            Armed = false;
            LeftHideout = false;
            var stateTask = FetchVagabondState();
            while (!stateTask.IsCompleted)
            {
                yield return null;
            }
            var state = stateTask.Status == TaskStatus.RanToCompletion ? stateTask.Result : null;
            Plugin.Log.LogInfo("Continue: map=" + (state?.CurrentMap ?? "?"));
            StartRaid(state?.CurrentMap ?? LastLocationId);
            _running = false;
        }

        /// <summary>Runs when the main menu appears after a raid or after leaving the hideout.</summary>
        public static IEnumerator AfterMenuShown()
        {
            if (_running)
            {
                yield break;
            }
            _running = true;
            Armed = false;
            bool leftHideout = LeftHideout;
            LeftHideout = false;

            NotificationManager.DisplayMessageNotification(
                "Continuing in " + Mathf.CeilToInt(Delay.Value) + " s. Hold " + StayKey.Value + " to stay in the menu.",
                ENotificationDurationType.Long);
            float remaining = Mathf.Max(1f, Delay.Value);
            while (remaining > 0f)
            {
                if (Input.GetKey(StayKey.Value))
                {
                    NotificationManager.DisplayMessageNotification("Staying in the menu.");
                    Plugin.Log.LogInfo("No-menu flow cancelled by player");
                    _running = false;
                    yield break;
                }
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            var stateTask = FetchVagabondState();
            while (!stateTask.IsCompleted)
            {
                yield return null;
            }
            var state = stateTask.Status == TaskStatus.RanToCompletion ? stateTask.Result : null;
            Plugin.Log.LogInfo("No-menu flow: map=" + (state?.CurrentMap ?? "?") + " hideoutAccessible=" + (state?.HideoutAccessible ?? false) + " leftHideout=" + leftHideout);

            if (!leftHideout && state != null && state.HideoutAccessible)
            {
                yield return OpenHideout();
            }
            else
            {
                StartRaid(state?.CurrentMap ?? LastLocationId);
            }
            _running = false;
        }

        private static IEnumerator OpenHideout()
        {
            // Vagabond refreshes its own "may enter hideout" flag a moment after the menu appears,
            // so the first attempt can be refused. Try a few times.
            for (int attempt = 1; attempt <= 6; attempt++)
            {
                var menu = MenuOperation();
                if (menu == null)
                {
                    break;
                }
                if (EftScreenManager.Instance.RootScreenType == EEftScreenType.Hideout)
                {
                    Plugin.Log.LogInfo("Hideout opened (attempt " + (attempt - 1) + ")");
                    yield break;
                }
                Plugin.Log.LogInfo("Opening hideout, attempt " + attempt);
                var open = menu.method_24(null);
                while (!open.IsCompleted)
                {
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(3f);
            }
            if (EftScreenManager.Instance.RootScreenType != EEftScreenType.Hideout)
            {
                Plugin.Log.LogWarning("Hideout did not open");
                NotificationManager.DisplayWarningNotification("Could not open the hideout automatically.");
            }
        }

        /// <summary>In co-op: the profile id of the host whose raid the next start joins. Null: host one's own.</summary>
        public static string JoinHost;

        /// <summary>This raid was joined as a guest: the player comes in beside the group, never at a bed.</summary>
        public static bool Joined;

        /// <summary>The co-op layer's part first (create or join the match), then the game's own start.</summary>
        private static async void Launch(RaidSettings settings, Func<Task> start)
        {
            try
            {
                string join = JoinHost;
                JoinHost = null;
                // Joining someone's raid in progress (not asking a headless for one's own).
                Joined = join != null && join != "headless";
                if (!await Net.Current.PrepareRaid(settings, join))
                {
                    Plugin.Log.LogWarning("The raid was not started: the co-op match could not be " + (join == null ? "created" : "joined"));
                    NotificationManager.DisplayWarningNotification("Could not " + (join == null ? "start" : "join") + " the co-op raid.", ENotificationDurationType.Long);
                    MenuBackdrop.MenuShownAgain();
                    return;
                }
                await start();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Auto-start failed: " + e);
            }
        }

        public static void StartRaid(string locationId)
        {
            if (GamePlayerOwner.MyPlayer != null)
            {
                // A raid is running. Starting another over it would take the hideout away from under the player.
                Plugin.Log.LogWarning("A raid is already running; not starting another.");
                return;
            }
            try
            {
                MenuBackdrop.Release();
                var app = ClientAppUtils.GetMainApp();
                var menu = MenuOperation();
                if (app == null || menu == null || string.IsNullOrEmpty(locationId))
                {
                    Plugin.Log.LogWarning("Cannot auto-start: app=" + (app != null) + " menu=" + (menu != null) + " location=" + locationId);
                    return;
                }
                // Vagabond reports the map by its database id, the game's own lookup uses the short
                // name (factory4_day); accept either, and fall back to the map of the raid that just ended.
                app.TryGetLocationById(locationId, out var location);
                foreach (var wanted in new[] { locationId, LastLocationId })
                {
                    if (string.IsNullOrEmpty(wanted))
                    {
                        continue;
                    }
                    foreach (var candidate in app.Session.LocationSettings.locations.Values)
                    {
                        if (string.Equals(candidate.Id, wanted, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(candidate._Id, wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            location = candidate;
                            break;
                        }
                    }
                    if (location != null)
                    {
                        break;
                    }
                }
                if (location == null)
                {
                    Plugin.Log.LogWarning("Cannot auto-start: unknown location '" + locationId + "'");
                    NotificationManager.DisplayWarningNotification("Could not start the raid automatically (unknown map).");
                    return;
                }

                // The same choices the side, map and settings screens would have written.
                foreach (var settings in new[] { menu.raidSettings_0, menu.raidSettings_1 })
                {
                    settings.Side = ESideType.Pmc;
                    settings.SelectedLocation = location;
                    settings.SelectedDateTime = JsonType.EDateTime.CURR;
                    settings.RaidMode = ERaidMode.Local;
                    settings.KeyId = null;
                    settings.transitionType |= ELocationTransition.Common;
                }

                // Bot and weather settings normally come from SPT's defaults on the settings screen.
                var offline = menu.raidSettings_1;
                try
                {
                    var defaults = Json.Deserialize<DefaultRaidSettings>(RequestHandler.GetJson("/singleplayer/settings/raid/menu"));
                    if (defaults != null)
                    {
                        offline.BotSettings.BotAmount = defaults.AiAmount;
                        offline.BotSettings.IsScavWars = false;
                        offline.WavesSettings.BotAmount = defaults.AiAmount;
                        offline.WavesSettings.BotDifficulty = defaults.AiDifficulty;
                        offline.WavesSettings.IsBosses = defaults.BossEnabled;
                        offline.WavesSettings.IsTaggedAndCursed = defaults.TaggedAndCursed;
                        offline.TimeAndWeatherSettings.IsRandomWeather = defaults.RandomWeather;
                        offline.TimeAndWeatherSettings.IsRandomTime = defaults.RandomTime;
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Raid defaults not loaded: " + e.Message);
                }

                Plugin.Log.LogInfo("Auto-starting raid on " + location.Id + " (asked for " + locationId + ")");
                Launch(offline, () => menu.method_27());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Auto-start failed: " + e);
            }
        }
    }

    /// <summary>Note how and where the raid ended.</summary>
    public class RaidStopPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(LocalGame), nameof(LocalGame.Stop));
        }

        [PatchPrefix]
        public static void Prefix(ExitStatus exitStatus, string exitName)
        {
            // Alone, this is the signal. With Fika its own game class may never come here; then the
            // co-op layer gives the signal, and whichever comes first counts.
            Net.RaiseRaidEnded(exitStatus, exitName);
        }

        /// <summary>The raid has ended for this machine. Subscribed to <see cref="Net.RaidEnded"/>.</summary>
        public static void OnRaidEnded(ExitStatus exitStatus, string exitName)
        {
            // The hideout interior must not outlive the raid it was loaded into.
            // A record the server cannot read would make it refuse the whole raid result.
            try
            {
                var leaving = GamePlayerOwner.MyPlayer;
                if (leaving != null)
                {
                    SaveGame.CleanDamageRecords(leaving.Profile);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cleaning damage records failed: " + e.Message);
            }
            WorldClock.End();
            HideoutDoors.Close();
            try
            {
                Seamless.LastLocationId = (Layout.Map != "" ? Layout.Map : GamePlayerOwner.MyPlayer?.Location) ?? Seamless.LastLocationId;
                // A transit already goes raid-to-raid by itself.
                bool toMenu = Seamless.SaveAndMenuRequested;
                Seamless.SaveAndMenuRequested = false;
                Seamless.SkipResults = Seamless.Enabled.Value && exitStatus != ExitStatus.Transit;
                // Dying (or going missing, or abandoning the raid) ends the run: stay in the menu.
                bool died = exitStatus == ExitStatus.Killed || exitStatus == ExitStatus.MissingInAction || exitStatus == ExitStatus.Left;
                Seamless.Died = died;
                // In co-op the next raid is the player's own choice: who hosts, who joins.
                Seamless.Armed = Seamless.SkipResults && !toMenu && !died && !Net.FikaInstalled;
                Death.Pending = died && Death.Ask.Value;
                Seamless.LeftHideout = false;
                Plugin.Log.LogInfo("Raid ended: " + exitStatus + " via '" + exitName + "' on " + Seamless.LastLocationId
                    + "; skip results: " + Seamless.SkipResults + ", continue automatically: " + Seamless.Armed);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("RaidStopPatch: " + e);
            }
        }
    }

    /// <summary>Pass through the post-raid result screens without clicks.</summary>
    public class SkipResultsPatch : ModulePatch
    {
        private static readonly ConditionalWeakTable<SessionResultShowOperation, object> Done =
            new ConditionalWeakTable<SessionResultShowOperation, object>();

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SessionResultShowOperation), nameof(SessionResultShowOperation.Init));
        }

        [PatchPostfix]
        public static void Postfix(SessionResultShowOperation __instance, ref Task __result)
        {
            if (!Seamless.Enabled.Value || !Seamless.SkipResults || Done.TryGetValue(__instance, out _))
            {
                return;
            }
            Done.Add(__instance, new object());
            __result = Skip(__instance, __result);
        }

        private static async Task Skip(SessionResultShowOperation operation, Task init)
        {
            await init;
            try
            {
                Plugin.Log.LogInfo("Skipping post-raid screens");
                await operation.GoToMainMenu();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Skipping post-raid screens failed: " + e);
            }
        }
    }

    /// <summary>When the main menu appears after a raid or the hideout, carry on automatically.</summary>
    public class MenuShownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MenuScreen), "Show",
                new[] { typeof(Profile), typeof(EFT.UI.Matchmaker.MatchmakerPlayersController), typeof(ESessionMode) });
        }

        [PatchPostfix]
        public static void Postfix()
        {
            QuestSync.Send("menu");
            if (Death.Pending && Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(Death.AskNow());
            }
            if (Seamless.Enabled.Value && Seamless.Armed && Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(Seamless.AfterMenuShown());
            }
        }
    }

    /// <summary>Leaving the hideout counts as walking out of its door: start a raid there.</summary>
    public class HideoutLeftPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.ShowMenuScreenSync));
        }

        [PatchPrefix]
        public static void Prefix()
        {
            try
            {
                if (Seamless.Enabled.Value && EftScreenManager.Instance.RootScreenType == EEftScreenType.Hideout)
                {
                    if (Seamless.Died)
                    {
                        Plugin.Log.LogInfo("Left the hideout after a death; staying in the menu");
                        return;
                    }
                    Seamless.Armed = true;
                    Seamless.LeftHideout = true;
                    Plugin.Log.LogInfo("Left the hideout; no-menu armed");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("HideoutLeftPatch: " + e);
            }
        }
    }
}
