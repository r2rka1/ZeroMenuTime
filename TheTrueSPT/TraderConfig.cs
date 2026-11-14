using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace TheTrueSPT
{
    /// <summary>One trader spot on one map. Edit traders.json to move, add or remove spots.</summary>
    public class TraderSpot
    {
        public string Name = "";
        public string TraderId = "";
        /// <summary>Map id as the game reports it (bigmap, Woods, Shoreline, ...). Several allowed.</summary>
        public List<string> Maps = new List<string>();
        public float[] Position = { 0f, 0f, 0f };
        public float RotationY = 0f;
        public bool Enabled = true;
        /// <summary>For doors: the map already has a door here, so use that one and draw nothing.</summary>
        public bool Hidden;
        /// <summary>For doors: the position is where the player stood, back to the door or wall; move it back onto that.</summary>
        public bool AtFeet;
    }

    public static class TraderConfig
    {
        public const string Prapor = "54cb50c76803fa8b248b4571";
        public const string Therapist = "54cb57776803fa99248b456e";
        public const string Fence = "579dc571d53a0658a154fbec";
        public const string Skier = "58330581ace78e27b8b10cee";
        public const string Peacekeeper = "5935c25fb3acc3127c3d8cd9";
        public const string Mechanic = "5a7c2eca46aef81a7ca2145d";
        public const string Ragman = "5ac3b934156ae10c4430e83c";
        public const string Jaeger = "5c0647fdd443bc2504c2d371";

        public static string NameOf(string traderId)
        {
            switch (traderId)
            {
                case Prapor: return "Prapor";
                case Therapist: return "Therapist";
                case Fence: return "Fence";
                case Skier: return "Skier";
                case Peacekeeper: return "Peacekeeper";
                case Mechanic: return "Mechanic";
                case Ragman: return "Ragman";
                case Jaeger: return "Jaeger";
                default: return "the trader";
            }
        }

        public static string Dir => Path.Combine(BepInEx.Paths.PluginPath, "TheTrueSPT");
        public static string FilePath => Path.Combine(Dir, "traders.json");
        public static string CapturePath => Path.Combine(Dir, "captured_positions.txt");

        public static string DoorsPath => Path.Combine(Dir, "doors.json");

        /// <summary>
        /// A hand-picked place for a trader's door on a map, for where the automatic choice of wall
        /// is poor. doors.json holds a list of the same entries as traders.json: TraderId, Maps,
        /// Position (on the ground) and RotationY (the way the door faces). Null if there is none.
        /// </summary>
        public static TraderSpot DoorFor(string traderId, string map)
        {
            try
            {
                var doors = File.Exists(DoorsPath)
                    ? JsonConvert.DeserializeObject<List<TraderSpot>>(File.ReadAllText(DoorsPath)) ?? new List<TraderSpot>()
                    : new List<TraderSpot>();
                // Doors picked by hand; an entry in doors.json for the same trader and map comes first.
                doors.Add(new TraderSpot
                {
                    // The wooden door in the corridor right at Prapor's spot.
                    Name = "Prapor", TraderId = Prapor, Maps = new List<string> { "TarkovStreets" },
                    Position = new[] { -32.50f, 6.60f, -108.74f }, RotationY = 0f, Hidden = true,
                });
                doors.Add(new TraderSpot
                {
                    // The shut door of the shed north of his spot; picked in game, standing with the back to it.
                    Name = "Skier", TraderId = Skier, Maps = new List<string> { "bigmap" },
                    Position = new[] { 88.95f, 1.33f, -106.78f }, RotationY = 140f, Hidden = true, AtFeet = true,
                });
                doors.Add(new TraderSpot
                {
                    // A shut door by his spot on Streets; picked in game, standing with the back to it.
                    Name = "Fence", TraderId = Fence, Maps = new List<string> { "TarkovStreets" },
                    Position = new[] { 73.89f, -2.05f, 48.81f }, RotationY = 90f, Hidden = true, AtFeet = true,
                });
                foreach (var door in doors)
                {
                    if (door.Enabled && door.TraderId == traderId && door.Position != null && door.Position.Length >= 3
                        && door.Maps.Exists(m => string.Equals(m, map, StringComparison.OrdinalIgnoreCase)))
                    {
                        return door;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read doors.json: " + e);
            }
            return null;
        }

        public static List<TraderSpot> Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(Defaults(), Formatting.Indented));
                }
                var spots = JsonConvert.DeserializeObject<List<TraderSpot>>(File.ReadAllText(FilePath));
                return spots ?? new List<TraderSpot>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read traders.json: " + e);
                return new List<TraderSpot>();
            }
        }

        // Starting spots, taken from the 3.11 In-Raid Traders mod. Coordinates may need adjusting for this client.
        private static List<TraderSpot> Defaults()
        {
            return new List<TraderSpot>
            {
                Spot("Prapor", Prapor, new[] { "Woods" }, -655.235f, 8.702f, 179.3f),
                Spot("Jaeger", Jaeger, new[] { "Woods" }, -255.45f, 8.75f, 14.5f),
                Spot("Therapist", Therapist, new[] { "TarkovStreets" }, -180.89f, 3.53f, 255.56f),
                Spot("Fence", Fence, new[] { "TarkovStreets" }, -71.7f, 4f, 265.3f),
                Spot("Skier", Skier, new[] { "Shoreline" }, -911.8f, -48.4f, 262.6f),
                Spot("Peacekeeper", Peacekeeper, new[] { "Shoreline" }, -152.5f, -14.1f, -301.3f),
                Spot("Ragman", Ragman, new[] { "Interchange" }, 261.4f, 22f, -4.8f),
                Spot("Mechanic", Mechanic, new[] { "factory4_day", "factory4_night" }, -15.21f, -1.23f, 43.4f),
            };
        }

        private static TraderSpot Spot(string name, string id, string[] maps, float x, float y, float z)
        {
            return new TraderSpot
            {
                Name = name,
                TraderId = id,
                Maps = new List<string>(maps),
                Position = new[] { x, y, z },
            };
        }
    }
}
