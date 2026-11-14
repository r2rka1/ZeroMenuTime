using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace TheTrueSPT.Server;

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
            Console.WriteLine("[TheTrueSPT] Put " + added + " field supply offers on the traders.");

            int maps = 0;
            try
            {
                foreach (var (traderId, map) in LocalMaps.ForEachTrader())
                {
                    if (Traders.Contains(traderId))
                    {
                        maps += Stock(new MongoId(traderId), [map]);
                    }
                }
            }
            catch (Exception e)
            {
                // Vagabond not installed, or its API changed: nobody stands anywhere, so no local maps.
                Console.WriteLine("[TheTrueSPT] Local maps not stocked: " + e.Message);
            }
            Console.WriteLine("[TheTrueSPT] Put " + maps + " local map offers on the traders.");
        }
        catch (Exception e)
        {
            Console.WriteLine("[TheTrueSPT] Could not stock the traders with field supplies: " + e);
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

            double handbookPrice = handbookHelper.GetTemplatePrice(tpl);
            if (handbookPrice <= 1)
            {
                // No price means no such item here, for instance a map another mod would have added.
                continue;
            }
            double price = Math.Max(100, Math.Round(handbookPrice * Markup));
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

/// <summary>
/// A trader sells the map of the place they stand on, so a player who arrives somewhere blind can
/// buy their bearings from the local trader. Where each trader stands comes from Vagabond. Kept
/// apart so a missing Vagabond only fails this one call.
/// </summary>
internal static class LocalMaps
{
    /// <summary>
    /// The map item for each location. The maps for the newer locations are added by Dynamic Maps;
    /// without it they have no price and are skipped.
    /// </summary>
    private static readonly Dictionary<string, Supply> Maps = new()
    {
        { "FactoryDay", new Supply("Factory plan map", "574eb85c245977648157eec3", 2) },
        { "FactoryNight", new Supply("Factory plan map", "574eb85c245977648157eec3", 2) },
        { "Woods", new Supply("Woods plan map", "5900b89686f7744e704a8747", 2) },
        { "Customs", new Supply("Customs plan map", "5798a2832459774b53341029", 2) },
        { "Interchange", new Supply("Interchange plan map", "5be4038986f774527d3fae60", 2) },
        { "Shoreline", new Supply("Shoreline plan map", "5a8036fb86f77407252ddc02", 2) },
        { "Reserve", new Supply("Reserve map", "6738034a9713b5f42b4a8b78", 2) },
        { "Labs", new Supply("Labs map", "6738034e9d22459ad7cd1b81", 2) },
        { "Lighthouse", new Supply("Lighthouse map", "6738035350b24a4ae4a57997", 2) },
        { "Streets", new Supply("Streets map", "673803448cb3819668d77b1b", 2) },
        { "GroundZero", new Supply("Ground Zero map", "6738033eb7305d3bdafe9518", 2) },
        { "Labyrinth", new Supply("Labyrinth map", "68f1ad32317cc52f4c0b6fae", 2) },
    };

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static List<(string TraderId, Supply Map)> ForEachTrader()
    {
        var result = new List<(string, Supply)>();
        foreach (var spot in Vagabond.Common.Api.Api.GetTraderExtractions())
        {
            if (Maps.TryGetValue(spot.Raid.ToString(), out var map))
            {
                result.Add((spot.TraderId, map));
            }
        }
        return result;
    }
}
