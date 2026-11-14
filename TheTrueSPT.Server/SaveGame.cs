using System.Reflection;
using System.Runtime.CompilerServices;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Utils;

namespace TheTrueSPT.Server;

public sealed class SaveRequest : IRequestData
{
    /// <summary>The hideout whose bed the character is saving at.</summary>
    public string Hideout { get; set; } = "";
    /// <summary>The character as the game holds it now: what it would send if the raid ended.</summary>
    public PmcData? Profile { get; set; }
    /// <summary>For "status": the client has acted on the wake-up-at-the-bed flag, clear it.</summary>
    public bool Consume { get; set; }
}

public sealed class SaveResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    /// <summary>Is there a save at all?</summary>
    public bool Has { get; set; }
    public string Hideout { get; set; } = "";
    public double Clock { get; set; }
    /// <summary>The next raid should begin at the bed of that hideout: the character died, or the game stopped without the raid ending.</summary>
    public bool Resume { get; set; }
}

/// <summary>What is kept beside the saved character.</summary>
public sealed class SaveMeta
{
    public string Hideout { get; set; } = "";
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

    private static string Dir(string sessionId) => System.IO.Path.Combine("user", "profileData", sessionId);
    private static string CharacterFile(string sessionId) => System.IO.Path.Combine(Dir(sessionId), "thetruespt.save.character.json");
    private static string HideoutsFile(string sessionId) => System.IO.Path.Combine(Dir(sessionId), "thetruespt.save.hideouts.json");
    private static string MetaFile(string sessionId) => System.IO.Path.Combine(Dir(sessionId), "thetruespt.save.meta.json");
    private static string LiveHideoutsFile(string sessionId) => System.IO.Path.Combine(Dir(sessionId), "thetruespt.hideouts.json");

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

    private static void PlaceAt(string sessionId, string hideout)
    {
        try
        {
            TellVagabond(sessionId, hideout);
        }
        catch (Exception e)
        {
            // Vagabond not installed, or its API changed: the character stays where the game has it.
            Console.WriteLine("[TheTrueSPT] Could not tell Vagabond where the bed is: " + e.Message);
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
            Console.WriteLine("[TheTrueSPT] New game: the first raid begins in the hideout, by its '" + hideout + "' door.");
        }
    }

    private SaveResponse Describe(string sessionId, SaveMeta? meta)
    {
        return new SaveResponse { Ok = true, Has = meta != null, Hideout = meta?.Hideout ?? "", Resume = meta?.Resume ?? false };
    }

    public SaveResponse Save(MongoId sessionId, SaveRequest request)
    {
        try
        {
            string id = sessionId.ToString();
            string hideout = (request.Hideout ?? "").ToLowerInvariant();
            if (!HideoutsService.Known.Contains(hideout) || request.Profile == null)
            {
                return new SaveResponse { Error = "A save needs a hideout and the character" };
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
                var meta = new SaveMeta { Hideout = hideout, SavedAtUtc = DateTime.UtcNow.ToString("u"), Resume = true };
                WriteMeta(id, meta);
                PlaceAt(id, hideout);
                Console.WriteLine("[TheTrueSPT] Game saved at the bed of the '" + hideout + "' hideout.");
                return Describe(id, meta);
            }
        }
        catch (Exception e)
        {
            var real = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
            Console.WriteLine("[TheTrueSPT] Saving failed: " + real);
            return new SaveResponse { Error = real.GetType().Name + ": " + real.Message };
        }
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
                if (meta != null && meta.Resume)
                {
                    if (request.Consume)
                    {
                        meta.Resume = false;
                        WriteMeta(id, meta);
                    }
                    else
                    {
                        // Other mods settle where the character is after a death; say it again.
                        PlaceAt(id, meta.Hideout);
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

    /// <summary>A raid has ended. After a death the save is put back; otherwise the bed stops being where the next raid begins.</summary>
    public void RaidEnded(MongoId sessionId, bool died)
    {
        try
        {
            string id = sessionId.ToString();
            lock (_gate)
            {
                var meta = ReadMeta(id);
                if (meta == null)
                {
                    return;
                }
                if (!died)
                {
                    if (meta.Resume)
                    {
                        meta.Resume = false;
                        WriteMeta(id, meta);
                    }
                    return;
                }
                var full = saveServer.GetProfile(sessionId);
                var saved = jsonUtil.Deserialize<PmcData>(File.ReadAllText(CharacterFile(id)));
                if (full?.CharacterData == null || saved == null)
                {
                    Console.WriteLine("[TheTrueSPT] The save could not be read; the death stands as the game has it.");
                    return;
                }
                full.CharacterData.PmcData = saved;
                if (File.Exists(HideoutsFile(id)))
                {
                    File.Copy(HideoutsFile(id), LiveHideoutsFile(id), true);
                }
                meta.Resume = true;
                WriteMeta(id, meta);
                PlaceAt(id, meta.Hideout);
                Console.WriteLine("[TheTrueSPT] The character died; the save from " + meta.SavedAtUtc + " at the '" + meta.Hideout + "' hideout was put back.");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("[TheTrueSPT] Putting the save back failed: " + e);
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
            Console.WriteLine("[TheTrueSPT] Could not enable the raid-end save patch: " + e.Message);
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
        SaveGameService.Instance?.RaidEnded(sessionId, died);
    }
}
