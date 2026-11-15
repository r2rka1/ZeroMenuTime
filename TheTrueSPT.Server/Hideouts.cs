using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using EftHideout = SPTarkov.Server.Core.Models.Eft.Common.Tables.Hideout;

namespace TheTrueSPT.Server;

/// <summary>Everything that makes one hideout different from another.</summary>
public sealed class HideoutSave
{
    /// <summary>Station levels, crafts in progress and the rest of the game's hideout record.</summary>
    public EftHideout? Hideout { get; set; }
    /// <summary>The character's bonuses while this hideout is the one in use.</summary>
    public List<Bonus>? Bonuses { get; set; }
    /// <summary>The stash's size follows the Stash station's level, by way of its template.</summary>
    public string? StashTemplate { get; set; }
    public Dictionary<string, MongoId>? AreaStashes { get; set; }
    /// <summary>What is in the stash and in the stations' own storage.</summary>
    public List<Item> Items { get; set; } = [];
}

/// <summary>Something being made at a station, timed on the world clock.</summary>
public sealed class HideoutCraft
{
    public string Recipe { get; set; } = "";
    public int Area { get; set; }
    public string Product { get; set; } = "";
    public int Count { get; set; }
    /// <summary>World clock readings, in game seconds.</summary>
    public double Started { get; set; }
    public double Due { get; set; }
    /// <summary>Told to the client: it is waiting for power and its time is not running.</summary>
    public bool Paused { get; set; }
}

/// <summary>A recipe a station of the hideout in use can make now.</summary>
public sealed class HideoutRecipe
{
    public string Id { get; set; } = "";
    public int Area { get; set; }
    public string Product { get; set; } = "";
    public int Count { get; set; }
    /// <summary>How long it takes, in game seconds.</summary>
    public double Seconds { get; set; }
    /// <summary>Used up.</summary>
    public List<HideoutCost> Items { get; set; } = [];
    /// <summary>Needed at hand, not used up.</summary>
    public List<string> Tools { get; set; } = [];
}

/// <summary>A fuel can standing in a generator.</summary>
public sealed class FuelCan
{
    public string Tpl { get; set; } = "";
    public double Left { get; set; }
    public double Max { get; set; }
}

/// <summary>A hideout's generator and what runs off it. Settled against the world clock whenever that moves.</summary>
public sealed class HideoutPower
{
    public bool On { get; set; }
    public List<FuelCan> Fuel { get; set; } = [];
    /// <summary>Graphics cards in the bitcoin farm.</summary>
    public int Gpus { get; set; }
    /// <summary>Bitcoins waiting to be collected, and how far along the next one is (0 to 1).</summary>
    public int Coins { get; set; }
    public double CoinProgress { get; set; }
    /// <summary>The clock reading this was last worked out at.</summary>
    public double Settled { get; set; }
    /// <summary>The water collector's filter, the bottles of clean water waiting, and how far along the next is.</summary>
    public FuelCan? WaterFilter { get; set; }
    public int Water { get; set; }
    public double WaterProgress { get; set; }
    /// <summary>True once the water collector is at the level that works by itself.</summary>
    public bool WaterWorks { get; set; }
    /// <summary>How much of the usual fuel the generator burns: half with Solar Power built.</summary>
    public double FuelFactor { get; set; } = 1;
    /// <summary>The scav case run under way: which offer, and when the scavs are back.</summary>
    public string ScavRecipe { get; set; } = "";
    public double ScavDue { get; set; }
    /// <summary>When the character last trained in the gym; muscles ache for a day after.</summary>
    public double LastWorkout { get; set; } = -1e12;
    /// <summary>When the cultists' circle gives up what it made of the last sacrifice.</summary>
    public double CircleDue { get; set; }
}

/// <summary>One thing the scav case can be sent out for.</summary>
public sealed class ScavOffer
{
    public string Id { get; set; } = "";
    public double Seconds { get; set; }
    public List<HideoutCost> Items { get; set; } = [];
    /// <summary>How many finds of each kind it brings back, as "least-most".</summary>
    public string Common { get; set; } = "";
    public string Rare { get; set; } = "";
    public string Superrare { get; set; } = "";
}

/// <summary>What the client is told about the power of the hideout in use.</summary>
public sealed class PowerReport
{
    public bool On { get; set; }
    /// <summary>On, and with fuel left.</summary>
    public bool Powered { get; set; }
    public List<FuelCan> Fuel { get; set; } = [];
    public int FuelSlots { get; set; }
    /// <summary>Game seconds the fuel lasts with the generator running.</summary>
    public double FuelLasts { get; set; }
    public int Gpus { get; set; }
    public int GpuSlots { get; set; }
    public int Coins { get; set; }
    public int CoinCap { get; set; }
    /// <summary>Game seconds of power until the next bitcoin; below zero when the farm is not working.</summary>
    public double NextCoinIn { get; set; }
    /// <summary>How many times longer a craft takes when started without power.</summary>
    public double SlowFactor { get; set; }
    /// <summary>In answer to taking a can out: the can.</summary>
    public FuelCan? Taken { get; set; }
    /// <summary>In answer to collecting: how many bitcoins were handed over.</summary>
    public int CoinsTaken { get; set; }
    public FuelCan? WaterFilter { get; set; }
    public int Water { get; set; }
    public int WaterCap { get; set; }
    /// <summary>Game seconds until the next bottle; below zero when nothing is being collected.</summary>
    public double NextWaterIn { get; set; }
    public int WaterTaken { get; set; }
    public double FuelFactor { get; set; }
    public List<ScavOffer> ScavOffers { get; set; } = [];
    public string ScavRecipe { get; set; } = "";
    public double ScavDue { get; set; }
    /// <summary>In answer to collecting the scav case: what was brought back, each find with its parts.</summary>
    public List<List<Item>> ScavLoot { get; set; } = [];
    /// <summary>Game seconds until the gym can be used again; zero when it can.</summary>
    public double WorkoutIn { get; set; }
    public double CircleDue { get; set; }
}

public sealed class HideoutStore
{
    /// <summary>2 once the three hideouts have been folded into one and crafts put on the pause-without-power rule.</summary>
    public int Version { get; set; }
    /// <summary>Each hideout's generator, fuel and bitcoin farm.</summary>
    public Dictionary<string, HideoutPower> Power { get; set; } = [];
    /// <summary>What is being made in each hideout, in use or put away. They all run on the one clock.</summary>
    public Dictionary<string, List<HideoutCraft>> Crafts { get; set; } = [];
    /// <summary>The hideout whose state is in the profile right now.</summary>
    public string Active { get; set; } = HideoutsService.First;
    /// <summary>The others, put away until they are entered.</summary>
    public Dictionary<string, HideoutSave> Away { get; set; } = [];
    /// <summary>
    /// The character's world clock, in game seconds. It runs while a raid runs, jumps when the
    /// character sleeps and stands still while the game is closed. Crafts in every hideout are
    /// timed against it.
    /// </summary>
    public double ClockSeconds { get; set; }
}

public sealed class HideoutRequest : IRequestData
{
    public string Id { get; set; } = "";
    /// <summary>For building: the station's number, as the game counts them.</summary>
    public int Area { get; set; } = -1;
    /// <summary>For the stash: everything in it, as the client holds it.</summary>
    public List<Item>? Items { get; set; }
    /// <summary>For the clock: what the game says the world clock reads now.</summary>
    public double Seconds { get; set; }
    /// <summary>For crafting: the recipe to start.</summary>
    public string Recipe { get; set; } = "";
    /// <summary>For power: on, off, fuel, unfuel, gpu, ungpu, coins.</summary>
    public string Action { get; set; } = "";
    /// <summary>For power: the fuel can put in (its item, what is left in it, what it holds full), or which can to take out.</summary>
    public string Tpl { get; set; } = "";
    public double Left { get; set; }
    public double Max { get; set; }
    public int Index { get; set; }
}

/// <summary>One kind of material a station level needs.</summary>
public sealed class HideoutCost
{
    public string Tpl { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>What a station keeps on it: the weapon stand's guns, the place of fame's trophies.</summary>
public sealed class ShelfReport
{
    public string Id { get; set; } = "";
    public string Template { get; set; } = "";
    public List<Item> Items { get; set; } = [];
}

public sealed class HideoutResponse
{
    /// <summary>Station number to the container that station keeps things in.</summary>
    public Dictionary<int, ShelfReport> Shelves { get; set; } = [];
    /// <summary>Station number to what its next level costs. A station at its top level is not listed.</summary>
    public Dictionary<int, List<HideoutCost>> NextCosts { get; set; } = [];
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string Active { get; set; } = "";
    /// <summary>Station number to level, for the hideout now in use.</summary>
    public Dictionary<int, int> Levels { get; set; } = [];
    public string? StashTemplate { get; set; }
    public string? StashId { get; set; }
    public List<Item> Stash { get; set; } = [];
    /// <summary>How many items each hideout's stash holds, in use or put away.</summary>
    public Dictionary<string, int> StashCounts { get; set; } = [];
    /// <summary>The world clock, in game seconds.</summary>
    public double Clock { get; set; }
    /// <summary>
    /// What the built stations of the hideout in use give while the character is inside: bonus kind
    /// (HealthRegeneration, EnergyRegeneration, HydrationRegeneration, MaximumEnergyReserve) to amount.
    /// </summary>
    public Dictionary<string, double> Boosts { get; set; } = [];
    /// <summary>What is being made in every hideout, by hideout.</summary>
    public Dictionary<string, List<HideoutCraft>> Crafts { get; set; } = [];
    /// <summary>Only in answer to "recipes": what the hideout in use can make.</summary>
    public List<HideoutRecipe> Recipes { get; set; } = [];
    /// <summary>Only in answer to "collect": the finished crafts handed over.</summary>
    public List<HideoutCraft> Collected { get; set; } = [];
    /// <summary>The generator, fuel and bitcoin farm of the hideout in use.</summary>
    public PowerReport? Power { get; set; }
}

/// <summary>
/// Three hideouts for one character. The game knows one hideout per profile, so the profile always
/// holds the hideout last entered and the other two are kept in a file beside Vagabond's. Entering
/// a hideout puts the current one away and brings the entered one into the profile.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class HideoutsService(
    ProfileHelper profileHelper,
    HideoutController hideoutController,
    HideoutHelper hideoutHelper,
    EventOutputHolder eventOutputHolder,
    HideoutTable hideoutTable,
    ICloner cloner,
    JsonUtil jsonUtil,
    SPTarkov.Server.Core.Generators.ScavCaseRewardGenerator scavCaseRewardGenerator,
    SPTarkov.Server.Core.Services.Hideout.CircleOfCultistService circleOfCultistService,
    SPTarkov.Server.Core.Helpers.Profile.InventoryHelper inventoryHelper)
{
    /// <summary>The BTR driver's id: what he delivers between maps arrives as mail from him.</summary>
    public const string BtrDriver = "656f0f98d80a697f855d34b1";

    /// <summary>
    /// Things sent between maps by the BTR driver are put in the hideout's stash. Done on entering
    /// the hideout, when the client reads the stash afresh. Whatever does not fit stays in the mail
    /// for next time.
    /// </summary>
    private void TakeDeliveries(MongoId sessionId, PmcData pmc)
    {
        var full = profileHelper.GetFullProfile(sessionId);
        if (full?.DialogueRecords == null || !full.DialogueRecords.TryGetValue(BtrDriver, out var dialogue) || dialogue?.Messages == null)
        {
            return;
        }
        int moved = 0, left = 0;
        foreach (var message in dialogue.Messages)
        {
            var data = message?.Items?.Data;
            if (data == null || data.Count == 0)
            {
                continue;
            }
            var ids = new HashSet<string>(data.Select(i => i.Id.ToString()));
            foreach (var root in data.Where(i => i.ParentId == null || !ids.Contains(i.ParentId)).ToList())
            {
                var group = new List<Item> { root with { ParentId = null, SlotId = null, Location = null } };
                var inGroup = new HashSet<string> { root.Id.ToString() };
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (var item in data)
                    {
                        if (item.ParentId != null && inGroup.Contains(item.ParentId) && inGroup.Add(item.Id.ToString()))
                        {
                            group.Add(item);
                            grew = true;
                        }
                    }
                }
                int before = pmc.Inventory!.Items!.Count;
                var output = eventOutputHolder.GetOutput(sessionId);
                inventoryHelper.AddItemsToStash(sessionId, new SPTarkov.Server.Core.Models.Eft.Inventory.AddItemsDirectRequest
                {
                    ItemsWithModsToAdd = [group],
                    FoundInRaid = true,
                    UseSortingTable = false,
                }, pmc, output);
                if (pmc.Inventory.Items.Count > before)
                {
                    data.RemoveAll(i => inGroup.Contains(i.Id.ToString()));
                    moved++;
                }
                else
                {
                    left++;
                }
            }
            if (data.Count == 0)
            {
                message!.RewardCollected = true;
            }
        }
        if (left == 0)
        {
            dialogue.AttachmentsNew = 0;
        }
        if (moved + left > 0)
        {
            Console.WriteLine("[TheTrueSPT] BTR deliveries: " + moved + " put in the hideout's stash" + (left > 0 ? ", " + left + " did not fit and wait in the mail" : "") + ".");
        }
    }

    public const string First = "streets";
    public static readonly string[] Known = ["streets", "customs", "shoreline"];

    private static readonly HashSet<string> BoostKinds = ["HealthRegeneration", "EnergyRegeneration", "HydrationRegeneration", "MaximumEnergyReserve"];

    private readonly object _gate = new();

    private static string PathFor(string sessionId)
    {
        return System.IO.Path.Combine("user", "profileData", sessionId, "thetruespt.hideouts.json");
    }

    private HideoutStore Load(string sessionId)
    {
        string path = PathFor(sessionId);
        if (File.Exists(path))
        {
            var store = jsonUtil.Deserialize<HideoutStore>(File.ReadAllText(path));
            if (store != null)
            {
                if (store.Version < 2)
                {
                    FoldIntoOne(sessionId, path, store);
                }
                return store;
            }
        }
        // First use: the hideout the character already has is the one hideout.
        return new HideoutStore { Version = 2 };
    }

    /// <summary>
    /// There used to be a hideout behind each of the three doors. Now there is one, reached by all
    /// three: the one that was in use. The others are written to a file beside the store and let
    /// go. Crafts started under the old rule (slower without power) get their ordinary time back.
    /// </summary>
    private void FoldIntoOne(string sessionId, string path, HideoutStore store)
    {
        try
        {
            File.Copy(path, path + ".before-one-hideout", true);
            if (store.Away.Count > 0)
            {
                string dropped = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "thetruespt.hideouts.dropped.json");
                File.WriteAllText(dropped, jsonUtil.Serialize(store.Away) ?? "{}");
                Console.WriteLine("[TheTrueSPT] One hideout now: kept '" + store.Active + "'; " + store.Away.Count + " other(s) with "
                                  + store.Away.Values.Sum(h => h.Items.Count) + " item(s) written to " + dropped + " and let go.");
                store.Away.Clear();
            }
            foreach (string other in store.Crafts.Keys.Where(k => k != store.Active).ToList())
            {
                store.Crafts.Remove(other);
            }
            foreach (string other in store.Power.Keys.Where(k => k != store.Active).ToList())
            {
                store.Power.Remove(other);
            }
            foreach (var craft in store.Crafts.Values.SelectMany(list => list))
            {
                var recipe = (hideoutTable.Production.Recipes ?? []).FirstOrDefault(r => r.Id.ToString() == craft.Recipe);
                double ordinary = recipe?.ProductionTime ?? 0;
                if (ordinary > 0 && craft.Due - craft.Started > ordinary)
                {
                    craft.Due = craft.Started + ordinary;
                }
            }
            store.Version = 2;
            Save(sessionId, store);
        }
        catch (Exception e)
        {
            Console.WriteLine("[TheTrueSPT] Folding the hideouts into one failed: " + e);
        }
    }

    private void Save(string sessionId, HideoutStore store)
    {
        string path = PathFor(sessionId);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, jsonUtil.Serialize(store, true));
        File.Move(temp, path, true);
    }

    /// <summary>The containers whose contents belong to the hideout: the stash and the stations' own storage.</summary>
    private static List<string> Containers(PmcData pmc)
    {
        var roots = new List<string>();
        if (pmc.Inventory?.Stash != null)
        {
            roots.Add(pmc.Inventory.Stash.Value.ToString());
        }
        if (pmc.Inventory?.HideoutAreaStashes != null)
        {
            roots.AddRange(pmc.Inventory.HideoutAreaStashes.Values.Select(x => x.ToString()));
        }
        return roots;
    }

    /// <summary>Every item inside the given containers, at any depth. The containers themselves are not included.</summary>
    private static List<Item> Inside(PmcData pmc, IEnumerable<string> containers)
    {
        var ids = new HashSet<string>(containers);
        var found = new List<Item>();
        var all = pmc.Inventory?.Items ?? [];
        bool added = true;
        while (added)
        {
            added = false;
            foreach (var item in all)
            {
                string id = item.Id.ToString();
                if (item.ParentId != null && ids.Contains(item.ParentId) && ids.Add(id))
                {
                    found.Add(item);
                    added = true;
                }
            }
        }
        return found;
    }

    private Item? StashRoot(PmcData pmc)
    {
        string? id = pmc.Inventory?.Stash?.ToString();
        return id == null ? null : pmc.Inventory!.Items!.FirstOrDefault(x => x.Id.ToString() == id);
    }

    private HideoutSave Capture(PmcData pmc)
    {
        return new HideoutSave
        {
            Hideout = cloner.Clone(pmc.Hideout),
            Bonuses = cloner.Clone(pmc.Bonuses),
            StashTemplate = StashRoot(pmc)?.Template.ToString(),
            AreaStashes = cloner.Clone(pmc.Inventory?.HideoutAreaStashes),
            Items = cloner.Clone(Inside(pmc, Containers(pmc))) ?? [],
        };
    }

    private void Apply(PmcData pmc, HideoutSave save)
    {
        var gone = new HashSet<string>(Inside(pmc, Containers(pmc)).Select(x => x.Id.ToString()));
        pmc.Inventory!.Items!.RemoveAll(x => gone.Contains(x.Id.ToString()));
        pmc.Hideout = save.Hideout;
        pmc.Bonuses = save.Bonuses;
        pmc.Inventory.HideoutAreaStashes = save.AreaStashes ?? [];
        var root = StashRoot(pmc);
        if (root != null && !string.IsNullOrEmpty(save.StashTemplate))
        {
            root.Template = new MongoId(save.StashTemplate);
        }
        pmc.Inventory.Items.AddRange(save.Items);
    }

    /// <summary>A hideout nobody has built anything in: an empty level 1 stash and every station at level 0.</summary>
    private HideoutSave Fresh(PmcData pmc)
    {
        var hideout = cloner.Clone(pmc.Hideout)!;
        foreach (var area in hideout.Areas ?? [])
        {
            area.Level = area.Type == HideoutAreas.Stash ? 1 : 0;
            area.Constructing = false;
            area.CompleteTime = 0;
            area.LastRecipe = null;
            area.Slots = [];
        }
        hideout.Production?.Clear();
        hideout.Improvements?.Clear();

        // Bonuses that came from stations go; whatever else the character has (edition, prestige) stays.
        var fromStations = new HashSet<string>(
            hideoutTable.Areas.SelectMany(a => a.Stages?.Values ?? Enumerable.Empty<Stage>())
                .SelectMany(s => s.Bonuses ?? [])
                .Select(b => b.Id.ToString()));
        var bonuses = (cloner.Clone(pmc.Bonuses) ?? []).Where(b => !fromStations.Contains(b.Id.ToString())).ToList();

        var stash = hideoutTable.Areas.FirstOrDefault(a => a.Type == HideoutAreas.Stash);
        Stage? first = null;
        stash?.Stages?.TryGetValue("1", out first);
        return new HideoutSave
        {
            Hideout = hideout,
            Bonuses = bonuses,
            StashTemplate = first?.Container != null && !first.Container.Value.IsEmpty
                ? first.Container.Value.ToString()
                : StashRoot(pmc)?.Template.ToString(),
            AreaStashes = [],
            Items = [],
        };
    }

    private HideoutResponse Describe(PmcData pmc, HideoutStore store)
    {
        var response = new HideoutResponse
        {
            Ok = true,
            Active = store.Active,
            Clock = store.ClockSeconds,
            Crafts = store.Crafts,
            StashTemplate = StashRoot(pmc)?.Template.ToString(),
            StashId = pmc.Inventory?.Stash?.ToString(),
            Stash = Inside(pmc, pmc.Inventory?.Stash != null ? [pmc.Inventory.Stash.Value.ToString()] : []),
        };
        foreach (var area in pmc.Hideout?.Areas ?? [])
        {
            response.Levels[(int)area.Type] = area.Level ?? 0;
            var station = hideoutTable.Areas.FirstOrDefault(a => a.Type == area.Type);
            // A level's bonus replaces the one of the level below it, so only the current level counts.
            Stage? current = null;
            if ((area.Level ?? 0) > 0 && station?.Stages != null && station.Stages.TryGetValue((area.Level ?? 0).ToString(), out current) && current?.Bonuses != null)
            {
                foreach (var bonus in current.Bonuses)
                {
                    string kind = bonus.Type.ToString();
                    if (BoostKinds.Contains(kind))
                    {
                        response.Boosts[kind] = response.Boosts.GetValueOrDefault(kind) + (bonus.Value ?? 0);
                    }
                }
            }
            Stage? next = null;
            if (station?.Stages != null && station.Stages.TryGetValue(((area.Level ?? 0) + 1).ToString(), out next) && next != null)
            {
                response.NextCosts[(int)area.Type] = (next.Requirements ?? [])
                    .Where(r => string.Equals(r.Type, "Item", StringComparison.OrdinalIgnoreCase) && (r.Count ?? 0) > 0)
                    .Select(r => new HideoutCost { Tpl = r.TemplateId.ToString(), Count = r.Count ?? 0 })
                    .ToList();
            }
        }
        response.Power = Report(store, store.Active, response.Levels);
        foreach (var shelf in pmc.Inventory?.HideoutAreaStashes ?? [])
        {
            string shelfId = shelf.Value.ToString();
            var root = pmc.Inventory!.Items!.FirstOrDefault(x => x.Id.ToString() == shelfId);
            if (root != null && int.TryParse(shelf.Key.ToString(), out int shelfArea))
            {
                response.Shelves[shelfArea] = new ShelfReport { Id = shelfId, Template = root.Template.ToString(), Items = Inside(pmc, [shelfId]) };
            }
        }
        foreach (var pair in store.Crafts)
        {
            bool lit = store.Power.TryGetValue(pair.Key, out var there) && there.On && there.Fuel.Sum(can => can.Left) > 0.0001;
            foreach (var craft in pair.Value)
            {
                craft.Paused = !lit && NeedsPower(craft.Area) && craft.Due > store.ClockSeconds;
            }
        }
        foreach (string id in Known)
        {
            response.StashCounts[id] = id == store.Active
                ? response.Stash.Count
                : (store.Away.TryGetValue(id, out var away) ? away.Items.Count : 0);
        }
        return response;
    }

    private HideoutResponse Guarded(MongoId sessionId, HideoutRequest request, bool needKnownId,
        Func<PmcData, HideoutStore, HideoutResponse> work)
    {
        try
        {
            string id = (request.Id ?? "").ToLowerInvariant();
            if (needKnownId && !Known.Contains(id))
            {
                return new HideoutResponse { Error = "No hideout called '" + request.Id + "'" };
            }
            request.Id = id;
            var pmc = profileHelper.GetPmcProfile(sessionId);
            if (pmc?.Hideout == null || pmc.Inventory?.Items == null)
            {
                return new HideoutResponse { Error = "No profile" };
            }
            lock (_gate)
            {
                var store = Load(sessionId.ToString());
                // The three doors lead to the one hideout; whichever was named, it is that one.
                request.Id = store.Active;
                // What the stations' levels mean for the running count is noted before any of it
                // is worked out: a solar panel halves the fuel burnt, a level 3 collector fills bottles.
                var built = (pmc.Hideout.Areas ?? []).ToDictionary(a => (int)a.Type, a => a.Level ?? 0);
                var running = PowerOf(store, store.Active);
                running.FuelFactor = built.GetValueOrDefault(SolarPower) >= 1 ? 0.5 : 1;
                running.WaterWorks = built.GetValueOrDefault(WaterCollector) >= 3;
                return work(pmc, store);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("[TheTrueSPT] Hideout request failed: " + e);
            return new HideoutResponse { Error = e.GetType().Name + ": " + e.Message };
        }
    }

    public HideoutResponse List(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, false, Describe);
    }

    /// <summary>The recipes the built stations of the hideout in use can make at their levels.</summary>
    private List<HideoutRecipe> RecipesFor(PmcData pmc)
    {
        var levels = (pmc.Hideout?.Areas ?? []).ToDictionary(a => (int)a.Type, a => a.Level ?? 0);
        var done = new HashSet<string>((pmc.Quests ?? []).Where(q => q.Status == QuestStatusEnum.Success).Select(q => q.QId.ToString()));
        var unlocked = new HashSet<string>((pmc.UnlockedInfo?.UnlockedProductionRecipe ?? []).Select(x => x.ToString()));
        var result = new List<HideoutRecipe>();
        foreach (var recipe in hideoutTable.Production.Recipes ?? [])
        {
            int area = (int)(recipe.AreaType ?? 0);
            if (!levels.TryGetValue(area, out int level) || level < 1)
            {
                continue;
            }
            if (recipe.Locked == true && !unlocked.Contains(recipe.Id.ToString()))
            {
                continue;
            }
            // The bitcoin farm and the water collector run by themselves; they are not started like a craft.
            if (recipe.Continuous == true)
            {
                continue;
            }
            var entry = new HideoutRecipe
            {
                Id = recipe.Id.ToString(),
                Area = area,
                Product = recipe.EndProduct.ToString(),
                Count = recipe.Count ?? 1,
                Seconds = recipe.ProductionTime ?? 0,
            };
            bool allowed = true;
            foreach (var need in recipe.Requirements ?? [])
            {
                switch (need.Type)
                {
                    case "Area":
                        allowed &= levels.GetValueOrDefault(need.AreaType ?? area) >= (need.RequiredLevel ?? 0);
                        break;
                    case "QuestComplete":
                        allowed &= need.QuestId != null && done.Contains(need.QuestId.Value.ToString());
                        break;
                    case "Item":
                        if (need.TemplateId != null && (need.Count ?? 0) > 0)
                        {
                            entry.Items.Add(new HideoutCost { Tpl = need.TemplateId.Value.ToString(), Count = need.Count ?? 0 });
                        }
                        break;
                    case "Tool":
                        if (need.TemplateId != null)
                        {
                            entry.Tools.Add(need.TemplateId.Value.ToString());
                        }
                        break;
                }
            }
            if (allowed)
            {
                result.Add(entry);
            }
        }
        return result;
    }

    private static void Advance(HideoutStore store, double seconds)
    {
        if (seconds > store.ClockSeconds)
        {
            store.ClockSeconds = seconds;
        }
        SettlePower(store);
    }

    // The game's own figures (hideout settings and the bitcoin recipe), counted on the world clock.
    private const double FuelPerSecond = 0.0013194444444444445;
    private const double SpeedWithoutPower = 0.07;
    private const double GpuBoost = 0.041225;
    private const double SecondsPerCoin = 145000;
    private const int CoinCap = 3;
    public const int Generator = 4, BoozeGenerator = 19, BitcoinFarm = 20, WaterCollector = 6, SolarPower = 18, ScavCase = 14;
    private const double FilterPerSecond = 0.00333;
    private const double SecondsPerWater = 19500;
    private const int WaterCap = 3;
    private const double SecondsOfMusclePain = 86400;
    public const int Gym = 23, CircleOfCultists = 27;

    private static HideoutPower PowerOf(HideoutStore store, string id)
    {
        if (!store.Power.TryGetValue(id, out var power))
        {
            power = new HideoutPower { Settled = store.ClockSeconds };
            store.Power[id] = power;
        }
        return power;
    }

    /// <summary>
    /// Brings every hideout's generator and bitcoin farm up to the clock: fuel burnt for as long
    /// as the generator ran, bitcoins mined for as long as there was power. They keep going while
    /// the character is elsewhere or asleep, as crafts do.
    /// </summary>
    private static void SettlePower(HideoutStore store)
    {
        // A hideout nobody has switched anything in yet still has crafts that wait for power.
        foreach (string id in store.Crafts.Keys.ToList())
        {
            PowerOf(store, id);
        }
        foreach (var pair in store.Power)
        {
            var power = pair.Value;
            double elapsed = store.ClockSeconds - power.Settled;
            power.Settled = store.ClockSeconds;
            if (elapsed <= 0)
            {
                continue;
            }
            double powered = 0;
            if (power.On)
            {
                double fuel = power.Fuel.Sum(can => can.Left);
                double rate = FuelPerSecond * (power.FuelFactor > 0 ? power.FuelFactor : 1);
                powered = Math.Min(elapsed, fuel / rate);
                double burn = powered * rate;
                foreach (var can in power.Fuel)
                {
                    double from = Math.Min(can.Left, burn);
                    can.Left -= from;
                    burn -= from;
                }
                if (power.Fuel.Sum(can => can.Left) <= 0.0001)
                {
                    power.On = false;
                }
            }
            // Time without power is time a craft did not move: its finish is put back by as much.
            double dark = elapsed - powered;
            if (dark > 0 && store.Crafts.TryGetValue(pair.Key, out var crafts))
            {
                foreach (var craft in crafts)
                {
                    double already = Math.Max(0, store.ClockSeconds - craft.Due);
                    if (NeedsPower(craft.Area) && already < dark)
                    {
                        craft.Due += dark - already;
                    }
                }
            }
            // The water collector needs no power, only a filter, and wears the filter out as it goes.
            if (power.WaterWorks && power.WaterFilter != null && power.WaterFilter.Left > 0 && power.Water < WaterCap)
            {
                double filtering = Math.Min(elapsed, power.WaterFilter.Left / FilterPerSecond);
                power.WaterFilter.Left -= filtering * FilterPerSecond;
                power.WaterProgress += filtering / SecondsPerWater;
                while (power.WaterProgress >= 1 && power.Water < WaterCap)
                {
                    power.Water++;
                    power.WaterProgress -= 1;
                }
                if (power.Water >= WaterCap)
                {
                    power.WaterProgress = 0;
                }
                if (power.WaterFilter.Left <= 0.0001)
                {
                    power.WaterFilter = null;
                }
            }
            if (powered > 0 && power.Gpus > 0 && power.Coins < CoinCap)
            {
                power.CoinProgress += powered * (1 + (power.Gpus - 1) * GpuBoost) / SecondsPerCoin;
                while (power.CoinProgress >= 1 && power.Coins < CoinCap)
                {
                    power.Coins++;
                    power.CoinProgress -= 1;
                }
                if (power.Coins >= CoinCap)
                {
                    power.CoinProgress = 0;
                }
            }
        }
    }

    public const int Lavatory = 2;
    /// <summary>Everything is electric but the Lavatory, where things are made by hand.</summary>
    private static bool NeedsPower(int area) => area != Lavatory;

    private static int FuelSlots(int generatorLevel) => generatorLevel <= 0 ? 0 : generatorLevel + 1;
    private static int GpuSlots(int farmLevel) => farmLevel switch { <= 0 => 0, 1 => 10, 2 => 25, _ => 50 };

    private PowerReport Report(HideoutStore store, string id, Dictionary<int, int> levels)
    {
        var power = PowerOf(store, id);
        // What the stations' levels mean for the count is noted here, where the levels are known.
        power.FuelFactor = levels.GetValueOrDefault(SolarPower) >= 1 ? 0.5 : 1;
        power.WaterWorks = levels.GetValueOrDefault(WaterCollector) >= 3;
        double fuel = power.Fuel.Sum(can => can.Left);
        bool powered = power.On && fuel > 0.0001;
        double rate = power.Gpus > 0 ? (1 + (power.Gpus - 1) * GpuBoost) / SecondsPerCoin : 0;
        var report = new PowerReport
        {
            On = power.On,
            Powered = powered,
            Fuel = power.Fuel,
            FuelSlots = FuelSlots(levels.GetValueOrDefault(Generator)),
            FuelLasts = fuel / (FuelPerSecond * power.FuelFactor),
            FuelFactor = power.FuelFactor,
            Gpus = power.Gpus,
            GpuSlots = GpuSlots(levels.GetValueOrDefault(BitcoinFarm)),
            Coins = power.Coins,
            CoinCap = CoinCap,
            NextCoinIn = powered && rate > 0 && power.Coins < CoinCap ? (1 - power.CoinProgress) / rate : -1,
            SlowFactor = 1,
            WaterFilter = power.WaterFilter,
            Water = power.Water,
            WaterCap = WaterCap,
            NextWaterIn = power.WaterWorks && power.WaterFilter != null && power.Water < WaterCap ? (1 - power.WaterProgress) * SecondsPerWater : -1,
            ScavRecipe = power.ScavRecipe,
            ScavDue = power.ScavDue,
            WorkoutIn = Math.Max(0, power.LastWorkout + SecondsOfMusclePain - store.ClockSeconds),
            CircleDue = power.CircleDue,
        };
        foreach (var recipe in hideoutTable.Production.ScavRecipes ?? [])
        {
            var offer = new ScavOffer { Id = recipe.Id.ToString(), Seconds = recipe.ProductionTime ?? 0 };
            foreach (var need in recipe.Requirements ?? [])
            {
                if (need.TemplateId != null && (need.Count ?? 0) > 0)
                {
                    offer.Items.Add(new HideoutCost { Tpl = need.TemplateId.Value.ToString(), Count = need.Count ?? 0 });
                }
            }
            offer.Common = Range(recipe.EndProducts?.Common);
            offer.Rare = Range(recipe.EndProducts?.Rare);
            offer.Superrare = Range(recipe.EndProducts?.Superrare);
            report.ScavOffers.Add(offer);
        }
        return report;
    }

    private static string Range(dynamic? range)
    {
        if (range == null)
        {
            return "0";
        }
        int least = (int)range.Min, most = (int)range.Max;
        return least == most ? least.ToString() : least + "-" + most;
    }

    /// <summary>The generator's switch and fuel, and the bitcoin farm's cards and coins. Items are settled on the client.</summary>
    public HideoutResponse Power(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, true, (pmc, store) =>
        {
            if (store.Active != request.Id)
            {
                return new HideoutResponse { Error = "Hideout '" + request.Id + "' is not the one in use; enter it first" };
            }
            Advance(store, request.Seconds);
            var levels = (pmc.Hideout?.Areas ?? []).ToDictionary(a => (int)a.Type, a => a.Level ?? 0);
            var power = PowerOf(store, request.Id);
            FuelCan? taken = null;
            int coins = 0, water = 0;
            var loot = new List<List<Item>>();
            switch ((request.Action ?? "").ToLowerInvariant())
            {
                case "on":
                    if (levels.GetValueOrDefault(Generator) < 1)
                    {
                        return new HideoutResponse { Error = "There is no generator here yet" };
                    }
                    if (power.Fuel.Sum(can => can.Left) <= 0.0001)
                    {
                        return new HideoutResponse { Error = "The generator has no fuel" };
                    }
                    power.On = true;
                    break;
                case "off":
                    power.On = false;
                    break;
                case "fuel":
                    if (power.Fuel.Count >= FuelSlots(levels.GetValueOrDefault(Generator)))
                    {
                        return new HideoutResponse { Error = "There is no room for another can" };
                    }
                    if (request.Left <= 0 || string.IsNullOrEmpty(request.Tpl))
                    {
                        return new HideoutResponse { Error = "That can is empty" };
                    }
                    power.Fuel.Add(new FuelCan { Tpl = request.Tpl, Left = request.Left, Max = Math.Max(request.Max, request.Left) });
                    break;
                case "unfuel":
                    if (request.Index < 0 || request.Index >= power.Fuel.Count)
                    {
                        return new HideoutResponse { Error = "There is no such can" };
                    }
                    taken = power.Fuel[request.Index];
                    power.Fuel.RemoveAt(request.Index);
                    if (power.Fuel.Sum(can => can.Left) <= 0.0001)
                    {
                        power.On = false;
                    }
                    break;
                case "gpu":
                    if (power.Gpus >= GpuSlots(levels.GetValueOrDefault(BitcoinFarm)))
                    {
                        return new HideoutResponse { Error = "There is no room for another card" };
                    }
                    power.Gpus++;
                    break;
                case "ungpu":
                    if (power.Gpus <= 0)
                    {
                        return new HideoutResponse { Error = "There are no cards in the farm" };
                    }
                    power.Gpus--;
                    break;
                case "coins":
                    coins = power.Coins;
                    power.Coins = 0;
                    break;
                case "filter":
                    if (levels.GetValueOrDefault(WaterCollector) < 3)
                    {
                        return new HideoutResponse { Error = "The water collector takes a filter from level 3" };
                    }
                    if (power.WaterFilter != null)
                    {
                        return new HideoutResponse { Error = "There is a filter in already" };
                    }
                    if (request.Left <= 0 || string.IsNullOrEmpty(request.Tpl))
                    {
                        return new HideoutResponse { Error = "That filter is used up" };
                    }
                    power.WaterFilter = new FuelCan { Tpl = request.Tpl, Left = request.Left, Max = Math.Max(request.Max, request.Left) };
                    break;
                case "unfilter":
                    if (power.WaterFilter == null)
                    {
                        return new HideoutResponse { Error = "There is no filter in" };
                    }
                    taken = power.WaterFilter;
                    power.WaterFilter = null;
                    break;
                case "water":
                    water = power.Water;
                    power.Water = 0;
                    break;
                case "scav":
                {
                    if (levels.GetValueOrDefault(ScavCase) < 1)
                    {
                        return new HideoutResponse { Error = "There is no scav case here yet" };
                    }
                    if (!string.IsNullOrEmpty(power.ScavRecipe))
                    {
                        return new HideoutResponse { Error = "The scavs are out already" };
                    }
                    var offer = (hideoutTable.Production.ScavRecipes ?? []).FirstOrDefault(r => r.Id.ToString() == request.Recipe);
                    if (offer == null)
                    {
                        return new HideoutResponse { Error = "No such offer" };
                    }
                    power.ScavRecipe = request.Recipe;
                    power.ScavDue = store.ClockSeconds + (offer.ProductionTime ?? 0);
                    break;
                }
                case "workout":
                    if (levels.GetValueOrDefault(Gym) < 1)
                    {
                        return new HideoutResponse { Error = "There is no gym here yet" };
                    }
                    if (power.LastWorkout + SecondsOfMusclePain > store.ClockSeconds)
                    {
                        return new HideoutResponse { Error = "Your muscles still ache from the last time" };
                    }
                    power.LastWorkout = store.ClockSeconds;
                    break;
                case "sacrifice":
                {
                    if (levels.GetValueOrDefault(CircleOfCultists) < 1)
                    {
                        return new HideoutResponse { Error = "There is no circle here yet" };
                    }
                    if (power.CircleDue > store.ClockSeconds)
                    {
                        return new HideoutResponse { Error = "The circle is still at work" };
                    }
                    // The game's own rite: it prices what lies in the circle, takes it, and lays what
                    // it gives in return in the same place. Only its waiting is kept here, on the
                    // world clock, so its own timer entry is taken out again.
                    var before = new HashSet<string>((pmc.Hideout?.Production ?? []).Keys.Select(k => k.ToString()));
                    circleOfCultistService.StartSacrifice(sessionId, pmc, new SPTarkov.Server.Core.Models.Eft.Hideout.HideoutCircleOfCultistProductionStartRequestData());
                    double wait = 43200;
                    foreach (var entry in (pmc.Hideout?.Production ?? []).ToList())
                    {
                        if (!before.Contains(entry.Key.ToString()))
                        {
                            wait = entry.Value?.ProductionTime ?? wait;
                            pmc.Hideout!.Production!.Remove(entry.Key);
                        }
                    }
                    power.CircleDue = store.ClockSeconds + wait;
                    break;
                }
                case "scavcollect":
                    if (string.IsNullOrEmpty(power.ScavRecipe) || power.ScavDue > store.ClockSeconds)
                    {
                        return new HideoutResponse { Error = "The scavs are not back yet" };
                    }
                    loot = scavCaseRewardGenerator.Generate(new MongoId(power.ScavRecipe)).ToList();
                    power.ScavRecipe = "";
                    power.ScavDue = 0;
                    break;
                default:
                    return new HideoutResponse { Error = "Unknown power action '" + request.Action + "'" };
            }
            Save(sessionId.ToString(), store);
            Console.WriteLine("[TheTrueSPT] Hideout '" + request.Id + "' power: " + request.Action + "; on " + power.On + ", fuel "
                              + power.Fuel.Sum(can => can.Left).ToString("0.#") + " in " + power.Fuel.Count + " can(s), cards " + power.Gpus + ", bitcoins waiting " + power.Coins + ".");
            var response = Describe(pmc, store);
            response.Power!.Taken = taken;
            response.Power.CoinsTaken = coins;
            response.Power.WaterTaken = water;
            response.Power.ScavLoot = loot;
            return response;
        });
    }

    public HideoutResponse Recipes(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, true, (pmc, store) =>
        {
            if (store.Active != request.Id)
            {
                return new HideoutResponse { Error = "Hideout '" + request.Id + "' is not the one in use; enter it first" };
            }
            var response = Describe(pmc, store);
            response.Recipes = RecipesFor(pmc);
            return response;
        });
    }

    /// <summary>Starts making something. Ingredients are settled on the client, as building is.</summary>
    public HideoutResponse Craft(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, true, (pmc, store) =>
        {
            if (store.Active != request.Id)
            {
                return new HideoutResponse { Error = "Hideout '" + request.Id + "' is not the one in use; enter it first" };
            }
            var recipe = RecipesFor(pmc).FirstOrDefault(r => r.Id == request.Recipe);
            if (recipe == null)
            {
                return new HideoutResponse { Error = "This hideout cannot make that" };
            }
            if (!store.Crafts.TryGetValue(request.Id, out var crafts))
            {
                crafts = [];
                store.Crafts[request.Id] = crafts;
            }
            if (crafts.Any(c => c.Area == recipe.Area))
            {
                return new HideoutResponse { Error = "That station is busy" };
            }
            Advance(store, request.Seconds);
            var power = PowerOf(store, request.Id);
            bool powered = power.On && power.Fuel.Sum(can => can.Left) > 0.0001;
            if (NeedsPower(recipe.Area) && !powered)
            {
                return new HideoutResponse { Error = "There is no power: start the generator first" };
            }
            double takes = recipe.Seconds;
            crafts.Add(new HideoutCraft
            {
                Recipe = recipe.Id,
                Area = recipe.Area,
                Product = recipe.Product,
                Count = recipe.Count,
                Started = store.ClockSeconds,
                Due = store.ClockSeconds + takes,
            });
            Save(sessionId.ToString(), store);
            Console.WriteLine("[TheTrueSPT] Craft started in hideout '" + request.Id + "' at station " + recipe.Area + ": " + recipe.Count + " x " + recipe.Product
                              + ", due in " + (takes / 3600.0).ToString("0.##") + " game hours.");
            return Describe(pmc, store);
        });
    }

    /// <summary>Hands over what a station has finished making. The client puts it in the stash.</summary>
    public HideoutResponse Collect(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, true, (pmc, store) =>
        {
            Advance(store, request.Seconds);
            var collected = new List<HideoutCraft>();
            if (store.Crafts.TryGetValue(request.Id, out var crafts))
            {
                collected = crafts.Where(c => c.Area == request.Area && c.Due <= store.ClockSeconds).ToList();
                crafts.RemoveAll(collected.Contains);
            }
            Save(sessionId.ToString(), store);
            var response = Describe(pmc, store);
            response.Collected = collected;
            if (collected.Count > 0)
            {
                Console.WriteLine("[TheTrueSPT] Collected " + collected.Count + " finished craft(s) from station " + request.Area + " in hideout '" + request.Id + "'.");
            }
            return response;
        });
    }

    /// <summary>Moves the world clock on to what the game reports. It never goes back.</summary>
    public HideoutResponse SetClock(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, false, (pmc, store) =>
        {
            if (request.Seconds > store.ClockSeconds)
            {
                Advance(store, request.Seconds);
                Save(sessionId.ToString(), store);
            }
            return Describe(pmc, store);
        });
    }

    /// <summary>Makes the named hideout the one in the profile, putting the current one away.</summary>
    public HideoutResponse Enter(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, true, (pmc, store) =>
        {
            if (!File.Exists(PathFor(sessionId.ToString())))
            {
                Save(sessionId.ToString(), store);
            }
            try
            {
                TakeDeliveries(sessionId, pmc);
            }
            catch (Exception e)
            {
                Console.WriteLine("[TheTrueSPT] Putting BTR deliveries in the stash failed: " + e.Message);
            }
            return Describe(pmc, store);
        });
    }

    /// <summary>Raises a station of the hideout in use by one level. Materials are settled on the client.</summary>
    public HideoutResponse Build(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, true, (pmc, store) =>
        {
            if (store.Active != request.Id)
            {
                return new HideoutResponse { Error = "Hideout '" + request.Id + "' is not the one in use; enter it first" };
            }
            var area = pmc.Hideout!.Areas?.FirstOrDefault(a => (int)a.Type == request.Area);
            if (area == null)
            {
                return new HideoutResponse { Error = "No station numbered " + request.Area };
            }
            int before = area.Level ?? 0;
            // The server's own "construction finished" step: level, bonuses, a bigger stash, skill points.
            hideoutController.UpgradeComplete(pmc, new HideoutUpgradeCompleteRequestData
            {
                Action = "HideoutUpgradeComplete",
                AreaType = area.Type,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            }, sessionId, eventOutputHolder.GetOutput(sessionId));
            if ((area.Level ?? 0) == before)
            {
                return new HideoutResponse { Error = area.Type + " is already at its highest level" };
            }
            Console.WriteLine("[TheTrueSPT] " + area.Type + " in hideout '" + request.Id + "' is now level " + area.Level + ".");
            return Describe(pmc, store);
        });
    }

    /// <summary>Replaces a hideout's stash contents with what the client holds.</summary>
    /// <summary>Replaces what a station keeps on it with what the client says is there now.</summary>
    public HideoutResponse SetShelf(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, true, (pmc, store) =>
        {
            string? shelfId = null;
            foreach (var shelf in pmc.Inventory?.HideoutAreaStashes ?? [])
            {
                if (shelf.Key.ToString() == request.Area.ToString())
                {
                    shelfId = shelf.Value.ToString();
                }
            }
            if (shelfId == null)
            {
                return new HideoutResponse { Error = "That station keeps nothing" };
            }
            // As with the stash: only what really hangs off this container is taken.
            var sent = request.Items ?? [];
            var reach = new HashSet<string> { shelfId };
            var items = new List<Item>();
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var item in sent)
                {
                    string id = item.Id.ToString();
                    if (id != shelfId && item.ParentId != null && reach.Contains(item.ParentId) && reach.Add(id))
                    {
                        items.Add(item);
                        grew = true;
                    }
                }
            }
            var gone = new HashSet<string>(Inside(pmc, [shelfId]).Select(x => x.Id.ToString()));
            pmc.Inventory!.Items!.RemoveAll(x => gone.Contains(x.Id.ToString()));
            var incoming = new HashSet<string>(items.Select(x => x.Id.ToString()));
            pmc.Inventory.Items.RemoveAll(x => incoming.Contains(x.Id.ToString()));
            pmc.Inventory.Items.AddRange(items);
            Console.WriteLine("[TheTrueSPT] Station " + request.Area + " now keeps " + items.Count + " item(s)" + (items.Count != sent.Count ? "; ignored " + (sent.Count - items.Count) + " not inside it." : "."));
            return Describe(pmc, store);
        });
    }

    public HideoutResponse SetStash(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, true, (pmc, store) =>
        {
            string? stashId = pmc.Inventory!.Stash?.ToString();
            if (stashId == null)
            {
                return new HideoutResponse { Error = "The profile has no stash" };
            }
            // Only what really hangs off the stash is accepted. The stash itself, or anything
            // attached to some other container, would damage the profile if it were written in.
            var sent = request.Items ?? [];
            var reach = new HashSet<string> { stashId };
            var items = new List<Item>();
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var item in sent)
                {
                    string id = item.Id.ToString();
                    if (id != stashId && item.ParentId != null && reach.Contains(item.ParentId) && reach.Add(id))
                    {
                        items.Add(item);
                        grew = true;
                    }
                }
            }
            if (items.Count != sent.Count)
            {
                Console.WriteLine("[TheTrueSPT] Stash of hideout '" + request.Id + "': ignored " + (sent.Count - items.Count) + " item(s) that are not inside the stash.");
            }
            if (store.Active == request.Id)
            {
                var gone = new HashSet<string>(Inside(pmc, [stashId]).Select(x => x.Id.ToString()));
                pmc.Inventory.Items!.RemoveAll(x => gone.Contains(x.Id.ToString()));
                // An item cannot be in the stash and somewhere else in the profile at once.
                var incoming = new HashSet<string>(items.Select(x => x.Id.ToString()));
                pmc.Inventory.Items.RemoveAll(x => incoming.Contains(x.Id.ToString()));
                pmc.Inventory.Items.AddRange(items);
            }
            else
            {
                if (!store.Away.TryGetValue(request.Id, out var away))
                {
                    away = Fresh(pmc);
                    store.Away[request.Id] = away;
                }
                var areaStashes = new HashSet<string>((away.AreaStashes ?? []).Values.Select(x => x.ToString()));
                // Keep what sits in the stations' own storage; replace only what is in the stash.
                var keep = new List<Item>();
                var kept = new HashSet<string>(areaStashes);
                bool added = true;
                while (added)
                {
                    added = false;
                    foreach (var item in away.Items)
                    {
                        if (item.ParentId != null && kept.Contains(item.ParentId) && kept.Add(item.Id.ToString()))
                        {
                            keep.Add(item);
                            added = true;
                        }
                    }
                }
                keep.AddRange(items);
                away.Items = keep;
                Save(sessionId.ToString(), store);
            }
            Console.WriteLine("[TheTrueSPT] Stash of hideout '" + request.Id + "' now holds " + items.Count + " item(s).");
            return Describe(pmc, store);
        });
    }
}

/// <summary>
/// The hideout rules this mod plays by: a station needs only its materials, and is built at once.
/// Requirements for other stations, trader loyalty and skills are taken out of every level, and
/// every construction time is set to zero. The client receives these along with the rest of the
/// hideout data, so the game's own screens show the same.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class HideoutRulesLoader(HideoutTable hideoutTable, LocationTable locationTable) : IOnLoad
{
    /// <summary>About ten weeks of real time.</summary>
    private const int EndlessMinutes = 100000;

    private static readonly HashSet<string> Dropped = new(StringComparer.OrdinalIgnoreCase) { "Area", "TraderLoyalty", "Skill" };

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            int removed = 0, timers = 0;
            foreach (var area in hideoutTable.Areas)
            {
                foreach (var stage in area.Stages?.Values ?? Enumerable.Empty<Stage>())
                {
                    if (stage.Requirements != null)
                    {
                        removed += stage.Requirements.RemoveAll(r => r.Type != null && Dropped.Contains(r.Type));
                    }
                    if ((stage.ConstructionTime ?? 0) > 0)
                    {
                        stage.ConstructionTime = 0;
                        timers++;
                    }
                }
            }
            // A raid lasts until the player ends it. The limit is set far beyond any sitting.
            int maps = 0;
            foreach (var property in typeof(LocationTable).GetProperties())
            {
                if (property.GetValue(locationTable) is SPTarkov.Server.Core.Models.Eft.Common.Location location && location.Base != null)
                {
                    location.Base.EscapeTimeLimit = EndlessMinutes;
                    location.Base.EscapeTimeLimitCoop = EndlessMinutes;
                    location.Base.EscapeTimeLimitPVE = EndlessMinutes;
                    maps++;
                }
            }
            Console.WriteLine("[TheTrueSPT] Raid time limit lifted on " + maps + " maps.");
            Console.WriteLine("[TheTrueSPT] Hideout rules: removed " + removed + " station, loyalty and skill requirements; made "
                              + timers + " station levels build at once.");
        }
        catch (Exception e)
        {
            Console.WriteLine("[TheTrueSPT] Could not change the hideout rules: " + e);
        }
        return Task.CompletedTask;
    }
}
