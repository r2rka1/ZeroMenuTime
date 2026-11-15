using System;
using EFT;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroMenuTime
{
    /// <summary>
    /// The trader rooms and the hideout lie under the map with nothing over them, so the map's sun,
    /// moon and sky light fall straight in. While the player is inside one, the sky's lights are
    /// switched off and the sky's fill light is held down to a dim grey, so only the lamps inside
    /// light the place. Everything is put back on stepping out.
    /// </summary>
    public static class IndoorLight
    {
        private static bool _dark;
        private static float _nextCheck, _nextSunCheck;
        private static AmbientMode _mode;
        private static Color _light, _sky, _equator, _ground;
        private static float _intensity, _reflection;

        private static readonly Color RoomFill = new Color(0.085f, 0.085f, 0.09f);
        private static readonly Color HideoutFill = new Color(0.06f, 0.06f, 0.065f);
        private static readonly Color HideoutDark = new Color(0.012f, 0.012f, 0.014f);

        /// <summary>Called every frame.</summary>
        public static void Tick()
        {
            if (Time.unscaledTime < _nextCheck)
            {
                return;
            }
            _nextCheck = Time.unscaledTime + 0.5f;
            var player = GamePlayerOwner.MyPlayer;
            bool inside = false;
            if (player != null)
            {
                if (HideoutDoors.Inside != null)
                {
                    inside = true;
                }
                else
                {
                    var at = player.Transform.position;
                    foreach (var room in TraderRoom.Rooms)
                    {
                        if (room.Indoor != null && room.Indoor.Check(at) != null)
                        {
                            inside = true;
                            break;
                        }
                    }
                }
            }
            if (inside)
            {
                if (!_dark)
                {
                    _dark = true;
                    _nextSunCheck = 0f;
                    _mode = RenderSettings.ambientMode;
                    _light = RenderSettings.ambientLight;
                    _sky = RenderSettings.ambientSkyColor;
                    _equator = RenderSettings.ambientEquatorColor;
                    _ground = RenderSettings.ambientGroundColor;
                    _intensity = RenderSettings.ambientIntensity;
                    _reflection = RenderSettings.reflectionIntensity;
                    Camera.onPreCull += Hold;
                }
                // The sky swaps sun for moon as the hours pass; catch whichever is lit now. Looking
                // through every light on the map is slow, so only now and then.
                if (Time.unscaledTime >= _nextSunCheck)
                {
                    _nextSunCheck = Time.unscaledTime + 15f;
                    HideoutBunker.Sun(false);
                }
            }
            else if (_dark)
            {
                Restore();
            }
        }

        /// <summary>Just before each picture is drawn: the sky's own code has set its light by now.</summary>
        private static void Hold(Camera camera)
        {
            // A hideout without power is close to black; with power its own lamps do the rest.
            var fill = HideoutDoors.Inside == null ? RoomFill : HideoutPower.Powered ? HideoutFill : HideoutDark;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = fill;
            RenderSettings.ambientSkyColor = fill;
            RenderSettings.ambientEquatorColor = fill;
            RenderSettings.ambientGroundColor = fill;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = Mathf.Min(_reflection, 0.25f);
        }

        /// <summary>
        /// Puts the map's daylight back as the map has it: its sun and moon switched on and its own
        /// level settings applied. Used after a reload of this plugin, which forgets what an earlier
        /// copy had switched off.
        /// </summary>
        public static string Repair()
        {
            int lit = 0;
            string applied = "none";
            try
            {
                foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>(true))
                {
                    if (light.type == LightType.Directional && !light.enabled && !HideoutBunker.Owns(light.gameObject)
                        && !light.gameObject.scene.name.StartsWith("bunker", StringComparison.OrdinalIgnoreCase))
                    {
                        light.enabled = true;
                        lit++;
                    }
                }
                foreach (var settings in UnityEngine.Object.FindObjectsOfType<LevelSettings>(true))
                {
                    if (!HideoutBunker.Owns(settings.gameObject) && !settings.gameObject.scene.name.StartsWith("bunker", StringComparison.OrdinalIgnoreCase))
                    {
                        Comfort.Common.Singleton<LevelSettings>.Create(settings);
                        settings.ApplySettings();
                        applied = settings.gameObject.scene.name;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                return "FAILED " + e.Message;
            }
            Plugin.Log.LogInfo("Daylight repaired: " + lit + " sky light(s) back on, level settings of " + applied);
            return "sky lights back on " + lit + ", level settings of " + applied;
        }

        public static void Restore()
        {
            if (!_dark)
            {
                return;
            }
            _dark = false;
            Camera.onPreCull -= Hold;
            try
            {
                RenderSettings.ambientMode = _mode;
                RenderSettings.ambientLight = _light;
                RenderSettings.ambientSkyColor = _sky;
                RenderSettings.ambientEquatorColor = _equator;
                RenderSettings.ambientGroundColor = _ground;
                RenderSettings.ambientIntensity = _intensity;
                RenderSettings.reflectionIntensity = _reflection;
                HideoutBunker.Sun(true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Putting the daylight back failed: " + e.Message);
            }
        }
    }
}
