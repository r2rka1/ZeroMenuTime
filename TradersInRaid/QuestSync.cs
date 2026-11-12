using System;
using System.Collections.Generic;
using EFT;
using EFT.Communications;
using EFT.Quests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.Reflection.Utils;

namespace TradersInRaid
{
    /// <summary>
    /// Quests accepted or handed in at a trader inside a raid do not go through the server call
    /// Vagabond listens to, so its quest extracts (the vehicle extracts and similar) stay off.
    /// This tells the server which quests are in progress so Vagabond can unlock them.
    /// </summary>
    public static class QuestSync
    {
        private static bool _busy;

        public static async void Send(string reason)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            try
            {
                var profile = GamePlayerOwner.MyPlayer?.Profile ?? ClientAppUtils.GetClientApp()?.Session?.Profile;
                if (profile?.QuestsData == null)
                {
                    return;
                }
                var active = new List<string>();
                var finished = new List<string>();
                foreach (var quest in profile.QuestsData)
                {
                    if (quest.Status == EQuestStatus.Started || quest.Status == EQuestStatus.AvailableForFinish)
                    {
                        active.Add(quest.Id);
                    }
                    else if (quest.Status == EQuestStatus.Success || quest.Status == EQuestStatus.Fail
                             || quest.Status == EQuestStatus.MarkedAsFailed || quest.Status == EQuestStatus.Expired)
                    {
                        finished.Add(quest.Id);
                    }
                }

                string body = JsonConvert.SerializeObject(new { Active = active, Finished = finished });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/tradersinraid/quests/sync", body));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogInfo("Quest sync (" + reason + ") not applied: " + reply["Error"]);
                    return;
                }
                int unlocked = (reply["Unlocked"] as JArray)?.Count ?? 0;
                int removed = (reply["Removed"] as JArray)?.Count ?? 0;
                Plugin.Log.LogInfo("Quest sync (" + reason + "): " + active.Count + " active, unlocked " + unlocked + ", removed " + removed);
                if (unlocked > 0)
                {
                    NotificationManager.DisplayMessageNotification(
                        "A quest extract is now unlocked. It becomes usable the next time this map loads.",
                        ENotificationDurationType.Long);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogInfo("Quest sync (" + reason + ") failed: " + e.Message);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
