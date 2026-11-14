using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using EFT.Communications;
using UnityEngine;

namespace TheTrueSPT
{
    /// <summary>
    /// Pictures for showing the mod off. One key saves what is on screen without the interface, at
    /// twice the screen's size; another hides the interface until it is pressed again, for use
    /// with any other capture tool.
    /// </summary>
    public static class Photo
    {
        public static ConfigEntry<KeyboardShortcut> ShotKey, HideKey;
        public static ConfigEntry<int> Scale;

        private static readonly List<Canvas> Hidden = new List<Canvas>();
        private static bool _hiding, _shooting;

        public static string Folder => Path.Combine(TraderConfig.Dir, "screenshots");

        public static void Bind(ConfigFile config)
        {
            ShotKey = config.Bind("Pictures", "Take a picture key", new KeyboardShortcut(KeyCode.F11),
                "Saves what is on screen, without the interface, to BepInEx/plugins/TheTrueSPT/screenshots.");
            HideKey = config.Bind("Pictures", "Hide the interface key", new KeyboardShortcut(KeyCode.F9),
                "Hides the whole interface until pressed again, for taking pictures with another tool.");
            Scale = config.Bind("Pictures", "Picture size", 2,
                new ConfigDescription("How many times the screen's size a saved picture is.", new AcceptableValueRange<int>(1, 4)));
        }

        /// <summary>Called every frame.</summary>
        public static void Tick()
        {
            if (ShotKey == null || Plugin.Instance == null)
            {
                return;
            }
            if (HideKey.Value.IsDown() && !_shooting)
            {
                _hiding = !_hiding;
                Show(!_hiding);
            }
            if (ShotKey.Value.IsDown() && !_shooting)
            {
                Plugin.Instance.StartCoroutine(Shoot());
            }
        }

        /// <summary>Takes a picture now, as the key would.</summary>
        public static void Take()
        {
            if (!_shooting && Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(Shoot());
            }
        }

        private static void Show(bool on)
        {
            if (on)
            {
                foreach (var canvas in Hidden)
                {
                    if (canvas != null)
                    {
                        canvas.enabled = true;
                    }
                }
                Hidden.Clear();
                return;
            }
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (canvas.enabled && canvas.isRootCanvas)
                {
                    canvas.enabled = false;
                    Hidden.Add(canvas);
                }
            }
        }

        private static IEnumerator Shoot()
        {
            _shooting = true;
            bool was = _hiding;
            if (!was)
            {
                Show(false);
            }
            // Two frames, so that the interface is gone from what gets saved.
            yield return null;
            yield return new WaitForEndOfFrame();
            string file = "";
            try
            {
                Directory.CreateDirectory(Folder);
                file = Path.Combine(Folder, "TheTrueSPT_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
                ScreenCapture.CaptureScreenshot(file, Mathf.Clamp(Scale.Value, 1, 4));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Taking a picture failed: " + e);
            }
            // The picture is written at the end of the next frame.
            yield return null;
            yield return null;
            yield return null;
            if (!was)
            {
                Show(true);
            }
            _shooting = false;
            if (file.Length > 0)
            {
                Plugin.Log.LogInfo("Picture saved: " + file);
                NotificationManager.DisplayMessageNotification("Picture saved: " + Path.GetFileName(file), ENotificationDurationType.Default);
            }
        }

        /// <summary>The interface must not stay hidden when the plugin is reloaded or a raid ends.</summary>
        public static void Restore()
        {
            _hiding = false;
            Show(true);
        }
    }
}
