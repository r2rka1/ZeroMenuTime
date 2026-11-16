using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace ZeroMenuTime
{
    /// <summary>
    /// Sound while the player is in the hideout. Two things were wrong down there. The map's own
    /// outdoor sounds (wind, birds, far-off shots) went on playing, though the map is hundreds of
    /// metres overhead. And the hideout's sounds are made for the menu's hideout, which has a mixer
    /// of its own that the volume setting of a raid does not reach, so they could not be turned down.
    /// Here the map's outdoor sounds are hushed while inside, and the hideout's sounds are sent
    /// through the raid's mixer, where the player's volume applies.
    /// </summary>
    public static class HideoutSound
    {
        private static readonly Dictionary<AudioSource, AudioMixerGroup> Rerouted = new Dictionary<AudioSource, AudioMixerGroup>();
        private static readonly HashSet<AudioSource> Hushed = new HashSet<AudioSource>();
        private static AudioMixerGroup _raidGroup;
        private static float _next;
        private static bool _inside;

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
                bool inside = HideoutDoors.Inside != null && HideoutBunker.Loaded;
                if (!inside)
                {
                    if (_inside)
                    {
                        Restore();
                    }
                    _inside = false;
                    return;
                }
                _inside = true;
                ApplyHideoutVolume();
                int hushed = 0, rerouted = 0;
                foreach (var source in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                {
                    var group = source.outputAudioMixerGroup;
                    if (HideoutBunker.Owns(source.gameObject))
                    {
                        var raid = RaidGroup();
                        if (raid != null && group != null && group.audioMixer != raid.audioMixer && !Rerouted.ContainsKey(source))
                        {
                            Rerouted[source] = group;
                            source.outputAudioMixerGroup = raid;
                            rerouted++;
                        }
                    }
                    else if (group != null && !source.mute && IsOutdoors(group.name) && Hushed.Add(source))
                    {
                        source.mute = true;
                        hushed++;
                    }
                }
                if (hushed > 0 || rerouted > 0)
                {
                    Plugin.Log.LogInfo("Hideout sound: " + hushed + " outdoor source(s) hushed, " + rerouted + " hideout source(s) put under the raid's volume");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hideout sound: " + e.Message);
            }
        }

        private static int _volumeSet = int.MinValue;

        /// <summary>
        /// The game has a volume setting for the hideout, but only applies it in the menu's hideout.
        /// Here it is applied while the player is in this one, and followed if it is changed.
        /// </summary>
        private static void ApplyHideoutVolume()
        {
            var settings = Comfort.Common.Singleton<EFT.Settings.SettingsManager>.Instance?.Sound?.Settings;
            var mixer = EFT.Settings.Sound.SoundSettingsController.MasterMixer;
            if (settings == null || mixer == null)
            {
                return;
            }
            int wanted = settings.HideoutVolumeValue;
            if (wanted != _volumeSet)
            {
                mixer.SetFloat("HideoutVolume", wanted);
                _volumeSet = wanted;
                Plugin.Log.LogInfo("Hideout sound: hideout volume set to " + wanted + " dB as in the game's settings");
            }
        }

        /// <summary>The map's weather and wildlife: mixer groups for ambience and precipitation.</summary>
        private static bool IsOutdoors(string group)
        {
            return group.IndexOf("Amb", StringComparison.OrdinalIgnoreCase) >= 0
                   || group.IndexOf("Precipitation", StringComparison.OrdinalIgnoreCase) >= 0
                   || group.IndexOf("Weather", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>A group of the raid's mixer for sounds heard indoors, found from the map's own sources.</summary>
        private static AudioMixerGroup RaidGroup()
        {
            if (_raidGroup != null)
            {
                return _raidGroup;
            }
            AudioMixer mixer = null;
            foreach (var source in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                var group = source.outputAudioMixerGroup;
                if (group != null && !HideoutBunker.Owns(source.gameObject) && IsOutdoors(group.name))
                {
                    mixer = group.audioMixer;
                    break;
                }
            }
            if (mixer == null)
            {
                return null;
            }
            foreach (string wanted in new[] { "CommonAmbInBypass", "AmbientIn", "Master" })
            {
                var found = mixer.FindMatchingGroups(wanted);
                if (found != null && found.Length > 0)
                {
                    _raidGroup = found[0];
                    Plugin.Log.LogInfo("Hideout sound: hideout sources go through the raid mixer's '" + _raidGroup.name + "'");
                    return _raidGroup;
                }
            }
            return null;
        }

        private static void Restore()
        {
            foreach (var source in Hushed)
            {
                if (source != null)
                {
                    source.mute = false;
                }
            }
            Hushed.Clear();
            foreach (var pair in Rerouted)
            {
                if (pair.Key != null)
                {
                    pair.Key.outputAudioMixerGroup = pair.Value;
                }
            }
            Rerouted.Clear();
            _raidGroup = null;
            _volumeSet = int.MinValue;
            Plugin.Log.LogInfo("Hideout sound: outdoor sounds back on");
        }
    }
}
