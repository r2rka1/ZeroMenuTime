using System;
using System.Collections.Generic;
using EFT;
using TMPro;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// A station that is not built yet is only a bare patch of floor, hard to find. While the player
    /// is in the hideout a small sign hangs over each such place, naming what can be built there.
    /// The sign goes when the station is built.
    /// </summary>
    public static class HideoutSigns
    {
        public static BepInEx.Configuration.ConfigEntry<bool> Enabled;

        private static readonly Dictionary<int, GameObject> Signs = new Dictionary<int, GameObject>();
        private static float _next;

        /// <summary>Called every frame; does its work about once a second.</summary>
        public static void Tick()
        {
            if (Time.unscaledTime < _next)
            {
                return;
            }
            _next = Time.unscaledTime + 1f;
            try
            {
                var controller = HideoutDoors.Inside != null && Enabled != null && Enabled.Value ? HideoutStations.Controller() : null;
                if (controller == null)
                {
                    Clear();
                    return;
                }
                var wanted = new HashSet<int>();
                foreach (var pair in controller.Areas)
                {
                    int number = (int)pair.Key;
                    int level;
                    HideoutStations.Levels.TryGetValue(number, out level);
                    Bounds bounds;
                    // Only what can be built, and has a place of its own to point at.
                    if (level != 0 || !HideoutStations.NextCosts.ContainsKey(number)
                        || !HideoutStations.LevelBounds(pair.Value, 1, out bounds) || bounds.size.magnitude > 18f)
                    {
                        continue;
                    }
                    wanted.Add(number);
                    if (!Signs.ContainsKey(number) || Signs[number] == null)
                    {
                        Signs[number] = Make(HideoutStations.Name(pair.Key), bounds);
                    }
                }
                foreach (int number in new List<int>(Signs.Keys))
                {
                    if (!wanted.Contains(number))
                    {
                        if (Signs[number] != null)
                        {
                            UnityEngine.Object.Destroy(Signs[number]);
                        }
                        Signs.Remove(number);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Station signs: " + e.Message);
            }
        }

        private static GameObject Make(string name, Bounds bounds)
        {
            var sign = new GameObject("ZeroMenuTime sign -- " + name);
            // Above the place, but never up in the ceiling.
            float floor = HideoutBunker.Moved.y;
            sign.transform.position = new Vector3(bounds.center.x, Mathf.Clamp(bounds.max.y + 0.3f, floor + 1.3f, floor + 2.1f), bounds.center.z);
            var text = sign.AddComponent<TextMeshPro>();
            var font = HideoutPanel.Font();
            if (font != null)
            {
                text.font = font;
            }
            text.text = name.ToUpperInvariant() + "\n<size=60%><color=#C8A050>not built</color></size>";
            text.fontSize = 1.6f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.93f, 0.92f, 0.86f);
            text.enableWordWrapping = false;
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(0, 0, 0, 255);
            sign.AddComponent<FaceTheEye>();
            return sign;
        }

        public static void Clear()
        {
            foreach (var sign in Signs.Values)
            {
                if (sign != null)
                {
                    UnityEngine.Object.Destroy(sign);
                }
            }
            Signs.Clear();
        }

        /// <summary>Turns the sign to whoever is looking, and shows it only from a sensible distance.</summary>
        private class FaceTheEye : MonoBehaviour
        {
            private Renderer _drawn;

            private void LateUpdate()
            {
                var eye = Camera.main;
                if (eye == null)
                {
                    return;
                }
                if (_drawn == null)
                {
                    _drawn = GetComponent<Renderer>();
                }
                var away = transform.position - eye.transform.position;
                if (_drawn != null)
                {
                    _drawn.enabled = away.magnitude > 1.5f && away.magnitude < 16f;
                }
                away.y = 0f;
                if (away.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(away);
                }
            }
        }
    }
}
