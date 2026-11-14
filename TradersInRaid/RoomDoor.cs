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
        public Vector3 Destination;
        public float DestinationYaw;

        /// <summary>How dark the screen is right now, 0 to 1. Drawn by the plugin.</summary>
        public static float Fade;
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
            if (_travelling || Plugin.Instance == null)
            {
                return;
            }
            Plugin.Instance.StartCoroutine(Travel(Destination, DestinationYaw));
        }

        private static IEnumerator Travel(Vector3 destination, float yaw)
        {
            _travelling = true;
            try
            {
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
        }
    }
}
