using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroMenuTime
{
    /// <summary>A door of a map that a trader's room can be put behind.</summary>
    public class MapDoor
    {
        public string Id;
        /// <summary>The map, by its short name.</summary>
        public string Map;
        /// <summary>What the player is told: where the door is and what it looks like.</summary>
        public string Label;
        public float X, Y, Z;
        /// <summary>The way the door faces: towards where the player stands.</summary>
        public float Yaw;
        /// <summary>The position is where someone stood with their back to the door, not the door itself.</summary>
        public bool AtFeet;
        /// <summary>The trader this door was picked for, if any.</summary>
        public string For;
    }

    /// <summary>
    /// The doors a trader can be given, map by map. Every one is a door of the map's own scenery
    /// that cannot be opened, has nothing behind it the player can get to, and has no way out of
    /// the map at it. They were found with the door survey (<see cref="DoorSurvey"/>) and looked
    /// over by eye in the game.
    /// </summary>
    public static class DoorList
    {
        /// <summary>
        /// The maps, by the short names used in the settings. Reserve is not among them: with a
        /// trader's room on that map the game closed by itself in testing, and until that is
        /// understood no trader is put there from the settings.
        /// </summary>
        public static readonly string[] Maps = { "Customs", "Factory", "Ground Zero", "Interchange", "Lighthouse", "Shoreline", "Streets", "Woods" };

        /// <summary>The short name of the map the game calls this; empty if it is none of them.</summary>
        public static string MapOf(string location)
        {
            switch ((location ?? "").ToLowerInvariant())
            {
                case "bigmap": return "Customs";
                case "factory4_day":
                case "factory4_night": return "Factory";
                case "sandbox":
                case "sandbox_high": return "Ground Zero";
                case "interchange": return "Interchange";
                case "lighthouse": return "Lighthouse";
                case "rezervbase": return "Reserve";
                case "shoreline": return "Shoreline";
                case "tarkovstreets": return "Streets";
                case "woods": return "Woods";
                default: return "";
            }
        }

        public static List<MapDoor> On(string map)
        {
            return All.Where(d => d.Map == map).ToList();
        }

        public static MapDoor Find(string id)
        {
            return string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static MapDoor D(string id, string map, string label, float x, float y, float z, float yaw, string forTrader = null, bool atFeet = false)
        {
            return new MapDoor { Id = id, Map = map, Label = label, X = x, Y = y, Z = z, Yaw = yaw, For = forTrader, AtFeet = atFeet };
        }

        public static readonly List<MapDoor> All = new List<MapDoor>
        {
            // The doors the traders of the game have had from the start.
            D("customs-skier", "Customs", "Shed north of Skier's depot", 88.95f, 1.33f, -106.78f, 140f, TraderConfig.Skier, true),
            D("factory-mechanic", "Factory", "Wooden door in the basement corridor", 1.09f, -2.63f, 57.45f, 270f, TraderConfig.Mechanic),
            D("groundzero-therapist", "Ground Zero", "Office door, second floor of the TerraGroup building", -26.85f, 29.72f, 92.94f, 180f, TraderConfig.Therapist),
            D("interchange-ragman", "Interchange", "Shut door of the mall by Ragman's spot", -51.22f, 21.33f, 46.19f, 7f, TraderConfig.Ragman, true),
            D("lighthouse-fence", "Lighthouse", "Shipping container in the drug lab", -125.39f, 10.6f, -839.8f, 90f, TraderConfig.Fence),
            D("shoreline-peacekeeper", "Shoreline", "Steel gate of checkpoint 11", -968.6f, -59.6f, 378.95f, 162f, TraderConfig.Peacekeeper),
            D("streets-prapor", "Streets", "Wooden door in the corridor at Prapor's", -32.50f, 6.60f, -108.74f, 0f, TraderConfig.Prapor),
            D("streets-fence", "Streets", "Shut door by Fence's spot", 73.89f, -2.05f, 48.81f, 90f, TraderConfig.Fence, true),
            D("woods-jaeger", "Woods", "Steel door of the stone bunker in the rock face", -156.25f, 51.2f, -272.45f, 357f, TraderConfig.Jaeger),
            // Found by the door survey and looked over by eye, map by map.
            D("customs-00", "Customs", "Steel door in a steel wall, Customs area, 20 m from Crossroads", -326.87f, -0.62f, -67.87f, 88.0f),
            D("customs-01", "Customs", "Red door beside a hanging tarp, Customs area, 65 m from Trailer Park", -314.69f, 0.86f, -169.57f, 92.0f),
            D("customs-02", "Customs", "Garage door 2, Customs area, 35 m from Crossroads", -312.82f, 1.01f, -115.72f, 92.0f),
            D("customs-03", "Customs", "Garage door 115, Customs area, 55 m from Crossroads", -280.4f, 0.87f, -98.6f, 271.0f),
            D("customs-04", "Customs", "Garage door 1, under a tarp, Customs area, 65 m from Trailer Park", -272.66f, 0.86f, -183.25f, 2.0f),
            D("customs-05", "Customs", "Shed door with pallets leaning on it, Customs area, 40 m from Trailer Park", -271.73f, 0.18f, -228.91f, 4.0f),
            D("customs-06", "Customs", "Garage door 42, Customs area, 70 m from Crossroads", -266.25f, 0.8f, -66.83f, 154.0f),
            D("customs-07", "Customs", "Garage door 58, Customs area, 90 m from Crossroads", -257.51f, 0.87f, -131.44f, 272.0f),
            D("customs-08", "Customs", "Red garage door, Wade area, 30 m from RUAF Roadblock", 12.9f, 1.11f, -116.9f, 262.0f),
            D("customs-10", "Customs", "Shed door, Wade area, 65 m from Boiler Room Basement (Co-op)", 83.48f, 1.25f, -108.18f, 165.0f),
            D("customs-14", "Customs", "Blast door, Factory Center area, 160 m from Old Gas Station", 459.09f, -2.57f, -111.25f, 97.0f),
            D("streets-00", "Streets", "Plain door indoors, Mvd area, 70 m from Sewer River", -247.85f, 2.94f, 153.31f, 0.0f),
            D("streets-02", "Streets", "Red door between two windows, Factory area, 95 m from Damaged House", -158.44f, 2.29f, 315.5f, 115.0f),
            D("streets-04", "Streets", "Padded door 2 in a stairwell, 75 m from Stylobate Building Elevator", -60.44f, 3.19f, -145.69f, 90.0f),
            D("streets-07", "Streets", "Dark door, 150 m from Collapsed Crane", 65.58f, 6.86f, 292.01f, 270.0f),
            D("streets-08", "Streets", "Dark door by a green pillar, 205 m from Expo Checkpoint", 100.02f, 4.06f, -273.15f, 270.0f),
            D("streets-10", "Streets", "Grey door under the stairs, 140 m from Primorsky Ave Taxi V-Ex", 138.01f, 3.06f, 452.54f, 240.0f),
            D("streets-11", "Streets", "Wooden door 4 in the hotel, 85 m from Expo Checkpoint", 140.19f, 3.17f, -64.86f, 270.0f),
            D("streets-12", "Streets", "Steel door in a concrete wall, 60 m from Collapsed Crane", 217.74f, 3.91f, 330.59f, 270.0f),
            D("streets-13", "Streets", "Red garage door, 70 m from Collapsed Crane", 219.03f, 0.39f, 200.7f, 90.0f),
            D("streets-15", "Streets", "Padded door 2, 60 m from Scav Checkpoint", 271.76f, -4.52f, 89.91f, 180.0f),
            D("interchange-00", "Interchange", "Rusty door of the substation, by a warning sign, Power Station area, 85 m from Power Station V-Ex", -248.46f, 21.33f, -284.24f, 90.0f),
            D("interchange-05", "Interchange", "Back-room door, OLIPark area, 65 m from Saferoom Exfil", -103.26f, 27.09f, 80.51f, 0.0f),
            D("interchange-06", "Interchange", "Back-room door, Goshan area, 155 m from Saferoom Exfil", -71.3f, 27.09f, -111.22f, 270.0f),
            D("interchange-07", "Interchange", "Door beside a price board, Goshan area, 65 m from Saferoom Exfil", -57.98f, 27.14f, -21.13f, 180.0f),
            D("interchange-08", "Interchange", "Grey door in a white wall, Goshan area, 155 m from Saferoom Exfil", -52.7f, 27.09f, -111.25f, 90.0f),
            D("interchange-10", "Interchange", "Staff door with a sign beside it, IDEA area, 215 m from Saferoom Exfil", -46.42f, 27.09f, -171.03f, 180.0f),
            D("interchange-13", "Interchange", "Door between shop shelves, OLIPark area, 110 m from Saferoom Exfil", 61.29f, 36.58f, 39.78f, 317.0f),
            D("interchange-14", "Interchange", "Door beside the price signs, Center Bot area, 115 m from Saferoom Exfil", 65.52f, 36.58f, 29.55f, 0.0f),
            D("interchange-15", "Interchange", "Back-room door, OLI area, 145 m from Saferoom Exfil", 84.75f, 27.09f, 104.67f, 0.0f),
            D("factory-01", "Factory", "Red door in a brick wall, by a barrel, 30 m from Med Tent Gate", 1.52f, 0.04f, -37.81f, 270.0f),
            D("factory-02", "Factory", "Steel hatch door, 50 m from Med Tent Gate", 4.47f, -3.89f, -17.47f, 270.0f),
            D("factory-03", "Factory", "Steel hatch door by ammunition crates, 45 m from Med Tent Gate", 4.58f, -3.89f, -25.27f, 270.0f),
            D("factory-06", "Factory", "Wooden door, 50 m from Cellars", 25.16f, -2.67f, -31.86f, 270.0f),
            D("factory-09", "Factory", "Red door in a green wall, 30 m from Courtyard Gate", 29.7f, 0.99f, 40.49f, 180.0f),
            D("factory-11", "Factory", "Red door under the pipes, 45 m from Cellars", 36.98f, -2.39f, 0.69f, 270.0f),
            D("factory-13", "Factory", "Blast door, 15 m from Cellars", 57.2f, -2.27f, -29.91f, 0.0f),
            D("groundzero-00", "Ground Zero", "Office door with a keypad, by a water cooler, 85 m from Police Cordon V-Ex", -62.85f, 24.18f, 42.7f, 90.0f),
            D("groundzero-01", "Ground Zero", "Door between beige wall panels, 75 m from Scav Checkpoint (Co-op)", -26.34f, 24.18f, -28.39f, 0.0f),
            D("groundzero-02", "Ground Zero", "Office door with a keypad, floor 1, 60 m from Police Cordon V-Ex", -24.06f, 24.18f, 57.47f, 270.0f),
            D("groundzero-03", "Ground Zero", "Grey door by a coat stand, 25 m from Police Cordon V-Ex", -22.05f, 29.67f, 92.67f, 180.0f),
            D("groundzero-10", "Ground Zero", "Door with a small sign on it, 140 m from Police Cordon V-Ex", 122.4f, 24.4f, 127.02f, 270.0f),
            D("groundzero-11", "Ground Zero", "Door in a wall of wooden slats, 90 m from Emercom Checkpoint", 124.65f, 22.77f, -10.85f, 180.0f),
            D("groundzero-12", "Ground Zero", "Staff door, 170 m from Nakatani Basement Stairs", 139.17f, 23.39f, 265.92f, 0.0f),
            D("groundzero-14", "Ground Zero", "Plain door, 65 m from Mira Ave (Flare)", 165.9f, 16.93f, -2.27f, 0.0f),
            D("shoreline-01", "Shoreline", "Bunker hatch D2, 155 m from Road to North V-Ex", -389.05f, -2.86f, -386.63f, 336.0f),
            D("shoreline-02", "Shoreline", "Grey door on a porch, 320 m from Climber's Trail", -361.55f, -3.74f, -80.01f, 180.0f),
            D("shoreline-06", "Shoreline", "Rusty door of a white brick hut, Power Station area, 365 m from Pier Boat", -248.58f, -41.01f, 205.29f, 178.0f),
            D("shoreline-07", "Shoreline", "Dark door in a purple wall, 225 m from Climber's Trail", -243.6f, -2.49f, -138.28f, 180.0f),
            D("shoreline-09", "Shoreline", "Door on a porch, under two lamps, Bus Station area, 290 m from Climber's Trail", -142.19f, -3.74f, -80.03f, 180.0f),
            D("shoreline-10", "Shoreline", "Green panelled door of a cabin, Green Houses area, 350 m from Tunnel", 103.0f, -48.41f, 102.78f, 180.0f),
            D("shoreline-11", "Shoreline", "Plank door of a log cabin, Green Houses area, 170 m from Tunnel", 273.22f, -56.34f, 186.83f, 180.0f),
            D("shoreline-12", "Shoreline", "Plank door of a wooden shed, 235 m from Tunnel", 285.32f, -56.39f, 102.38f, 270.0f),
            D("shoreline-13", "Shoreline", "Plank door of a log cabin, Start Village area, 165 m from Tunnel", 330.13f, -56.36f, 159.72f, 270.0f),
            D("woods-02", "Woods", "Rusty door of the white brick substation, left, 140 m from UN Roadblock", -614.67f, 8.13f, 169.13f, 169.0f),
            D("woods-03", "Woods", "Rusty door of the white brick substation, right, 140 m from UN Roadblock", -612.41f, 8.13f, 169.59f, 168.0f),
            D("woods-05", "Woods", "Panelled door indoors, 120 m from Bridge V-Ex", -512.64f, 15.54f, -388.42f, 0.0f),
            D("woods-06", "Woods", "Red garage door, 95 m from Bridge V-Ex", -496.19f, 14.99f, -409.36f, 90.0f),
            D("woods-07", "Woods", "Plank door of a log cabin, 100 m from Bridge V-Ex", -458.57f, 15.34f, -408.24f, 0.0f),
            D("lighthouse-00", "Lighthouse", "Plank door of a wooden house, Village area, 190 m from Path to Shoreline", -189.58f, 12.28f, -194.36f, 270.0f),
            D("lighthouse-03", "Lighthouse", "Steel door in grey siding, 300 m from Road to Military Base V-Ex", -171.24f, 4.65f, -527.41f, 90.0f),
            D("lighthouse-05", "Lighthouse", "Arched door of a dark wooden house, 115 m from Mountain Pass", -114.61f, 33.36f, 94.59f, 90.0f),
            D("lighthouse-06", "Lighthouse", "Blue garage door, Bridge area, 280 m from Mountain Pass", -91.18f, 6.04f, -274.42f, 90.0f),
            D("lighthouse-07", "Lighthouse", "Panelled door indoors, Chalet area, 125 m from Mountain Pass", -84.91f, 18.28f, -95.77f, 90.0f),
            D("lighthouse-08", "Lighthouse", "Panelled door by a fire point, Bridge area, 305 m from Mountain Pass", -75.34f, 9.05f, -291.14f, 92.0f),
            D("lighthouse-09", "Lighthouse", "Boarded-up door of a trailer, Bridge area, 265 m from Mountain Pass", -65.27f, 5.71f, -248.65f, 92.0f),
            D("lighthouse-10", "Lighthouse", "Grey door in white siding, Treatment Containers area, 195 m from Armored Train", -52.72f, 1.65f, -689.86f, 270.0f),
            D("lighthouse-11", "Lighthouse", "Door beside a high-voltage box, Roof Beach area, 240 m from Armored Train", 32.46f, 4.91f, -636.15f, 0.0f),
            D("lighthouse-13", "Lighthouse", "Rusty shed door under the stairs, Treatment Beach area, 320 m from Armored Train", 47.95f, 4.79f, -558.53f, 270.0f),
        };
    }
}
