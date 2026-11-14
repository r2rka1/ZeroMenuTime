using System.Collections;
using System.Collections.Generic;
using EFT;
using EFT.Interactive;
using EFT.UI;
using UnityEngine;

namespace TradersInRaid
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
                    || hit.distance < 0.7f || hit.distance >= best || Mathf.Abs(hit.normal.y) > 0.15f)
                {
                    continue;
                }
                // It has to be a flat stretch of wall as tall and wide as the door: probe it at the
                // door's corners, straight along the wall's own normal.
                var into = -new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                var along = Vector3.Cross(Vector3.up, into);
                var foot = hit.point - Vector3.up * 1.0f;
                bool flat = true;
                foreach (var offset in new[] { new Vector2(-0.55f, 0.25f), new Vector2(0.55f, 0.25f), new Vector2(-0.55f, 2.0f), new Vector2(0.55f, 2.0f), new Vector2(0f, 1.1f) })
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
                    root.name = "TradersInRaid door -- " + label;
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
            go.name = "TradersInRaid door -- " + label;
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
