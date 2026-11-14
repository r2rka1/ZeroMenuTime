using System.Collections.Concurrent;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace TradersInRaid.Server;

/// <summary>Asks for a quest's reward items to be handed over now, in the raid.</summary>
public sealed class QuestRewardRequest : IRequestData
{
    public string QuestId { get; set; } = "";
}

public sealed class QuestRewardResponse
{
    public bool Ok { get; set; }
    /// <summary>The trader the quest belongs to, so the client knows whose table to use.</summary>
    public string TraderId { get; set; } = "";
    public string? Error { get; set; }
}

/// <summary>
/// A quest handed in during a raid is paid in two parts.
///
/// The reward items are put in the trader's mail at once, so the client can lay them on the table
/// in the trader's room. Everything else (experience, standing, skills, unlocks) is still paid when
/// the raid ends, because the server replaces those values with the client's at that point and
/// anything added earlier would be lost.
///
/// This keeps track of which quests already had their items handed over, so the raid-end payout
/// does not send the same items a second time.
/// </summary>
public static class TableRewards
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> Delivered = new();

    public static bool WasDelivered(string sessionId, string questId)
    {
        return Delivered.TryGetValue(sessionId, out var quests) && quests.Contains(questId);
    }

    /// <summary>Forget everything for this player; called when a raid starts and when it ends.</summary>
    public static void Clear(string sessionId)
    {
        Delivered.TryRemove(sessionId, out _);
    }

    public static QuestRewardResponse Deliver(MongoId sessionId, QuestRewardRequest request,
        ProfileHelper profiles, QuestHelper quests, ICloner cloner)
    {
        var response = new QuestRewardResponse();
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

            var delivered = Delivered.GetOrAdd(sessionId.ToString(), _ => []);
            lock (delivered)
            {
                if (!delivered.Add(request.QuestId))
                {
                    // Asked twice for the same quest: the items are already in the mail.
                    response.Ok = true;
                    return response;
                }
            }

            // Run the normal payout against a throwaway copy of the character. The reward mail it
            // sends is real and goes to the player; the experience and standing land on the copy
            // and are discarded, to be paid properly when the raid ends.
            var copy = cloner.Clone(pmc);
            quests.CompleteQuest(copy!, new CompleteQuestRequestData
            {
                Action = "CompleteQuest",
                QuestId = questId,
                RemoveExcessItems = false,
            }, sessionId);
            Console.WriteLine("[TradersInRaid] Reward items for quest " + request.QuestId + " handed over in the raid.");
            response.Ok = true;
        }
        catch (Exception e)
        {
            response.Error = e.GetType().Name + ": " + e.Message;
            Console.WriteLine("[TradersInRaid] Handing over reward items failed: " + e);
        }
        return response;
    }
}
