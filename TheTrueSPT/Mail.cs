using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using JsonType;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace TheTrueSPT
{
    /// <summary>
    /// Picking up what a trader is holding for the player (quest rewards, insurance returns and other
    /// mail attachments) while standing at the trader. Items go into carried gear. Anything that does
    /// not fit stays with the trader for next time.
    /// </summary>
    public static class Mail
    {
        private static bool _busy;

        public static async void Collect(string traderId, string traderName)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                if (player == null)
                {
                    return;
                }

                string request = JsonConvert.SerializeObject(new { TraderId = "" });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/thetruespt/mail/list", request));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Mail list failed: " + reply["Error"]);
                    NotificationManager.DisplayWarningNotification("Could not check deliveries.");
                    return;
                }

                var parcels = reply["Parcels"] as JArray ?? new JArray();
                if (parcels.Count == 0)
                {
                    NotificationManager.DisplayMessageNotification("No deliveries are waiting for you.");
                    return;
                }

                // With rooms, deliveries are laid on the trader's table and picked up by hand.
                var room = TraderRoom.For(traderId);
                if (room != null)
                {
                    int laid = await RewardTable.LayOut(room, parcels);
                    Plugin.Log.LogInfo("Mail from " + traderName + ": laid " + laid + " on the table, " + RewardTable.Count + " lying there in all");
                    NotificationManager.DisplayMessageNotification(
                        laid > 0 ? laid + " item(s) for you are on the table." : "Everything for you is already on the table.",
                        ENotificationDurationType.Long);
                    return;
                }

                var factory = Singleton<ItemFactory>.Instance;
                var taken = new List<string>();
                int leftBehind = 0;
                foreach (var parcel in parcels)
                {
                    string rootId = (string)parcel["RootId"];
                    try
                    {
                        var flat = parcel["Items"].ToString().ParseJsonTo<FlatItem[]>();
                        var tree = factory.FlatItemsToTree(flat, true);
                        Item root;
                        if (tree.Items == null || !tree.Items.TryGetValue(rootId, out root) || root == null)
                        {
                            Plugin.Log.LogWarning("Mail item " + rootId + " could not be built");
                            leftBehind++;
                            continue;
                        }
                        if (await LocalTrade.Give(player, root))
                        {
                            taken.Add(rootId);
                        }
                        else
                        {
                            leftBehind++;
                        }
                    }
                    catch (Exception e)
                    {
                        leftBehind++;
                        Plugin.Log.LogError("Mail item " + rootId + " failed: " + e);
                    }
                }

                if (taken.Count > 0)
                {
                    string confirm = JsonConvert.SerializeObject(new { TraderId = "", RootIds = taken });
                    var confirmed = JObject.Parse(await RequestHandler.PostJsonAsync("/thetruespt/mail/taken", confirm));
                    if (!((bool?)confirmed["Ok"] ?? false))
                    {
                        Plugin.Log.LogWarning("Mail confirm failed: " + confirmed["Error"]);
                    }
                }

                Plugin.Log.LogInfo("Mail from " + traderName + ": took " + taken.Count + ", left " + leftBehind);
                NotificationManager.DisplayMessageNotification(
                    "Collected " + taken.Count + " delivery item(s)"
                    + (leftBehind > 0 ? ". No room for " + leftBehind + " more: they are kept for you, free up space and collect again." : "."),
                    ENotificationDurationType.Long);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Collecting mail failed: " + e);
                NotificationManager.DisplayWarningNotification("Could not collect deliveries, see log.");
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
