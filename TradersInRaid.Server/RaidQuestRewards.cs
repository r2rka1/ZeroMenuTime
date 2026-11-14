using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.InRaid;

namespace TradersInRaid.Server;

/// <summary>Switches on this mod's server patches when the server starts.</summary>
[Injectable(TypePriority = OnLoadOrder.Preload)]
public sealed class TradersInRaidLoader : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // One patch failing must not take the rest of the mod down with it.
        foreach (var patch in new AbstractPatch[] { new RaidQuestRewardsPatch(), new RaidStartLocationPatch() })
        {
            try
            {
                patch.Enable();
            }
            catch (Exception e)
            {
                Console.WriteLine("[TradersInRaid] Could not enable " + patch.GetType().Name + ": " + e.Message);
            }
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// A quest handed in at a trader inside a raid is only marked finished on the client. When the raid
/// ends the server accepts that status but pays nothing, except for Lightkeeper, whose quests it
/// runs through its normal "complete quest" step. This does the same for every other trader, so the
/// experience, standing, unlocks and reward mail arrive as they would from the menu.
/// </summary>
public sealed class RaidQuestRewardsPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod("LightkeeperQuestWorkaround",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    }

    private static T? Dependency<T>(object owner) where T : class
    {
        foreach (var field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (field.FieldType == typeof(T))
            {
                return field.GetValue(owner) as T;
            }
        }
        return null;
    }

    private static List<SPTarkov.Server.Core.Models.Eft.Profile.Message> Messages(
        SPTarkov.Server.Core.Helpers.Profile.ProfileHelper? profiles, MongoId sessionId, MongoId traderId)
    {
        var records = profiles?.GetFullProfile(sessionId)?.DialogueRecords;
        if (records != null && records.TryGetValue(traderId, out var dialogue) && dialogue?.Messages != null)
        {
            return dialogue.Messages;
        }
        return [];
    }

    private static HashSet<string> MessageIds(
        SPTarkov.Server.Core.Helpers.Profile.ProfileHelper? profiles, MongoId sessionId, MongoId traderId)
    {
        return new HashSet<string>(Messages(profiles, sessionId, traderId).Select(m => m.Id.ToString()));
    }

    private static void EmptyNewMail(
        SPTarkov.Server.Core.Helpers.Profile.ProfileHelper? profiles, MongoId sessionId, MongoId traderId, HashSet<string> before)
    {
        foreach (var message in Messages(profiles, sessionId, traderId))
        {
            if (before.Contains(message.Id.ToString()) || message.Items?.Data == null || message.Items.Data.Count == 0)
            {
                continue;
            }
            message.Items.Data.Clear();
            message.RewardCollected = true;
            Console.WriteLine("[TradersInRaid] Raid-end reward mail emptied: its items were already handed over in the raid.");
        }
    }

    [PatchPostfix]
    public static void Postfix(LocationLifecycleService __instance, MongoId sessionId,
        List<QuestStatus> postRaidQuests, List<QuestStatus> preRaidQuests, PmcData pmcProfile)
    {
        try
        {
            var questHelper = Dependency<QuestHelper>(__instance);
            var profileHelper = Dependency<SPTarkov.Server.Core.Helpers.Profile.ProfileHelper>(__instance);
            var templates = Dependency<TemplateTable>(__instance);
            if (questHelper == null || templates == null || postRaidQuests == null || preRaidQuests == null)
            {
                return;
            }

            foreach (var finished in postRaidQuests.Where(x => x.Status == QuestStatusEnum.Success).ToList())
            {
                bool wasUnfinished = preRaidQuests.Any(x => x.QId == finished.QId && x.Status != QuestStatusEnum.Success)
                                     || preRaidQuests.All(x => x.QId != finished.QId);
                if (!wasUnfinished)
                {
                    continue;
                }
                if (!templates.Quests.TryGetValue(finished.QId, out Quest? quest) || quest == null)
                {
                    continue;
                }
                // The server already handles Lightkeeper itself.
                if (quest.TraderId == Traders.LIGHTHOUSEKEEPER)
                {
                    continue;
                }
                // The items may already have been handed over on the trader's table during the raid.
                // Pay the rest now, and empty the reward mail this payout sends so they are not given twice.
                bool itemsGiven = TableRewards.WasDelivered(sessionId.ToString(), finished.QId.ToString());
                var mailBefore = itemsGiven ? MessageIds(profileHelper, sessionId, quest.TraderId) : null;
                questHelper.CompleteQuest(pmcProfile, new CompleteQuestRequestData
                {
                    Action = "CompleteQuest",
                    QuestId = finished.QId,
                    RemoveExcessItems = false,
                }, sessionId);
                if (mailBefore != null)
                {
                    EmptyNewMail(profileHelper, sessionId, quest.TraderId, mailBefore);
                }
            }
            TableRewards.Clear(sessionId.ToString());
        }
        catch (Exception e)
        {
            Console.WriteLine("[TradersInRaid] Paying out raid-finished quests failed: " + e);
        }
    }
}
