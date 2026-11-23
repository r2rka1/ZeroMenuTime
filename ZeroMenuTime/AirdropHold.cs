using System.Reflection;
using EFT.Airdrop;
using SPT.Reflection.Patching;

namespace ZeroMenuTime
{
    /// <summary>
    /// While the raid stands still (the player is in the hideout) the game's clock for airdrops
    /// stands still too, so no plane is sent while nobody is up there to see it. The clock runs on
    /// from where it stopped when the raid does. A plane already in the air flies on.
    /// </summary>
    public class AirdropHoldPatch : ModulePatch
    {
        /// <summary>The map's airdrop clock, once it has ticked; for the test harness.</summary>
        public static ServerAirdropManager Seen;

        protected override MethodBase GetTargetMethod()
        {
            return typeof(ServerAirdropManager).GetMethod("ManualUpdate", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        public static bool Prefix(ServerAirdropManager __instance)
        {
            Seen = __instance;
            var clock = __instance._spawnAirdropTimer;
            if (clock == null || !clock.IsStarted)
            {
                return true;
            }
            if (BotFreeze.On)
            {
                if (!clock.IsPaused)
                {
                    clock.SetPause(true);
                    Plugin.Log.LogInfo("Airdrops: the clock is held at " + clock.Time.ToString("F0") + " s while the raid stands still");
                }
                return false;
            }
            if (clock.IsPaused)
            {
                clock.SetPause(false);
                Plugin.Log.LogInfo("Airdrops: the clock runs on from " + clock.Time.ToString("F0") + " s");
            }
            return true;
        }
    }
}
