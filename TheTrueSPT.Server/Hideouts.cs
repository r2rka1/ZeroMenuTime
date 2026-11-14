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

public sealed class HideoutStore
{
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
}

/// <summary>One kind of material a station level needs.</summary>
public sealed class HideoutCost
{
    public string Tpl { get; set; } = "";
    public int Count { get; set; }
}

public sealed class HideoutResponse
{
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
    JsonUtil jsonUtil)
{
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
                return store;
            }
        }
        // First use: the hideout the character already has is the Streets one.
        return new HideoutStore();
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
                return work(pmc, Load(sessionId.ToString()));
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
            crafts.Add(new HideoutCraft
            {
                Recipe = recipe.Id,
                Area = recipe.Area,
                Product = recipe.Product,
                Count = recipe.Count,
                Started = store.ClockSeconds,
                Due = store.ClockSeconds + recipe.Seconds,
            });
            Save(sessionId.ToString(), store);
            Console.WriteLine("[TheTrueSPT] Craft started in hideout '" + request.Id + "' at station " + recipe.Area + ": " + recipe.Count + " x " + recipe.Product
                              + ", due in " + (recipe.Seconds / 3600.0).ToString("0.##") + " game hours.");
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
                store.ClockSeconds = request.Seconds;
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
            if (store.Active != request.Id)
            {
                store.Away[store.Active] = Capture(pmc);
                HideoutSave incoming;
                if (!store.Away.Remove(request.Id, out incoming!))
                {
                    incoming = Fresh(pmc);
                    Console.WriteLine("[TheTrueSPT] Hideout '" + request.Id + "' entered for the first time; starting it empty.");
                }
                Apply(pmc, incoming);
                string was = store.Active;
                store.Active = request.Id;
                Save(sessionId.ToString(), store);
                Console.WriteLine("[TheTrueSPT] Hideout in use changed from '" + was + "' to '" + request.Id + "'.");
            }
            else if (!File.Exists(PathFor(sessionId.ToString())))
            {
                Save(sessionId.ToString(), store);
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
