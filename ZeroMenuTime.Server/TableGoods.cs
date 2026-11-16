using System.Collections.Concurrent;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Commerce;

namespace ZeroMenuTime.Server;

/// <summary>Items bought in a raid, to be left with the trader for the player.</summary>
public sealed class PurchaseMailRequest : IRequestData
{
    public string TraderId { get; set; } = "";
    /// <summary>The bought items and everything in or on them; the top items have no parent.</summary>
    public List<Item> Items { get; set; } = [];
}

public sealed class TableGoodsResponse
{
    public bool Ok { get; set; }
    public string TraderId { get; set; } = "";
    /// <summary>How many items were left with the trader.</summary>
    public int Count { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Things a trader hands over in a raid go into that trader's mail, so the client can lay them on
/// the table in the trader's room, where they wait until picked up: what the player bought, and the
/// items a quest gives when it is started.
/// </summary>
public static class TableGoods
{
    /// <summary>Quests whose starting items were already handed over, per player, since the server started.</summary>
    private static readonly ConcurrentDictionary<string, HashSet<string>> Started = new();

    /// <summary>Kept with the trader for a week, like any other mail with items.</summary>
    private const long StorageSeconds = 7 * 24 * 3600;

    public static TableGoodsResponse Purchase(MongoId sessionId, PurchaseMailRequest request, ProfileHelper profiles, MailSendService mail)
    {
        var response = new TableGoodsResponse { TraderId = request.TraderId };
        try
        {
            var pmc = profiles.GetPmcProfile(sessionId);
            MongoId traderId = request.TraderId;
            if (pmc?.TradersInfo == null || !pmc.TradersInfo.ContainsKey(traderId))
            {
                response.Error = "Unknown trader or profile";
                return response;
            }
            if (request.Items == null || request.Items.Count == 0)
            {
                response.Error = "Nothing bought";
                return response;
            }
            // Counted first: the mail gives the top items a parent of its own.
            response.Count = request.Items.Count(x => x.ParentId == null);
            mail.SendDirectNpcMessageToPlayer(sessionId, request.TraderId, MessageType.MessageWithItems,
                "Here is what you bought. It is on the table.", request.Items, StorageSeconds);
            response.Ok = true;
            Console.WriteLine("[ZeroMenuTime] " + response.Count + " bought item(s) left on the table of trader " + request.TraderId);
        }
        catch (Exception e)
        {
            response.Error = e.GetType().Name + ": " + e.Message;
            Console.WriteLine("[ZeroMenuTime] Leaving bought items with the trader failed: " + e);
        }
        return response;
    }

    /// <summary>
    /// A quest started in a raid only changes its status in the game; the items it gives at the
    /// start are handed over here, through the trader's mail, as starting it from the menu would.
    /// </summary>
    public static TableGoodsResponse QuestStarted(MongoId sessionId, QuestRewardRequest request, ProfileHelper profiles,
        QuestHelper quests, QuestRewardHelper rewards, EventOutputHolder outputs, MailSendService mail)
    {
        var response = new TableGoodsResponse();
        try
        {
            var pmc = profiles.GetPmcProfile(sessionId);
            if (pmc == null || string.IsNullOrEmpty(request.QuestId))
            {
                response.Error = "No profile or no quest";
                return response;
            }
            MongoId questId = request.QuestId;
            var quest = quests.GetQuestFromDb(questId, pmc);
            if (quest == null)
            {
                response.Error = "Unknown quest";
                return response;
            }
            response.TraderId = quest.TraderId.ToString();

            // Started from the menu before, the items were given then.
            var known = pmc.Quests?.FirstOrDefault(x => x.QId == questId);
            if (known != null && known.Status != QuestStatusEnum.AvailableForStart && known.Status != QuestStatusEnum.Locked)
            {
                response.Ok = true;
                return response;
            }
            var handed = Started.GetOrAdd(sessionId.ToString(), _ => []);
            lock (handed)
            {
                if (!handed.Add(request.QuestId))
                {
                    response.Ok = true;
                    return response;
                }
            }

            var output = outputs.GetOutput(sessionId);
            var items = rewards.ApplyQuestReward(pmc, questId, QuestStatusEnum.Started, sessionId, output).ToList();
            var ids = new HashSet<string>(items.Select(x => x.Id.ToString()));
            response.Count = items.Count(x => x.ParentId == null || !ids.Contains(x.ParentId));
            if (items.Count > 0)
            {
                string text = quests.GetMessageIdForQuestStart(quest.StartedMessageText, quest.Description);
                mail.SendLocalisedNpcMessageToPlayer(sessionId, quest.TraderId, MessageType.QuestStart, text, items, StorageSeconds);
            }
            response.Ok = true;
            Console.WriteLine("[ZeroMenuTime] Quest " + request.QuestId + " started in a raid; " + response.Count + " starting item(s) left with its trader.");
        }
        catch (Exception e)
        {
            response.Error = e.GetType().Name + ": " + e.Message;
            Console.WriteLine("[ZeroMenuTime] Handing over a quest's starting items failed: " + e);
        }
        return response;
    }
}
