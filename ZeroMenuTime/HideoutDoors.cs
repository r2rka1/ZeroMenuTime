using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using EFT;
using EFT.Communications;
using EFT.EnvironmentEffect;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>One hideout: which map its door is on, and where.</summary>
    public class HideoutSpot
    {
        public string Id = "";
        /// <summary>Map id as the game reports it (TarkovStreets, bigmap, Shoreline).</summary>
        public string Map = "";
        /// <summary>The middle of the door on the map, at floor level.</summary>
        public float[] Position = { 0f, 0f, 0f };
        /// <summary>The way the door faces, out from its wall.</summary>
        public float RotationY;
        public bool Enabled = true;
        /// <summary>The position is where someone stood with their back to the door; it is moved back onto the door.</summary>
        public bool AtFeet;
    }

    /// <summary>
    /// The doors on the maps that lead into the player's hideouts, and the trip through them. Each
    /// hideout is a separate one on the server (its own stations and stash); they share the game's
    /// one bunker interior, which is loaded on the way in and unloaded on the way out.
    /// </summary>
    public static class HideoutDoors
    {
        /// <summary>How far under the map the bunker is put.</summary>
        private const float Depth = 600f;

        /// <summary>Where the player arrives, measured from the bunker's own origin.</summary>
        private static readonly Vector3 Arrival = new Vector3(0f, 0.05f, 2f);
        private static readonly Vector3 ExitDoor = new Vector3(-12.88f, 3.95f, -18.49f);
        private const float ExitDoorYaw = 135f;

        public static string FilePath => Path.Combine(TraderConfig.Dir, "hideouts.json");

        /// <summary>The hideout the player is standing in, or null.</summary>
        public static string Inside;

        /// <summary>For each hideout with a door on this map: where one stands in front of it, and the way one faces.</summary>
        internal static readonly Dictionary<string, KeyValuePair<Vector3, float>> Fronts = new Dictionary<string, KeyValuePair<Vector3, float>>();

        private static Vector3 _returnTo;
        private static float _returnYaw;
        private static bool _busy;
        private static GameObject _indoorBox;
        private static IndoorTrigger _indoor;
        private static RoomDoor _exit;

        /// <summary>Doors picked by hand on each map. hideouts.json, if present, replaces the list.</summary>
        private static List<HideoutSpot> Defaults()
        {
            return new List<HideoutSpot>
            {
                // A rust-red metal double door in a tiled wall, at street level a few steps from Prapor.
                new HideoutSpot { Id = "streets", Map = "TarkovStreets", Position = new[] { -15.30f, 3.00f, -108.65f }, RotationY = 0f },
                // The door of one of the metal cabins north of Skier.
                new HideoutSpot { Id = "customs", Map = "bigmap", Position = new[] { 622.70f, -2.67f, -126.40f }, RotationY = 101f, AtFeet = true },
                // The big gate of checkpoint 11: its east half, a few steps from Peacekeeper's door in the west half.
                // (The shed behind the gate, used before, stands beyond the checkpoint wall and cannot be walked to.)
                new HideoutSpot { Id = "shoreline", Map = "Shoreline", Position = new[] { -963.37f, -59.60f, 380.60f }, RotationY = 162f },
            };
        }

        public static List<HideoutSpot> Load()
        {
            if (Layout.Active != null)
            {
                // In co-op the host's doors are the doors.
                return Layout.Active.Hideouts ?? new List<HideoutSpot>();
            }
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonConvert.DeserializeObject<List<HideoutSpot>>(File.ReadAllText(FilePath)) ?? new List<HideoutSpot>();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read hideouts.json: " + e);
            }
            return Defaults();
        }

        /// <summary>
        /// Some maps keep a door's collider and shadow but switch its mesh off, which looks like a
        /// blank wall. The hideout door has to be seen, so the map's own door at that spot is drawn.
        /// </summary>
        private static void ShowMapDoor(Vector3 centre)
        {
            int shown = 0;
            foreach (var hit in Physics.OverlapSphere(centre, 1.5f, ~0, QueryTriggerInteraction.Collide))
            {
                var root = hit.transform.parent != null ? hit.transform.parent : hit.transform;
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.enabled && renderer.gameObject.activeInHierarchy
                        && renderer.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0
                        && renderer.name.IndexOf("SHADOW", StringComparison.OrdinalIgnoreCase) < 0
                        && !renderer.name.StartsWith("ZeroMenuTime", StringComparison.Ordinal)
                        && Vector3.Distance(renderer.bounds.center, centre) < 1.5f)
                    {
                        renderer.enabled = true;
                        shown++;
                    }
                }
            }
            if (shown > 0)
            {
                Plugin.Log.LogInfo("Hideout door: the map's own door drawn again (" + shown + " part(s))");
            }
        }

        /// <summary>Puts this map's hideout doors in place. Called when the raid starts.</summary>
        public static int Spawn(string map)
        {
            int placed = 0;
            Fronts.Clear();
            // A new raid: nothing is under way, whatever the last one was doing when it ended.
            _busy = false;
            Inside = null;
            foreach (var spot in Load())
            {
                if (!spot.Enabled || spot.Position == null || spot.Position.Length < 3
                    || !string.Equals(spot.Map, map, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // The door itself is part of the map already; this is only something to look at and use,
                // laid over it and not drawn.
                var ground = new Vector3(spot.Position[0], spot.Position[1], spot.Position[2]);
                float yaw = spot.RotationY;
                if (spot.AtFeet)
                {
                    RoomDoor.BackToWall(ref ground, ref yaw);
                }
                var facing = Quaternion.Euler(0f, yaw, 0f);
                // The ground in front of a door is rarely level with where it was measured; stand the
                // player on what is actually there.
                RaycastHit floor;
                if (Physics.Raycast(ground + facing * new Vector3(0f, 1.6f, 0.9f), Vector3.down, out floor, 4f,
                        LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain"), QueryTriggerInteraction.Ignore))
                {
                    ground.y = floor.point.y;
                }
                var door = RoomDoor.Spawn("Enter hideout", ground + Vector3.up * (RoomDoor.Height / 2f) + facing * new Vector3(0f, 0f, 0.08f), yaw);
                var renderer = door.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
                ShowMapDoor(ground + Vector3.up * (RoomDoor.Height / 2f));
                string id = spot.Id;
                var back = ground + facing * new Vector3(0f, 0.05f, 1.0f);
                float backYaw = yaw;
                door.OnUse = () => Enter(id, back, backYaw);
                Fronts[id] = new KeyValuePair<Vector3, float>(back, backYaw);
                placed++;
                Plugin.Log.LogInfo("Hideout door for '" + spot.Id + "' at " + ground.ToString("F1") + " facing " + yaw.ToString("F0"));
            }
            // The interior is wanted for its own sake where there is a hideout door, and for its
            // furniture wherever a trader has a room.
            bool rooms = Layout.Shell && TraderRoom.Rooms.Count > 0;
            // A headless host has no use for the interior yet (the hideout in co-op comes later).
            if ((placed > 0 || rooms) && Plugin.Instance != null && !Net.Headless)
            {
                Plugin.Instance.StartCoroutine(Preload());
            }
            return placed;
        }

        /// <summary>
        /// On a map with a hideout door, brings the interior in ahead of time, once the map itself
        /// has finished streaming in. Loading it at the moment the door is used can take the better
        /// part of a minute on a large map; loaded beforehand, stepping through is immediate.
        /// </summary>
        private static IEnumerator Preload()
        {
            // After a death, a crash since the last save, or a new game, the raid begins at the bed.
            // That is settled at once, behind a dark screen, so the map is never seen first.
            var waking = SaveGame.Status(false);
            while (!waking.IsCompleted)
            {
                yield return null;
            }
            var first = waking.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? waking.Result : null;
            string wakeAt = first != null && ((bool?)first["Resume"] ?? false) ? (string)first["Hideout"] : null;
            KeyValuePair<Vector3, float> wakeFront;
            if (wakeAt != null && Fronts.TryGetValue(wakeAt, out wakeFront) && Inside == null && !_busy && !(Net.Coop && Seamless.Joined) && Coop.Allowed(Coop.Feature.Hideout, false))
            {
                RoomDoor.Fade = 1f;
                RoomDoor.FadeText = "Waking up...";
                while (GamePlayerOwner.MyPlayer == null)
                {
                    yield return null;
                }
                Plugin.Log.LogInfo("Waking up at the bed of hideout '" + wakeAt + "'");
                var taken = SaveGame.Status(true);
                Enter(wakeAt, wakeFront.Key, wakeFront.Value, true);
                while (_busy)
                {
                    yield return null;
                }
                RoomDoor.FadeText = null;
                NotificationManager.DisplayMessageNotification("You wake up at your bed.", ENotificationDurationType.Long);
                yield break;
            }
            yield return new WaitForSecondsRealtime(25f);
            if (GamePlayerOwner.MyPlayer == null)
            {
                yield break;
            }
            // Someone may have opened a hideout already; the rooms are furnished either way.
            while (_busy || HideoutBunker.Loading)
            {
                yield return null;
            }
            bool wasLoaded = HideoutBunker.Loaded;
            yield return Prepare(false);
            RoomProps.DressAll();
            if (wasLoaded)
            {
                yield break;
            }
            if (Fronts.Count == 0)
            {
                // No hideout on this map: the furniture has been copied out, so the interior can go.
                var closing = Close();
                while (!closing.IsCompleted)
                {
                    yield return null;
                }
                Plugin.Log.LogInfo("Hideout interior let go after furnishing the rooms");
                yield break;
            }

            // After a death, or a crash since the last save, the raid begins at the bed.
            var asking = SaveGame.Status(false);
            while (!asking.IsCompleted)
            {
                yield return null;
            }
            var status = asking.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? asking.Result : null;
            string bed = status != null && ((bool?)status["Resume"] ?? false) ? (string)status["Hideout"] : null;
            KeyValuePair<Vector3, float> front;
            if (bed != null && Fronts.TryGetValue(bed, out front) && Inside == null && !_busy && GamePlayerOwner.MyPlayer != null && !(Net.Coop && Seamless.Joined) && Coop.Allowed(Coop.Feature.Hideout, false))
            {
                Plugin.Log.LogInfo("Waking up at the bed of hideout '" + bed + "'");
                var consumed = SaveGame.Status(true);
                Enter(bed, front.Key, front.Value, true);
                NotificationManager.DisplayMessageNotification("You wake up at your bed.", ENotificationDurationType.Long);
            }
        }

        /// <summary>Co-op: someone changed the hideout; if this player is inside, look at it again.</summary>
        public static async void OnChanged(byte[] body, string from)
        {
            if (Inside == null)
            {
                return;
            }
            try
            {
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/hideout/list", "{}"));
                if (!((bool?)reply["Ok"] ?? false) || Inside == null)
                {
                    return;
                }
                HideoutStations.Read(reply);
                HideoutBoosts.Read(reply);
                HideoutCrafting.Read(reply);
                HideoutPower.Read(reply);
                HideoutShelves.Read(reply);
                HideoutStations.ShowAll();
                HideoutPower.ApplyLook();
                RefreshExits();
                Plugin.Log.LogInfo("Hideout looked at again after a change by another player");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hideout not refreshed: " + e.Message);
            }
        }

        /// <summary>Where this map's hideout doors are, for the map.</summary>
        public static IEnumerable<Vector3> DoorPlaces()
        {
            foreach (var front in Fronts.Values)
            {
                yield return front.Key;
            }
        }

        /// <summary>True within a few steps of the door that leads out.</summary>
        public static bool NearExit(Vector3 position)
        {
            return HideoutBunker.Loaded && Vector3.Distance(position, HideoutBunker.Moved + ExitDoor) < 3.2f;
        }

        private static RoomDoor _securityExit;
        // Where the Security station puts its door, across the way to the steps.
        private static readonly Vector3 SecurityDoor = new Vector3(-8.7f, 1.1f, -8.5f);

        /// <summary>
        /// A built Security station shuts a door across the way to the steps, which lead up to the
        /// cultists' circle and the way out. That door can be gone through either way. Called on
        /// entering and after anything is built.
        /// </summary>
        public static void RefreshExits()
        {
            int security;
            HideoutStations.Levels.TryGetValue((int)EAreaType.Security, out security);
            bool wanted = Inside != null || _busy;
            if (security >= 1 && wanted && HideoutBunker.Loaded)
            {
                if (_securityExit == null)
                {
                    _securityExit = RoomDoor.Spawn("Go through", HideoutBunker.Moved + SecurityDoor, 0f);
                    // As deep as it is wide, so it is met from whichever side the door is come at.
                    _securityExit.transform.localScale = new Vector3(1.3f, RoomDoor.Height, 1.3f);
                    var drawn = _securityExit.GetComponent<MeshRenderer>();
                    if (drawn != null)
                    {
                        drawn.enabled = false;
                    }
                    _securityExit.OnUse = ThroughSecurityDoor;
                    Plugin.Log.LogInfo("Hideout: Security is built; its door can be gone through");
                }
            }
            else if (_securityExit != null)
            {
                UnityEngine.Object.Destroy(_securityExit.gameObject);
                _securityExit = null;
            }
        }

        /// <summary>Steps the player to the other side of the Security station's door.</summary>
        private static void ThroughSecurityDoor()
        {
            var player = GamePlayerOwner.MyPlayer;
            if (player == null || !HideoutBunker.Loaded)
            {
                return;
            }
            var door = HideoutBunker.Moved + SecurityDoor;
            // The hall lies on the door's +z side, the steps on its -z side.
            bool fromHall = player.Transform.position.z > door.z;
            // Both sides are on the hall's floor; the first step of the stairs starts just beyond.
            var to = new Vector3(door.x, HideoutBunker.Moved.y + 0.1f, door.z + (fromHall ? -1.1f : 1.1f));
            float yaw;
            if (fromHall)
            {
                // Beyond the door the walls have nothing to feel for, so this side is set by hand:
                // clear of the gap in the east wall, facing up the steps.
                to.x -= 0.45f;
                yaw = 232f;
            }
            else
            {
                yaw = StandClear(ref to, door);
            }
            player.Teleport(to, false);
            player.Rotation = new Vector2(yaw, 0f);
            Plugin.Log.LogInfo("Hideout: through the Security door to " + to.ToString("F1") + " facing " + yaw.ToString("F0"));
        }

        /// <summary>
        /// Having stepped through a door, the player must not stand with their face in a wall: the
        /// spot is moved clear of anything closer than half a metre, and they face the most open
        /// way that is not back at the door. Returns that way.
        /// </summary>
        private static float StandClear(ref Vector3 feet, Vector3 door)
        {
            int solid = LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain");
            const float Room = 0.55f;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < 16; i++)
                {
                    var way = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                    RaycastHit hit;
                    if (Physics.Raycast(feet + Vector3.up * 1.5f, way, out hit, Room, solid, QueryTriggerInteraction.Ignore))
                    {
                        feet -= way * (Room - hit.distance);
                    }
                }
            }
            var back = door - feet;
            back.y = 0f;
            back.Normalize();
            float best = -1f, bestYaw = 0f;
            for (int i = 0; i < 16; i++)
            {
                float yaw = i * 22.5f;
                var way = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                if (Vector3.Dot(way, back) > 0.3f)
                {
                    continue;
                }
                RaycastHit hit;
                float open = Physics.Raycast(feet + Vector3.up * 1.5f, way, out hit, 12f, solid, QueryTriggerInteraction.Ignore) ? hit.distance : 12f;
                // Straight on from the door is preferred when it is about as open as anything else.
                open += Vector3.Dot(way, -back) * 0.75f;
                if (open > best)
                {
                    best = open;
                    bestYaw = yaw;
                }
            }
            return bestYaw;
        }

        /// <summary>Brings the interior in now, if it is not there yet, and furnishes the rooms still bare.</summary>
        public static IEnumerator FurnishNow()
        {
            while (_busy || HideoutBunker.Loading)
            {
                yield return null;
            }
            yield return Prepare(false);
            RoomProps.DressAll();
        }

        /// <summary>Testing aid: bring the interior in now and furnish the rooms again.</summary>
        public static IEnumerator Furnish()
        {
            yield return Prepare(false);
            int done = RoomProps.DressAll(true);
            TestHarness.Report(done > 0 ? "furnished " + done + " room(s)" : "FAILED no room furnished");
        }

        /// <summary>Loads the interior if need be and puts it in place under the map.</summary>
        private static IEnumerator Prepare(bool waited)
        {
            if (!HideoutBunker.Loaded)
            {
                var loading = HideoutBunker.Load();
                while (!loading.IsCompleted)
                {
                    yield return null;
                }
                if (loading.IsFaulted || !HideoutBunker.Loaded)
                {
                    Plugin.Log.LogError("Hideout interior did not load: " + (loading.Exception != null ? loading.Exception.InnerException.ToString() : "?"));
                    yield break;
                }
                Plugin.Log.LogInfo("Hideout interior " + (waited ? "(at the door): " : "(ahead of time): ") + loading.Result);
            }
            if (HideoutBunker.Moved == Vector3.zero)
            {
                HideoutBunker.Move(new Vector3(0f, -Depth, 0f));
            }
            if (_indoorBox == null)
            {
                // Indoors as far as the game is concerned, and inside the physics engine's reach.
                var bounds = HideoutBunker.VisibleBounds();
                _indoorBox = new GameObject("ZeroMenuTime hideout indoor area");
                _indoorBox.transform.position = bounds.center;
                _indoorBox.transform.localScale = bounds.size + Vector3.one * 4f;
                _indoor = _indoorBox.AddComponent<IndoorTrigger>();
                _indoor.IsBunker = true;
                _indoor.Reinit();
                TraderRoom.ExtendPhysics();
            }
            // Whenever the interior is at hand, rooms still bare get their furniture.
            RoomProps.DressAll();
            // Loaded ahead of time for a quick way in; until someone goes in it is switched off.
            if (!waited && Inside == null && !_busy)
            {
if (!Net.Coop)
                {
                    HideoutBunker.Park(true);
                }
            }
        }

        public static IndoorTrigger IndoorAt(Vector3 position)
        {
            return _indoor != null ? _indoor.Check(position) : null;
        }

        /// <summary>A way in or out of the hideout is under way.</summary>
        public static bool Busy => _busy;

        public static void Enter(string id, Vector3 returnTo, float returnYaw, bool atBed = false)
        {
            if (_busy || Inside != null || Plugin.Instance == null)
            {
                return;
            }
            // Waking at the bed is not told on screen: the player did not ask for anything.
            if (!Coop.Allowed(Coop.Feature.Hideout, !atBed))
            {
                return;
            }
            Plugin.Instance.StartCoroutine(Going(id, returnTo, returnYaw, atBed));
        }

        public static void Leave()
        {
            if (_busy || Inside == null || Plugin.Instance == null)
            {
                return;
            }
            Plugin.Instance.StartCoroutine(Leaving());
        }

        private static IEnumerator Going(string id, Vector3 returnTo, float returnYaw, bool atBed)
        {
            _busy = true;
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                RoomDoor.PlayDoorSound(true, player.Transform.position);
                yield return RoomDoor.FadeTo(1f);

                // Tell the server which hideout this is; it brings that one's stations and stash forward.
                var call = RequestHandler.PostJsonAsync("/zeromenutime/hideout/enter", JsonConvert.SerializeObject(new { Id = id }));
                while (!call.IsCompleted)
                {
                    yield return null;
                }
                JObject reply = null;
                try
                {
                    reply = JObject.Parse(call.Result);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Hideout enter: " + e.Message);
                }
                if (reply == null || !((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Hideout '" + id + "' could not be entered: " + (reply != null ? reply["Error"] : "no answer"));
                    NotificationManager.DisplayWarningNotification("The hideout could not be opened.");
                    yield return RoomDoor.FadeTo(0f);
                    _busy = false;
                    yield break;
                }
                HideoutStations.Read(reply);
                HideoutStash.Read(id, reply);
                HideoutShelves.Read(reply);
                HideoutBoosts.Read(reply);
                HideoutCrafting.Read(reply);

                if (!HideoutBunker.Loaded)
                {
                    RoomDoor.FadeText = "Opening the hideout...";
                }
                yield return RoomDoor.Within(Prepare(true), 120f, "the hideout interior");
                RoomDoor.FadeText = null;
                if (!HideoutBunker.Loaded)
                {
                    NotificationManager.DisplayWarningNotification("The hideout could not be opened.");
                    yield return RoomDoor.FadeTo(0f);
                    _busy = false;
                    yield break;
                }
                var origin = HideoutBunker.Moved;

                HideoutBunker.Park(false);
                Presence.Refresh();
                HideoutStations.ShowAll();
                HideoutBunker.Sun(false);
                if (HideoutBunker.BunkerSettings != null)
                {
                    HideoutBunker.BunkerSettings.ApplySettings();
                }

                // Coming in, the player stands just inside the door they will leave by, back to it.
                var arrival = origin + Arrival;
                float exitYaw = ExitDoorYaw;
                bool byDoor = false;
                var doorCentre = origin + ExitDoor;
                int solid = LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain");
                foreach (float side in new[] { 1f, -1f })
                {
                    float yaw = ExitDoorYaw + (side > 0f ? 0f : 180f);
                    var spot = doorCentre + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 1.1f);
                    RaycastHit floor;
                    // Room to stand: open between the door and the spot, and a floor under it.
                    if (!Physics.Linecast(doorCentre + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 0.35f), spot, solid, QueryTriggerInteraction.Ignore)
                        && Physics.Raycast(spot, Vector3.down, out floor, 3f, solid, QueryTriggerInteraction.Ignore))
                    {
                        arrival = floor.point + Vector3.up * 0.05f;
                        exitYaw = yaw;
                        byDoor = true;
                        break;
                    }
                }
                if (!byDoor)
                {
                    Vector3 wallGround;
                    if (!RoomDoor.FindWall(arrival, out wallGround, out exitYaw))
                    {
                        exitYaw = 0f;
                    }
                    Plugin.Log.LogWarning("Hideout: no room to stand by the exit door; arriving in the hall instead");
                }
                // The way out is the closed door at the end of the small room up the steps (the one
                // the cultists' room lies behind): something to use, laid over that door and not drawn.
                _exit = RoomDoor.Spawn("Leave hideout", origin + ExitDoor, ExitDoorYaw);
                var exitDrawn = _exit.GetComponent<MeshRenderer>();
                if (exitDrawn != null)
                {
                    exitDrawn.enabled = false;
                }
                _exit.OnUse = Leave;
                RefreshExits();

                _returnTo = returnTo;
                _returnYaw = returnYaw;
                Inside = id;
                if (atBed)
                {
                    // Waking up: stand by the Rest Space, on whatever floor is beside it.
                    var controller = HideoutStations.Controller();
                    EFT.Hideout.HideoutArea rest;
                    Bounds bedBounds;
                    if (controller != null && controller.Areas.TryGetValue(EAreaType.RestSpace, out rest) && HideoutStations.LevelBounds(rest, 1, out bedBounds))
                    {
                        var towardsHall = (origin - bedBounds.center);
                        towardsHall.y = 0f;
                        var beside = bedBounds.center + towardsHall.normalized * (Mathf.Max(bedBounds.extents.x, bedBounds.extents.z) + 0.9f);
                        RaycastHit floor;
                        if (Physics.Raycast(new Vector3(beside.x, bedBounds.max.y + 1.5f, beside.z), Vector3.down, out floor, 6f,
                                LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain"), QueryTriggerInteraction.Ignore))
                        {
                            arrival = floor.point + Vector3.up * 0.05f;
                        }
                    }
                }
                // Needs to know which hideout this is, so only now.
                var recipes = HideoutCrafting.Load();
                player.Teleport(arrival, false);
                player.Rotation = new Vector2(exitYaw, 0f);
                yield return new WaitForSecondsRealtime(0.3f);
                RoomDoor.PlayDoorSound(false, arrival);
                yield return RoomDoor.FadeTo(0f);
                Plugin.Log.LogInfo("Entered hideout '" + id + "'; exit door at " + (origin + ExitDoor).ToString("F1") + "; boosts: " + HideoutBoosts.Describe());
                HideoutBoosts.Announce();
                HideoutCrafting.AnnounceDue();
                WorldClock.Send("entered a hideout");
                _busy = false;
            }
            finally
            {
                // However the way through ended, the doors work again; a way in that broke
                // off leaves the player where they were, able to see.
                _busy = false;
                if (Inside == null)
                {
                    RoomDoor.FadeText = null;
                    RoomDoor.Fade = 0f;
                }
            }
        }

        private static IEnumerator Leaving()
        {
            _busy = true;
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                RoomDoor.PlayDoorSound(true, player.Transform.position);
                yield return RoomDoor.FadeTo(1f);
                player.Teleport(_returnTo, false);
                player.Rotation = new Vector2(_returnYaw, 0f);
                string was = Inside;
                StepOut();
                yield return new WaitForSecondsRealtime(0.2f);
if (!Net.Coop)
                {
                    // In co-op others may be inside: the interior stays for the whole raid.
                    HideoutBunker.Park(true);
                }
                Presence.Refresh();
                RoomDoor.PlayDoorSound(false, _returnTo);
                yield return RoomDoor.FadeTo(0f);
                Plugin.Log.LogInfo("Left hideout '" + was + "'");
                _busy = false;
            }
            finally
            {
                // However the way through ended, the doors work again; a way in that broke
                // off leaves the player where they were, able to see.
                _busy = false;
                if (Inside == null)
                {
                    RoomDoor.FadeText = null;
                    RoomDoor.Fade = 0f;
                }
            }
        }

        /// <summary>Puts the map's own light back. The interior stays loaded for the next visit.</summary>
        private static void StepOut()
        {
            HideoutStash.Send("leaving");
            Inside = null;
            HideoutStations.Clear();
            HideoutCrafting.Clear();
            HideoutStash.Clear();
            HideoutBunker.Sun(true);
            if (HideoutBunker.RaidSettings != null)
            {
                HideoutBunker.RaidSettings.ApplySettings();
            }
            if (_exit != null)
            {
                UnityEngine.Object.Destroy(_exit.transform.root.gameObject);
                _exit = null;
                if (_securityExit != null)
                {
                    UnityEngine.Object.Destroy(_securityExit.gameObject);
                    _securityExit = null;
                }
            }
        }

        /// <summary>Puts the map back as it was and lets the bunker go. Safe to call at any time.</summary>
        public static async System.Threading.Tasks.Task Close()
        {
            HideoutPanel.Close();
            HideoutGunsmith.Close();
            BotFreeze.Set(false);
            _busy = false;
            if (Inside != null)
            {
                HideoutStash.Send("raid ending");
            }
            Inside = null;
            HideoutStations.Clear();
            HideoutStash.Clear();
            HideoutShelves.Clear();
            try
            {
                HideoutBunker.Sun(true);
                if (HideoutBunker.RaidSettings != null)
                {
                    HideoutBunker.RaidSettings.ApplySettings();
                }
                if (_exit != null)
                {
                    UnityEngine.Object.Destroy(_exit.transform.root.gameObject);
                    _exit = null;
                if (_securityExit != null)
                {
                    UnityEngine.Object.Destroy(_securityExit.gameObject);
                    _securityExit = null;
                }
                }
                if (_indoorBox != null)
                {
                    UnityEngine.Object.Destroy(_indoorBox);
                    _indoorBox = null;
                    _indoor = null;
                }
                if (HideoutBunker.Loaded)
                {
                    await HideoutBunker.Unload();
                }
            }
            catch (Exception e)
            {
                if (e is ArgumentException && e.Message.Contains("Scene to unload is invalid"))
                {
                    // The raid's scenes have gone already (a co-op raid ends before this is told): nothing left to close.
                    Plugin.Log.LogInfo("Hideout interior already gone with the raid");
                }
                else
                {
                    Plugin.Log.LogError("Closing the hideout failed: " + e);
                }
            }
        }
    }
}
