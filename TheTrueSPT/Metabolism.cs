using System;
using System.Reflection;
using BepInEx.Configuration;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TheTrueSPT
{
    /// <summary>
    /// Hunger and thirst normally fall at one steady rate. This slows them down as the bar empties,
    /// so a full stomach burns at the usual speed and a nearly empty one ticks away slowly. It makes
    /// long stretches between traders survivable without making food pointless.
    /// </summary>
    public static class Metabolism
    {
        public static ConfigEntry<bool> Enabled;

        /// <summary>Upper bound of each band and the share of the normal drain inside it.</summary>
        private static readonly float[,] Bands =
        {
            { float.MaxValue, 1.00f }, // above 100: the usual rate
            { 100f, 0.80f },
            { 80f, 0.65f },
            { 50f, 0.50f },
            { 25f, 0.35f },           // the last quarter drains slowest
        };

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Metabolism", "Slow hunger and thirst as they empty", true,
                "Food and water fall at the normal rate while high and ever more slowly as they run low.");
        }

        /// <summary>How much of the normal drain applies at this level.</summary>
        public static float Factor(float current)
        {
            if (Enabled == null || !Enabled.Value)
            {
                return 1f;
            }
            // Walk down the bands: the first one the value falls under decides the rate.
            float factor = Bands[Bands.GetLength(0) - 1, 1];
            for (int i = 0; i < Bands.GetLength(0); i++)
            {
                if (current > Bands[i, 0])
                {
                    continue;
                }
                factor = Bands[i, 1];
            }
            return factor;
        }
    }

    /// <summary>Slows hunger as the energy bar empties.</summary>
    public class EnergyDrainPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController.Existence), "GetEnergyDamage");
        }

        [PatchPostfix]
        public static void Postfix(ActiveHealthController.Existence __instance, ref float __result)
        {
            try
            {
                __result *= Metabolism.Factor(__instance.HealthController.Energy.Current);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Energy drain not adjusted: " + e.Message);
            }
        }
    }

    /// <summary>Slows thirst as the hydration bar empties.</summary>
    public class HydrationDrainPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController.Existence), "GetHydrationDamage");
        }

        [PatchPostfix]
        public static void Postfix(ActiveHealthController.Existence __instance, ref float __result)
        {
            try
            {
                __result *= Metabolism.Factor(__instance.HealthController.Hydration.Current);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hydration drain not adjusted: " + e.Message);
            }
        }
    }
}
