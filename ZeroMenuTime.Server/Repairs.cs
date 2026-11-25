using System.Reflection;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Commerce;

namespace ZeroMenuTime.Server;

/// <summary>An item being repaired with kit points in a raid: what it is and how worn.</summary>
public sealed class KitRepairRequest : IRequestData
{
    public string Tpl { get; set; } = "";
    public double Durability { get; set; }
    public double MaxDurability { get; set; }
    /// <summary>Kit points spent on it.</summary>
    public double Points { get; set; }
}

/// <summary>An item being repaired by a trader in a raid: what it is, how worn, by how many points, and by whom.</summary>
public sealed class TraderRepairRequest : IRequestData
{
    public string Tpl { get; set; } = "";
    public double Durability { get; set; }
    public double MaxDurability { get; set; }
    /// <summary>Durability points to put back.</summary>
    public double Points { get; set; }
    public string Trader { get; set; } = "";
}

public sealed class KitRepairResponse
{
    public bool Ok { get; set; }
    public double Durability { get; set; }
    public double MaxDurability { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// In a raid the gear is in the game, not in the saved character, so the server cannot repair it
/// where it lies. The game sends the item's numbers instead and the server's own repair rules are
/// run on those: how much durability the points buy, and how much of the maximum is lost.
/// </summary>
public static class Repairs
{
    public static KitRepairResponse ByKit(MongoId sessionId, KitRepairRequest request, ProfileHelper profiles,
        RepairService service, RepairHelper helper, TemplateTable templates)
    {
        var response = new KitRepairResponse();
        try
        {
            var pmc = profiles.GetPmcProfile(sessionId);
            MongoId tpl = request.Tpl;
            if (pmc == null || !templates.Items.TryGetValue(tpl, out var template) || template?.Properties == null)
            {
                response.Error = "Unknown item or profile";
                return response;
            }
            if (request.Points <= 0)
            {
                response.Error = "No kit points given";
                return response;
            }
            bool armour = template.Properties.ArmorMaterial.HasValue;
            const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            double divisor = (double)typeof(RepairService).GetMethod("GetKitDivisor", Hidden)!.Invoke(service, [template, armour, pmc])!;
            if (divisor <= 0)
            {
                response.Error = "No repair cost known for this item";
                return response;
            }
            // Elite repair skill spares the maximum; the server decides, as it does in the menus.
            bool wear = true;
            var wearRule = typeof(RepairService).GetMethod("ShouldRepairKitApplyDurabilityLoss", Hidden);
            if (wearRule != null)
            {
                var arguments = wearRule.GetParameters().Length == 2 ? new object?[] { pmc, true } : new object?[] { pmc };
                wear = (bool)wearRule.Invoke(service, arguments)!;
            }
            var item = new Item
            {
                Id = new MongoId(),
                Template = tpl,
                Upd = new Upd { Repairable = new UpdRepairable { Durability = request.Durability, MaxDurability = request.MaxDurability } },
            };
            helper.UpdateItemDurability(item, template, armour, request.Points / divisor, true, 1.0, wear);
            response.Durability = item.Upd.Repairable!.Durability ?? request.Durability;
            response.MaxDurability = item.Upd.Repairable.MaxDurability ?? request.MaxDurability;
            response.Ok = true;
            Console.WriteLine("[ZeroMenuTime] Repair by kit: " + request.Points.ToString("F1") + " point(s) on " + template.Name + ": "
                              + request.Durability.ToString("F1") + "/" + request.MaxDurability.ToString("F1") + " -> "
                              + response.Durability.ToString("F1") + "/" + response.MaxDurability.ToString("F1"));
        }
        catch (Exception e)
        {
            response.Error = e.GetType().Name + ": " + (e.InnerException?.Message ?? e.Message);
            Console.WriteLine("[ZeroMenuTime] Repair by kit failed: " + e);
        }
        return response;
    }

    /// <summary>
    /// A trader's repair, by the server's own rule for it: the points asked for are put back and the
    /// maximum wears by the trader's quality. The money is taken in the game, from what is carried.
    /// </summary>
    public static KitRepairResponse ByTrader(MongoId sessionId, TraderRepairRequest request, SPTarkov.Server.Core.Helpers.Traders.TraderHelper traders,
        RepairService service, RepairHelper helper, TemplateTable templates)
    {
        var response = new KitRepairResponse();
        try
        {
            MongoId tpl = request.Tpl;
            if (!templates.Items.TryGetValue(tpl, out var template) || template?.Properties == null)
            {
                response.Error = "Unknown item";
                return response;
            }
            if (request.Points <= 0)
            {
                response.Error = "Nothing to repair";
                return response;
            }
            var repair = traders.GetTrader(new MongoId(request.Trader), sessionId)?.Repair;
            if (repair == null || repair.Availability != true)
            {
                response.Error = "This trader does not repair";
                return response;
            }
            double quality = repair.Quality ?? 0;
            // The server's setting for wear at random, read from its own repair service; on, as it is by default, if it cannot be read.
            bool random = true;
            try
            {
                const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var held = typeof(RepairService).GetFields(Hidden).FirstOrDefault(f => f.FieldType.Name == "RepairConfig")?.GetValue(service);
                var setting = held?.GetType().GetProperty("ApplyRandomizeDurabilityLoss")?.GetValue(held);
                if (setting is bool value)
                {
                    random = value;
                }
            }
            catch (Exception)
            {
            }
            bool armour = template.Properties.ArmorMaterial.HasValue;
            var item = new Item
            {
                Id = new MongoId(),
                Template = tpl,
                Upd = new Upd { Repairable = new UpdRepairable { Durability = request.Durability, MaxDurability = request.MaxDurability } },
            };
            helper.UpdateItemDurability(item, template, armour, request.Points, false, quality, quality != 0.0 && random);
            response.Durability = item.Upd.Repairable!.Durability ?? request.Durability;
            response.MaxDurability = item.Upd.Repairable.MaxDurability ?? request.MaxDurability;
            response.Ok = true;
            Console.WriteLine("[ZeroMenuTime] Repair by trader " + request.Trader + ": " + request.Points.ToString("F1") + " point(s) on " + template.Name + ": "
                              + request.Durability.ToString("F1") + "/" + request.MaxDurability.ToString("F1") + " -> "
                              + response.Durability.ToString("F1") + "/" + response.MaxDurability.ToString("F1"));
        }
        catch (Exception e)
        {
            response.Error = e.GetType().Name + ": " + (e.InnerException?.Message ?? e.Message);
            Console.WriteLine("[ZeroMenuTime] Repair by trader failed: " + e);
        }
        return response;
    }
}
