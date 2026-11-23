using System;
using System.Collections.Generic;
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

        /// <summary>The player is in a trader's room or in the hideout now.</summary>
        public static bool Inside => _dark;
        private static float _nextCheck, _nextSunCheck;
        private static AmbientMode _mode;
        private static Color _light, _sky, _equator, _ground;
        private static float _intensity, _reflection;

        private static readonly Color RoomFill = new Color(0.085f, 0.085f, 0.09f);
        private static readonly Color HideoutFill = new Color(0.06f, 0.06f, 0.065f);
        private static readonly Color HideoutDark = new Color(0.012f, 0.012f, 0.014f);

        // What the game's ambient light is held at indoors, whatever the hour.
        public static Color RoomSky = new Color(0.20f, 0.20f, 0.22f);
        public static Color HideoutSky = new Color(0.13f, 0.13f, 0.145f);
        public static Color HideoutSkyDark = new Color(0.05f, 0.05f, 0.06f);
        // How much of the sky may still be mirrored in shiny things indoors.
        public static float Mirror = 0.2f, MirrorDark = 0.04f;

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
                    PushAmbient();
                    Snow(false);
                }
                else if (_pushed != Indoors())
                {
                    // The power went on or off, or the player went from a room into the hideout.
                    PushAmbient();
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

        /// <summary>True while the player is in a room under the ground.</summary>
        public static bool Dark => _dark;

        /// <summary>The sky's own ambient light as it last gave it, to put back on stepping out.</summary>
        public static SphericalHarmonicsL2 LastSky;
        public static bool HasSky;
        private static Color _pushed;

        /// <summary>What the game's ambient light is held at indoors, whatever the hour outside.</summary>
        public static Color Indoors()
        {
            return HideoutDoors.Inside == null ? RoomSky : HideoutPower.Powered ? HideoutSky : HideoutSkyDark;
        }

        public static SphericalHarmonicsL2 IndoorsSh()
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(Indoors().linear);
            return sh;
        }

        /// <summary>
        /// The game lights everything by an ambient light of its own, which the sky feeds with the
        /// colour of the hour; left alone, a room under the ground is as bright as the day outside.
        /// This hands it the light again: indoors the patch swaps in the indoor light, outdoors
        /// the sky's own goes through.
        /// </summary>
        private static void PushAmbient()
        {
            try
            {
                _pushed = Indoors();
                if (!HasSky)
                {
                    return;
                }
                foreach (var ambient in UnityEngine.Object.FindObjectsOfType<AmbientLight>())
                {
                    ambient.SetSH(LastSky);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Setting the indoor light failed: " + e.Message);
            }
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

        private static readonly List<SnowWetRenderer> SnowOff = new List<SnowWetRenderer>();
        private static bool _winter;

        /// <summary>
        /// In winter the game lays snow over every surface that faces up, as an effect of the camera,
        /// and so over the floors of the rooms and the hideout too. Indoors that effect is switched
        /// off, and on again on the way out.
        /// </summary>
        private static void Snow(bool on)
        {
            try
            {
                if (on)
                {
                    foreach (var snow in SnowOff)
                    {
                        if (snow != null)
                        {
                            snow.enabled = true;
                        }
                    }
                    SnowOff.Clear();
                    if (_winter)
                    {
                        Shader.EnableKeyword("WINTER_SNOW");
                        _winter = false;
                    }
                    return;
                }
                foreach (var snow in UnityEngine.Object.FindObjectsOfType<SnowWetRenderer>())
                {
                    if (snow.enabled)
                    {
                        snow.enabled = false;
                        SnowOff.Add(snow);
                        // Switched off, it no longer renews its pass over the picture, but the pass
                        // already hung on the camera stays: that is taken off too. Switched on
                        // again, it makes itself a new one.
                        var passes = HarmonyLib.AccessTools.Field(typeof(SnowWetRenderer), "_cbSnowManager")?.GetValue(snow) as CommandBufferManager;
                        if (passes != null)
                        {
                            foreach (var pass in new List<KeyValuePair<Camera, UnityEngine.Rendering.CommandBuffer>>(passes.Cameras))
                            {
                                if (pass.Key != null && pass.Value != null)
                                {
                                    pass.Key.RemoveCommandBuffer(passes.CameraEvent, pass.Value);
                                }
                            }
                            HarmonyLib.AccessTools.Method(typeof(CommandBufferManager), "DestroyBuffers")?.Invoke(passes, null);
                        }
                    }
                }
                // The surfaces themselves draw snow while this is on, whatever the effect above does.
                _winter = Shader.IsKeywordEnabled("WINTER_SNOW");
                if (_winter)
                {
                    Shader.DisableKeyword("WINTER_SNOW");
                }
                if (SnowOff.Count > 0 || _winter)
                {
                    Plugin.Log.LogInfo("Indoors: the snow on surfaces is switched off (effects " + SnowOff.Count + ", winter surfaces " + _winter + ")");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Switching the snow failed: " + e.Message);
            }
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
                PushAmbient();
                Snow(true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Putting the daylight back failed: " + e.Message);
            }
        }
    }

    /// <summary>Indoors, the ambient light the sky hands the game is swapped for the indoor one.</summary>
    public class IndoorAmbientPatch : SPT.Reflection.Patching.ModulePatch
    {
        protected override System.Reflection.MethodBase GetTargetMethod()
        {
            return HarmonyLib.AccessTools.Method(typeof(AmbientLight), nameof(AmbientLight.SetSH));
        }

        [SPT.Reflection.Patching.PatchPrefix]
        public static void Prefix(ref SphericalHarmonicsL2 sh)
        {
            IndoorLight.LastSky = sh;
            IndoorLight.HasSky = true;
            if (IndoorLight.Dark)
            {
                sh = IndoorLight.IndoorsSh();
            }
        }
    }

    /// <summary>
    /// The game also mirrors the sky in everything that shines. Under the ground there is no sky to
    /// mirror, so indoors those reflections are turned well down.
    /// </summary>
    public class IndoorReflectionPatch : SPT.Reflection.Patching.ModulePatch
    {
        private static readonly int Strength = Shader.PropertyToID("_ReflectionIntensity");

        protected override System.Reflection.MethodBase GetTargetMethod()
        {
            return HarmonyLib.AccessTools.Method(typeof(AmbientLight), "method_3");
        }

        [SPT.Reflection.Patching.PatchPostfix]
        public static void Postfix(MaterialPropertyBlock ____screenAmbientBlock)
        {
            if (!IndoorLight.Dark || ____screenAmbientBlock == null)
            {
                return;
            }
            float most = HideoutDoors.Inside != null && !HideoutPower.Powered ? IndoorLight.MirrorDark : IndoorLight.Mirror;
            ____screenAmbientBlock.SetFloat(Strength, Mathf.Min(____screenAmbientBlock.GetFloat(Strength), most));
        }
    }
}
