using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace ZeroMenuTime.Server;

public class ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.r2rka1.zeromenutime";
    public string Name { get; init; } = "ZeroMenuTime";
    public string Author { get; init; } = "r2rka1";
    public SemanticVersioning.Version Version { get; init; } = new("1.2.4");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public bool HasPrepatcher { get; init; }
    public string? Url { get; init; } = "https://github.com/r2rka1/ZeroMenuTime";
    public string License { get; init; } = "MIT";
    public List<string>? Contributors { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new() { { "dev.oogabooga.spt-vagabond", new SemanticVersioning.Range(">=0.8.3") } };
}

/// <summary>One kind of money that changed hands in a trade.</summary>
public sealed class MoneyAmount
{
    public string Tpl { get; set; } = "";
    public double Count { get; set; }
}

/// <summary>Sent by the client after a trade settled in a raid.</summary>
public sealed class TradeReport : IRequestData
{
    public string TraderId { get; set; } = "";
    public List<MoneyAmount> Money { get; set; } = [];
}

public sealed class TradeReportResponse
{
    public bool Ok { get; set; }
    public double SalesSum { get; set; }
    public int LoyaltyLevel { get; set; }
    public string? Error { get; set; }
}

public sealed class QuestSyncRequest : IRequestData
{
    /// <summary>Quests the player currently has in progress.</summary>
    public List<string> Active { get; set; } = [];
    /// <summary>Quests the player has finished or failed.</summary>
    public List<string> Finished { get; set; } = [];
}

public sealed class QuestSyncResponse
{
    public bool Ok { get; set; }
    public List<string> Unlocked { get; set; } = [];
    public List<string> Removed { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>Everything that touches Vagabond's assembly lives here, so a missing Vagabond only fails this call.</summary>
internal static class VagabondBridge
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Sync(string sessionId, QuestSyncRequest request, QuestSyncResponse response)
    {
        var state = Vagabond.Common.Api.Api.GetState(sessionId);
        if (state == null)
        {
            return;
        }
        bool changed = false;
        foreach (var questId in request.Active)
        {
            if (Vagabond.Common.Data.ExfilQuests.List.ContainsKey(questId) && state.QuestExfils.Add(questId))
            {
                response.Unlocked.Add(questId);
                changed = true;
            }
        }
        foreach (var questId in request.Finished)
        {
            if (state.QuestExfils.Remove(questId))
            {
                response.Removed.Add(questId);
                changed = true;
            }
        }
        if (changed)
        {
            Vagabond.Common.Api.Api.SaveState(sessionId, state);
        }
    }
}

/// <summary>Asks about, or reports taking, items a trader is holding in the player's mail.</summary>
public sealed class MailRequest : IRequestData
{
    public string TraderId { get; set; } = "";
    public List<string>? RootIds { get; set; }
}

public sealed class MailParcel
{
    public string RootId { get; set; } = "";
    public List<Item> Items { get; set; } = [];
}

public sealed class MailListResponse
{
    public bool Ok { get; set; }
    public List<MailParcel> Parcels { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>
/// In-raid trades are settled on the client, so the server never sees them. This route records the
/// money that changed hands against the trader, the same way a normal trade does, so trader loyalty
/// still grows.
/// </summary>
[Injectable]
public class ZeroMenuTimeRouter(
    JsonUtil jsonUtil,
    ProfileHelper profileHelper,
    TraderHelper traderHelper,
    HandbookHelper handbookHelper,
    SPTarkov.Server.Core.Helpers.Quest.QuestHelper questHelper,
    SPTarkov.Server.Core.Utils.Cloners.ICloner cloner,
    HideoutsService hideouts,
    SaveGameService saves,
    NewGameService newGame,
    SPTarkov.Server.Core.Services.Commerce.MailSendService mailSend,
    SPTarkov.Server.Core.Helpers.Quest.QuestRewardHelper questRewards,
    SPTarkov.Server.Core.Routers.EventOutputHolder outputs,
    SPTarkov.Server.Core.Services.Commerce.RepairService repairService,
    SPTarkov.Server.Core.Helpers.Ragfair.RepairHelper repairHelper,
    SPTarkov.Server.Core.Helpers.Profile.InventoryHelper inventoryHelper,
    SPTarkov.Server.Core.Generators.Loot.LootGenerator lootGenerator,
    SPTarkov.Server.Core.Models.Spt.Tables.TemplateTable templates) : StaticRouter(jsonUtil, [
    new RouteAction<UnpackRequest>(
        "/zeromenutime/unpack",
        (_, payload, _, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(Unpack.Open(payload, templates, inventoryHelper, lootGenerator)) ?? "{}")
    ),
    new RouteAction<KitRepairRequest>(
        "/zeromenutime/repair/kit",
        (_, payload, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(Repairs.ByKit(sessionId, payload, profileHelper, repairService, repairHelper, templates)) ?? "{}")
    ),
    new RouteAction<TradeReport>(
        "/zeromenutime/trade",
        (_, payload, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(Record(sessionId, payload, profileHelper, traderHelper, handbookHelper)) ?? "{}")
    ),
    new RouteAction<MailRequest>(
        "/zeromenutime/coop/who",
        (_, _, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(FikaLink.Describe(sessionId)) ?? "{}")
    ),
    new RouteAction<MailRequest>(
        "/zeromenutime/mail/list",
        (_, payload, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(ListMail(sessionId, payload, profileHelper)) ?? "{}")
    ),
    new RouteAction<MailRequest>(
        "/zeromenutime/mail/taken",
        (_, payload, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(MarkTaken(sessionId, payload, profileHelper)) ?? "{}")
    ),
    new RouteAction<QuestSyncRequest>(
        "/zeromenutime/quests/sync",
        (_, payload, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(SyncQuests(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<QuestRewardRequest>(
        "/zeromenutime/quest/rewards",
        (_, payload, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(TableRewards.Deliver(sessionId, payload, profileHelper, questHelper, cloner)) ?? "{}")
    ),
    new RouteAction<PurchaseMailRequest>(
        "/zeromenutime/mail/purchase",
        (_, payload, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(TableGoods.Purchase(sessionId, payload, profileHelper, mailSend)) ?? "{}")
    ),
    new RouteAction<QuestRewardRequest>(
        "/zeromenutime/quest/started",
        (_, payload, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(TableGoods.QuestStarted(sessionId, payload, profileHelper, questHelper, questRewards, outputs, mailSend)) ?? "{}")
    ),
    new RouteAction<TraderListRequest>(
        "/zeromenutime/vagabond/traders",
        (_, _, _, _, _) => ValueTask.FromResult(jsonUtil.Serialize(VagabondTraders.Read()) ?? "{}")
    ),
    new RouteAction<SaveRequest>(
        "/zeromenutime/death/mode",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(saves.SetDeathMode(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<NewGameRequest>(
        "/zeromenutime/newgame",
        async (_, payload, sessionId, _, _) => jsonUtil.Serialize(await newGame.Reset(sessionId, payload)) ?? "{}"
    ),
    new RouteAction<SaveRequest>(
        "/zeromenutime/save",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(saves.Save(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<SaveRequest>(
        "/zeromenutime/save/trader",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(saves.TraderSaveComing(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<SaveRequest>(
        "/zeromenutime/test/travel",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(saves.Travel(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<SaveRequest>(
        "/zeromenutime/save/status",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(saves.Status(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/clock",
        // The clock is the world's, and the world is its owner's: whoever hosts reports it for the owner.
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.SetClock(FikaLink.OwnerOf(sessionId), payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/hideout/recipes",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.Recipes(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/hideout/craft",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.Craft(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/hideout/collect",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.Collect(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/hideout/shelf",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.SetShelf(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/hideout/power",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.Power(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/hideout/list",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.List(FikaLink.OwnerOf(sessionId), payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/hideout/enter",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.Enter(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/hideout/build",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.Build(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<HideoutRequest>(
        "/zeromenutime/hideout/stash",
        (_, payload, sessionId, _, _) => ValueTask.FromResult(jsonUtil.Serialize(hideouts.SetStash(sessionId, payload)) ?? "{}")
    ),
    new RouteAction<DeathChoiceRequest>(
        "/zeromenutime/death/goto",
        (_, payload, sessionId, _, _) =>
            ValueTask.FromResult(jsonUtil.Serialize(ApplyDeathChoice(sessionId, payload)) ?? "{}")
    ),
])
{
    /// <summary>
    /// After a death the player picks where to carry on: their hideout, or the place they died.
    /// Vagabond has already moved them by then (its OnDeathGoTo setting), so this writes the
    /// chosen place back into its state.
    /// </summary>
    private static DeathChoiceResponse ApplyDeathChoice(MongoId sessionId, DeathChoiceRequest request)
    {
        var response = new DeathChoiceResponse();
        try
        {
            DeathChoiceBridge.Apply(sessionId.ToString(), request.Choice, response);
        }
        catch (Exception e)
        {
            // Vagabond not installed, or its API changed.
            response.Error = e.GetType().Name + ": " + e.Message;
        }
        return response;
    }

    /// <summary>
    /// Quests taken or finished at a trader inside a raid never pass through the server's normal
    /// accept/complete calls, so Vagabond does not learn about them and keeps quest extracts
    /// (the vehicle extracts and similar) switched off. Tell it here.
    /// </summary>
    private static QuestSyncResponse SyncQuests(MongoId sessionId, QuestSyncRequest request)
    {
        var response = new QuestSyncResponse();
        try
        {
            VagabondBridge.Sync(sessionId.ToString(), request, response);
            response.Ok = true;
        }
        catch (Exception e)
        {
            // Vagabond not installed, or its API changed: nothing to sync.
            response.Error = e.GetType().Name + ": " + e.Message;
        }
        return response;
    }

    /// <summary>Messages from this trader that still hold items and have not expired.</summary>
    private static IEnumerable<Message> PendingMessages(SptProfile? profile, string traderId)
    {
        if (profile?.DialogueRecords == null)
        {
            yield break;
        }
        // An empty trader id means every sender: all traders, insurance returns and system mail.
        // What the BTR driver carries between maps goes to the hideout's stash, never to a table.
        IEnumerable<Dialogue?> dialogues = string.IsNullOrEmpty(traderId)
            ? profile.DialogueRecords.Where(d => d.Key.ToString() != HideoutsService.BtrDriver).Select(d => d.Value)
            : (profile.DialogueRecords.TryGetValue(traderId, out var single) ? new[] { single } : []);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var message in dialogues.Where(d => d?.Messages != null).SelectMany(d => d!.Messages!))
        {
            if (message?.Items?.Data == null || message.Items.Data.Count == 0)
            {
                continue;
            }
            if (message.DateTime.HasValue && message.MaxStorageTime.HasValue && message.DateTime + message.MaxStorageTime < now)
            {
                continue;
            }
            yield return message;
        }
    }

    /// <summary>A top-level item in a message plus everything attached to or inside it.</summary>
    private static List<Item> WithChildren(Item root, List<Item> all)
    {
        var group = new List<Item> { root };
        var ids = new HashSet<string> { root.Id.ToString() };
        bool added = true;
        while (added)
        {
            added = false;
            foreach (var item in all)
            {
                if (item.ParentId != null && ids.Contains(item.ParentId) && ids.Add(item.Id.ToString()))
                {
                    group.Add(item);
                    added = true;
                }
            }
        }
        return group;
    }

    private static List<Item> Roots(Message message)
    {
        var data = message.Items!.Data!;
        var ids = new HashSet<string>(data.Select(x => x.Id.ToString()));
        return data.Where(x => x.ParentId == null || !ids.Contains(x.ParentId)).ToList();
    }

    /// <summary>What the trader is holding for the player. Nothing is removed yet.</summary>
    private static MailListResponse ListMail(MongoId sessionId, MailRequest request, ProfileHelper profiles)
    {
        var response = new MailListResponse();
        try
        {
            foreach (var message in PendingMessages(profiles.GetFullProfile(sessionId), request.TraderId))
            {
                foreach (var root in Roots(message))
                {
                    var group = WithChildren(root, message.Items!.Data!);
                    // The top item is sent without its place in the mail, so the client can put it anywhere.
                    group[0] = root with { ParentId = null, SlotId = null, Location = null };
                    response.Parcels.Add(new MailParcel { RootId = root.Id.ToString(), Items = group });
                }
            }
            response.Ok = true;
        }
        catch (Exception e)
        {
            response.Error = e.Message;
        }
        return response;
    }

    /// <summary>The client put these items into the player's gear: remove them from the mail.</summary>
    private static MailListResponse MarkTaken(MongoId sessionId, MailRequest request, ProfileHelper profiles)
    {
        var response = new MailListResponse();
        try
        {
            var taken = new HashSet<string>(request.RootIds ?? []);
            var profile = profiles.GetFullProfile(sessionId);
            foreach (var message in PendingMessages(profile, request.TraderId).ToList())
            {
                var data = message.Items!.Data!;
                foreach (var root in Roots(message).Where(x => taken.Contains(x.Id.ToString())))
                {
                    var remove = new HashSet<string>(WithChildren(root, data).Select(x => x.Id.ToString()));
                    data.RemoveAll(x => remove.Contains(x.Id.ToString()));
                }
                if (data.Count == 0)
                {
                    message.RewardCollected = true;
                }
            }
            // Clear the 'new attachments' marker on every conversation that has nothing left to collect.
            if (profile?.DialogueRecords != null)
            {
                foreach (var entry in profile.DialogueRecords)
                {
                    if (entry.Value != null && !PendingMessages(profile, entry.Key.ToString()).Any())
                    {
                        entry.Value.AttachmentsNew = 0;
                    }
                }
            }
            response.Ok = true;
        }
        catch (Exception e)
        {
            response.Error = e.Message;
        }
        return response;
    }

    private static TradeReportResponse Record(
        MongoId sessionId, TradeReport report, ProfileHelper profiles, TraderHelper traders, HandbookHelper handbook)
    {
        try
        {
            var pmc = profiles.GetPmcProfile(sessionId);
            MongoId traderId = report.TraderId;
            if (pmc?.TradersInfo == null || !pmc.TradersInfo.TryGetValue(traderId, out var info))
            {
                return new TradeReportResponse { Error = "Unknown trader or profile" };
            }

            double roubles = 0;
            foreach (var money in report.Money)
            {
                if (money.Count > 0)
                {
                    roubles += handbook.InRoubles(money.Count, money.Tpl);
                }
            }

            var trader = traders.GetTrader(traderId, sessionId);
            double inTraderCurrency = trader?.Currency == null
                ? roubles
                : handbook.FromRoubles(roubles, trader.Currency.Value.GetCurrencyTpl());

            info.SalesSum = (info.SalesSum ?? 0) + inTraderCurrency;
            traders.LevelUp(traderId, pmc);

            return new TradeReportResponse
            {
                Ok = true,
                SalesSum = info.SalesSum ?? 0,
                LoyaltyLevel = info.LoyaltyLevel ?? 1,
            };
        }
        catch (Exception e)
        {
            return new TradeReportResponse { Error = e.Message };
        }
    }
}
