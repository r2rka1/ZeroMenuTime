using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils;

namespace TheTrueSPT.Server;

public sealed class NewGameRequest : IRequestData
{
    /// <summary>Must be "yes"; a guard against the route being called by accident.</summary>
    public string Confirm { get; set; } = "";
}

public sealed class NewGameResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    /// <summary>Where the character that was replaced has been kept.</summary>
    public string? Backup { get; set; }
}

/// <summary>
/// "New game": the character is made afresh, with the same name, side, face and voice, by the
/// server's own profile creation, which other mods (Vagabond among them) hook to set up their own
/// state. The three hideouts, the world clock and the save go with it. Everything that is replaced
/// is copied to a backup folder first.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class NewGameService(SaveServer saveServer, CreateProfileService createProfile, JsonUtil jsonUtil)
{
    public async ValueTask<NewGameResponse> Reset(MongoId sessionId, NewGameRequest request)
    {
        try
        {
            if (!string.Equals(request.Confirm, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return new NewGameResponse { Error = "Not confirmed" };
            }
            string id = sessionId.ToString();
            var full = saveServer.GetProfile(sessionId);
            var pmc = full?.CharacterData?.PmcData;
            if (pmc?.Info == null || pmc.Customization == null)
            {
                return new NewGameResponse { Error = "No character to start over from" };
            }

            // Keep what is about to be replaced.
            string dir = System.IO.Path.Combine("user", "profileData", id);
            string backup = System.IO.Path.Combine(dir, "newgame-backups", DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
            Directory.CreateDirectory(backup);
            File.WriteAllText(System.IO.Path.Combine(backup, id + ".json"), jsonUtil.Serialize(full));
            if (Directory.Exists(dir))
            {
                foreach (string file in Directory.GetFiles(dir))
                {
                    File.Copy(file, System.IO.Path.Combine(backup, System.IO.Path.GetFileName(file)), true);
                }
            }

            var fresh = new ProfileCreateRequestData
            {
                Side = pmc.Info.Side,
                Nickname = pmc.Info.Nickname,
                HeadId = pmc.Customization.Head!.Value,
                VoiceId = pmc.Customization.Voice!.Value,
            };
            await createProfile.CreateProfile(sessionId, fresh);

            // Our own state: hideouts, clock, save.
            foreach (string file in Directory.GetFiles(dir, "thetruespt.*"))
            {
                File.Delete(file);
            }
            TableRewards.Clear(id);
            Console.WriteLine("[TheTrueSPT] New game started for " + fresh.Nickname + "; the old character is kept in " + backup);
            return new NewGameResponse { Ok = true, Backup = System.IO.Path.GetFullPath(backup) };
        }
        catch (Exception e)
        {
            Console.WriteLine("[TheTrueSPT] New game failed: " + e);
            return new NewGameResponse { Error = e.GetType().Name + ": " + e.Message };
        }
    }
}
