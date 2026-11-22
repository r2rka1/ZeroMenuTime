using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using EFT.EnvironmentEffect;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// A trader's own room. It sits far outside the playable map and is reached through a door at the
    /// trader's spot, so bots can neither walk nor shoot into it and it never clashes with scenery.
    ///
    /// This is the blockout: plain walls, a table and a lamp made in code. The dressed room built in
    /// Unity replaces the geometry later; the named points (entry, trader's stand, table) stay.
    /// </summary>
    public class TraderRoom
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> Depth;
        public static ConfigEntry<bool> RedrawPhysics;

        // Inside measurements, in metres.
        private const float Width = 6f, Height = 3f, Length = 7f, Wall = 0.3f;
        private const float TableHeight = 0.8f;

        public string TraderId;
        public string TraderName;
        /// <summary>Whose room this is furnished like, when not the trader's own ("Base" for plain).</summary>
        public string Style;
        public GameObject Root;

        /// <summary>True for the plain room made in code, which takes furniture from the game.</summary>
        public bool Shell;
        public bool Dressed;
        public bool DoorDressed;
        public IndoorTrigger Indoor;

        /// <summary>Where the player stands after coming in, and the way they face.</summary>
        public Vector3 Entry;
        public float EntryYaw;

        /// <summary>The floor under the trader, and the way the trader faces.</summary>
        public Vector3 TraderStand;
        public float TraderYaw;

        /// <summary>The middle of the table top, and its usable size.</summary>
        public Vector3 TableTop;
        public Vector2 TableSize = new Vector2(1.6f, 0.8f);

        /// <summary>Exact places on the table for items, when the room comes from the bundle.</summary>
        public List<Vector3> TableSlots;

        /// <summary>The door inside the room that leads back out.</summary>
        public RoomDoor ExitDoor;

        /// <summary>The door on the map that leads in.</summary>
        public RoomDoor OutsideDoor;

        public static readonly List<TraderRoom> Rooms = new List<TraderRoom>();

        /// <summary>Testing aid: build every room in this trader's style, to look at one without travelling to it.</summary>
        public static string LookOverride;

        public static void Bind(ConfigFile config)
        {
            // BepInEx forbids = \n \t \ " ' [ ] in section and key names.
            Enabled = config.Bind("Trader rooms", "Put traders in rooms", true,
                "Each trader gets a room of their own, entered through a door at their spot. Turn off to have traders stand in the open as before. Applies from the next raid.");
            RedrawPhysics = config.Bind("Trader rooms", "Redraw the physics box", true, new ConfigDescription(
                "The rooms lie outside the part of the world the physics engine looks after, so that part is redrawn to take them in; without it, things dropped in a room fall through the floor. Turn off only to find out whether a crash comes from it.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            Depth = config.Bind("Trader rooms", "Room depth below the door", 300f,
                "How far under the map the rooms are built, in metres. Only change this if a map misbehaves down there.");
        }

        public static TraderRoom For(string traderId)
        {
            foreach (var room in Rooms)
            {
                if (room.TraderId == traderId)
                {
                    return room;
                }
            }
            return null;
        }

        /// <summary>The indoor area of whichever room holds this point, if any.</summary>
        public static IndoorTrigger IndoorAt(Vector3 position)
        {
            foreach (var room in Rooms)
            {
                if (room.Indoor != null)
                {
                    var found = room.Indoor.Check(position);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return HideoutDoors.IndoorAt(position);
        }

        /// <summary>Builds the room for a trader whose door is at the given spot on the map.</summary>
        public static TraderRoom Build(string traderId, string traderName, Vector3 doorGround, string style)
        {
            var room = Build(traderId, traderName, doorGround, style, true);
            room.Style = style;
            return room;
        }

        private static TraderRoom Build(string traderId, string traderName, Vector3 doorGround, string style, bool styled)
        {
            // Rooms on the same map are kept well apart so one cannot be seen or heard from another.
            var origin = doorGround + new Vector3(Rooms.Count * 40f, -Mathf.Abs(Layout.Depth), 0f);
            // Each trader has a room of their own in the bundle; anyone without one gets the plain shop.
            // With furniture from the game, the room is a plain shell made here and filled in once the
            // hideout interior is at hand; otherwise it is one of the rooms built in Unity.
            bool shell = Layout.Shell;
            var prefab = shell ? null : RoomBundle.Prefab("TraderRoom_" + (LookOverride ?? style ?? traderName)) ?? RoomBundle.Prefab("TraderRoom_Base");
            if (prefab != null)
            {
                try
                {
                    return FromPrefab(prefab, traderId, traderName, origin);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Room from the bundle failed for " + traderName + ", using the plain one: " + e);
                }
            }

            var root = new GameObject("ZeroMenuTime room -- " + traderName);
            root.transform.position = origin;
            int solid = SolidLayer();

            var wallColour = new Color(0.42f, 0.43f, 0.40f);
            var floorColour = new Color(0.20f, 0.19f, 0.18f);
            var woodColour = new Color(0.36f, 0.25f, 0.15f);

            // The origin is the middle of the floor; the room runs along z, the player comes in at -z.
            Box(root, "floor", new Vector3(0f, -Wall / 2f, 0f), new Vector3(Width + 2 * Wall, Wall, Length + 2 * Wall), floorColour, solid);
            Box(root, "ceiling", new Vector3(0f, Height + Wall / 2f, 0f), new Vector3(Width + 2 * Wall, Wall, Length + 2 * Wall), wallColour, solid);
            Box(root, "wall west", new Vector3(-(Width + Wall) / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Length + 2 * Wall), wallColour, solid);
            Box(root, "wall east", new Vector3((Width + Wall) / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Length + 2 * Wall), wallColour, solid);
            Box(root, "wall south", new Vector3(0f, Height / 2f, -(Length + Wall) / 2f), new Vector3(Width, Height, Wall), wallColour, solid);
            Box(root, "wall north", new Vector3(0f, Height / 2f, (Length + Wall) / 2f), new Vector3(Width, Height, Wall), wallColour, solid);

            // The table stands against the east wall, to the player's right as they come in.
            var tableCentre = new Vector3(Width / 2f - 0.6f, TableHeight / 2f, -0.4f);
            Box(root, "table", tableCentre, new Vector3(0.8f, TableHeight, 1.6f), woodColour, solid);

            var lamp = new GameObject("lamp");
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localPosition = new Vector3(0f, Height - 0.3f, 0f);
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 12f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.93f, 0.80f);
            light.shadows = LightShadows.None;

            // Tells the game this is indoors: no rain, indoor sound, indoor exposure.
            var indoorBox = new GameObject("indoor area");
            indoorBox.transform.SetParent(root.transform, false);
            indoorBox.transform.localPosition = new Vector3(0f, Height / 2f, 0f);
            indoorBox.transform.localScale = new Vector3(Width + 2 * Wall, Height + 2 * Wall, Length + 2 * Wall);
            var indoor = indoorBox.AddComponent<IndoorTrigger>();
            indoor.IsBunker = true;
            indoor.Reinit();

            var room = new TraderRoom
            {
                TraderId = traderId,
                TraderName = traderName,
                Shell = true,
                Root = root,
                Indoor = indoor,
                Entry = origin + new Vector3(0f, 0.05f, -Length / 2f + 1.2f),
                EntryYaw = 0f,
                TraderStand = origin + new Vector3(0f, 0f, Length / 2f - 1.3f),
                TraderYaw = 180f,
                TableTop = origin + new Vector3(tableCentre.x, TableHeight, tableCentre.z),
                TableSize = new Vector2(0.8f, 1.6f),
            };
            room.ExitDoor = RoomDoor.Spawn("Leave the room", origin + new Vector3(0f, RoomDoor.Height / 2f, -Length / 2f + RoomDoor.Thickness / 2f), 0f);
            Rooms.Add(room);
            Plugin.Log.LogInfo("Room for " + traderName + " built at " + origin.ToString("F1"));
            return room;
        }

        /// <summary>
        /// The physics engine only works out collisions for moving things inside a box drawn around
        /// the map; outside it a dropped item falls through every surface (the player does not,
        /// because walking uses a different kind of check). The rooms lie outside that box, so it is
        /// redrawn here to take in the whole map and the rooms. Call once all rooms are built.
        /// </summary>
        /// <summary>The box as last drawn in this raid.</summary>
        private static Bounds? _box;
        private static int _boxWorld;

        public static void ExtendPhysics()
        {
            if (Rooms.Count == 0 && !HideoutBunker.Loaded)
            {
                return;
            }
            if (RedrawPhysics != null && !RedrawPhysics.Value)
            {
                Plugin.Log.LogInfo("Physics box left alone by the setting");
                return;
            }
            // The map's own box was made deeper and wider when the map loaded (see LevelPhysicsPatch).
            // When that takes in every room and the place of the hideout, nothing is redrawn at all.
            if (LevelPhysicsPatch.Box.HasValue)
            {
                var level = LevelPhysicsPatch.Box.Value;
                float deep = Mathf.Abs(Layout.Depth);
                var needed = new Bounds(new Vector3(0f, -deep, 0f), new Vector3(300f, 100f, 300f));
                foreach (var room in Rooms)
                {
                    if (room.Root != null)
                    {
                        needed.Encapsulate(new Bounds(room.Root.transform.position, new Vector3(30f, 20f, 30f)));
                    }
                }
                if (level.Contains(needed.min) && level.Contains(needed.max))
                {
                    Plugin.Log.LogInfo("Physics box: the map's own, as enlarged at loading, takes in the rooms; nothing redrawn");
                    return;
                }
                // Never drawn again once the raid has begun: on Reserve that closed the game, at the next
                // step through a room's door. A thing dropped in a room outside the box may fall through the floor.
                Plugin.Log.LogWarning("Physics box: the map's own " + level.center.ToString("F0") + " size " + level.size.ToString("F0")
                                      + " does not take in " + needed.center.ToString("F0") + " size " + needed.size.ToString("F0") + "; left as it is");
                return;
            }
            // Only a map that drew no box of its own as it loaded comes this far. Reserve is known to
            // draw one, and to close the game when it is drawn after the raid has begun: never there.
            string location = Comfort.Common.Singleton<EFT.GameWorld>.Instantiated ? Comfort.Common.Singleton<EFT.GameWorld>.Instance.LocationId ?? "" : "";
            if (location.Equals("RezervBase", StringComparison.OrdinalIgnoreCase) && !LevelPhysicsPatch.OldWay)
            {
                Plugin.Log.LogWarning("Physics box: not enlarged as Reserve loaded, and not drawn now; things dropped in the rooms may fall through the floor");
                return;
            }
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                // Measured from the middle of the world, never from where the player happens to be:
                // a player who has fallen out of the map must not drag the box away with them.
                var centre = Vector3.zero;
                var world = new Bounds(centre, Vector3.one);
                // Anything further out than this is not part of the playable map (sky boxes, kill
                // planes), and stretching the box over it would only make the engine's grid coarser.
                var limit = new Bounds(centre, new Vector3(9000f, 3000f, 9000f));
                int counted = 0;
                foreach (var collider in UnityEngine.Object.FindObjectsOfType<Collider>())
                {
                    // Only fixed scenery counts; a dropped item that fell out of the world must not stretch the box.
                    if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != null)
                    {
                        continue;
                    }
                    var bounds = collider.bounds;
                    if (limit.Contains(bounds.min) && limit.Contains(bounds.max))
                    {
                        world.Encapsulate(bounds);
                        counted++;
                    }
                }
                if (counted < 500)
                {
                    // A map has tens of thousands; this few means the map was not what got measured.
                    Plugin.Log.LogWarning("Physics box left alone: only " + counted + " colliders found");
                    return;
                }
                // Room is left from the start for the hideout interior, which is loaded under the middle
                // of the map later in the raid: the box is then drawn once. Drawing it a second time
                // while the raid runs brought the game down on Reserve.
                // A new raid is a new world: nothing drawn before counts.
                int worldNow = Comfort.Common.Singleton<EFT.GameWorld>.Instantiated ? Comfort.Common.Singleton<EFT.GameWorld>.Instance.GetInstanceID() : 0;
                if (worldNow != _boxWorld)
                {
                    _boxWorld = worldNow;
                    _box = null;
                }
                float depth = Mathf.Abs(Layout.Depth);
                world.Encapsulate(new Bounds(new Vector3(0f, -depth, 0f), new Vector3(400f, 2f * depth + 200f, 400f)));
                world.Expand(20f);
                if (_box.HasValue && _box.Value.Contains(world.min) && _box.Value.Contains(world.max))
                {
                    Plugin.Log.LogInfo("Physics box already takes in everything; left as it is");
                    return;
                }
                if (_box.HasValue)
                {
                    // Grown a little beyond what is needed now, so it need not be drawn again soon.
                    world.Encapsulate(_box.Value);
                    world.Expand(50f);
                }
                // What is left outside the box: for the log, to know what the box does not take in.
                int outside = 0;
                var named = new List<string>();
                foreach (var collider in UnityEngine.Object.FindObjectsOfType<Collider>())
                {
                    if (collider.enabled && !world.Intersects(collider.bounds))
                    {
                        outside++;
                        if (named.Count < 6)
                        {
                            named.Add(collider.name + (collider.attachedRigidbody != null ? (collider.attachedRigidbody.isKinematic ? " (moved by the game)" : " (falls)") : "")
                                      + (collider.isTrigger ? " (trigger)" : "") + " at " + collider.bounds.center.ToString("F0"));
                        }
                    }
                }
                Plugin.Log.LogInfo("Physics box: " + outside + " collider(s) lie outside it" + (outside > 0 ? ": " + string.Join("; ", named) : ""));
                Plugin.Log.LogInfo("Physics box: redrawing to centre " + world.center.ToString("F0") + ", size " + world.size.ToString("F0") + ", from " + counted + " colliders");
                Physics.RebuildBroadphaseRegions(world, 8);
                _box = world;
                Plugin.Log.LogInfo("Physics box redrawn to take in the rooms: centre " + world.center.ToString("F0") + ", size "
                                   + world.size.ToString("F0") + ", from " + counted + " colliders in " + timer.ElapsedMilliseconds + " ms");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Redrawing the physics box failed: " + e);
            }
        }

        private static GameObject Box(GameObject parent, string name, Vector3 localCentre, Vector3 size, Color colour, int layer)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent.transform, false);
            box.transform.localPosition = localCentre;
            box.transform.localScale = size;
            box.layer = layer;
            var renderer = box.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
            {
                renderer.material.color = colour;
            }
            MakeSolid(box.GetComponent<Collider>(), layer);
            return box;
        }

        public static int SolidLayer()
        {
            int solid = LayerMask.NameToLayer("HighPolyCollider");
            if (solid < 0)
            {
                solid = LayerMask.NameToLayer("Terrain");
            }
            return solid < 0 ? 0 : solid;
        }

        /// <summary>
        /// The game keeps separate collision layers for bullets, for walking and for terrain, and a
        /// collider on only one of them lets the player fall through. Puts the collider on the first
        /// and adds a copy of it on each of the others.
        /// </summary>
        public static void MakeSolid(Collider collider, int layer)
        {
            if (collider == null || collider.isTrigger)
            {
                return;
            }
            collider.gameObject.layer = layer;
            foreach (var other in new[] { "LowPolyCollider", "Terrain" })
            {
                int otherLayer = LayerMask.NameToLayer(other);
                if (otherLayer < 0 || otherLayer == layer)
                {
                    continue;
                }
                var copy = new GameObject(collider.name + " (" + other + ")");
                copy.transform.SetParent(collider.transform, false);
                copy.layer = otherLayer;
                var box = collider as BoxCollider;
                var mesh = collider as MeshCollider;
                if (box != null)
                {
                    var boxCopy = copy.AddComponent<BoxCollider>();
                    boxCopy.center = box.center;
                    boxCopy.size = box.size;
                }
                else if (mesh != null)
                {
                    var meshCopy = copy.AddComponent<MeshCollider>();
                    meshCopy.sharedMesh = mesh.sharedMesh;
                    meshCopy.convex = mesh.convex;
                }
                else
                {
                    UnityEngine.Object.Destroy(copy);
                }
            }
        }

        public static Transform FindDeep(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>The dressed room built in Unity. The named children say where everything is.</summary>
        private static TraderRoom FromPrefab(GameObject prefab, string traderId, string traderName, Vector3 origin)
        {
            var root = UnityEngine.Object.Instantiate(prefab, origin, Quaternion.identity);
            root.name = "ZeroMenuTime room -- " + traderName;

            var entry = FindDeep(root.transform, "Entry");
            var stand = FindDeep(root.transform, "TraderStand");
            var exit = FindDeep(root.transform, "ExitDoor");
            var indoorBox = FindDeep(root.transform, "IndoorBounds");
            if (entry == null || stand == null || exit == null || indoorBox == null)
            {
                UnityEngine.Object.Destroy(root);
                throw new InvalidOperationException("the room prefab lacks Entry, TraderStand, ExitDoor or IndoorBounds");
            }

            int solid = SolidLayer();
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                // The way out is something to look at and use, not a wall.
                if (collider.transform != exit)
                {
                    MakeSolid(collider, solid);
                }
            }

            var indoor = indoorBox.gameObject.AddComponent<IndoorTrigger>();
            indoor.IsBunker = true;
            indoor.Reinit();

            var slots = new List<Vector3>();
            var middle = Vector3.zero;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith("TableSlot_", StringComparison.Ordinal))
                {
                    slots.Add(child.position);
                    middle += child.position;
                }
            }


            var room = new TraderRoom
            {
                TraderId = traderId,
                TraderName = traderName,
                Root = root,
                Indoor = indoor,
                Entry = entry.position,
                EntryYaw = entry.eulerAngles.y,
                TraderStand = stand.position,
                TraderYaw = stand.eulerAngles.y,
                TableTop = slots.Count > 0 ? middle / slots.Count : origin + Vector3.up * TableHeight,
                TableSize = new Vector2(0.8f, 1.6f),
                TableSlots = slots.Count > 0 ? slots : null,
            };
            room.ExitDoor = RoomDoor.Attach(exit.gameObject, "Leave the room");
            Rooms.Add(room);
            Plugin.Log.LogInfo("Room for " + traderName + " placed from the bundle at " + origin.ToString("F1") + " with " + slots.Count + " table slots");
            return room;
        }

        public static void DestroyAll()
        {
            foreach (var room in Rooms)
            {
                if (room.Root != null)
                {
                    UnityEngine.Object.Destroy(room.Root);
                }
            }
            Rooms.Clear();
            RewardTable.Clear();
            RoomDoor.DestroyAll();
        }
    }

    /// <summary>
    /// The rooms and doors built in the Unity project (Unity/TraderRooms). Optional: without the
    /// bundle file the mod builds plain rooms in code.
    /// </summary>
    public static class RoomBundle
    {
        private const string BundleName = "traderrooms";
        private static AssetBundle _bundle;
        private static bool _tried;

        public static GameObject Prefab(string name)
        {
            if (!_tried)
            {
                _tried = true;
                try
                {
                    // A hot reload forgets the bundle here while the game still holds the old one;
                    // let go of that so a rebuilt file is picked up.
                    foreach (var loaded in AssetBundle.GetAllLoadedAssetBundles())
                    {
                        if (loaded != null && loaded.name == BundleName)
                        {
                            loaded.Unload(false);
                        }
                    }
                    // Read into memory rather than opened in place, so the file is never locked and
                    // can be replaced while the game runs.
                    string path = System.IO.Path.Combine(TraderConfig.Dir, BundleName);
                    if (System.IO.File.Exists(path))
                    {
                        _bundle = AssetBundle.LoadFromMemory(System.IO.File.ReadAllBytes(path));
                    }
                    Plugin.Log.LogInfo(_bundle != null
                        ? "Room bundle loaded: " + string.Join(", ", _bundle.GetAllAssetNames())
                        : "No room bundle at " + path + "; building plain rooms.");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Loading the room bundle failed: " + e);
                }
            }
            return _bundle != null ? _bundle.LoadAsset<GameObject>(name) : null;
        }

        /// <summary>Lets the bundle file be replaced while the game runs.</summary>
        public static void Unload()
        {
            if (_bundle != null)
            {
                _bundle.Unload(false);
                _bundle = null;
            }
            _tried = false;
        }
    }

    /// <summary>
    /// The game builds its list of indoor areas once, when the map loads, so a room added afterwards
    /// is unknown to it. These two patches answer "is this point indoors?" for our rooms.
    /// </summary>
    public class RoomIndoorPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EnvironmentManager).GetMethod("TryFindTriggerByPos",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        }

        [PatchPostfix]
        public static void Postfix(Vector3 pos, ref IndoorTrigger __result)
        {
            if (__result == null)
            {
                __result = TraderRoom.IndoorAt(pos);
            }
        }
    }

    /// <summary>The same question, as the game's background check asks it.</summary>
    public class RoomIndoorBasePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EnvironmentManager).BaseType.GetMethod("TryFindTriggerByPos",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        }

        [PatchPostfix]
        public static void Postfix(Vector3 pos, ref IndoorTrigger __result)
        {
            if (__result == null)
            {
                __result = TraderRoom.IndoorAt(pos);
            }
        }
    }

    /// <summary>
    /// Each map says what box the physics engine is to look after, and the game draws it once, as the
    /// map loads. The rooms and the hideout lie under the map, outside that box, where a dropped
    /// item would fall through every floor. Here a deeper and somewhat wider box is drawn right after
    /// the game draws the map's own, so it never has to be drawn again while a raid runs: doing
    /// that brought the game down on Reserve.
    /// </summary>
    public class LevelPhysicsPatch : ModulePatch
    {
        /// <summary>The box as the game drew it for the map that is loaded.</summary>
        public static Bounds? Box;
        /// <summary>For the test harness: leave the map's box alone at loading, as versions before 2.1.0 did.</summary>
        public static bool OldWay;
        /// <summary>For the test harness: this many cells a side in place of the map's own number; 0 leaves it.</summary>
        public static int TestCells;

        protected override MethodBase GetTargetMethod()
        {
            return HarmonyLib.AccessTools.Method(typeof(LevelPhysicsSettings), nameof(LevelPhysicsSettings.ApplySettings));
        }

        [PatchPostfix]
        public static void Postfix(LevelPhysicsSettings __instance)
        {
            try
            {
                Box = null;
                if (OldWay)
                {
                    Plugin.Log.LogWarning("Physics box left as the game drew it at loading (the old way, for a test)");
                    return;
                }
                if (TraderRoom.RedrawPhysics != null && !TraderRoom.RedrawPhysics.Value)
                {
                    return;
                }
                // The box as the game has just drawn it, in the world's own measure. The map's
                // settings are left as they are: the larger box is drawn right after the game's.
                var was = __instance.GetGlobalBounds();
                float depth = Mathf.Abs(TraderRoom.Depth != null ? TraderRoom.Depth.Value : 300f);
                // Down to under the deepest room there could be (a door at the bottom of the map and
                // a room that far again below it), out to the sides for the rooms that are set apart
                // from one another, and over the place of the hideout, under the middle of the world.
                var now = was;
                now.SetMinMax(was.min - new Vector3(450f, depth + 450f, 450f), was.max + new Vector3(450f, 0f, 450f));
                now.Encapsulate(new Bounds(new Vector3(0f, -depth - 100f, 0f), new Vector3(500f, 700f, 500f)));
                int cells = TestCells > 0 ? TestCells : __instance._mbpSubdivisions;
                Physics.RebuildBroadphaseRegions(now, cells);
                Box = now;
                Plugin.Log.LogInfo("Physics box of the map enlarged at loading: from centre " + was.center.ToString("F0") + ", size " + was.size.ToString("F0")
                                   + " to centre " + now.center.ToString("F0") + ", size " + now.size.ToString("F0") + ", cells " + cells);
            }
            catch (Exception e)
            {
                Box = null;
                Plugin.Log.LogWarning("Enlarging the map's physics box failed: " + e.Message);
            }
        }
    }
}
