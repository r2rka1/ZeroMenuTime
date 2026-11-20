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
    /// SPIKE, not for release: what stage 0 of plans/fika tried and later stages will build properly
    /// (the dead state of stage 5, the late-join switch of stage 6), kept behind the test harness.
    /// </summary>
    public static class FikaSpike
    {
        public static bool Present => Chainloader.PluginInfos.ContainsKey("com.fika.core");

        private static Type Utils => AccessTools.TypeByName("Fika.Core.Main.Utils.FikaBackendUtils");

        public static object Flag(string name)
        {
            var p = Utils != null ? Utils.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
            return p != null ? p.GetValue(null) : null;
        }

        /// <summary>SPIKE S2: the host lets new players in after the raid has started.</summary>
        public static bool LateJoin;

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
