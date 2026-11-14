using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using EFT.EnvironmentEffect;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TradersInRaid
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

        // Inside measurements, in metres.
        private const float Width = 6f, Height = 3f, Length = 7f, Wall = 0.3f;
        private const float TableHeight = 0.8f;

        public string TraderId;
        public GameObject Root;
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
            return null;
        }

        /// <summary>Builds the room for a trader whose door is at the given spot on the map.</summary>
        public static TraderRoom Build(string traderId, string traderName, Vector3 doorGround)
        {
            // Rooms on the same map are kept well apart so one cannot be seen or heard from another.
            var origin = doorGround + new Vector3(Rooms.Count * 40f, -Mathf.Abs(Depth.Value), 0f);
            // Each trader has a room of their own in the bundle; anyone without one gets the plain shop.
            var prefab = RoomBundle.Prefab("TraderRoom_" + (LookOverride ?? traderName)) ?? RoomBundle.Prefab("TraderRoom_Base");
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

            var root = new GameObject("TradersInRaid room -- " + traderName);
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
            var tableCentre = new Vector3(Width / 2f - 0.6f, TableHeight / 2f, 0.3f);
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
                Root = root,
                Indoor = indoor,
                Entry = origin + new Vector3(0f, 0.05f, -Length / 2f + 1.2f),
                EntryYaw = 0f,
                TraderStand = origin + new Vector3(0f, 0f, Length / 2f - 1.3f),
                TraderYaw = 180f,
                TableTop = origin + new Vector3(tableCentre.x, TableHeight, tableCentre.z),
                TableSize = new Vector2(0.8f, 1.6f),
            };
            room.ExitDoor = RoomDoor.Spawn("Leave", origin + new Vector3(0f, RoomDoor.Height / 2f, -Length / 2f + RoomDoor.Thickness / 2f), 0f);
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
        public static void ExtendPhysics()
        {
            if (Rooms.Count == 0)
            {
                return;
            }
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var player = EFT.GamePlayerOwner.MyPlayer;
                var centre = player != null ? player.Transform.position : Rooms[0].Root.transform.position + Vector3.up * Mathf.Abs(Depth.Value);
                var world = new Bounds(centre, Vector3.one);
                // Anything further out than this is not part of the playable map (sky boxes, kill
                // planes), and stretching the box over it would only make the engine's grid coarser.
                var limit = new Bounds(centre, new Vector3(6000f, 2000f, 6000f));
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
                world.Expand(20f);
                Physics.RebuildBroadphaseRegions(world, 8);
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

        private static int SolidLayer()
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
            root.name = "TradersInRaid room -- " + traderName;

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
            room.ExitDoor = RoomDoor.Attach(exit.gameObject, "Leave");
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
            if (__result == null && TraderRoom.Rooms.Count > 0)
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
            if (__result == null && TraderRoom.Rooms.Count > 0)
            {
                __result = TraderRoom.IndoorAt(pos);
            }
        }
    }
}
