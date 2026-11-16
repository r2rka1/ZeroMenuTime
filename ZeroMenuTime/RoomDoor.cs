using System.Collections;
using System.Collections.Generic;
using EFT;
using EFT.Interactive;
using EFT.UI;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// A door the player uses to step between the map and a trader's room. Using it fades the screen,
    /// moves the player to the other side and fades back.
    /// </summary>
    public class RoomDoor : InteractableObject
    {
        public const float Height = 2.1f, DoorWidth = 1.0f, Thickness = 0.12f;

        public string Label;

        /// <summary>What using the door does, when it is not a plain step to a fixed place.</summary>
        public System.Action OnUse;
        public Vector3 Destination;
        public float DestinationYaw;

        /// <summary>How dark the screen is right now, 0 to 1. Drawn by the plugin.</summary>
        public static float Fade;
        /// <summary>Shown on the dark screen while something slow happens behind it.</summary>
        public static string FadeText;
        private static bool _travelling;

        private static readonly List<GameObject> Spawned = new List<GameObject>();

        /// <summary>Every door of ours that exists right now.</summary>
        internal static readonly List<RoomDoor> All = new List<RoomDoor>();

        private void Awake()
        {
            All.Add(this);
        }

        private void OnDestroy()
        {
            All.Remove(this);
        }

        /// <summary>How deep the space is, through the door, in which looking at it counts.</summary>
        private const float ReachDepth = 1.0f;

        /// <summary>
        /// Where the ray meets the door, measured along the ray. The door counts as a block as wide
        /// and tall as itself and a metre deep, centred on it, so it is met from either side and
        /// through the map's own door leaf or frame laid over it.
        /// </summary>
        public bool Meets(Ray ray, out float distance, out Matrix4x4 toDoor, out Vector3 size)
        {
            distance = 0f;
            toDoor = Matrix4x4.identity;
            size = Vector3.zero;
            if (!isActiveAndEnabled)
            {
                return false;
            }
            Vector3 centre;
            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                centre = transform.TransformPoint(box.center);
                size = Vector3.Scale(box.size, transform.lossyScale);
            }
            else
            {
                var collider = GetComponent<Collider>();
                if (collider == null)
                {
                    return false;
                }
                centre = collider.bounds.center;
                size = Quaternion.Inverse(transform.rotation) * collider.bounds.size;
            }
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            // The thinnest side is the way through the door.
            if (size.x <= size.y && size.x <= size.z)
            {
                size.x = Mathf.Max(size.x, ReachDepth);
            }
            else if (size.z <= size.y)
            {
                size.z = Mathf.Max(size.z, ReachDepth);
            }
            else
            {
                size.y = Mathf.Max(size.y, ReachDepth);
            }
            toDoor = Matrix4x4.TRS(centre, transform.rotation, Vector3.one).inverse;
            var local = new Ray(toDoor.MultiplyPoint3x4(ray.origin), toDoor.MultiplyVector(ray.direction));
            var bounds = new Bounds(Vector3.zero, size);
            if (bounds.Contains(local.origin))
            {
                return true;
            }
            return bounds.IntersectRay(local, out distance);
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>
        /// The door of ours the ray finds within reach with nothing solid in between, or null. The
        /// door's own frame and the map's door it is laid over do not count as in between.
        /// </summary>
        public static RoomDoor Seen(Ray ray, float reach, out float distance)
        {
            distance = 0f;
            RoomDoor best = null;
            float nearest = reach;
            foreach (var door in All)
            {
                float at;
                Matrix4x4 toDoor;
                Vector3 size;
                if (door == null || !door.Meets(ray, out at, out toDoor, out size) || at > nearest)
                {
                    continue;
                }
                bool blocked = false;
                int count = Physics.RaycastNonAlloc(ray, Hits, at, SolidMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count && !blocked; i++)
                {
                    // Anything within the door's own block, or its frame just around it, is the doorway.
                    var p = toDoor.MultiplyPoint3x4(Hits[i].point);
                    blocked = Mathf.Abs(p.x) > size.x / 2f + 0.35f || Mathf.Abs(p.y) > size.y / 2f + 0.35f || Mathf.Abs(p.z) > size.z / 2f + 0.35f;
                }
                if (blocked)
                {
                    continue;
                }
                best = door;
                nearest = at;
            }
            distance = nearest;
            return best;
        }

        private static int _solidMask;
        private static int SolidMask
        {
            get
            {
                if (_solidMask == 0)
                {
                    _solidMask = LayerMask.GetMask("HighPolyCollider", "TransparentCollider", "LowPolyCollider", "Terrain");
                }
                return _solidMask;
            }
        }

        public AvailableInteractionState GetActions()
        {
            var state = new AvailableInteractionState();
            state.Actions.Add(new InteractionAction
            {
                Name = Label,
                Action = Use,
                Disabled = false,
            });
            return state;
        }

        /// <summary>Turns an existing object, such as the door leaf of a prefab, into a usable door.</summary>
        public static RoomDoor Attach(GameObject go, string label)
        {
            if (go.GetComponent<Collider>() == null)
            {
                go.AddComponent<BoxCollider>();
            }
            go.layer = LayerMask.NameToLayer("Interactive");
            var door = go.AddComponent<RoomDoor>();
            door.Label = label;
            return door;
        }

        /// <summary>
        /// Looks for a wall near the trader's spot to stand the door against, so it reads as a door
        /// into a building and not a slab in the open. Gives the point on the ground at the foot of
        /// the wall and the way the door should face (out from the wall). False if nothing suitable
        /// is close by.
        /// </summary>
        private static readonly string[] NotWalls = { "fence", "zabor", "setka", "rabic", "grid", "lattice", "grille", "gate", "vorota", "glass", "window", "net_", "mesh_fence", "container", "vehicle", "car_", "truck", "bus_" };

        /// <summary>True for things a door cannot sensibly be in: fences, gates, glass, vehicles.</summary>
        private static bool IsFence(Collider collider)
        {
            int up = 0;
            for (var t = collider.transform; t != null && up < 4; t = t.parent, up++)
            {
                string name = t.name.ToLowerInvariant();
                foreach (var word in NotWalls)
                {
                    if (name.Contains(word))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// A door placed by hand is given as where the player stood, back to the wall. Moves it
        /// back onto that wall and squares it to the wall, if there is one within reach.
        /// </summary>
        public static void BackToWall(ref Vector3 ground, ref float yaw)
        {
            int mask = LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain", "Default");
            var back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            RaycastHit hit;
            if (Physics.Raycast(ground + Vector3.up * 1.1f, back, out hit, 2.0f, mask, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.2f)
            {
                var outward = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                var atWall = hit.point + outward * 0.1f;
                ground = new Vector3(atWall.x, ground.y, atWall.z);
                yaw = Quaternion.LookRotation(outward).eulerAngles.y;
            }
            RaycastHit floor;
            if (Physics.Raycast(ground + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.3f + Vector3.up * 1.2f, Vector3.down, out floor, 2.5f, mask, QueryTriggerInteraction.Ignore))
            {
                ground.y = floor.point.y;
            }
        }

        public static bool FindWall(Vector3 spot, out Vector3 ground, out float yaw)
        {
            ground = spot;
            yaw = 0f;
            int mask = LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain", "Default");
            var chest = spot + Vector3.up * 1.0f;
            float best = float.MaxValue;
            bool found = false;
            for (int step = 0; step < 24; step++)
            {
                var direction = Quaternion.Euler(0f, step * 15f, 0f) * Vector3.forward;
                RaycastHit hit;
                if (!Physics.Raycast(chest, direction, out hit, 8f, mask, QueryTriggerInteraction.Ignore)
                    || hit.distance < 0.7f || hit.distance >= best || Mathf.Abs(hit.normal.y) > 0.15f || IsFence(hit.collider))
                {
                    continue;
                }
                // It has to be a flat stretch of wall as tall and wide as the door: probe it at the
                // door's corners, straight along the wall's own normal.
                var into = -new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                var along = Vector3.Cross(Vector3.up, into);
                var foot = hit.point - Vector3.up * 1.0f;
                bool flat = true;
                foreach (var offset in new[] { new Vector2(-0.55f, 0.25f), new Vector2(0.55f, 0.25f), new Vector2(-0.55f, 2.0f), new Vector2(0.55f, 2.0f), new Vector2(0f, 1.1f),
                             // A wall of a building goes on above a door; a fence or a parked container ends about here.
                             new Vector2(-0.4f, 2.75f), new Vector2(0.4f, 2.75f) })
                {
                    var from = foot - into * 0.6f + along * offset.x + Vector3.up * offset.y;
                    RaycastHit probe;
                    if (!Physics.Raycast(from, into, out probe, 1.0f, mask, QueryTriggerInteraction.Ignore)
                        || Mathf.Abs(probe.distance - 0.6f) > 0.12f)
                    {
                        flat = false;
                        break;
                    }
                }
                // And there must be floor to stand on in front of it, about level with the trader's spot.
                RaycastHit floor;
                if (!flat || !Physics.Raycast(foot - into * 0.7f + Vector3.up * 1.2f, Vector3.down, out floor, 2.2f, mask, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(floor.point.y - spot.y) > 0.6f)
                {
                    continue;
                }
                best = hit.distance;
                found = true;
                var atWall = hit.point - into * 0.1f;
                ground = new Vector3(atWall.x, floor.point.y, atWall.z);
                yaw = Quaternion.LookRotation(-into).eulerAngles.y;
            }
            return found;
        }

        /// <summary>The door on the map: the one from the bundle if there is one, otherwise a plain slab.</summary>
        public static RoomDoor SpawnOutside(string label, Vector3 ground, float rotationY)
        {
            var prefab = RoomBundle.Prefab("TraderDoor");
            if (prefab != null)
            {
                try
                {
                    var root = Instantiate(prefab, ground, Quaternion.Euler(0f, rotationY, 0f));
                    root.name = "ZeroMenuTime door -- " + label;
                    Spawned.Add(root);
                    var leaf = TraderRoom.FindDeep(root.transform, "Leaf");
                    if (leaf == null)
                    {
                        throw new System.InvalidOperationException("the door prefab has no Leaf");
                    }
                    int solid = LayerMask.NameToLayer("HighPolyCollider");
                    foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                    {
                        if (collider.transform != leaf)
                        {
                            TraderRoom.MakeSolid(collider, solid < 0 ? 0 : solid);
                        }
                    }
                    return Attach(leaf.gameObject, label);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError("Door from the bundle failed, using the plain one: " + e);
                }
            }
            return Spawn(label, ground + new Vector3(0f, Height / 2f, 0f), rotationY);
        }

        public static RoomDoor Spawn(string label, Vector3 centre, float rotationY)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ZeroMenuTime door -- " + label;
            go.transform.position = centre;
            go.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
            go.transform.localScale = new Vector3(DoorWidth, Height, Thickness);
            go.layer = LayerMask.NameToLayer("Interactive");

            var door = go.AddComponent<RoomDoor>();
            door.Label = label;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
            {
                renderer.material.color = new Color(0.30f, 0.20f, 0.12f);
            }
            go.SetActive(true);
            Spawned.Add(go);
            return door;
        }

        public void Use()
        {
            if (OnUse != null)
            {
                OnUse();
                return;
            }
            if (_travelling || Plugin.Instance == null)
            {
                return;
            }
            Plugin.Instance.StartCoroutine(Travel(Destination, DestinationYaw));
        }

        private static AudioClip[] _openSounds, _shutSounds;
        private static bool _soundsLooked;

        /// <summary>
        /// Borrows the open and shut sounds of a door already on this map, so ours sounds like the
        /// doors around it. Maps without doors stay silent.
        /// </summary>
        public static void PlayDoorSound(bool opening, Vector3 at)
        {
            try
            {
                if (!_soundsLooked)
                {
                    _soundsLooked = true;
                    foreach (var door in FindObjectsOfType<Door>())
                    {
                        if (door.OpenSound != null && door.OpenSound.Length > 0 && door.ShutSound != null && door.ShutSound.Length > 0)
                        {
                            _openSounds = door.OpenSound;
                            _shutSounds = door.ShutSound;
                            break;
                        }
                    }
                    Plugin.Log.LogInfo(_openSounds != null ? "Door sounds borrowed from a door on this map" : "No door on this map to borrow sounds from");
                }
                var clips = opening ? _openSounds : _shutSounds;
                if (clips == null || clips.Length == 0 || !MonoBehaviourSingleton<BetterAudio>.Instantiated)
                {
                    return;
                }
                var clip = clips[Random.Range(0, clips.Length)];
                MonoBehaviourSingleton<BetterAudio>.Instance.PlayAtPoint(at, clip, BetterAudio.AudioSourceGroupType.InteractiveObjects, 10, 1f);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Door sound failed: " + e.Message);
            }
        }

        /// <summary>Darkens or clears the screen over about a third of a second.</summary>
        public static IEnumerator FadeTo(float target)
        {
            float from = Fade;
            for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
            {
                Fade = Mathf.Lerp(from, target, t / 0.35f);
                yield return null;
            }
            Fade = target;
        }

        private static IEnumerator Travel(Vector3 destination, float yaw)
        {
            _travelling = true;
            try
            {
                var leaving = GamePlayerOwner.MyPlayer;
                if (leaving != null)
                {
                    PlayDoorSound(true, leaving.Transform.position);
                }
                for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
                {
                    Fade = t / 0.35f;
                    yield return null;
                }
                Fade = 1f;

                // The furniture is fetched half a minute into a raid. Someone who steps through a
                // trader's door before that waits here, behind the dark screen, until it is in.
                if (RoomProps.AnyBare())
                {
                    FadeText = "Opening the door...";
                    yield return HideoutDoors.FurnishNow();
                    FadeText = null;
                }

                var player = GamePlayerOwner.MyPlayer;
                if (player != null)
                {
                    player.Teleport(destination, false);
                    player.Rotation = new Vector2(yaw, 0f);
                    Plugin.Log.LogInfo("Stepped through a door to " + destination.ToString("F1"));
                    PlayDoorSound(false, destination);
                }
                yield return new WaitForSecondsRealtime(0.2f);

                for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
                {
                    Fade = 1f - t / 0.35f;
                    yield return null;
                }
            }
            finally
            {
                Fade = 0f;
                _travelling = false;
            }
        }

        public static void DestroyAll()
        {
            foreach (var go in Spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            Spawned.Clear();
            Fade = 0f;
            _travelling = false;
            // The next raid may be on another map, with other doors.
            _soundsLooked = false;
            _openSounds = null;
            _shutSounds = null;
        }
    }
}
