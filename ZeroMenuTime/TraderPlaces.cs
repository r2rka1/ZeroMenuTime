using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using EFT.Communications;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>Where one trader is: which map, behind which door, in what kind of room, looking like whom.</summary>
    public class TraderPlace
    {
        public string TraderId = "";
        public string Name = "";
        /// <summary>A map by its short name (see <see cref="DoorList.Maps"/>), or empty for nowhere.</summary>
        public string Map = "";
        /// <summary>A door of <see cref="DoorList"/> by its id; empty for the first free one on the map.</summary>
        public string Door = "";
        /// <summary>The trader whose room this one is furnished like, or "Plain".</summary>
        public string Room = "";
        /// <summary>The trader whose figure stands in the room.</summary>
        public string Model = "";
        /// <summary>A trader added by another mod, or one Vagabond gives no place.</summary>
        public bool Custom;
    }

    /// <summary>
    /// The settings that say where each trader is. The traders of the game start where Vagabond has
    /// them, behind the doors picked for them; any of them can be moved to another door or another
    /// map. A trader added by another mod has no place until the player gives them one, and gets a
    /// kind of room and a figure to go with it. In co-op the host's settings are the ones that count.
    /// </summary>
    public static class TraderPlaces
    {
        public const string NotPlaced = "Not placed";
        public const string Plain = "Plain";

        private class Entry
        {
            public string TraderId, Name;
            public bool Custom;
            public ConfigEntry<string> Map, Door, Room, Model;
        }

        /// <summary>As a door: by the trader's extract of Vagabond, against the nearest wall, as it was before doors could be chosen.</summary>
        public const string AtExtract = "extract";

        private static ConfigEntry<bool> _taken;
        private static ConfigEntry<string> _told;
        private static readonly List<Entry> Entries = new List<Entry>();
        private static ConfigFile _config;
        private static bool _looked;

        /// <summary>Names of traders that are not the game's own eight, by id.</summary>
        public static readonly Dictionary<string, string> CustomNames = new Dictionary<string, string>();

        /// <summary>Traders that are no shopkeepers to stand in a room: the keeper of the lighthouse, the driver and the storyteller.</summary>
        private static readonly HashSet<string> Never = new HashSet<string> { "638f541a29ffd1183d187f57", "656f0f98d80a697f855d34b1", "6864e812f9fe664cb8b8e152" };

        public const string Ref = "6617beeaa9cfa777ca915b7c";

        private static readonly string[] Own =
        {
            TraderConfig.Prapor, TraderConfig.Therapist, TraderConfig.Fence, TraderConfig.Skier,
            TraderConfig.Peacekeeper, TraderConfig.Mechanic, TraderConfig.Ragman, TraderConfig.Jaeger,
        };

        /// <summary>The names a room or a figure can be chosen by.</summary>
        private static readonly string[] Looks = { "Prapor", "Therapist", "Fence", "Skier", "Peacekeeper", "Mechanic", "Ragman", "Jaeger" };

        /// <summary>Where the game's own traders are when nothing has been changed: as Vagabond has them.</summary>
        public static List<TraderPlace> Defaults()
        {
            return new List<TraderPlace>
            {
                Own_(TraderConfig.Prapor, "Streets", "streets-prapor"),
                Own_(TraderConfig.Therapist, "Ground Zero", "groundzero-therapist"),
                Own_(TraderConfig.Fence, "Streets", "streets-fence"),
                Own_(TraderConfig.Fence, "Lighthouse", "lighthouse-fence"),
                Own_(TraderConfig.Skier, "Customs", "customs-skier"),
                Own_(TraderConfig.Peacekeeper, "Shoreline", "shoreline-peacekeeper"),
                Own_(TraderConfig.Mechanic, "Factory", "factory-mechanic"),
                Own_(TraderConfig.Ragman, "Interchange", "interchange-ragman"),
                Own_(TraderConfig.Jaeger, "Woods", "woods-jaeger"),
            };
        }

        private static TraderPlace Own_(string id, string map, string door)
        {
            string name = TraderConfig.NameOf(id);
            return new TraderPlace { TraderId = id, Name = name, Map = map, Door = door, Room = name, Model = name };
        }

        public static void Bind(ConfigFile config)
        {
            _config = config;
            _taken = config.Bind("Trader places", "Taken from Vagabond", false, new ConfigDescription(
                "Set by the mod once it has looked at where Vagabond has the traders and set the groups \"Trader - NAME\" to match. Turn off to have it look again at the next raid; that puts the game's traders back where Vagabond has them.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            _told = config.Bind("Trader places", "Traders pointed out", "", new ConfigDescription(
                "The traders of other mods the mod has already told you have no door. Empty it to be told again.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            var seen = new HashSet<string>();
            foreach (var place in Defaults())
            {
                bool second = !seen.Add(place.TraderId);
                Add(place, second ? "Trader - " + place.Name + ", second place" : "Trader - " + place.Name, false);
            }
        }

        private static string Clean(string name)
        {
            // BepInEx forbids = \n \t \ " ' [ ] in section and key names.
            var text = new System.Text.StringBuilder();
            foreach (char c in name ?? "")
            {
                text.Append("=\n\t\\\"'[]".IndexOf(c) >= 0 ? ' ' : c);
            }
            return text.ToString().Trim();
        }

        private static void Add(TraderPlace start, string section, bool custom)
        {
            var entry = new Entry { TraderId = start.TraderId, Name = start.Name, Custom = custom };
            var maps = new List<string>();
            if (custom)
            {
                maps.Add(NotPlaced);
            }
            maps.AddRange(DoorList.Maps);
            entry.Map = _config.Bind(section, "Map", custom ? NotPlaced : start.Map, new ConfigDescription(
                custom
                    ? "The map " + start.Name + " is on. Pick a map, then a door below. Applies from the next raid."
                    : "The map " + start.Name + " is on. Vagabond's own extract for them is used on the map Vagabond has them on; on any other map they are behind the door only. Applies from the next raid.",
                new AcceptableValueList<string>(maps.ToArray()), new ConfigurationManagerAttributes { Order = 4 }));
            entry.Door = _config.Bind(section, "Door", custom ? "" : start.Door, new ConfigDescription(
                "The door on that map that leads to " + start.Name + "'s room. Use the arrows to go through the doors listed for the map. Left empty, the first free door of the map is taken. Applies from the next raid.",
                null, new ConfigurationManagerAttributes { Order = 3, CustomDrawer = DrawDoor, HideDefaultButton = true }));
            if (custom)
            {
                var rooms = new List<string> { Plain };
                rooms.AddRange(Looks);
                rooms.Add("Ref");
                entry.Room = _config.Bind(section, "Room", start.Room.Length > 0 ? start.Room : Plain, new ConfigDescription(
                    "How the room is furnished: plainly, or like the room of one of the game's traders. Applies from the next raid.",
                    new AcceptableValueList<string>(rooms.ToArray()), new ConfigurationManagerAttributes { Order = 2 }));
                entry.Model = _config.Bind(section, "Figure", start.Model.Length > 0 ? start.Model : "Fence", new ConfigDescription(
                    "Whose figure stands in the room for " + start.Name + ". Applies from the next raid.",
                    new AcceptableValueList<string>(Looks), new ConfigurationManagerAttributes { Order = 1 }));
            }
            entry.Map.SettingChanged += (sender, args) =>
            {
                // A door of another map means nothing here: take the one picked for this trader, or the first free.
                var door = DoorList.Find(entry.Door.Value);
                if (door == null || door.Map != entry.Map.Value)
                {
                    var free = FirstFree(entry.TraderId, entry.Map.Value, Mine(), entry);
                    entry.Door.Value = free != null ? free.Id : "";
                }
            };
            Entries.Add(entry);
        }

        /// <summary>
        /// Looks at the traders the game has and gives a group of settings to each one that is not
        /// among the game's own eight. Called when the main menu is up and when a raid starts.
        /// </summary>
        public static void FindCustom(bool atMenu)
        {
            if (_looked || _config == null)
            {
                return;
            }
            try
            {
                var session = SPT.Reflection.Utils.ClientAppUtils.GetClientApp()?.Session;
                if (session == null || session.Traders == null)
                {
                    return;
                }
                var unplaced = new List<string>();
                foreach (var trader in session.Traders)
                {
                    string id = trader.Id;
                    if (string.IsNullOrEmpty(id) || Own.Contains(id) || Never.Contains(id) || Entries.Any(e => e.TraderId == id))
                    {
                        continue;
                    }
                    string name = Clean(trader.LocalizedName);
                    if (name.Length == 0 || name.Contains(id))
                    {
                        name = Clean(trader.Settings != null ? trader.Settings.Nickname : "");
                    }
                    if (name.Length == 0)
                    {
                        name = "Trader " + id.Substring(Math.Max(0, id.Length - 4));
                    }
                    CustomNames[id] = name;
                    // Ref is the game's own, but Vagabond gives him no place: he can be given one like a trader of another mod.
                    Add(new TraderPlace { TraderId = id, Name = name, Room = id == Ref ? "Ref" : Plain, Model = "Fence" }, (id == Ref ? "Trader - " : "Custom trader - ") + name, true);
                    // Told once for each trader of another mod; never for Ref, whom everybody has.
                    var told = new List<string>((_told.Value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    if (Entries[Entries.Count - 1].Map.Value == NotPlaced && id != Ref && !told.Contains(id))
                    {
                        unplaced.Add(name);
                        told.Add(id);
                        _told.Value = string.Join(",", told);
                    }
                }
                _looked = true;
                Plugin.Log.LogInfo("Traders: " + Entries.Count(e => e.Custom) + " besides the game's own eight ("
                                   + string.Join(", ", Entries.Where(e => e.Custom).Select(e => e.Name + " " + (e.Map.Value == NotPlaced ? "not placed" : "on " + e.Map.Value))) + ")");
                if (atMenu && unplaced.Count > 0 && !Net.Headless)
                {
                    NotificationManager.DisplayMessageNotification(
                        "Zero Menu Time: " + string.Join(", ", unplaced) + (unplaced.Count == 1 ? " has" : " have")
                        + " no door on any map yet. Press F12, open r2rka1-ZeroMenuTime and pick a map and a door under \"Custom trader - "
                        + unplaced[0] + "\". The guide on the main menu has a page on this.", ENotificationDurationType.Long);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Looking for traders of other mods failed: " + e.Message);
            }
        }

        /// <summary>Vagabond's name for a map, as the short name used here.</summary>
        private static string MapOfRaid(string raid)
        {
            switch ((raid ?? "").ToLowerInvariant())
            {
                case "factoryday":
                case "factorynight": return "Factory";
                case "groundzero": return "Ground Zero";
                default: return DoorList.Maps.FirstOrDefault(m => string.Equals(m, raid, StringComparison.OrdinalIgnoreCase)) ?? "";
            }
        }

        /// <summary>
        /// Once: sets the places of the game's traders to where Vagabond has them, for a player who
        /// changed Vagabond's own list. Every time: a trader of another mod that has no place yet but
        /// has an extract in Vagabond's list is given that map, with the door by the extract.
        /// </summary>
        public static void TakeFromVagabond(List<VagabondLink.TraderExtract> extracts)
        {
            if (_config == null || extracts == null || extracts.Count == 0)
            {
                return;
            }
            try
            {
                var mapsOf = new Dictionary<string, List<string>>();
                foreach (var extract in extracts)
                {
                    string map = MapOfRaid(extract.Raid);
                    if (map.Length == 0)
                    {
                        continue;
                    }
                    List<string> maps;
                    if (!mapsOf.TryGetValue(extract.TraderId, out maps))
                    {
                        mapsOf[extract.TraderId] = maps = new List<string>();
                    }
                    if (!maps.Contains(map))
                    {
                        maps.Add(map);
                    }
                }
                foreach (var trader in mapsOf)
                {
                    var entries = Entries.Where(e => e.TraderId == trader.Key).ToList();
                    if (entries.Count == 0)
                    {
                        continue;
                    }
                    if (entries[0].Custom)
                    {
                        if (entries[0].Map.Value == NotPlaced)
                        {
                            entries[0].Map.Value = trader.Value[0];
                            entries[0].Door.Value = AtExtract;
                            Plugin.Log.LogInfo("Traders: " + entries[0].Name + " has an extract of Vagabond on " + trader.Value[0] + "; placed there, door by the extract");
                        }
                        continue;
                    }
                    if (_taken.Value)
                    {
                        continue;
                    }
                    // Maps Vagabond has them on that no group is set to yet go to the groups set to maps Vagabond does not have them on.
                    var missing = trader.Value.Where(m => !entries.Any(e => e.Map.Value == m)).ToList();
                    // Only a group nobody has touched: a place the player has chosen is theirs.
                    foreach (var entry in entries.Where(e => !trader.Value.Contains(e.Map.Value)
                                                             && e.Map.Value == (string)e.Map.DefaultValue && e.Door.Value == (string)e.Door.DefaultValue))
                    {
                        if (missing.Count == 0)
                        {
                            break;
                        }
                        entry.Map.Value = missing[0];
                        entry.Door.Value = AtExtract;
                        Plugin.Log.LogInfo("Traders: Vagabond has " + entry.Name + " on " + missing[0] + "; the settings now say so, door by the extract");
                        missing.RemoveAt(0);
                    }
                }
                _taken.Value = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Taking the traders' places from Vagabond failed: " + e.Message);
            }
        }

        /// <summary>This machine's own settings, as a list.</summary>
        public static List<TraderPlace> Mine()
        {
            var list = new List<TraderPlace>();
            foreach (var entry in Entries)
            {
                if (entry.Map.Value == NotPlaced)
                {
                    continue;
                }
                list.Add(new TraderPlace
                {
                    TraderId = entry.TraderId, Name = entry.Name, Map = entry.Map.Value, Door = entry.Door.Value ?? "",
                    Room = entry.Room != null ? entry.Room.Value : entry.Name, Model = entry.Model != null ? entry.Model.Value : entry.Name,
                    Custom = entry.Custom,
                });
            }
            return list;
        }

        /// <summary>The places that count in this raid: the host's in co-op, one's own otherwise.</summary>
        public static List<TraderPlace> All()
        {
            if (Layout.Active != null)
            {
                // A host with an older version says nothing of places: it built the way it always did.
                return Layout.Active.Places ?? Defaults();
            }
            return Mine();
        }

        /// <summary>The places on the map the game calls this.</summary>
        public static List<TraderPlace> On(string location)
        {
            string map = DoorList.MapOf(location);
            return All().Where(p => p.Map == map).ToList();
        }

        public static TraderPlace Of(string traderId, string location)
        {
            return On(location).FirstOrDefault(p => p.TraderId == traderId);
        }

        /// <summary>The trader has a place of their own somewhere: Vagabond's extract counts only where the settings say.</summary>
        public static bool Manages(string traderId)
        {
            return All().Any(p => p.TraderId == traderId) || Own.Contains(traderId);
        }

        public static string NameOf(string traderId)
        {
            foreach (var place in All())
            {
                if (place.TraderId == traderId && !string.IsNullOrEmpty(place.Name))
                {
                    return place.Name;
                }
            }
            string name;
            return CustomNames.TryGetValue(traderId, out name) ? name : null;
        }

        /// <summary>The door of a place: the one chosen if it is on that map and free, else the first free one. Null if the map has none left.</summary>
        public static MapDoor DoorOf(TraderPlace place, List<TraderPlace> all)
        {
            if (place.Door == AtExtract)
            {
                return null;
            }
            var chosen = DoorList.Find(place.Door);
            if (chosen != null && chosen.Map == place.Map)
            {
                // Two traders set to one door: the one listed first keeps it.
                var first = all.First(p => p.Map == place.Map && p.Door == place.Door);
                if (ReferenceEquals(first, place))
                {
                    return chosen;
                }
            }
            return FirstFree(place.TraderId, place.Map, all, null);
        }

        private static MapDoor FirstFree(string traderId, string map, List<TraderPlace> all, Entry asking)
        {
            var doors = DoorList.On(map);
            var taken = new HashSet<string>(all.Where(p => p.Map == map && p.TraderId != traderId).Select(p => p.Door));
            return doors.FirstOrDefault(d => d.For == traderId && !taken.Contains(d.Id))
                   ?? doors.FirstOrDefault(d => d.For == null && !taken.Contains(d.Id))
                   ?? doors.FirstOrDefault(d => !taken.Contains(d.Id));
        }

        /// <summary>The Door setting, drawn as: arrow, the door's name and number, arrow.</summary>
        private static void DrawDoor(ConfigEntryBase raw)
        {
            var entry = Entries.FirstOrDefault(e => ReferenceEquals(e.Door, raw));
            if (entry == null)
            {
                return;
            }
            if (entry.Map.Value == NotPlaced)
            {
                GUILayout.Label("Pick a map first", GUILayout.ExpandWidth(true));
                return;
            }
            var doors = DoorList.On(entry.Map.Value);
            if (doors.Count == 0)
            {
                GUILayout.Label("No doors are listed for this map", GUILayout.ExpandWidth(true));
                return;
            }
            // After the listed doors comes one more choice: by the extract Vagabond has for the trader.
            doors.Add(new MapDoor { Id = AtExtract, Map = entry.Map.Value, Label = "By Vagabond's extract for them, against the nearest wall" });
            int at = doors.FindIndex(d => d.Id == entry.Door.Value);
            if (GUILayout.Button("<", GUILayout.Width(28f)))
            {
                at = at <= 0 ? doors.Count - 1 : at - 1;
                entry.Door.Value = doors[at].Id;
            }
            string text = at < 0 ? "The first free door" : (at + 1) + " of " + doors.Count + ": " + doors[at].Label;
            if (at >= 0 && doors[at].Id != AtExtract)
            {
                var other = Entries.FirstOrDefault(e => !ReferenceEquals(e, entry) && e.Map.Value == entry.Map.Value && e.Door.Value == doors[at].Id);
                if (other != null)
                {
                    text += "  (taken by " + other.Name + ")";
                }
            }
            GUILayout.Label(text, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(28f)))
            {
                at = at >= doors.Count - 1 ? 0 : at + 1;
                entry.Door.Value = doors[at].Id;
            }
        }

        /// <summary>For the test harness: set a trader's place by words. Returns what was set.</summary>
        public static string Set(string word, string map, string door, string room, string model)
        {
            var entry = Entries.FirstOrDefault(e => e.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
            if (entry == null)
            {
                return "FAILED no trader settings match '" + word + "'; there are: " + string.Join(", ", Entries.Select(e => e.Name));
            }
            entry.Map.Value = map;
            if (door != null)
            {
                entry.Door.Value = door;
            }
            if (room != null && entry.Room != null)
            {
                entry.Room.Value = room;
            }
            if (model != null && entry.Model != null)
            {
                entry.Model.Value = model;
            }
            return entry.Name + " set to " + entry.Map.Value + ", door '" + entry.Door.Value + "'"
                   + (entry.Room != null ? ", room " + entry.Room.Value + ", figure " + entry.Model.Value : "");
        }

        public static string Report()
        {
            return string.Join("; ", Entries.Select(e => e.Name + (e.Custom ? "*" : "") + "=" + e.Map.Value + "/" + e.Door.Value
                                                        + (e.Room != null ? "/" + e.Room.Value + "/" + e.Model.Value : "")));
        }
    }

#pragma warning disable 0169, 0414, 0649
    /// <summary>
    /// Read by the settings window (the Configuration Manager) by the names of its fields, to draw a
    /// setting in a way of its own. The class has to be called exactly this.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? ShowRangeAsPercent;
        public Action<ConfigEntryBase> CustomDrawer;
        public bool? Browsable;
        public string Category;
        public object DefaultValue;
        public bool? HideDefaultButton;
        public bool? HideSettingName;
        public string Description;
        public string DispName;
        public int? Order;
        public bool? ReadOnly;
        public bool? IsAdvanced;
    }
#pragma warning restore 0169, 0414, 0649
}
