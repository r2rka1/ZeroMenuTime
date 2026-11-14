using System;
using EFT;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheTrueSPT
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
        private static float _nextCheck;
        private static AmbientMode _mode;
        private static Color _light, _sky, _equator, _ground;
        private static float _intensity, _reflection;

        private static readonly Color RoomFill = new Color(0.085f, 0.085f, 0.09f);
        private static readonly Color HideoutFill = new Color(0.05f, 0.05f, 0.055f);

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
                    _mode = RenderSettings.ambientMode;
                    _light = RenderSettings.ambientLight;
                    _sky = RenderSettings.ambientSkyColor;
                    _equator = RenderSettings.ambientEquatorColor;
                    _ground = RenderSettings.ambientGroundColor;
                    _intensity = RenderSettings.ambientIntensity;
                    _reflection = RenderSettings.reflectionIntensity;
                    Camera.onPreCull += Hold;
                }
                // The sky swaps sun for moon as the hours pass; catch whichever is lit now.
                HideoutBunker.Sun(false);
            }
            else if (_dark)
            {
                Restore();
            }
        }

        /// <summary>Just before each picture is drawn: the sky's own code has set its light by now.</summary>
        private static void Hold(Camera camera)
        {
            var fill = HideoutDoors.Inside != null ? HideoutFill : RoomFill;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = fill;
            RenderSettings.ambientSkyColor = fill;
            RenderSettings.ambientEquatorColor = fill;
            RenderSettings.ambientGroundColor = fill;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = Mathf.Min(_reflection, 0.25f);
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
