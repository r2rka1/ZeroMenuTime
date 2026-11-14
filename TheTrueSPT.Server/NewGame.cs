using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
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
public sealed class NewGameService(SaveServer saveServer, CreateProfileService createProfile, JsonUtil jsonUtil, PresetHelper presetHelper, SaveGameService saveGame)
{
    /// <summary>Which door of the hideout a new character starts behind.</summary>
    public const string StartDoor = "customs";

    private const string Alpha = "544a11ac4bdc2d470e8b456a", Roubles = "5449016a4bdc2d6f028b456f";
    private const string Pistol = "5448bd6b4bdc2dfc2f8b4569", PistolMag = "5448c12b4bdc2d02308b456f", PistolRound = "5737201124597760fc4431f1";
    private const string Armour = "5648a7494bdc2d9d488b4583", Backpack = "56e335e4d2720b6c058b456d", Rig = "572b7adb24597762ae139821";
    private const string Medkit = "5755356824597772cb798962", Bandage = "544fb25a4bdc2dfb738b4567", Salewa = "544fb45d4bdc2dee738b4568";
    private const string Splint = "544fb3364bdc2d34748b456a", Painkillers = "544fb37f4bdc2dee738b4567", Water = "5448fee04bdc2dbc018b4567", Crackers = "5448ff904bdc2d6f028b456e";

    /// <summary>How much wear each part of the kit can take, where it wears at all.</summary>
    private static readonly Dictionary<string, double> Wear = new()
    {
        { Pistol, 100 }, { "65703d866584602f7d057a8a", 40 }, { "65703fa06584602f7d057a8e", 40 }, { "65703fe46a912c8b5c03468b", 10 }, { "657040374e67e8ec7a0d261c", 10 },
    };

    /// <summary>
    /// A new character starts with little: a pistol with three magazines, soft armour, a rig and a
    /// backpack, something for wounds, a bottle of water, and the small secure container with a
    /// little money in it. Whatever the game's edition would have handed out, worn or in the stash,
    /// is taken away first; the pockets, the knife and the dogtag stay.
    /// </summary>
    private void GiveStartingKit(PmcData pmc)
    {
        var inventory = pmc.Inventory!;
        var items = inventory.Items!;
        string gear = inventory.Equipment!.Value.ToString();
        string? stash = inventory.Stash?.ToString();
        var keep = new HashSet<string> { "Pockets", "Scabbard", "Dogtag", "ArmBand" };
        var kept = new HashSet<string>(items.Where(i => i.ParentId == gear && keep.Contains(i.SlotId ?? "")).Select(i => i.Id.ToString()));
        string? pockets = items.FirstOrDefault(i => i.ParentId == gear && i.SlotId == "Pockets")?.Id.ToString();

        // Everything hanging off the gear (but what is kept) and everything in the stash goes.
        var doomed = new HashSet<string>();
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var item in items)
            {
                string id = item.Id.ToString();
                if (doomed.Contains(id) || kept.Contains(id) || item.ParentId == null)
                {
                    continue;
                }
                bool under = item.ParentId == gear || item.ParentId == stash || item.ParentId == pockets || doomed.Contains(item.ParentId);
                // What sits inside the knife's or dogtag's own slots is left alone.
                if (under && doomed.Add(id))
                {
                    grew = true;
                }
            }
        }
        items.RemoveAll(i => doomed.Contains(i.Id.ToString()));

        string Add(string tpl, string parent, string slot, int? x = null, int? y = null, Upd? upd = null, object? place = null)
        {
            var id = new MongoId();
            items.Add(new Item
            {
                Id = id,
                Template = new MongoId(tpl),
                ParentId = parent,
                SlotId = slot,
                Location = place ?? (x.HasValue ? new ItemLocation { X = x, Y = y ?? 0, R = ItemRotation.Horizontal } : null),
                Upd = upd,
            });
            return id.ToString();
        }

        string Magazine(string parent, string slot, int? x = null, int? y = null)
        {
            string mag = Add(PistolMag, parent, slot, x, y);
            Add(PistolRound, mag, "cartridges", upd: new Upd { StackObjectsCount = 8 }, place: 0);
            return mag;
        }

        // A thing that comes in parts (the pistol, the armour with its soft plates), from the game's own default build of it.
        string? Built(string tpl, string slot)
        {
            var preset = presetHelper.GetDefaultPreset(new MongoId(tpl));
            if (preset?.Items == null || preset.Items.Count == 0)
            {
                return Add(tpl, gear, slot);
            }
            var fresh = preset.Items.ToDictionary(i => i.Id.ToString(), _ => new MongoId());
            string? root = null;
            foreach (var part in preset.Items)
            {
                bool isRoot = part.ParentId == null || !fresh.ContainsKey(part.ParentId);
                string id = fresh[part.Id.ToString()].ToString();
                string partTpl = part.Template.ToString();
                items.Add(new Item
                {
                    Id = fresh[part.Id.ToString()],
                    Template = part.Template,
                    ParentId = isRoot ? gear : fresh[part.ParentId!].ToString(),
                    SlotId = isRoot ? slot : part.SlotId,
                    Upd = Wear.TryGetValue(partTpl, out double wear) ? new Upd { Repairable = new UpdRepairable { Durability = wear, MaxDurability = wear } } : null,
                });
                if (isRoot)
                {
                    root = id;
                }
            }
            return root;
        }

        string secure = Add(Alpha, gear, "SecuredContainer");
        Add(Roubles, secure, "main", 0, 0, new Upd { StackObjectsCount = 30000 });

        string? pistol = Built(Pistol, "Holster");
        // The default build has an empty magazine in it; fill that one.
        var loaded = items.FirstOrDefault(i => i.ParentId == pistol && i.SlotId == "mod_magazine");
        if (loaded != null)
        {
            Add(PistolRound, loaded.Id.ToString(), "cartridges", upd: new Upd { StackObjectsCount = 8 }, place: 0);
        }
        else if (pistol != null)
        {
            Magazine(pistol, "mod_magazine");
        }
        Built(Armour, "ArmorVest");

        // The rig has four pouches: two single ones for magazines, two double ones for the medicine.
        string rig = Add(Rig, gear, "TacticalVest");
        Magazine(rig, "1", 0, 0);
        Magazine(rig, "4", 0, 0);
        Add(Salewa, rig, "2", 0, 0, new Upd { MedKit = new UpdMedKit { HpResource = 400 } });
        Add(Medkit, rig, "3", 0, 0, new Upd { MedKit = new UpdMedKit { HpResource = 100 } });
        Add(Bandage, rig, "3", 0, 1);
        if (pockets != null)
        {
            Magazine(pockets, "pocket1", 0, 0);
            Add(Bandage, pockets, "pocket2", 0, 0);
        }

        string pack = Add(Backpack, gear, "Backpack");
        Add(PistolRound, pack, "main", 0, 0, new Upd { StackObjectsCount = 32 });
        Add(Medkit, pack, "main", 1, 0, new Upd { MedKit = new UpdMedKit { HpResource = 100 } });
        Add(Splint, pack, "main", 2, 0);
        Add(Painkillers, pack, "main", 3, 0, new Upd { MedKit = new UpdMedKit { HpResource = 4 } });
        Add(Water, pack, "main", 0, 1, new Upd { FoodDrink = new UpdFoodDrink { HpPercent = 60 } });
        Add(Crackers, pack, "main", 1, 1, new Upd { FoodDrink = new UpdFoodDrink { HpPercent = 1 } });
        Add(Crackers, pack, "main", 2, 1, new Upd { FoodDrink = new UpdFoodDrink { HpPercent = 1 } });
    }

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

            // The new character: a small kit and nothing else, waking in the hideout by its Customs door.
            var made = saveServer.GetProfile(sessionId)?.CharacterData?.PmcData;
            if (made?.Inventory?.Items != null && made.Inventory.Equipment != null)
            {
                try
                {
                    GiveStartingKit(made);
                    saveGame.StartFresh(id, made, StartDoor);
                }
                catch (Exception e)
                {
                    Console.WriteLine("[TheTrueSPT] New game: the starting kit or the starting place could not be set: " + e);
                }
            }
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
