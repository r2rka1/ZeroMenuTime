using SPTarkov.Server.Core.Models.Utils;

namespace ZeroMenuTime.Server;

public sealed class TraderListRequest : IRequestData
{
}

public sealed class TraderListResponse
{
    public bool Ok { get; set; }
    /// <summary>Vagabond's trader_locations.json as it is on disk, comments included.</summary>
    public string Json { get; set; } = "";
    public string? Path { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Vagabond's list of which trader stands at which extract lives in the server's mod folder. The game
/// may be installed somewhere else entirely, so the client asks for the list here instead of looking
/// for the file beside the game.
/// </summary>
public static class VagabondTraders
{
    public static TraderListResponse Read()
    {
        var response = new TraderListResponse();
        try
        {
            foreach (string path in Candidates())
            {
                if (File.Exists(path))
                {
                    response.Json = File.ReadAllText(path);
                    response.Path = path;
                    response.Ok = true;
                    return response;
                }
            }
            response.Error = "Vagabond's trader_locations.json was not found; looked in " + string.Join(", ", Candidates());
        }
        catch (Exception e)
        {
            response.Error = e.GetType().Name + ": " + e.Message;
        }
        Console.WriteLine("[ZeroMenuTime] " + response.Error);
        return response;
    }

    private static IEnumerable<string> Candidates()
    {
        // Beside this mod: user/mods/ZeroMenuTime/../Vagabond.
        string? ours = System.IO.Path.GetDirectoryName(typeof(VagabondTraders).Assembly.Location);
        if (!string.IsNullOrEmpty(ours))
        {
            yield return System.IO.Path.GetFullPath(System.IO.Path.Combine(ours, "..", "Vagabond", "config", "trader_locations.json"));
        }
        // The server runs from its SPT_Runtime folder.
        yield return System.IO.Path.GetFullPath(System.IO.Path.Combine("user", "mods", "Vagabond", "config", "trader_locations.json"));
    }
}
