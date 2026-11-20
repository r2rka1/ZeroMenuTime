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
    }
}
