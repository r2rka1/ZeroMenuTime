using System;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using SPT.Reflection.Utils;

namespace ZeroMenuTime
{
    /// <summary>
    /// SPIKE, not for release. Stage 0 of plans/fika: lets this mod's own raid start go through Fika,
    /// by reflection, so two test games can get into one raid. The real thing is the add-on of stage 1.
    /// </summary>
    public static class FikaSpike
    {
        public static bool Present => Chainloader.PluginInfos.ContainsKey("com.fika.core");

        /// <summary>"host", or the profile id of the host to join.</summary>
        public static string Mode = "host";

        private static Type Utils => AccessTools.TypeByName("Fika.Core.Main.Utils.FikaBackendUtils");

        public static object Flag(string name)
        {
            var p = Utils != null ? Utils.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
            return p != null ? p.GetValue(null) : null;
        }

        /// <summary>What Fika's host or join button does before the game's own start is pressed.</summary>
        public static async Task<bool> Prepare(RaidSettings settings)
        {
            var app = ClientAppUtils.GetMainApp();
            var profile = app.Session.Profile;
            Utils.GetProperty("Profile", BindingFlags.Public | BindingFlags.Static).SetValue(null, profile);
            if (Mode == "host")
            {
                var create = Utils.GetMethod("CreateMatch", BindingFlags.Public | BindingFlags.Static);
                await (Task)create.Invoke(null, new object[] { profile.ProfileId, profile.Nickname, settings });
                Plugin.Log.LogInfo("Fika spike: match created as host " + profile.ProfileId);
                return true;
            }
            var ui = AccessTools.TypeByName("Fika.Core.UI.Custom.MatchMakerUIScript");
            var join = ui.GetMethod("JoinMatch", BindingFlags.Public | BindingFlags.Static);
            bool ok = await (Task<bool>)join.Invoke(null, new object[] { profile.Id, Mode, null, false });
            Plugin.Log.LogInfo("Fika spike: joining " + Mode + ": " + ok);
            return ok;
        }

        /// <summary>The host's START RAID button in Fika's waiting room.</summary>
        public static string StartNow()
        {
            var gameType = AccessTools.TypeByName("Fika.Core.Main.GameMode.IFikaGame");
            var game = typeof(Comfort.Common.Singleton<>).MakeGenericType(gameType).GetProperty("Instance").GetValue(null);
            if (game == null)
            {
                return "no Fika game yet";
            }
            var controller = gameType.GetProperty("GameController").GetValue(game);
            var started = controller.GetType().GetProperty("RaidStarted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            bool was = (bool)started.GetValue(controller);
            started.SetValue(controller, true);
            return "raid started flag: " + was + " -> true on " + controller.GetType().Name;
        }

        public static async void Launch(RaidSettings settings, Func<Task> start)
        {
            try
            {
                if (Present && !await Prepare(settings))
                {
                    Plugin.Log.LogWarning("Fika spike: not started");
                    return;
                }
                await start();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Auto-start failed: " + e);
            }
        }
    }
}
