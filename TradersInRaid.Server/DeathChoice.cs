using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.InRaid;

namespace TradersInRaid.Server;

/// <summary>The player's answer to "where do you want to continue?" after a death.</summary>
public sealed class DeathChoiceRequest : IRequestData
{
    /// <summary>"hideout" or "stay".</summary>
    public string Choice { get; set; } = "";
}

public sealed class DeathChoiceResponse
{
    public bool Ok { get; set; }
    public string? Map { get; set; }
    public string? Exit { get; set; }
    public string? Error { get; set; }
}

/// <summary>Where the player was standing when the raid began, so "stay where you died" can undo the move.</summary>
public static class RaidStartLocations
{
    private sealed record Place(string? Map, string? Exit);

    private static readonly ConcurrentDictionary<string, Place> Known = new();

    public static void Remember(string sessionId, string? map, string? exit)
    {
        Known[sessionId] = new Place(map, exit);
    }

    public static bool TryGet(string sessionId, out string? map, out string? exit)
    {
        if (Known.TryGetValue(sessionId, out var place))
        {
            map = place.Map;
            exit = place.Exit;
            return !string.IsNullOrEmpty(map);
        }
        map = null;
        exit = null;
        return false;
    }
}

/// <summary>
/// Note the open-world position each raid starts from. When the player dies, Vagabond immediately
/// overwrites that position with wherever its OnDeathGoTo setting points, so the only way to offer
/// "stay where you died" afterwards is to have kept the old position here.
/// </summary>
public sealed class RaidStartLocationPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod("StartLocalRaidAsync",
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId)
    {
        try
        {
            DeathChoiceBridge.RememberRaidStart(sessionId.ToString());
        }
        catch (Exception e)
        {
            // Vagabond not installed, or its API changed: there is nothing to remember.
            Console.WriteLine("[TradersInRaid] Could not note the raid's starting place: " + e.Message);
        }
    }
}

/// <summary>Everything that touches Vagabond's assembly, kept apart so a missing Vagabond only fails these calls.</summary>
internal static class DeathChoiceBridge
{
    private const string HideoutExitPrefix = "VGB_HO_";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RememberRaidStart(string sessionId)
    {
        var state = Vagabond.Common.Api.Api.GetState(sessionId);
        if (state != null)
        {
            RaidStartLocations.Remember(sessionId, state.CurrentMap, state.LastExit);
            Console.WriteLine("[TradersInRaid] Raid starts at " + state.CurrentMap + " / " + state.LastExit
                              + "; that is where 'stay where you died' leads back to.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Apply(string sessionId, string choice, DeathChoiceResponse response)
    {
        var state = Vagabond.Common.Api.Api.GetState(sessionId);
        if (state == null)
        {
            response.Error = "the open world does not know this profile";
            return;
        }

        if (string.Equals(choice, "hideout", StringComparison.OrdinalIgnoreCase))
        {
            var map = Vagabond.Common.Data.VagabondLocations.NormaliseMapName(state.HideoutState?.Map);
            if (string.IsNullOrEmpty(state.HideoutState?.Id) || map == Vagabond.Common.Enums.RaidLocation.Nil)
            {
                response.Error = "you have no hideout yet";
                return;
            }
            state.CurrentMap = map.ToString();
            state.LastExit = HideoutExitPrefix + state.HideoutState!.Id;
        }
        else if (string.Equals(choice, "stay", StringComparison.OrdinalIgnoreCase))
        {
            if (!RaidStartLocations.TryGet(sessionId, out var map, out var exit))
            {
                response.Error = "the place you died at is no longer known (the server was restarted)";
                return;
            }
            state.CurrentMap = map;
            state.LastExit = exit ?? "";
        }
        else
        {
            response.Error = "unknown choice '" + choice + "'";
            return;
        }

        state.TransitState = null;
        Vagabond.Common.Api.Api.SaveState(sessionId, state);
        response.Ok = true;
        response.Map = state.CurrentMap;
        response.Exit = state.LastExit;
    }
}
