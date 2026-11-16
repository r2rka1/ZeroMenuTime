using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// The traders' own models and voices, taken at run time from the player's own installed copy of
    /// the 1.0 game. That game keeps them in one asset bundle built with the same engine version, so
    /// it can be opened where it lies. Nothing of it is shipped with this mod, copied or changed: the
    /// file is only read. Without that game installed the mod falls back to dressed-up figures and
    /// the traders stay silent.
    /// </summary>
    public static class TraderModelBundle
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<string> GameFolder;
        public static ConfigEntry<bool> Voices;
        public static ConfigEntry<float> VoiceVolume;

        private const string BundleInGame =
            "EscapeFromTarkov_Data/StreamingAssets/Windows/assets/scripts/dialogsystem/traderlipsyncscriptableobject.bundle";

        /// <summary>Which model in the bundle is which trader.</summary>
        private static readonly Dictionary<string, string> Models = new Dictionary<string, string>
        {
            { TraderConfig.Prapor, "Vendor_Prapor" },
            { TraderConfig.Therapist, "therapist_model" },
            { TraderConfig.Skier, "skier_model" },
            { TraderConfig.Mechanic, "Mechanic_model" },
            { TraderConfig.Ragman, "Ragman_Model" },
            { TraderConfig.Jaeger, "Jaeger_Model" },
            { TraderConfig.Fence, "Fence_Model" },
        };

        /// <summary>How each trader's sound clips begin.</summary>
        private static readonly Dictionary<string, string> VoicePrefix = new Dictionary<string, string>
        {
            { TraderConfig.Prapor, "prapor" },
            { TraderConfig.Therapist, "terapevt" },
            { TraderConfig.Skier, "lizhnik" },
            { TraderConfig.Mechanic, "mechanic" },
            { TraderConfig.Ragman, "baraholschik" },
            { TraderConfig.Jaeger, "eger" },
            { TraderConfig.Fence, "skupschik" },
        };

        /// <summary>What a trader says when trade starts, in order of preference.</summary>
        private static readonly string[] TradeLines = { "_start_trade_", "_trade_" };
        private static readonly string[] GreetingLines = { "_greetings_" };

        private static AssetBundle _bundle;
        private static bool _tried;
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<GameObject> Placed = new List<GameObject>();
        private static readonly Dictionary<string, GameObject> PlacedFor = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, float> SpokeAt = new Dictionary<string, float>();
        private static readonly Dictionary<string, string> LastLine = new Dictionary<string, string>();

        public static void Bind(ConfigFile config)
        {
            // BepInEx forbids = \n \t \ " ' [ ] in section and key names.
            Enabled = config.Bind("Trader models", "Use the real trader models", true,
                "Stand the traders' own models in their rooms. They are read from your own installed copy of the 1.0 game; without it the dressed-up figures are used.");
            GameFolder = config.Bind("Trader models", "Folder of the 1.0 game", "",
                "The folder your own copy of the 1.0 game is installed in. Left empty, the mod finds it by itself. The folder is only read, never changed.");
            Voices = config.Bind("Trader models", "Trader voices", true,
                "A trader says a line of their own when you start to trade. Needs your own installed copy of the 1.0 game.");
            VoiceVolume = config.Bind("Trader models", "Voice volume", 0.8f,
                new ConfigDescription("How loud the traders speak.", new AcceptableValueRange<float>(0f, 1f)));
        }

        /// <summary>The bundle in the player's own copy of the 1.0 game, or null if it is not there.</summary>
        private static string ResolvePath()
        {
            var folders = new List<string>();
            if (!string.IsNullOrEmpty(GameFolder.Value))
            {
                folders.Add(GameFolder.Value);
            }
            else
            {
                string installed = InstalledFolder();
                if (!string.IsNullOrEmpty(installed))
                {
                    folders.Add(installed);
                }
            }
            foreach (string folder in folders)
            {
                try
                {
                    string path = Path.Combine(folder, BundleInGame);
                    // Never this game's own folder: it has no such file, and nothing is to be mixed up.
                    if (File.Exists(path)
                        && !string.Equals(Path.GetFullPath(folder).TrimEnd('\\', '/'), Path.GetFullPath(BepInEx.Paths.GameRootPath).TrimEnd('\\', '/'),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Looking for the 1.0 game in '" + folder + "' failed: " + e.Message);
                }
            }
            return null;
        }

        /// <summary>The folder of the player's own copy of the 1.0 game, if the trader bundle is there; else null.</summary>
        public static string FoundAt()
        {
            try
            {
                string path = ResolvePath();
                return path == null ? null : Path.GetFullPath(path.Substring(0, path.Length - BundleInGame.Length)).TrimEnd('\\', '/');
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Where Windows says the game is installed. Read by reflection: the registry is not in this profile's references.</summary>
        private static string InstalledFolder()
        {
            try
            {
                var registry = Type.GetType("Microsoft.Win32.Registry, mscorlib");
                var get = registry?.GetMethod("GetValue", new[] { typeof(string), typeof(string), typeof(object) });
                if (get == null)
                {
                    return null;
                }
                foreach (string key in new[]
                         {
                             @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                             @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                         })
                {
                    string found = get.Invoke(null, new object[] { key, "InstallLocation", null }) as string;
                    if (!string.IsNullOrEmpty(found))
                    {
                        return found;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not ask Windows where the 1.0 game is installed: " + e.Message);
            }
            return null;
        }

        private static bool Load()
        {
            if (_tried)
            {
                return _bundle != null;
            }
            _tried = true;
            try
            {
                string path = ResolvePath();
                if (path == null)
                {
                    Plugin.Log.LogInfo("No installed copy of the 1.0 game found; traders are dressed-up figures and do not speak.");
                    return false;
                }
                var watch = Stopwatch.StartNew();
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle == null)
                {
                    Plugin.Log.LogWarning("The trader bundle at " + path + " could not be opened.");
                    return false;
                }
                long opened = watch.ElapsedMilliseconds;
                // The bundle names one thing only, a list the game's own dialogue code reads; the models and
                // sound clips come into memory with it, and are then found by name. Sound is not
                // unpacked until a clip is played.
                var listed = _bundle.LoadAllAssets();
                foreach (var prefab in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (prefab != null && !prefab.scene.IsValid() && prefab.transform.parent == null
                        && Models.Values.Contains(prefab.name) && !Prefabs.ContainsKey(prefab.name))
                    {
                        Prefabs[prefab.name] = prefab;
                    }
                }
                long models = watch.ElapsedMilliseconds;
                foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                {
                    if (clip != null && VoicePrefix.Values.Any(v => clip.name.StartsWith(v + "_", StringComparison.OrdinalIgnoreCase)))
                    {
                        Clips[clip.name] = clip;
                    }
                }
                Plugin.Log.LogInfo("Trader bundle lists " + listed.Length + " thing(s)");
                Plugin.Log.LogInfo("Trader bundle opened from the 1.0 game in " + opened + " ms; " + Prefabs.Count + " model(s) in "
                                   + (models - opened) + " ms, " + Clips.Count + " sound clip(s) in " + (watch.ElapsedMilliseconds - models) + " ms: "
                                   + string.Join(", ", Prefabs.Keys));
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Opening the trader bundle failed: " + e);
                return false;
            }
        }

        /// <summary>Stands the trader's model at the spot. Returns false if there is no model for them.</summary>
        public static bool TryPlace(string traderId, string traderName, Vector3 ground, float rotationY, GameObject markerBox)
        {
            try
            {
                if (!Enabled.Value || !Models.TryGetValue(traderId, out string modelName) || !Load())
                {
                    return false;
                }
                if (!Prefabs.TryGetValue(modelName, out var prefab))
                {
                    Plugin.Log.LogInfo("The trader bundle holds no model named " + modelName + " for " + traderName + ".");
                    return false;
                }

                var figure = UnityEngine.Object.Instantiate(prefab, ground, Quaternion.Euler(0f, rotationY, 0f));
                figure.name = "ZeroMenuTime model -- " + traderName;
                SetLayer(figure, LayerMask.NameToLayer("Player"));
                Tidy(figure);
                Placed.Add(figure);
                PlacedFor[traderId] = figure;

                if (markerBox != null)
                {
                    var renderer = markerBox.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                }
                Plugin.Log.LogInfo("Trader model for " + traderName + " placed at " + ground.ToString("F1"));
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Placing the trader model for " + traderName + " failed: " + e);
                return false;
            }
        }

        /// <summary>The model is for looking at: it makes no sound of its own, casts no light and stops nobody.</summary>
        private static void Tidy(GameObject figure)
        {
            foreach (var source in figure.GetComponentsInChildren<AudioSource>(true))
            {
                source.enabled = false;
            }
            foreach (var light in figure.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }
            foreach (var collider in figure.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            foreach (var animator in figure.GetComponentsInChildren<Animator>(true))
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            foreach (var renderer in figure.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
            }
        }

        /// <summary>The trader says one of their lines for starting a trade. Does nothing without the 1.0 game.</summary>
        public static void Speak(string traderId, Vector3 from, bool greeting = false)
        {
            try
            {
                if (!Voices.Value || !VoicePrefix.TryGetValue(traderId, out string prefix) || !Load())
                {
                    return;
                }
                // Not every time the screen is opened and shut.
                if (SpokeAt.TryGetValue(traderId, out float last) && Time.realtimeSinceStartup - last < 12f)
                {
                    return;
                }
                List<AudioClip> lines = null;
                foreach (string[] kinds in greeting ? new[] { GreetingLines, TradeLines } : new[] { TradeLines, GreetingLines })
                {
                    lines = Clips.Where(c => kinds.Any(k => c.Key.StartsWith(prefix + k, StringComparison.OrdinalIgnoreCase)))
                        .Select(c => c.Value).ToList();
                    if (lines.Count > 0)
                    {
                        break;
                    }
                }
                if (lines == null || lines.Count == 0)
                {
                    return;
                }
                LastLine.TryGetValue(traderId, out string before);
                if (lines.Count > 1)
                {
                    lines.RemoveAll(c => c.name == before);
                }
                var line = lines[UnityEngine.Random.Range(0, lines.Count)];
                LastLine[traderId] = line.name;
                SpokeAt[traderId] = Time.realtimeSinceStartup;

                PlacedFor.TryGetValue(traderId, out var figure);
                var mouth = new GameObject("ZeroMenuTime voice");
                mouth.transform.position = (figure != null ? figure.transform.position : from) + Vector3.up * 1.6f;
                var source = mouth.AddComponent<AudioSource>();
                source.clip = line;
                source.volume = VoiceVolume.Value;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 2f;
                source.maxDistance = 14f;
                source.dopplerLevel = 0f;
                source.Play();
                UnityEngine.Object.Destroy(mouth, line.length + 1f);
                Plugin.Log.LogInfo("Trader voice: " + line.name + " (" + line.length.ToString("F1") + " s)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Trader voice failed: " + e.Message);
            }
        }

        /// <summary>For the test harness: what was found.</summary>
        public static string Report()
        {
            bool loaded = Load();
            return "trader bundle " + (loaded ? "open" : "not found") + "; models " + Prefabs.Count + " [" + string.Join(", ", Prefabs.Keys)
                   + "]; clips " + Clips.Count + "; placed " + Placed.Count(p => p != null)
                   + "; lines per trader: " + string.Join(", ", VoicePrefix.Select(v => v.Value + " "
                       + Clips.Keys.Count(k => TradeLines.Concat(GreetingLines).Any(t => k.StartsWith(v.Value + t, StringComparison.OrdinalIgnoreCase)))));
        }

        private static void SetLayer(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayer(child.gameObject, layer);
            }
        }

        public static void DestroyAll()
        {
            foreach (var figure in Placed)
            {
                if (figure != null)
                {
                    UnityEngine.Object.Destroy(figure);
                }
            }
            Placed.Clear();
            PlacedFor.Clear();
        }

        /// <summary>Lets the bundle go, so it can be opened afresh.</summary>
        public static void Forget()
        {
            DestroyAll();
            Prefabs.Clear();
            Clips.Clear();
            if (_bundle != null)
            {
                _bundle.Unload(true);
                _bundle = null;
            }
            _tried = false;
        }
    }
}
