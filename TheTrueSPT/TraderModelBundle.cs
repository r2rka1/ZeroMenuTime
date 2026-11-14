using System;
using System.Collections.Generic;
using System.IO;

using BepInEx.Configuration;
using UnityEngine;

namespace TheTrueSPT
{
    /// <summary>
    /// Stands the trader's own model at a spot, loaded from an asset bundle built from the game's
    /// vendor scenes. The bundle is optional: without it the mod falls back to a dressed-up
    /// character, so nothing here is required for the mod to work.
    ///
    /// The bundle is never shipped with this mod. It is built from the player's own copy of the
    /// game, because the models belong to the game's makers.
    /// </summary>
    public static class TraderModelBundle
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<string> BundlePath;

        /// <summary>Which prefab in the bundle stands in for which trader.</summary>
        private static readonly Dictionary<string, string> Prefabs = new Dictionary<string, string>
        {
            { TraderConfig.Prapor, "Vendor_Prapor_Figure" },
            { TraderConfig.Therapist, "therapist_model_Figure" },
            { TraderConfig.Skier, "skier_model_Figure" },
            { TraderConfig.Mechanic, "Mechanic_model_Figure" },
            { TraderConfig.Ragman, "Ragman_Model_Figure" },
            { TraderConfig.Jaeger, "Jaeger_Model_Figure" },
            { TraderConfig.Fence, "Fence_Model_Figure" },
        };

        private static AssetBundle _bundle;
        private static bool _tried;
        private static readonly List<GameObject> Placed = new List<GameObject>();

        public static void Bind(ConfigFile config)
        {
            // BepInEx forbids = \n \t \ " ' [ ] in section and key names.
            Enabled = config.Bind("Trader models", "Use trader models from a bundle", true,
                "Stand the real trader models at their spots, if a bundle built from your own copy of the game is present.");
            BundlePath = config.Bind("Trader models", "Bundle file", "",
                "Where the trader bundle is. Left empty, the mod looks for a file named traders next to this plugin.");
        }

        private static string ResolvePath()
        {
            if (!string.IsNullOrEmpty(BundlePath.Value))
            {
                return BundlePath.Value;
            }
            // While hot reloading, this assembly is loaded from memory and has no file path of its
            // own, so go by BepInEx's own folders instead.
            foreach (var candidate in new[]
                     {
                         Path.Combine(BepInEx.Paths.PluginPath, "TheTrueSPT", "traders"),
                         Path.Combine(BepInEx.Paths.PluginPath, "traders"),
                     })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static AssetBundle Bundle()
        {
            if (_tried)
            {
                return _bundle;
            }
            _tried = true;
            try
            {
                string path = ResolvePath();
                if (path == null)
                {
                    Plugin.Log.LogInfo("No trader model bundle found; using dressed-up figures instead.");
                    return null;
                }
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle == null)
                {
                    Plugin.Log.LogWarning("The trader model bundle at " + path + " could not be read.");
                    return null;
                }
                Plugin.Log.LogInfo("Trader model bundle loaded from " + path + " holding: "
                                   + string.Join(", ", _bundle.GetAllAssetNames()));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Loading the trader model bundle failed: " + e);
            }
            return _bundle;
        }

        /// <summary>Stands the trader's model at the spot. Returns false if there is no model for them.</summary>
        public static bool TryPlace(string traderId, string traderName, Vector3 ground, float rotationY, GameObject markerBox)
        {
            try
            {
                if (!Enabled.Value || !Prefabs.TryGetValue(traderId, out string prefabName))
                {
                    return false;
                }
                var bundle = Bundle();
                if (bundle == null)
                {
                    return false;
                }

                var prefab = bundle.LoadAsset<GameObject>(prefabName);
                if (prefab == null)
                {
                    Plugin.Log.LogInfo("The bundle holds no model named " + prefabName + " for " + traderName + ".");
                    return false;
                }

                var figure = UnityEngine.Object.Instantiate(prefab, ground, Quaternion.Euler(0f, rotationY, 0f));
                figure.name = "TheTrueSPT model -- " + traderName;
                SetLayer(figure, LayerMask.NameToLayer("Player"));
                Placed.Add(figure);

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
        }

        /// <summary>Lets the bundle be swapped without restarting the game while testing.</summary>
        public static void Forget()
        {
            DestroyAll();
            if (_bundle != null)
            {
                _bundle.Unload(true);
                _bundle = null;
            }
            _tried = false;
        }
    }
}
