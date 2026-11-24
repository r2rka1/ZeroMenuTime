using System.Reflection;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// The game keeps rain and snowfall out of a place only where the map has a roof it knows of.
    /// The traders' rooms and the hideout lie under the map with open ground above them, so the
    /// drops fell straight in. While the player is inside one, the game's rain is told there is
    /// none: no drops, no splashes, no water on the screen, no sound of it. It carries on as the
    /// weather has it on stepping out.
    /// </summary>
    public static class IndoorRain
    {
        /// <summary>For the test harness: let it rain indoors, to compare.</summary>
        public static bool LetRain;

        public static bool Hold => IndoorLight.Inside && !LetRain;
    }

    /// <summary>The falling rain or snow, the splashes and the water lying about.</summary>
    public class IndoorRainPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(RainController).GetMethod("ControlRain", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        public static void Prefix(ref float rain, ref float laingWater)
        {
            if (IndoorRain.Hold)
            {
                rain = 0f;
                laingWater = 0f;
            }
        }
    }

    /// <summary>How wet the surfaces look.</summary>
    public class IndoorWettingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(RainController).GetMethod("SetWetting", new[] { typeof(float), typeof(float), typeof(Vector2), typeof(float) });
        }

        [PatchPrefix]
        public static void Prefix(ref float wetting)
        {
            if (IndoorRain.Hold)
            {
                wetting = 0f;
            }
        }
    }

    /// <summary>
    /// Whether the camera stands under open sky. The game asks this for the sound of rain and the
    /// drops on the screen, and weather mods ask it for their own rain sound, thunder and lightning.
    /// In a trader's room or the hideout the answer is no: the player is under a roof.
    /// </summary>
    public class IndoorShelterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(DepthPhotograper).GetMethod("CheckIfCameraUnderRain", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        public static void Postfix(ref bool __result)
        {
            if (IndoorRain.Hold)
            {
                __result = false;
            }
        }
    }
}
