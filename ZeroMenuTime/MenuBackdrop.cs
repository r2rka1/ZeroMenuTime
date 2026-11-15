using System;
using System.Collections;
using System.Collections.Generic;
using EFT;
using EFT.UI;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// What is seen behind the main menu: the player's own hideout, live, through a slowly drifting
    /// camera, in place of the game's stock scene. Nothing is shipped for it; the interior is the
    /// game's own, loaded the way it is loaded into a raid.
    /// </summary>
    public static class MenuBackdrop
    {
        /// <summary>A place to look from: where, which way, and how far the view sways.</summary>
        public struct Framing
        {
            public Vector3 Position;
            public float Yaw, Pitch;
        }

        /// <summary>Framings the view moves between, in the interior's own coordinates.</summary>
        public static readonly List<Framing> Framings = new List<Framing>
        {
            // Both stand in the main hall with walls and ceiling all round; a view from the unbuilt
            // side rooms looks out through walls that are only drawn from the inside.
            new Framing { Position = new Vector3(-6f, 1.7f, 3f), Yaw = 120f, Pitch = 6f },
            new Framing { Position = new Vector3(0f, 1.7f, -3f), Yaw = 0f, Pitch = 4f },
        };

        private const string RigName = "ZeroMenuTime menu camera";

        // Testing can change these before the backdrop is built.
        public static Vector3 LampOffset = new Vector3(1.0f, 0.1f, 3.0f);
        public static float LampRange = 16f, LampPower = 1.5f;
        private static Light _lamp, _fill;
        public static bool ApplyLightSettings = false;
        private const float SecondsPerFraming = 40f;

        private static Camera _camera;
        private static bool _loading, _ready;
        private static float _started;

        /// <summary>For testing: hold the view at one spot.</summary>
        public static Framing? Fixed;

        public static bool Showing => _camera != null && _camera.enabled;
        public static string Label = "";

        /// <summary>Called every frame; says whether the main menu is on screen.</summary>
        public static void Tick(bool menuShowing)
        {
            if (Plugin.Instance == null)
            {
                return;
            }
            if (!menuShowing || GamePlayerOwner.MyPlayer != null)
            {
                if (Showing)
                {
                    Hide(true);
                }
                return;
            }
            if (!_ready)
            {
                if (!_loading)
                {
                    _loading = true;
                    Plugin.Instance.StartCoroutine(Prepare());
                }
                return;
            }
            if (_camera == null)
            {
                return;
            }
            if (!_camera.enabled)
            {
                _camera.enabled = true;
                _started = Time.unscaledTime;
            }
            Move();
        }

        /// <summary>The stock scene goes while ours is up. Called twice a second, since screens switch it back on.</summary>
        public static void KeepStockHidden()
        {
            if (!Showing)
            {
                return;
            }
            try
            {
                var stock = MonoBehaviourSingleton<EnvironmentUI>.Instance;
                if (stock != null)
                {
                    stock.ShowEnvironment(false);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void Move()
        {
            float at = (Time.unscaledTime - _started) / SecondsPerFraming;
            float phase = at - Mathf.Floor(at);
            var view = Fixed ?? Framings[Mathf.FloorToInt(at) % Framings.Count];
            // The view eases forward and sways a little, so it is never quite still.
            float sway = Fixed.HasValue ? 0f : Mathf.Sin(Time.unscaledTime * 0.12f) * 2.5f;
            float push = Fixed.HasValue ? 0f : phase * 0.7f;
            var rotation = Quaternion.Euler(view.Pitch, view.Yaw + sway, 0f);
            var position = view.Position + HideoutBunker.Moved + Quaternion.Euler(0f, view.Yaw, 0f) * Vector3.forward * push;
            _camera.transform.SetPositionAndRotation(position, rotation);
            // The lamps go down before the view changes and come up after, so the change is a dip
            // into darkness and not a jump.
            float lit = Fixed.HasValue || Framings.Count < 2 ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Min(phase, 1f - phase) / 0.05f);
            if (_lamp != null)
            {
                _lamp.intensity = LampPower * lit;
            }
            if (_fill != null)
            {
                _fill.intensity = LampPower * 0.35f * lit;
            }
        }

        private static IEnumerator Prepare()
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
                    Plugin.Log.LogError("Menu backdrop: the hideout interior did not load: " + (loading.Exception != null ? loading.Exception.InnerException.ToString() : "?"));
                    yield break;   // _loading stays set: no second try this session
                }
                Plugin.Log.LogInfo("Menu backdrop: " + loading.Result);
            }
            if (GamePlayerOwner.MyPlayer != null)
            {
                _loading = false;
                yield break;
            }

            // The stations as they stand in the hideout the character uses.
            var asking = RequestHandler.PostJsonAsync("/zeromenutime/hideout/list", "{}");
            while (!asking.IsCompleted)
            {
                yield return null;
            }
            try
            {
                var reply = JObject.Parse(asking.Result);
                HideoutStations.Read(reply);
                HideoutStations.ShowAll();
                Label = (string)reply["Active"] ?? "";
                double clock = (double?)reply["Clock"] ?? 0;
                MainMenu.When = WorldClock.Describe(clock);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Menu backdrop: stations not shown: " + e.Message);
            }
            try
            {
                if (ApplyLightSettings && HideoutBunker.BunkerSettings != null)
                {
                    HideoutBunker.BunkerSettings.ApplySettings();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Menu backdrop: the interior's light settings were not applied: " + e.Message);
            }

            var old = GameObject.Find(RigName);
            if (old != null)
            {
                UnityEngine.Object.Destroy(old);
            }
            var rig = new GameObject(RigName);
            UnityEngine.Object.DontDestroyOnLoad(rig);
            _camera = rig.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.fieldOfView = 55f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 200f;
            _camera.depth = -5f;
            _camera.allowHDR = true;
            _camera.enabled = false;

            // The hideout is nearly unlit until its generator and lamps are built; a warm lamp
            // travels with the view so there is always something to see.
            var lamp = new GameObject("lamp");
            lamp.transform.SetParent(rig.transform, false);
            lamp.transform.localPosition = LampOffset;
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.82f, 0.60f);
            light.range = LampRange;
            light.intensity = LampPower;
            light.shadows = LightShadows.Soft;
            _lamp = light;
            var fill = new GameObject("fill");
            fill.transform.SetParent(rig.transform, false);
            fill.transform.localPosition = new Vector3(-1.5f, 0.6f, -1f);
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.color = new Color(0.55f, 0.68f, 0.85f);
            fillLight.range = 30f;
            fillLight.intensity = LampPower * 0.35f;
            fillLight.shadows = LightShadows.None;
            _fill = fillLight;
            _ready = true;
            _loading = false;
            Plugin.Log.LogInfo("Menu backdrop ready; hideout shown: " + Label);
        }

        /// <summary>Stops showing the hideout; the stock scene comes back for the game's other screens.</summary>
        public static void Hide(bool stockBack)
        {
            if (_camera != null)
            {
                _camera.enabled = false;
            }
            if (!stockBack)
            {
                return;
            }
            try
            {
                var stock = MonoBehaviourSingleton<EnvironmentUI>.Instance;
                if (stock != null)
                {
                    stock.ShowEnvironment(true);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>A raid is about to load: the interior must not be carried into it.</summary>
        public static async void Release()
        {
            Hide(true);
            _ready = false;
            if (_camera != null)
            {
                UnityEngine.Object.Destroy(_camera.gameObject);
                _camera = null;
            }
            try
            {
                HideoutStations.Clear();
                if (HideoutBunker.Loaded)
                {
                    await HideoutBunker.Unload();
                    Plugin.Log.LogInfo("Menu backdrop: interior let go for the raid");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Menu backdrop: letting the interior go failed: " + e);
            }
        }

        /// <summary>What draws the menu, for working out what sits in front of what.</summary>
        public static List<string> Describe()
        {
            var lines = new List<string>();
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (canvas.isRootCanvas && canvas.isActiveAndEnabled)
                {
                    lines.Add("canvas " + canvas.name + ": " + canvas.renderMode + ", order " + canvas.sortingOrder
                              + ", camera " + (canvas.worldCamera != null ? canvas.worldCamera.name : "-") + ", size " + ((RectTransform)canvas.transform).rect.size.ToString("F0"));
                }
            }
            foreach (var camera in Camera.allCameras)
            {
                lines.Add("camera " + camera.name + ": depth " + camera.depth + ", clears " + camera.clearFlags + ", mask " + camera.cullingMask.ToString("X")
                          + ", path " + camera.actualRenderingPath + ", at " + camera.transform.position.ToString("F1"));
            }
            lines.Add("backdrop ready " + _ready + ", showing " + Showing + ", interior loaded " + HideoutBunker.Loaded + ", moved " + HideoutBunker.Moved.ToString("F1"));
            foreach (var root in HideoutBunker.Roots())
            {
                var controller = root.GetComponentInChildren<EFT.Hideout.HideoutController>(true);
                if (controller == null)
                {
                    continue;
                }
                foreach (var pair in controller.Areas)
                {
                    int level;
                    HideoutStations.Levels.TryGetValue((int)pair.Key, out level);
                    if (pair.Value != null)
                    {
                        lines.Add("station " + pair.Key + " level " + level + " at " + pair.Value.transform.position.ToString("F1"));
                    }
                }
            }
            if (HideoutBunker.Loaded)
            {
                var b = HideoutBunker.VisibleBounds();
                lines.Add("interior from " + b.min.ToString("F1") + " to " + b.max.ToString("F1"));
            }
            return lines;
        }
    }
}
