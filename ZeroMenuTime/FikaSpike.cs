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

        // ---- Spike S1: never truly die ----

        /// <summary>When on, a kill of the local player is stopped and they become a ghost.</summary>
        public static bool GhostOnDeath;
        public static bool Ghost;
        public static UnityEngine.Vector3 DiedAt;
        public static string DiedOf = "";
        private static UnityEngine.GameObject _limbo;

        public static void BecomeGhost(Player me, EDamageType cause)
        {
            if (Ghost)
            {
                return;
            }
            Ghost = true;
            DiedAt = me.Transform.position;
            DiedOf = cause.ToString();
            var health = me.ActiveHealthController;
            health.RestoreFullHealth();
            health.SetDamageCoeff(0f);
            // Somewhere nobody is: a slab far under the map. Others see the player gone; bots lose them.
            var at = new UnityEngine.Vector3(DiedAt.x, -450f, DiedAt.z);
            if (_limbo == null)
            {
                _limbo = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
                _limbo.name = "ZeroMenuTime limbo";
                _limbo.layer = UnityEngine.LayerMask.NameToLayer("HighPolyCollider");
                _limbo.transform.localScale = new UnityEngine.Vector3(8f, 0.5f, 8f);
            }
            _limbo.transform.position = at;
            me.Teleport(at + UnityEngine.Vector3.up * 0.4f, false);
            Plugin.Log.LogWarning("Ghost spike: kill by " + DiedOf + " stopped at " + DiedAt.ToString("F1") + "; now a ghost in limbo, alive " + health.IsAlive);
        }

        public static string Revive(UnityEngine.Vector3 at)
        {
            var me = GamePlayerOwner.MyPlayer;
            if (me == null || !Ghost)
            {
                return "not a ghost";
            }
            me.Teleport(at, false);
            me.ActiveHealthController.SetDamageCoeff(Plugin.Undying != null && Plugin.Undying.Value ? 0f : 1f);
            Ghost = false;
            return "revived at " + at.ToString("F1") + ", alive " + me.ActiveHealthController.IsAlive;
        }

        /// <summary>Every human this machine has in the raid: name, alive, where.</summary>
        public static System.Collections.Generic.List<string> Humans()
        {
            var lines = new System.Collections.Generic.List<string>();
            var world = Comfort.Common.Singleton<GameWorld>.Instance;
            if (world == null)
            {
                return lines;
            }
            foreach (var p in world.AllPlayersEverExisted)
            {
                if (p == null || p.IsAI)
                {
                    continue;
                }
                lines.Add("  " + p.Profile.Nickname + (p.IsYourPlayer ? " (me)" : "") + " " + p.GetType().Name + " alive " + (p.HealthController != null && p.HealthController.IsAlive)
                          + " at " + p.Transform.position.ToString("F1") + " shown " + (p.gameObject.activeInHierarchy));
            }
            return lines;
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

    /// <summary>SPIKE S1: a kill of the local player is stopped; they become a ghost instead.</summary>
    public class GhostKillPatch : SPT.Reflection.Patching.ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.HealthSystem.ActiveHealthController), "Kill");
        }

        [SPT.Reflection.Patching.PatchPrefix]
        public static bool Prefix(EFT.HealthSystem.ActiveHealthController __instance, EDamageType damageType)
        {
            var me = GamePlayerOwner.MyPlayer;
            if (!FikaSpike.GhostOnDeath || me == null || !ReferenceEquals(me.ActiveHealthController, __instance))
            {
                return true;
            }
            FikaSpike.BecomeGhost(me, damageType);
            return false;
        }
    }
}
