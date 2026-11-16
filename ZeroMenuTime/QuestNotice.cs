using System;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ZeroMenuTime
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
                    // The game itself has already put the reward items into the player's gear. They
                    // are laid on the trader's table instead, so the game's own copies go.
                    await TakeBackGameRewards(result.Value.Rewards);
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

        private static async Task TakeBackGameRewards(System.Collections.Generic.IEnumerable<MoveResult> rewards)
        {
            var player = EFT.GamePlayerOwner.MyPlayer;
            if (player == null || rewards == null)
            {
                return;
            }
            int taken = 0, failed = 0;
            foreach (var reward in rewards)
            {
                var item = reward != null ? reward.Item : null;
                // Only what really ended up in the player's gear; a reward with nowhere to go was never given.
                if (item == null || item.Parent == null || item.Parent.Container == null)
                {
                    continue;
                }
                if (await LocalTrade.Take(player, item, item.StackObjectsCount))
                {
                    taken++;
                }
                else
                {
                    failed++;
                }
            }
            if (taken + failed > 0)
            {
                Plugin.Log.LogInfo("Quest reward items the game put in the player's gear taken back: " + taken + (failed > 0 ? ", could not take " + failed : ""));
            }
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
                    await SPT.Common.Http.RequestHandler.PostJsonAsync("/zeromenutime/quest/rewards", body));
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

    /// <summary>
    /// Starting a quest in a raid only changes its status in the game; the items some quests give
    /// at the start never came. They are asked for from the server, which leaves them with the
    /// quest's trader, and laid on that trader's table.
    /// </summary>
    public class QuestStartItemsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(QuestControllerClientLocalGame), nameof(QuestControllerClientLocalGame.AcceptQuest));
        }

        [PatchPostfix]
        public static void Postfix(Quest quest, ref Task<IResult> __result)
        {
            __result = After(__result, quest != null ? quest.Id : null);
        }

        private static async Task<IResult> After(Task<IResult> original, string questId)
        {
            var result = await original;
            try
            {
                if (result != null && !result.Failed && !string.IsNullOrEmpty(questId))
                {
                    string body = Newtonsoft.Json.JsonConvert.SerializeObject(new { QuestId = questId });
                    var reply = Newtonsoft.Json.Linq.JObject.Parse(
                        await SPT.Common.Http.RequestHandler.PostJsonAsync("/zeromenutime/quest/started", body));
                    int count = (int?)reply["Count"] ?? 0;
                    string traderId = (string)reply["TraderId"];
                    if (!((bool?)reply["Ok"] ?? false))
                    {
                        Plugin.Log.LogWarning("Starting items for quest " + questId + " not handed over: " + reply["Error"]);
                    }
                    else if (count > 0)
                    {
                        Plugin.Log.LogInfo("Quest " + questId + " started; " + count + " starting item(s) are with " + TraderConfig.NameOf(traderId));
                        if (TraderRoom.For(traderId) != null)
                        {
                            Mail.Collect(traderId, TraderConfig.NameOf(traderId));
                        }
                        else
                        {
                            NotificationManager.DisplayMessageNotification("Quest started. Collect its items from " + TraderConfig.NameOf(traderId) + ".",
                                ENotificationDurationType.Long);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Quest starting items failed: " + e.Message);
            }
            return result;
        }
    }
}
