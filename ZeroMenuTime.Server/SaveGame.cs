using System.Reflection;
using System.Runtime.CompilerServices;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Utils;

namespace ZeroMenuTime.Server;

public sealed class SaveRequest : IRequestData
{
    /// <summary>The hideout whose bed the character is saving at.</summary>
    public string Hideout { get; set; } = "";
    /// <summary>The character as the game holds it now: what it would send if the raid ended.</summary>
    public PmcData? Profile { get; set; }
    /// <summary>For "status": the client has acted on the wake-up-at-the-bed flag, clear it.</summary>
    public bool Consume { get; set; }
    /// <summary>For "trader": the trader's extract the player chose "Save and go to menu" at.</summary>
    public string Exit { get; set; } = "";
    /// <summary>For the testing route "travel": the map (Vagabond's name) to continue on.</summary>
    public string Map { get; set; } = "";
    /// <summary>For "death/mode": what a death takes. "keep", "tarkov" or "everything".</summary>
    public string Mode { get; set; } = "";
}

public sealed class SaveResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    /// <summary>Is there a save at all?</summary>
    public bool Has { get; set; }
    public string Hideout { get; set; } = "";
    /// <summary>For a save made at a trader: the trader's extract (VGB_EXT_...); empty for a bed save.</summary>
    public string Exit { get; set; } = "";
    public double Clock { get; set; }
    /// <summary>The next raid should begin at the bed of that hideout: the character died, or the game stopped without the raid ending.</summary>
    public bool Resume { get; set; }
}

/// <summary>What is kept beside the saved character.</summary>
public sealed class SaveMeta
{
    /// <summary>The hideout whose bed the save was made at; empty for a save made at a trader.</summary>
    public string Hideout { get; set; } = "";
    /// <summary>For a save made at a trader: the map and the trader's extract, where the character continues.</summary>
    public string Map { get; set; } = "";
    public string Exit { get; set; } = "";
    /// <summary>A trader save was put back after a death; until the next raid starts, the place is said again.</summary>
    public bool Respawn { get; set; }
    public string SavedAtUtc { get; set; } = "";
    public bool Resume { get; set; }
}

/// <summary>
/// Saving at a bed. A save is the character as it is at that moment (gear, health, skills, quests,
/// experience), the three hideouts (levels, stashes, crafts) and the world clock. Making one writes
/// the character into the profile without ending the raid, so a crash loses nothing since the save;
/// dying puts the save back, and the next raid begins at that bed.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class SaveGameService(SaveServer saveServer, LocationLifecycleService raids, JsonUtil jsonUtil)
{
    public static SaveGameService? Instance;

    private readonly object _gate = new();

    /// <summary>Players who chose "Save and go to menu" at a trader in the raid now ending, and at which extract.</summary>
    private readonly Dictionary<string, string> _traderSaves = new();

    /// <summary>The client says the raid is about to end by "Save and go to menu" at a trader.</summary>
    public SaveResponse TraderSaveComing(MongoId sessionId, SaveRequest request)
    {
        string exit = request.Exit ?? "";
        if (!exit.StartsWith("VGB_EXT_", StringComparison.OrdinalIgnoreCase))
        {
            return new SaveResponse { Error = "Not a trader's extract: " + exit };
        }
        lock (_gate)
        {
            _traderSaves[sessionId.ToString()] = exit;
        }
        return new SaveResponse { Ok = true };
    }

    private static string Dir(string sessionId) => System.IO.Path.Combine("user", "profileData", sessionId);
    private static string CharacterFile(string sessionId) => System.IO.Path.Combine(Dir(sessionId), "zeromenutime.save.character.json");
    private static string HideoutsFile(string sessionId) => System.IO.Path.Combine(Dir(sessionId), "zeromenutime.save.hideouts.json");
    private static string MetaFile(string sessionId) => System.IO.Path.Combine(Dir(sessionId), "zeromenutime.save.meta.json");
    private static string LiveHideoutsFile(string sessionId) => System.IO.Path.Combine(Dir(sessionId), "zeromenutime.hideouts.json");
    private static string DeathModeFile(string sessionId) => System.IO.Path.Combine(Dir(sessionId), "zeromenutime.deathmode.txt");

    /// <summary>The client's choice of what a death takes; kept on disk so a restarted server still knows it.</summary>
    public SaveResponse SetDeathMode(MongoId sessionId, SaveRequest request)
    {
        string mode = (request.Mode ?? "").ToLowerInvariant();
        if (mode != "keep" && mode != "tarkov" && mode != "everything")
        {
            return new SaveResponse { Error = "Unknown mode: " + request.Mode };
        }
        try
        {
            Directory.CreateDirectory(Dir(sessionId.ToString()));
            File.WriteAllText(DeathModeFile(sessionId.ToString()), mode);
            return new SaveResponse { Ok = true };
        }
        catch (Exception e)
        {
            return new SaveResponse { Error = e.Message };
        }
    }

    private static string DeathMode(string sessionId)
    {
        try
        {
            string path = DeathModeFile(sessionId);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "keep";
        }
        catch
        {
            return "keep";
        }
    }

    private static readonly HashSet<string> KeptSlots = new(StringComparer.OrdinalIgnoreCase) { "Pockets", "SecuredContainer", "Dogtag", "ArmBand" };

    private static List<Item> Below(List<Item> items, string parentId)
    {
        var found = new List<Item>();
        var ids = new HashSet<string> { parentId };
        bool added = true;
        while (added)
        {
            added = false;
            foreach (var item in items)
            {
                if (item.ParentId != null && ids.Contains(item.ParentId) && ids.Add(item.Id.ToString()))
                {
                    found.Add(item);
                    added = true;
                }
            }
        }
        return found;
    }

    /// <summary>
    /// "Lose everything": of what the character wears, only the pockets and the secure container
    /// themselves stay (the game needs them), empty.
    /// </summary>
    private static int StripAll(PmcData pmc)
    {
        var items = pmc.Inventory?.Items;
        string? equipment = pmc.Inventory?.Equipment?.ToString();
        if (items == null || equipment == null)
        {
            return 0;
        }
        var lost = new HashSet<string>();
        foreach (var worn in items.Where(x => x.ParentId == equipment).ToList())
        {
            if (!KeptSlots.Contains(worn.SlotId ?? ""))
            {
                lost.Add(worn.Id.ToString());
            }
            foreach (var inside in Below(items, worn.Id.ToString()))
            {
                lost.Add(inside.Id.ToString());
            }
        }
        items.RemoveAll(x => lost.Contains(x.Id.ToString()));
        return lost.Count;
    }

    /// <summary>
    /// The save is put back after a death, but what the character wore and carried is what the game
    /// left after that death, not what it had when saving.
    /// </summary>
    private static void KeepTheLoss(PmcData dead, PmcData saved)
    {
        var savedItems = saved.Inventory?.Items;
        var deadItems = dead.Inventory?.Items;
        string? savedEquipment = saved.Inventory?.Equipment?.ToString();
        string? deadEquipment = dead.Inventory?.Equipment?.ToString();
        if (savedItems == null || deadItems == null || savedEquipment == null || deadEquipment == null)
        {
            return;
        }
        var gone = new HashSet<string>(Below(savedItems, savedEquipment).Select(x => x.Id.ToString()));
        savedItems.RemoveAll(x => gone.Contains(x.Id.ToString()));
        var have = new HashSet<string>(savedItems.Select(x => x.Id.ToString()));
        foreach (var item in Below(deadItems, deadEquipment))
        {
            if (have.Contains(item.Id.ToString()))
            {
                continue;
            }
            savedItems.Add(item.ParentId == deadEquipment ? item with { ParentId = savedEquipment } : item);
        }
    }

    private SaveMeta? ReadMeta(string sessionId)
    {
        string path = MetaFile(sessionId);
        return File.Exists(path) && File.Exists(CharacterFile(sessionId)) ? jsonUtil.Deserialize<SaveMeta>(File.ReadAllText(path)) : null;
    }

    private void WriteMeta(string sessionId, SaveMeta meta)
    {
        File.WriteAllText(MetaFile(sessionId), jsonUtil.Serialize(meta, true));
    }

    /// <summary>The trader extract nearest each hideout's door: where Vagabond is told the character is.</summary>
    private static (string Map, string Exit) Place(string hideout)
    {
        return hideout switch
        {
            "customs" => ("Customs", "VGB_EXT_SKIER"),
            "shoreline" => ("Shoreline", "VGB_EXT_PEACEKEEPER"),
            _ => ("Streets", "VGB_EXT_PRAPOR"),
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TellVagabond(string sessionId, string hideout)
    {
        var state = Vagabond.Common.Api.Api.GetState(sessionId);
        if (state == null)
        {
            return;
        }
        var (map, exit) = Place(hideout);
        state.CurrentMap = map;
        state.LastExit = exit;
        state.TransitState = null;
        Vagabond.Common.Api.Api.SaveState(sessionId, state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    /// <summary>Testing aid: the next raid starts on this map, at this extract or transit.</summary>
    public SaveResponse Travel(MongoId sessionId, SaveRequest request)
    {
        try
        {
            TellVagabondExit(sessionId.ToString(), request.Map, request.Exit);
            Console.WriteLine("[ZeroMenuTime] Test travel: next raid on " + request.Map + " at " + request.Exit);
            return new SaveResponse { Ok = true, Exit = request.Exit };
        }
        catch (Exception e)
        {
            return new SaveResponse { Error = e.Message };
        }
    }

    /// <summary>
    /// What the character wore and carried at its last save, as a flat list of items: for the
    /// "keep items" death setting when a dead player comes back on a new map in co-op. The secure
    /// container, armband, knife, dog tag and pockets are left out; the player still has those.
    /// </summary>
    public SavedGear SavedGearOf(MongoId sessionId)
    {
        try
        {
            string id = sessionId.ToString();
            lock (_gate)
            {
                if (!File.Exists(CharacterFile(id)))
                {
                    return new SavedGear { Ok = true };
                }
                var saved = jsonUtil.Deserialize<PmcData>(File.ReadAllText(CharacterFile(id)));
                var items = saved?.Inventory?.Items;
                string? equipment = saved?.Inventory?.Equipment?.ToString();
                var gear = new SavedGear { Ok = true };
                if (items == null || equipment == null)
                {
                    return gear;
                }
                var notThese = new HashSet<string> { "SecuredContainer", "ArmBand", "Scabbard", "Dogtag", "Pockets" };
                foreach (var worn in items.Where(x => x.ParentId == equipment && !notThese.Contains(x.SlotId ?? "")).ToList())
                {
                    // Each thing worn becomes the top of its own little tree, with what was in or on it.
                    gear.Items.Add(worn with { ParentId = null, SlotId = null });
                    gear.Items.AddRange(Below(items, worn.Id.ToString()));
                    gear.Roots.Add(worn.Id.ToString());
                }
                return gear;
            }
        }
        catch (Exception e)
        {
            return new SavedGear { Error = e.Message };
        }
    }

    /// <summary>
    /// A player joins someone else's world: as far as Vagabond goes they are now where that world's
    /// owner is (same map, same way in), so they come in beside the others and their own next start
    /// is from there.
    /// </summary>
    public SaveResponse GoWhereTheOwnerIs(MongoId sessionId, string owner)
    {
        try
        {
            if (string.IsNullOrEmpty(owner) || owner == sessionId.ToString())
            {
                return new SaveResponse { Ok = true };
            }
            return CopyPlace(sessionId.ToString(), owner);
        }
        catch (Exception e)
        {
            return new SaveResponse { Error = e.Message };
        }
    }

    /// <summary>
    /// A guest who went through a transit with the group: their place is made the owner's again,
    /// the transit under way included, so that their game loads the map the group is going to.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public SaveResponse FollowTheOwner(MongoId sessionId, string owner)
    {
        try
        {
            if (string.IsNullOrEmpty(owner) || owner == sessionId.ToString())
            {
                return new SaveResponse { Ok = true };
            }
            return CopyPlace(sessionId.ToString(), owner, true);
        }
        catch (Exception e)
        {
            return new SaveResponse { Error = e.Message };
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SaveResponse CopyPlace(string guest, string owner, bool following = false)
    {
        var theirs = Vagabond.Common.Api.Api.GetState(owner);
        var mine = Vagabond.Common.Api.Api.GetState(guest);
        if (theirs == null || mine == null)
        {
            return new SaveResponse { Error = "no Vagabond state" };
        }
        if (following && mine.CurrentMap == theirs.CurrentMap && mine.LastExit == theirs.LastExit && Equals(mine.TransitState, theirs.TransitState))
        {
            return new SaveResponse { Ok = true, Exit = theirs.LastExit ?? "" };
        }
        mine.CurrentMap = theirs.CurrentMap;
        mine.LastExit = theirs.LastExit;
        mine.TransitState = following ? theirs.TransitState : null;
        Vagabond.Common.Api.Api.SaveState(guest, mine);
        Console.WriteLine("[ZeroMenuTime] " + guest + (following ? " follows " : " joins the world of ") + owner + ": now on " + theirs.CurrentMap + " by " + theirs.LastExit);
        return new SaveResponse { Ok = true, Exit = theirs.LastExit ?? "" };
    }

    private static void TellVagabondExit(string sessionId, string map, string exit)
    {
        var state = Vagabond.Common.Api.Api.GetState(sessionId);
        if (state == null)
        {
            return;
        }
        state.CurrentMap = map;
        state.LastExit = exit;
        state.TransitState = null;
        Vagabond.Common.Api.Api.SaveState(sessionId, state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? VagabondMap(string sessionId)
    {
        return Vagabond.Common.Api.Api.GetState(sessionId)?.CurrentMap;
    }

    /// <summary>Where the character continues from a save: the bed of its hideout, or the trader it was made at.</summary>
    private static void PlaceAt(string sessionId, SaveMeta meta)
    {
        if (!string.IsNullOrEmpty(meta.Hideout))
        {
            PlaceAt(sessionId, meta.Hideout);
            return;
        }
        if (string.IsNullOrEmpty(meta.Map) || string.IsNullOrEmpty(meta.Exit))
        {
            return;
        }
        try
        {
            TellVagabondExit(sessionId, meta.Map, meta.Exit);
        }
        catch (Exception e)
        {
            Console.WriteLine("[ZeroMenuTime] Could not tell Vagabond where the trader save was made: " + e.Message);
        }
    }

    private static void PlaceAt(string sessionId, string hideout)
    {
        try
        {
            TellVagabond(sessionId, hideout);
        }
        catch (Exception e)
        {
            // Vagabond not installed, or its API changed: the character stays where the game has it.
            Console.WriteLine("[ZeroMenuTime] Could not tell Vagabond where the bed is: " + e.Message);
        }
    }

    /// <summary>
    /// The first save of a new game: the character as just made, with the next raid set to begin
    /// inside the hideout by the given door.
    /// </summary>
    public void StartFresh(string sessionId, PmcData pmc, string hideout)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Dir(sessionId));
            File.WriteAllText(CharacterFile(sessionId), jsonUtil.Serialize(pmc));
            WriteMeta(sessionId, new SaveMeta { Hideout = hideout, SavedAtUtc = DateTime.UtcNow.ToString("u"), Resume = true });
            PlaceAt(sessionId, hideout);
            Console.WriteLine("[ZeroMenuTime] New game: the first raid begins in the hideout, by its '" + hideout + "' door.");
        }
    }

    private SaveResponse Describe(string sessionId, SaveMeta? meta)
    {
        return new SaveResponse { Ok = true, Has = meta != null, Hideout = meta?.Hideout ?? "", Exit = meta?.Exit ?? "", Resume = meta?.Resume ?? false };
    }

    public SaveResponse Save(MongoId sessionId, SaveRequest request)
    {
        try
        {
            string id = sessionId.ToString();
            string hideout = (request.Hideout ?? "").ToLowerInvariant();
            // A save can also be made standing at a trader, without ending the raid.
            string exit = request.Exit ?? "";
            bool atTrader = hideout.Length == 0 && exit.StartsWith("VGB_EXT_", StringComparison.OrdinalIgnoreCase);
            if ((!atTrader && !HideoutsService.Known.Contains(hideout)) || request.Profile == null)
            {
                return new SaveResponse { Error = "A save needs a hideout or a trader, and the character" };
            }
            lock (_gate)
            {
                var full = saveServer.GetProfile(sessionId);
                var pmc = full?.CharacterData?.PmcData;
                if (pmc == null)
                {
                    return new SaveResponse { Error = "No profile" };
                }
                // The server's own "a raid ended and the character survived" step for the character,
                // without the raid ending: gear, health, skills, quests, experience.
                var step = typeof(LocationLifecycleService).GetMethod("HandlePostRaidPmc", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                           ?? throw new MissingMethodException("HandlePostRaidPmc");
                step.Invoke(raids, [
                    sessionId, full, full!.CharacterData!.ScavData, false, true, false,
                    new EndLocalRaidRequestData
                    {
                        Results = new EndRaidResult { Profile = request.Profile, Result = ExitStatus.SURVIVED, ExitName = "save" },
                        LostInsuredItems = [],
                    },
                    "save",
                ]);

                Directory.CreateDirectory(Dir(id));
                // Keep the save before this one, in case this one turns out bad.
                foreach (string file in new[] { CharacterFile(id), HideoutsFile(id) })
                {
                    if (File.Exists(file))
                    {
                        File.Copy(file, file + ".previous", true);
                    }
                }
                File.WriteAllText(CharacterFile(id), jsonUtil.Serialize(full.CharacterData.PmcData));
                if (File.Exists(LiveHideoutsFile(id)))
                {
                    File.Copy(LiveHideoutsFile(id), HideoutsFile(id), true);
                }
                // Until the raid ends in some ordinary way, the bed is where the next raid begins.
                SaveMeta meta;
                if (atTrader)
                {
                    string map = "";
                    try
                    {
                        map = VagabondMap(id) ?? "";
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[ZeroMenuTime] Could not ask Vagabond which map the trader save was made on: " + e.Message);
                    }
                    meta = new SaveMeta { Hideout = "", Map = map, Exit = exit, SavedAtUtc = DateTime.UtcNow.ToString("u"), Resume = true };
                    WriteMeta(id, meta);
                    PlaceAt(id, meta);
                    Console.WriteLine("[ZeroMenuTime] Game saved at the trader (" + exit + " on " + map + "), the raid goes on.");
                    return Describe(id, meta);
                }
                meta = new SaveMeta { Hideout = hideout, SavedAtUtc = DateTime.UtcNow.ToString("u"), Resume = true };
                WriteMeta(id, meta);
                PlaceAt(id, hideout);
                Console.WriteLine("[ZeroMenuTime] Game saved at the bed of the '" + hideout + "' hideout.");
                return Describe(id, meta);
            }
        }
        catch (Exception e)
        {
            var real = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
            Console.WriteLine("[ZeroMenuTime] Saving failed: " + real);
            return new SaveResponse { Error = real.GetType().Name + ": " + real.Message };
        }
    }

    /// <summary>
    /// "Save and go to menu" at a trader ends the raid at the trader's extract, and the server has
    /// just written the character as it left. That is kept as the save, with the hideout and the
    /// clock as they are now, so dying later puts it back and the character continues at that trader.
    /// </summary>
    private void SaveAtTrader(MongoId sessionId, string id, string exit)
    {
        var full = saveServer.GetProfile(sessionId);
        var pmc = full?.CharacterData?.PmcData;
        if (pmc == null)
        {
            return;
        }
        string map = "";
        try
        {
            map = VagabondMap(id) ?? "";
        }
        catch (Exception e)
        {
            Console.WriteLine("[ZeroMenuTime] Could not ask Vagabond which map the trader save was made on: " + e.Message);
        }
        Directory.CreateDirectory(Dir(id));
        foreach (string file in new[] { CharacterFile(id), HideoutsFile(id) })
        {
            if (File.Exists(file))
            {
                File.Copy(file, file + ".previous", true);
            }
        }
        File.WriteAllText(CharacterFile(id), jsonUtil.Serialize(pmc));
        if (File.Exists(LiveHideoutsFile(id)))
        {
            File.Copy(LiveHideoutsFile(id), HideoutsFile(id), true);
        }
        WriteMeta(id, new SaveMeta { Hideout = "", Map = map, Exit = exit, SavedAtUtc = DateTime.UtcNow.ToString("u") });
        Console.WriteLine("[ZeroMenuTime] Game saved at the trader (" + exit + " on " + map + ").");
    }

    public SaveResponse Status(MongoId sessionId, SaveRequest request)
    {
        try
        {
            string id = sessionId.ToString();
            lock (_gate)
            {
                var meta = ReadMeta(id);
                var response = Describe(id, meta);
                if (meta != null && (meta.Resume || meta.Respawn))
                {
                    if (request.Consume)
                    {
                        meta.Resume = false;
                        meta.Respawn = false;
                        WriteMeta(id, meta);
                    }
                    else
                    {
                        // Other mods settle where the character is after a death; say it again.
                        PlaceAt(id, meta);
                    }
                }
                return response;
            }
        }
        catch (Exception e)
        {
            return new SaveResponse { Error = e.GetType().Name + ": " + e.Message };
        }
    }

    /// <summary>
    /// Continue was chosen and Vagabond says the character is on no map at all (seen in the field:
    /// after the game was closed in a raid on a heavily modded install). Rather than doing nothing,
    /// the character is put where the last save was made, or at the Customs hideout door if there
    /// never was one. Says which map that is.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public SaveResponse Recover(MongoId sessionId)
    {
        try
        {
            string id = sessionId.ToString();
            lock (_gate)
            {
                string before = VagabondMap(id) ?? "";
                if (before.Length > 0)
                {
                    return new SaveResponse { Ok = true, Exit = before };
                }
                var meta = ReadMeta(id);
                if (meta != null && (!string.IsNullOrEmpty(meta.Hideout) || (!string.IsNullOrEmpty(meta.Map) && !string.IsNullOrEmpty(meta.Exit))))
                {
                    if (!string.IsNullOrEmpty(meta.Hideout))
                    {
                        // At the bed, inside, as after any raid that did not end properly.
                        meta.Resume = true;
                        WriteMeta(id, meta);
                    }
                    PlaceAt(id, meta);
                }
                else
                {
                    PlaceAt(id, "customs");
                }
                string after = VagabondMap(id) ?? "";
                Console.WriteLine("[ZeroMenuTime] The character was on no map; put back " + (meta != null ? "at the last save" : "at the Customs hideout door") + ": " + (after.Length > 0 ? after : "(Vagabond still says nowhere)"));
                return new SaveResponse { Ok = after.Length > 0, Exit = after, Has = meta != null, Hideout = meta?.Hideout ?? "", Error = after.Length > 0 ? null : "Vagabond still has no map for the character" };
            }
        }
        catch (Exception e)
        {
            return new SaveResponse { Error = e.GetType().Name + ": " + e.Message };
        }
    }

    /// <summary>A raid has ended. After a death the save is put back; otherwise the bed stops being where the next raid begins.</summary>
    public void RaidEnded(MongoId sessionId, bool died)
    {
        try
        {
            string id = sessionId.ToString();
            lock (_gate)
            {
                // Whatever happened, a "Save and go to menu" from this raid is settled now.
                bool atTrader = _traderSaves.Remove(id, out string? traderExit);
                if (!died && atTrader && traderExit != null)
                {
                    SaveAtTrader(sessionId, id, traderExit);
                    return;
                }
                var meta = ReadMeta(id);
                string mode = died ? DeathMode(id) : "keep";
                if (meta == null)
                {
                    if (mode == "everything")
                    {
                        var pmc = saveServer.GetProfile(sessionId)?.CharacterData?.PmcData;
                        int stripped = pmc != null ? StripAll(pmc) : 0;
                        Console.WriteLine("[ZeroMenuTime] The character died; everything carried is lost (" + stripped + " item(s)).");
                    }
                    return;
                }
                if (!died)
                {
                    if (meta.Resume || meta.Respawn)
                    {
                        meta.Resume = false;
                        meta.Respawn = false;
                        WriteMeta(id, meta);
                    }
                    return;
                }
                var full = saveServer.GetProfile(sessionId);
                var saved = jsonUtil.Deserialize<PmcData>(File.ReadAllText(CharacterFile(id)));
                if (full?.CharacterData == null || saved == null)
                {
                    Console.WriteLine("[ZeroMenuTime] The save could not be read; the death stands as the game has it.");
                    return;
                }
                var dead = full.CharacterData.PmcData;
                full.CharacterData.PmcData = saved;
                if (mode != "keep" && dead != null)
                {
                    // The save is put back, but not the gear it had: what was on the character is lost.
                    KeepTheLoss(dead, saved);
                    int stripped = mode == "everything" ? StripAll(saved) : 0;
                    Console.WriteLine("[ZeroMenuTime] Death mode '" + mode + "': the gear lost in the death stays lost"
                        + (mode == "everything" ? ", the secure container's contents too (" + stripped + " more item(s))" : "") + ".");
                }
                if (File.Exists(HideoutsFile(id)))
                {
                    File.Copy(HideoutsFile(id), LiveHideoutsFile(id), true);
                }
                bool atBed = !string.IsNullOrEmpty(meta.Hideout);
                meta.Resume = atBed;
                meta.Respawn = !atBed;
                WriteMeta(id, meta);
                PlaceAt(id, meta);
                Console.WriteLine("[ZeroMenuTime] The character died; the save from " + meta.SavedAtUtc + " at "
                    + (atBed ? "the '" + meta.Hideout + "' hideout" : meta.Exit + " on " + meta.Map) + " was put back.");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("[ZeroMenuTime] Putting the save back failed: " + e);
        }
    }
}

/// <summary>Makes the save service known to the raid-end patch and switches that patch on.</summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class SaveGameLoader(SaveGameService saves) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        SaveGameService.Instance = saves;
        try
        {
            new RaidEndSavePatch().Enable();
        }
        catch (Exception e)
        {
            Console.WriteLine("[ZeroMenuTime] Could not enable the raid-end save patch: " + e.Message);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Once the server has finished its own handling of a raid's end, look at whether the save should be put back.</summary>
public sealed class RaidEndSavePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod("EndLocalRaidAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, EndLocalRaidRequestData request, ref Task __result)
    {
        bool died = request?.Results != null && request.Results.IsPlayerDead();
        __result = After(__result, sessionId, died);
    }

    private static async Task After(Task original, MongoId sessionId, bool died)
    {
        await original;
        if (FikaLink.IsHeadlessProfile(sessionId))
        {
            return;
        }
        var host = FikaLink.TakeLastHost(sessionId);
        if (host != null)
        {
            HideoutsService.Instance?.CatchUp(sessionId, host.Value);
        }
        SaveGameService.Instance?.RaidEnded(sessionId, died);
    }
}

public sealed class SavedGear
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public List<string> Roots { get; set; } = [];
    public List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> Items { get; set; } = [];
}
