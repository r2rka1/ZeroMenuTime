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

namespace TradersInRaid.Server;

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

public sealed class HideoutStore
{
    /// <summary>The hideout whose state is in the profile right now.</summary>
    public string Active { get; set; } = HideoutsService.First;
    /// <summary>The others, put away until they are entered.</summary>
    public Dictionary<string, HideoutSave> Away { get; set; } = [];
}

public sealed class HideoutRequest : IRequestData
{
    public string Id { get; set; } = "";
    /// <summary>For building: the station's number, as the game counts them.</summary>
    public int Area { get; set; } = -1;
    /// <summary>For the stash: everything in it, as the client holds it.</summary>
    public List<Item>? Items { get; set; }
}

public sealed class HideoutResponse
{
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

    private readonly object _gate = new();

    private static string PathFor(string sessionId)
    {
        return System.IO.Path.Combine("user", "profileData", sessionId, "tradersinraid.hideouts.json");
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
            StashTemplate = StashRoot(pmc)?.Template.ToString(),
            StashId = pmc.Inventory?.Stash?.ToString(),
            Stash = Inside(pmc, pmc.Inventory?.Stash != null ? [pmc.Inventory.Stash.Value.ToString()] : []),
        };
        foreach (var area in pmc.Hideout?.Areas ?? [])
        {
            response.Levels[(int)area.Type] = area.Level ?? 0;
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
            Console.WriteLine("[TradersInRaid] Hideout request failed: " + e);
            return new HideoutResponse { Error = e.GetType().Name + ": " + e.Message };
        }
    }

    public HideoutResponse List(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, false, Describe);
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
                    Console.WriteLine("[TradersInRaid] Hideout '" + request.Id + "' entered for the first time; starting it empty.");
                }
                Apply(pmc, incoming);
                string was = store.Active;
                store.Active = request.Id;
                Save(sessionId.ToString(), store);
                Console.WriteLine("[TradersInRaid] Hideout in use changed from '" + was + "' to '" + request.Id + "'.");
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
            Console.WriteLine("[TradersInRaid] " + area.Type + " in hideout '" + request.Id + "' is now level " + area.Level + ".");
            return Describe(pmc, store);
        });
    }

    /// <summary>Replaces a hideout's stash contents with what the client holds.</summary>
    public HideoutResponse SetStash(MongoId sessionId, HideoutRequest request)
    {
        return Guarded(sessionId, request, true, (pmc, store) =>
        {
            var items = request.Items ?? [];
            string? stashId = pmc.Inventory!.Stash?.ToString();
            if (stashId == null)
            {
                return new HideoutResponse { Error = "The profile has no stash" };
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
            Console.WriteLine("[TradersInRaid] Stash of hideout '" + request.Id + "' now holds " + items.Count + " item(s).");
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
public sealed class HideoutRulesLoader(HideoutTable hideoutTable) : IOnLoad
{
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
            Console.WriteLine("[TradersInRaid] Hideout rules: removed " + removed + " station, loyalty and skill requirements; made "
                              + timers + " station levels build at once.");
        }
        catch (Exception e)
        {
            Console.WriteLine("[TradersInRaid] Could not change the hideout rules: " + e);
        }
        return Task.CompletedTask;
    }
}
