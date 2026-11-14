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
            __result = Notify(__result, quest != null ? quest.Id : null);
        }

        private static async Task<OperationResult<ConditionalFinishResult<Quest>>> Notify(
            Task<OperationResult<ConditionalFinishResult<Quest>>> original, string questId)
        {
            var result = await original;
            try
            {
                if (!result.Failed && !string.IsNullOrEmpty(questId) && await HandOverItems(questId))
                {
                    return result;
                }
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

        /// <summary>
        /// Asks the server for the quest's reward items now and lays them on the trader's table.
        /// Experience and standing are still paid when the raid ends. False if there is no room to
        /// lay them out in, or the server could not do it; the old notice is shown then.
        /// </summary>
        private static async Task<bool> HandOverItems(string questId)
        {
            try
            {
                string body = Newtonsoft.Json.JsonConvert.SerializeObject(new { QuestId = questId });
                var reply = Newtonsoft.Json.Linq.JObject.Parse(
                    await SPT.Common.Http.RequestHandler.PostJsonAsync("/tradersinraid/quest/rewards", body));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Reward items for quest " + questId + " not handed over: " + reply["Error"]);
                    return false;
                }
                string traderId = (string)reply["TraderId"];
                Plugin.Log.LogInfo("Quest " + questId + " handed in; reward items are with " + TraderConfig.NameOf(traderId));
                if (TraderRoom.For(traderId) == null)
                {
                    NotificationManager.DisplayMessageNotification(
                        "Quest handed in. Collect the reward items from the trader; experience and standing follow when you leave.",
                        ENotificationDurationType.Long);
                    return true;
                }
                NotificationManager.DisplayMessageNotification(
                    "Quest handed in. Experience and standing follow when you leave.", ENotificationDurationType.Long);
                Mail.Collect(traderId, TraderConfig.NameOf(traderId));
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Handing over reward items failed: " + e.Message);
                return false;
            }
        }
    }
}
