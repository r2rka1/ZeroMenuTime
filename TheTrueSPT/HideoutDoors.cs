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

namespace TheTrueSPT
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
        private static readonly Dictionary<string, KeyValuePair<Vector3, float>> Fronts = new Dictionary<string, KeyValuePair<Vector3, float>>();

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
                new HideoutSpot { Id = "customs", Map = "bigmap", Position = new[] { 74.90f, 1.40f, -110.00f }, RotationY = 165f },
                // A grey steel door in a red corrugated shed, west of Peacekeeper.
                new HideoutSpot { Id = "shoreline", Map = "Shoreline", Position = new[] { -1005.70f, -60.15f, 391.60f }, RotationY = 0f },
            };
        }

        public static List<HideoutSpot> Load()
        {
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

        /// <summary>Puts this map's hideout doors in place. Called when the raid starts.</summary>
        public static int Spawn(string map)
        {
            int placed = 0;
            Fronts.Clear();
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
                var facing = Quaternion.Euler(0f, spot.RotationY, 0f);
                // The ground in front of a door is rarely level with where it was measured; stand the
                // player on what is actually there.
                RaycastHit floor;
                if (Physics.Raycast(ground + facing * new Vector3(0f, 1.6f, 0.9f), Vector3.down, out floor, 4f,
                        LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain"), QueryTriggerInteraction.Ignore))
                {
                    ground.y = floor.point.y;
                }
                var door = RoomDoor.Spawn("Enter hideout", ground + Vector3.up * (RoomDoor.Height / 2f) + facing * new Vector3(0f, 0f, 0.08f), spot.RotationY);
                var renderer = door.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
                string id = spot.Id;
                var back = ground + facing * new Vector3(0f, 0.05f, 1.0f);
                float backYaw = spot.RotationY;
                door.OnUse = () => Enter(id, back, backYaw);
                Fronts[id] = new KeyValuePair<Vector3, float>(back, backYaw);
                placed++;
                Plugin.Log.LogInfo("Hideout door for '" + spot.Id + "' at " + ground.ToString("F1") + " facing " + spot.RotationY.ToString("F0"));
            }
            // The interior is wanted for its own sake where there is a hideout door, and for its
            // furniture wherever a trader has a room.
            bool rooms = RoomProps.Use != null && RoomProps.Use.Value && TraderRoom.Rooms.Count > 0;
            if ((placed > 0 || rooms) && Plugin.Instance != null)
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
            if (bed != null && Fronts.TryGetValue(bed, out front) && Inside == null && !_busy && GamePlayerOwner.MyPlayer != null)
            {
                Plugin.Log.LogInfo("Waking up at the bed of hideout '" + bed + "'");
                var consumed = SaveGame.Status(true);
                Enter(bed, front.Key, front.Value, true);
                NotificationManager.DisplayMessageNotification("You wake up at your bed.", ENotificationDurationType.Long);
            }
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
                _indoorBox = new GameObject("TheTrueSPT hideout indoor area");
                _indoorBox.transform.position = bounds.center;
                _indoorBox.transform.localScale = bounds.size + Vector3.one * 4f;
                _indoor = _indoorBox.AddComponent<IndoorTrigger>();
                _indoor.IsBunker = true;
                _indoor.Reinit();
                TraderRoom.ExtendPhysics();
            }
            // Whenever the interior is at hand, rooms still bare get their furniture.
            RoomProps.DressAll();
        }

        public static IndoorTrigger IndoorAt(Vector3 position)
        {
            return _indoor != null ? _indoor.Check(position) : null;
        }

        public static void Enter(string id, Vector3 returnTo, float returnYaw, bool atBed = false)
        {
            if (_busy || Inside != null || Plugin.Instance == null)
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
            var player = GamePlayerOwner.MyPlayer;
            RoomDoor.PlayDoorSound(true, player.Transform.position);
            yield return RoomDoor.FadeTo(1f);

            // Tell the server which hideout this is; it brings that one's stations and stash forward.
            var call = RequestHandler.PostJsonAsync("/thetruespt/hideout/enter", JsonConvert.SerializeObject(new { Id = id }));
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
            HideoutBoosts.Read(reply);
            HideoutCrafting.Read(reply);

            if (!HideoutBunker.Loaded)
            {
                RoomDoor.FadeText = "Opening the hideout...";
            }
            yield return Prepare(true);
            RoomDoor.FadeText = null;
            if (!HideoutBunker.Loaded)
            {
                NotificationManager.DisplayWarningNotification("The hideout could not be opened.");
                yield return RoomDoor.FadeTo(0f);
                _busy = false;
                yield break;
            }
            var origin = HideoutBunker.Moved;

            HideoutStations.ShowAll();
            HideoutBunker.Sun(false);
            if (HideoutBunker.BunkerSettings != null)
            {
                HideoutBunker.BunkerSettings.ApplySettings();
            }

            var arrival = origin + Arrival;
            // Arriving, the player faces the way they always have: out from the nearest wall.
            Vector3 wallGround;
            float exitYaw;
            if (!RoomDoor.FindWall(arrival, out wallGround, out exitYaw))
            {
                exitYaw = 0f;
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

        private static IEnumerator Leaving()
        {
            _busy = true;
            var player = GamePlayerOwner.MyPlayer;
            RoomDoor.PlayDoorSound(true, player.Transform.position);
            yield return RoomDoor.FadeTo(1f);
            player.Teleport(_returnTo, false);
            player.Rotation = new Vector2(_returnYaw, 0f);
            string was = Inside;
            StepOut();
            yield return new WaitForSecondsRealtime(0.2f);
            RoomDoor.PlayDoorSound(false, _returnTo);
            yield return RoomDoor.FadeTo(0f);
            Plugin.Log.LogInfo("Left hideout '" + was + "'");
            _busy = false;
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
            }
        }

        /// <summary>Puts the map back as it was and lets the bunker go. Safe to call at any time.</summary>
        public static async System.Threading.Tasks.Task Close()
        {
            HideoutPanel.Close();
            if (Inside != null)
            {
                HideoutStash.Send("raid ending");
            }
            Inside = null;
            HideoutStations.Clear();
            HideoutStash.Clear();
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
                Plugin.Log.LogError("Closing the hideout failed: " + e);
            }
        }
    }
}
