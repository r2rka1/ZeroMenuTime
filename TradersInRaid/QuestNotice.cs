using System;
using System.Reflection;
using System.Threading.Tasks;
using Diz.LanguageExtensions;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TradersInRaid
{
    /// <summary>
    /// A quest handed in inside a raid is only paid out when that raid ends. Say so at the moment of
    /// the hand-in, so the player knows when and how the reward can be collected.
    /// </summary>
    public class QuestHandInNoticePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(QuestControllerClientLocalGame), nameof(QuestControllerClientLocalGame.FinishQuest));
        }

        [PatchPostfix]
        public static void Postfix(Quest quest, ref Task<OperationResult<ConditionalFinishResult<Quest>>> __result)
        {
            __result = Notify(__result);
        }

        private static async Task<OperationResult<ConditionalFinishResult<Quest>>> Notify(
            Task<OperationResult<ConditionalFinishResult<Quest>>> original)
        {
            var result = await original;
            try
            {
                if (!result.Failed)
                {
                    NotificationManager.DisplayMessageNotification(
                        "Quest handed in. The reward can be collected from any trader after your next transit, save or hideout visit.",
                        ENotificationDurationType.Long);
                    Plugin.Log.LogInfo("Quest handed in during the raid; reward is paid when the raid ends");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Quest hand-in notice failed: " + e.Message);
            }
            return result;
        }
    }
}
