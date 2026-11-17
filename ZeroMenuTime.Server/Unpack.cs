using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace ZeroMenuTime.Server;

/// <summary>A sealed case being opened in a raid: only what kind of case it is.</summary>
public sealed class UnpackRequest : IRequestData
{
    public string Tpl { get; set; } = "";
}

public sealed class UnpackResponse
{
    public bool Ok { get; set; }
    /// <summary>What was inside, each thing with its parts.</summary>
    public List<List<Item>> Loot { get; set; } = [];
    public bool FoundInRaid { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// The server opens a sealed case (an airdrop's weapon case and the like) on the saved character,
/// in the menus. In a raid the case is in the game, so the game asks only what is inside; the
/// contents are drawn by the server's own rules, as they would be in the menus.
/// </summary>
public static class Unpack
{
    private static readonly HashSet<string> WeaponCrates =
    [
        ItemTpl.RANDOMLOOTCONTAINER_ARENA_WEAPONCRATE_VIOLET_OPEN.ToString(),
        ItemTpl.RANDOMLOOTCONTAINER_ARENA_WEAPONCRATE_BLUE_OPEN.ToString(),
        ItemTpl.RANDOMLOOTCONTAINER_ARENA_WEAPONCRATE_GREEN_OPEN.ToString(),
    ];

    public static UnpackResponse Open(UnpackRequest request, TemplateTable templates, InventoryHelper inventory, LootGenerator loot)
    {
        var response = new UnpackResponse();
        try
        {
            MongoId tpl = request.Tpl;
            if (!templates.Items.TryGetValue(tpl, out var template) || template == null)
            {
                response.Error = "Unknown item";
                return response;
            }
            if ((template.Name ?? "").Contains("event_container_airdrop") || WeaponCrates.Contains(tpl.ToString()))
            {
                var settings = inventory.GetInventoryConfig().SealedAirdropContainer;
                response.Loot.AddRange(loot.GetSealedWeaponCaseLoot(settings));
                response.FoundInRaid = settings.FoundInRaid;
            }
            else
            {
                var details = inventory.GetRandomLootContainerRewardDetails(tpl);
                if (details == null)
                {
                    response.Error = "Nothing is known to be inside this";
                    return response;
                }
                response.Loot.AddRange(loot.GetRandomLootContainerLoot(details));
                response.FoundInRaid = details.FoundInRaid;
            }
            response.Ok = response.Loot.Count > 0;
            if (!response.Ok)
            {
                response.Error = "It was empty";
            }
            Console.WriteLine("[ZeroMenuTime] Unpacked " + template.Name + ": " + response.Loot.Count + " thing(s).");
        }
        catch (Exception e)
        {
            response.Error = e.GetType().Name + ": " + e.Message;
            Console.WriteLine("[ZeroMenuTime] Unpacking failed: " + e);
        }
        return response;
    }
}
