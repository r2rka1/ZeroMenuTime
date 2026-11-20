using System.Collections;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace ZeroMenuTime.Server;

/// <summary>Who a session is playing with, as far as this mod needs to know.</summary>
public sealed class CoopWho
{
    public bool Fika { get; set; }
    /// <summary>The match the session is in ("" when in none).</summary>
    public string Match { get; set; } = "";
    /// <summary>Whose world it is: the hosting player; for a headless raid, the player who asked for it; alone, oneself.</summary>
    public string Owner { get; set; } = "";
    public bool HeadlessMatch { get; set; }
    public bool HeadlessProfile { get; set; }
    public List<string> Members { get; set; } = [];
}

/// <summary>A raid that is running or waiting, as offered to someone who might join it.</summary>
public sealed class CoopRaid
{
    public string Match { get; set; } = "";
    public string Host { get; set; } = "";
    public string Owner { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Location { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Headless { get; set; }
    public int Players { get; set; }
    /// <summary>The asking player is already on this raid's list (was in it before).</summary>
    public bool Mine { get; set; }
}

public sealed class CoopRaids
{
    public bool Ok { get; set; } = true;
    public List<CoopRaid> Raids { get; set; } = [];
}

/// <summary>
/// What this mod asks of Fika's server: which sessions are in a raid together and whose raid it
/// is. All by reflection, so the mod neither needs Fika to build nor to run. Without Fika every
/// session is its own owner, which is single-player behaviour.
/// </summary>
public static class FikaLink
{
    private static bool _ready;
    private static object? _matches;
    private static MethodInfo? _matchIdByProfile;
    private static MethodInfo? _getMatch;
    private static object? _headless;
    private static PropertyInfo? _headlessClients;
    private static SaveServer? _saves;

    public static bool Installed => _matches != null;

    public static void Init(IServiceProvider services, SaveServer saves)
    {
        if (_ready)
        {
            return;
        }
        _ready = true;
        _saves = saves;
        var fika = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FikaServer");
        if (fika == null)
        {
            return;
        }
        var matchService = fika.GetType("FikaServer.Services.MatchService");
        if (matchService != null)
        {
            _matches = services.GetService(matchService);
            _matchIdByProfile = matchService.GetMethod("GetMatchIdByProfile", [typeof(MongoId)]);
            _getMatch = matchService.GetMethod("GetMatch", [typeof(MongoId?)]);
        }
        var headlessService = fika.GetType("FikaServer.Services.Headless.HeadlessService");
        if (headlessService != null)
        {
            _headless = services.GetService(headlessService);
            _headlessClients = headlessService.GetProperty("HeadlessClients");
        }
        Console.WriteLine("[ZeroMenuTime] Fika found: matches " + (_matches != null && _matchIdByProfile != null && _getMatch != null ? "readable" : "NOT readable")
                          + ", headless " + (_headless != null && _headlessClients != null ? "readable" : "NOT readable")
                          + " (Fika server " + fika.GetName().Version?.ToString(3) + ")");
    }

    /// <summary>Fika's headless hosts log in with profiles named this way; they are not players.</summary>
    public static bool IsHeadlessProfile(MongoId sessionId)
    {
        try
        {
            string? name = _saves?.GetProfile(sessionId)?.ProfileInfo?.Username;
            return name != null && name.StartsWith("headless_", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static object? MatchOf(MongoId sessionId, out MongoId matchId)
    {
        matchId = default;
        if (_matches == null || _matchIdByProfile == null || _getMatch == null)
        {
            return null;
        }
        // A player whose game died stays listed in the old match until it times out. Of the matches
        // that list the session, the one made last is the one it is in.
        if (_matches.GetType().GetProperty("Matches")?.GetValue(_matches) is IDictionary all)
        {
            object? latest = null;
            long made = long.MinValue;
            foreach (DictionaryEntry entry in all)
            {
                var candidate = entry.Value;
                if (candidate?.GetType().GetProperty("Players")?.GetValue(candidate) is not IDictionary players || !players.Contains(sessionId))
                {
                    continue;
                }
                long stamp = candidate.GetType().GetProperty("Timestamp")?.GetValue(candidate) as long? ?? 0;
                if (latest == null || stamp > made)
                {
                    latest = candidate;
                    made = stamp;
                    matchId = (MongoId)entry.Key;
                }
            }
            return latest;
        }
        if (_matchIdByProfile.Invoke(_matches, [sessionId]) is not MongoId found)
        {
            return null;
        }
        matchId = found;
        return _getMatch.Invoke(_matches, [(MongoId?)found]);
    }

    /// <summary>The player who asked this headless host for its raid, if it is one and has one.</summary>
    private static MongoId? RequesterOf(MongoId headlessSession)
    {
        if (_headless == null || _headlessClients?.GetValue(_headless) is not IDictionary clients || !clients.Contains(headlessSession))
        {
            return null;
        }
        var info = clients[headlessSession];
        string? requester = info?.GetType().GetProperty("RequesterSessionID")?.GetValue(info) as string;
        return string.IsNullOrEmpty(requester) ? null : new MongoId(requester);
    }

    /// <summary>
    /// Whose world the session is in: the hosting player of its match; for a headless-hosted match
    /// the player who asked for it; in no match, the session itself.
    /// </summary>
    public static MongoId OwnerOf(MongoId sessionId)
    {
        var owner = FindOwner(sessionId);
        if (owner != sessionId)
        {
            Guests[sessionId.ToString()] = owner;
        }
        return owner;
    }

    /// <summary>Whose world each session was last a guest in, kept until its raid end has been dealt with.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MongoId> Guests = new();

    /// <summary>The owner of the world this session was a guest in, once; null if it was in its own.</summary>
    public static MongoId? TakeLastHost(MongoId sessionId)
    {
        return Guests.TryRemove(sessionId.ToString(), out var owner) ? owner : null;
    }

    private static MongoId FindOwner(MongoId sessionId)
    {
        try
        {
            var match = MatchOf(sessionId, out var matchId);
            if (match == null)
            {
                // A headless host asks before its match exists, too.
                return RequesterOf(sessionId) ?? sessionId;
            }
            bool headless = match.GetType().GetProperty("IsHeadless")?.GetValue(match) as bool? ?? false;
            return headless ? RequesterOf(matchId) ?? matchId : matchId;
        }
        catch (Exception e)
        {
            Console.WriteLine("[ZeroMenuTime] Could not ask Fika whose raid it is: " + e.Message);
            return sessionId;
        }
    }

    /// <summary>Which raid (by its own id, new for every raid) each session was last in.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> WasIn = new();

    /// <summary>Notes that the session is in its match now. Called when it asks who it is with, at every raid start.</summary>
    public static void NoteRaid(MongoId sessionId)
    {
        try
        {
            var match = MatchOf(sessionId, out _);
            string instance = match?.GetType().GetProperty("ServerGuid")?.GetValue(match)?.ToString() ?? "";
            if (instance != "")
            {
                WasIn[sessionId.ToString()] = instance;
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>The raids someone could join now.</summary>
    public static List<CoopRaid> Raids(MongoId asking)
    {
        var raids = new List<CoopRaid>();
        try
        {
            if (_matches?.GetType().GetProperty("Matches")?.GetValue(_matches) is not IDictionary all)
            {
                return raids;
            }
            foreach (DictionaryEntry entry in all)
            {
                var match = entry.Value;
                if (match == null)
                {
                    continue;
                }
                var type = match.GetType();
                var raid = new CoopRaid
                {
                    Match = entry.Key?.ToString() ?? "",
                    Host = type.GetProperty("HostUsername")?.GetValue(match) as string ?? "",
                    Headless = type.GetProperty("IsHeadless")?.GetValue(match) as bool? ?? false,
                    Status = type.GetProperty("Status")?.GetValue(match)?.ToString() ?? "",
                };
                var config = type.GetProperty("RaidConfig")?.GetValue(match);
                raid.Location = config?.GetType().GetProperty("Location")?.GetValue(config) as string ?? "";
                string instance = type.GetProperty("ServerGuid")?.GetValue(match)?.ToString() ?? "";
                if (type.GetProperty("Players")?.GetValue(match) is IDictionary players)
                {
                    raid.Players = players.Count;
                    // Fika forgets a player who leaves; this mod remembers which raid each was last in.
                    // "Mine" is only for someone still on Fika's own list (their game died and the host
                    // holds the character). Someone who left properly comes back as a fresh arrival.
                    raid.Mine = players.Contains(asking);
                }
                var owner = raid.Headless ? RequesterOf((MongoId)entry.Key) : (MongoId)entry.Key;
                raid.Owner = owner?.ToString() ?? "";
                try
                {
                    raid.OwnerName = owner != null ? _saves?.GetProfile(owner.Value)?.CharacterData?.PmcData?.Info?.Nickname ?? "" : "";
                }
                catch (Exception)
                {
                }
                raids.Add(raid);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("[ZeroMenuTime] Could not list the Fika raids: " + e.Message);
        }
        return raids;
    }

    public static CoopWho Describe(MongoId sessionId)
    {
        NoteRaid(sessionId);
        var who = new CoopWho { Fika = Installed, Owner = OwnerOf(sessionId).ToString(), HeadlessProfile = IsHeadlessProfile(sessionId) };
        try
        {
            var match = MatchOf(sessionId, out var matchId);
            if (match != null)
            {
                who.Match = matchId.ToString();
                who.HeadlessMatch = match.GetType().GetProperty("IsHeadless")?.GetValue(match) as bool? ?? false;
                if (match.GetType().GetProperty("Players")?.GetValue(match) is IDictionary players)
                {
                    foreach (var key in players.Keys)
                    {
                        who.Members.Add(key?.ToString() ?? "");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("[ZeroMenuTime] Could not read the Fika match: " + e.Message);
        }
        return who;
    }
}

/// <summary>Finds Fika once every mod has loaded.</summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class FikaLinkLoader(IServiceProvider services, SaveServer saves, HideoutsService hideouts) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        HideoutsService.Instance = hideouts;
        try
        {
            FikaLink.Init(services, saves);
        }
        catch (Exception e)
        {
            Console.WriteLine("[ZeroMenuTime] Looking for Fika failed: " + e.Message);
        }
        return Task.CompletedTask;
    }
}
