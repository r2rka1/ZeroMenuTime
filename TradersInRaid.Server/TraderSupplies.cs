using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace TradersInRaid.Server;

/// <summary>One kind of item a trader should always have on the shelf.</summary>
internal sealed record Supply(string Name, string Tpl, int Stock);

/// <summary>
/// With the flea market off and traders scattered across the maps, a player can end up with no way
/// to buy a bandage. This puts a small, guaranteed stock of field supplies on every trader we place:
/// basic meds, something to eat and drink, and a surgical kit, all at loyalty level 1.
/// The stock refills on the trader's normal restock, because the items are added to the database
/// assort the restock is rebuilt from.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class TraderSuppliesLoader(TraderHelper traderHelper, HandbookHelper handbookHelper) : IOnLoad
{
    /// <summary>What a trader charges over the handbook price.</summary>
    private const double Markup = 1.2;

    /// <summary>Carried by every trader: the things you should never be stranded without.</summary>
    private static readonly Supply[] Basics =
    [
        new("Aseptic bandage", "544fb25a4bdc2dfb738b4567", 10),
        new("Esmarch tourniquet", "5e831507ea0a7c419c2f9bd9", 10),
        new("Analgin painkillers", "544fb37f4bdc2dee738b4567", 6),
        new("Immobilizing splint", "544fb3364bdc2d34748b456a", 6),
        new("AI-2 medkit", "5755356824597772cb798962", 8),
        new("CMS surgical kit", "5d02778e86f774203e7dedbe", 2),
        new("Army crackers", "5448ff904bdc2d6f028b456e", 10),
        new("Bottle of water (0.6L)", "5448fee04bdc2dbc018b4567", 10),
    ];

    /// <summary>Therapist is still the one with the proper medicine.</summary>
    private static readonly Supply[] TherapistExtras =
    [
        new("Army bandage", "5751a25924597722c463c472", 10),
        new("CALOK-B hemostatic applicator", "5e8488fa988a8701445df1e4", 6),
        new("Aluminum splint", "5af0454c86f7746bf20992e8", 6),
        new("Car first aid kit", "590c661e86f7741e566b646a", 4),
        new("Salewa first aid kit", "544fb45d4bdc2dee738b4568", 3),
        new("Surv12 field surgical kit", "5d02797c86f774203f38e30a", 1),
    ];

    /// <summary>Jaeger lives in the woods and feeds himself.</summary>
    private static readonly Supply[] JaegerExtras =
    [
        new("MRE ration pack", "590c5f0d86f77413997acfab", 5),
    ];

    /// <summary>
    /// Fence is left out: his stock is generated fresh each restock rather than read from the
    /// database, so anything added here would simply disappear.
    /// </summary>
    private static readonly string[] Traders =
    [
        SPTarkov.Server.Core.Models.Enums.Traders.PRAPOR,
        SPTarkov.Server.Core.Models.Enums.Traders.THERAPIST,
        SPTarkov.Server.Core.Models.Enums.Traders.SKIER,
        SPTarkov.Server.Core.Models.Enums.Traders.PEACEKEEPER,
        SPTarkov.Server.Core.Models.Enums.Traders.MECHANIC,
        SPTarkov.Server.Core.Models.Enums.Traders.RAGMAN,
        SPTarkov.Server.Core.Models.Enums.Traders.JAEGER,
    ];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            int added = 0;
            foreach (var traderId in Traders)
            {
                var supplies = Basics.AsEnumerable();
                if (traderId == SPTarkov.Server.Core.Models.Enums.Traders.THERAPIST)
                {
                    supplies = supplies.Concat(TherapistExtras);
                }
                if (traderId == SPTarkov.Server.Core.Models.Enums.Traders.JAEGER)
                {
                    supplies = supplies.Concat(JaegerExtras);
                }
                added += Stock(new MongoId(traderId), supplies);
            }
            Console.WriteLine("[TradersInRaid] Put " + added + " field supply offers on the traders.");
        }
        catch (Exception e)
        {
            Console.WriteLine("[TradersInRaid] Could not stock the traders with field supplies: " + e);
        }
        return Task.CompletedTask;
    }

    private int Stock(MongoId traderId, IEnumerable<Supply> supplies)
    {
        var assort = traderHelper.GetTraderAssortsByTraderId(traderId);
        if (assort?.Items == null || assort.BarterScheme == null || assort.LoyalLevelItems == null)
        {
            return 0;
        }

        int added = 0;
        foreach (var supply in supplies)
        {
            var tpl = new MongoId(supply.Tpl);
            // Nothing to do if the trader already offers it at the lowest loyalty level.
            bool alreadySold = assort.Items.Any(item =>
                item.Template == tpl
                && item.ParentId == "hideout"
                && assort.LoyalLevelItems.TryGetValue(item.Id, out int level)
                && level <= 1);
            if (alreadySold)
            {
                continue;
            }

            double price = Math.Max(100, Math.Round(handbookHelper.GetTemplatePrice(tpl) * Markup));
            var offerId = new MongoId();
            assort.Items.Add(new Item
            {
                Id = offerId,
                Template = tpl,
                ParentId = "hideout",
                SlotId = "hideout",
                Upd = new Upd
                {
                    StackObjectsCount = supply.Stock,
                    UnlimitedCount = false,
                },
            });
            assort.BarterScheme[offerId] =
            [
                [new BarterScheme { Count = price, Template = new MongoId(Money.ROUBLES) }],
            ];
            assort.LoyalLevelItems[offerId] = 1;
            added++;
        }
        return added;
    }
}
