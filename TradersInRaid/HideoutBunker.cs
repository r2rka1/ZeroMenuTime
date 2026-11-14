using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TradersInRaid
{
    /// <summary>
    /// The game's own hideout interior, loaded into a raid. The game only ever loads it on its own,
    /// as a separate session, so this takes care of what clashes when it shares the world with a map:
    /// its level settings, cameras and listeners.
    ///
    /// Stage 1 of the hideout work: find out whether it can live inside a raid at all.
    /// </summary>
    public static class HideoutBunker
    {
        private static ModernLoadScenesFromPreset.SceneToken _token;
        private static readonly List<Scene> BunkerScenes = new List<Scene>();

        /// <summary>True while the hideout's scenes are being loaded; its level settings must not take over then.</summary>
        public static bool Loading;
        public static bool Loaded => _token != null || _adopted;

        // A hot reload of this plugin forgets the token while the scene stays in the world. The
        // scene is then found again by name, and a marker object remembers how far it was moved.
        private static bool _adopted;
        private const string MarkerName = "TradersInRaid bunker marker";

        private static void Mark()
        {
            var marker = GameObject.Find(MarkerName) ?? new GameObject(MarkerName);
            marker.transform.position = Moved;
        }

        /// <summary>Takes over an interior loaded before this plugin was reloaded.</summary>
        public static bool Adopt()
        {
            if (Loaded || Loading)
            {
                return Loaded;
            }
            var marker = GameObject.Find(MarkerName);
            BunkerScenes.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.name.StartsWith("bunker", StringComparison.OrdinalIgnoreCase))
                {
                    BunkerScenes.Add(scene);
                }
            }
            if (BunkerScenes.Count == 0)
            {
                var names = new List<string>();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    names.Add(SceneManager.GetSceneAt(i).name);
                }
                Plugin.Log.LogInfo("No hideout interior among the loaded scenes: " + string.Join(", ", names.ToArray()));
                return false;
            }
            Moved = marker != null ? marker.transform.position : Vector3.zero;
            _adopted = true;
            RaidSettings = Singleton<LevelSettings>.Instantiated ? Singleton<LevelSettings>.Instance : null;
            foreach (var root in Roots())
            {
                var settings = root.GetComponentInChildren<LevelSettings>(true);
                if (settings != null && settings != RaidSettings)
                {
                    BunkerSettings = settings;
                }
            }
            Plugin.Log.LogInfo("Hideout interior taken over after a reload, moved " + Moved.ToString("F1"));
            return true;
        }

        /// <summary>The map's own level settings, and the bunker's, so either can be applied.</summary>
        public static LevelSettings RaidSettings, BunkerSettings;

        public static Vector3 Moved;

        public static async Task<string> Load()
        {
            if (Loaded || Loading || Adopt())
            {
                return "already loaded";
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            long before = GC.GetTotalMemory(false);
            RaidSettings = Singleton<LevelSettings>.Instantiated ? Singleton<LevelSettings>.Instance : null;
            var known = new HashSet<int>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                known.Add(SceneManager.GetSceneAt(i).handle);
            }

            Loading = true;
            try
            {
                _token = await ModernLoadScenesFromPreset.Load(Scenes.HideoutScenesPreset);
            }
            finally
            {
                Loading = false;
            }

            BunkerScenes.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!known.Contains(scene.handle))
                {
                    BunkerScenes.Add(scene);
                }
            }
            // Another scene can finish loading in the same moment (the menu's own, after a raid);
            // only the interior's are ours.
            if (BunkerScenes.Exists(s => s.name.StartsWith("bunker", StringComparison.OrdinalIgnoreCase)))
            {
                BunkerScenes.RemoveAll(s => !s.name.StartsWith("bunker", StringComparison.OrdinalIgnoreCase));
            }
            Moved = Vector3.zero;
            Mark();

            // Nothing in the bunker may render the screen or listen for sound in place of the player.
            int cameras = 0, listeners = 0;
            foreach (var root in Roots())
            {
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                {
                    camera.enabled = false;
                    cameras++;
                }
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
                {
                    listener.enabled = false;
                    listeners++;
                }
            }
            return "loaded " + BunkerScenes.Count + " scene(s) in " + timer.ElapsedMilliseconds + " ms, managed memory +"
                   + ((GC.GetTotalMemory(false) - before) / (1024 * 1024)) + " MB, switched off " + cameras + " camera(s) and "
                   + listeners + " listener(s), level settings kept: " + (Singleton<LevelSettings>.Instantiated && Singleton<LevelSettings>.Instance == RaidSettings);
        }

        /// <summary>True for anything that belongs to the bunker's scenes.</summary>
        public static bool Owns(GameObject go)
        {
            if (go == null)
            {
                return false;
            }
            foreach (var scene in BunkerScenes)
            {
                if (scene.handle == go.scene.handle)
                {
                    return true;
                }
            }
            return false;
        }

        public static IEnumerable<GameObject> Roots()
        {
            foreach (var scene in BunkerScenes)
            {
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }
                foreach (var root in scene.GetRootGameObjects())
                {
                    yield return root;
                }
            }
        }

        /// <summary>The box around everything in the bunker that is drawn.</summary>
        public static Bounds VisibleBounds()
        {
            var bounds = new Bounds(Moved, Vector3.one);
            bool any = false;
            foreach (var root in Roots())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!renderer.enabled)
                    {
                        continue;
                    }
                    if (!any)
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
            }
            return bounds;
        }

        /// <summary>Where the bunker is, what it is made of and whether it can be moved.</summary>
        public static List<string> Describe()
        {
            var lines = new List<string>();
            if (!Loaded)
            {
                lines.Add("bunker not loaded");
                return lines;
            }
            foreach (var scene in BunkerScenes)
            {
                lines.Add("scene " + scene.name + ": " + scene.rootCount + " root object(s)");
            }
            int renderers = 0, batched = 0, lights = 0, colliders = 0, bodies = 0;
            bool any = false;
            var bounds = new Bounds();
            var layers = new Dictionary<string, int>();
            foreach (var root in Roots())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderers++;
                    if (renderer.isPartOfStaticBatch)
                    {
                        batched++;
                    }
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    if (!any)
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
                lights += root.GetComponentsInChildren<Light>(true).Length;
                bodies += root.GetComponentsInChildren<Rigidbody>(true).Length;
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                {
                    colliders++;
                    string layer = LayerMask.LayerToName(collider.gameObject.layer);
                    int count;
                    layers.TryGetValue(layer, out count);
                    layers[layer] = count + 1;
                }
            }
            lines.Add("renderers " + renderers + " (statically batched " + batched + "), lights " + lights + ", colliders " + colliders + ", rigidbodies " + bodies);
            lines.Add("visible bounds: centre " + bounds.center.ToString("F1") + ", size " + bounds.size.ToString("F1")
                      + ", from " + bounds.min.ToString("F1") + " to " + bounds.max.ToString("F1") + "; moved so far " + Moved.ToString("F1"));
            var layerText = new List<string>();
            foreach (var pair in layers)
            {
                layerText.Add(pair.Key + " " + pair.Value);
            }
            lines.Add("collider layers: " + string.Join(", ", layerText.ToArray()));
            lines.Add("lightmaps in the world now: " + LightmapSettings.lightmaps.Length + ", ambient " + RenderSettings.ambientMode + " " + RenderSettings.ambientLight);

            foreach (var root in Roots())
            {
                var controller = root.GetComponentInChildren<HideoutController>(true);
                if (controller == null)
                {
                    continue;
                }
                lines.Add("hideout controller at " + controller.transform.position.ToString("F1") + " with " + controller.Areas.Count + " station(s):");
                foreach (var pair in controller.Areas)
                {
                    if (pair.Value != null)
                    {
                        lines.Add("  " + pair.Key + " at " + pair.Value.transform.position.ToString("F1") + ", levels " + pair.Value.AreaLevels.Length);
                    }
                }
            }
            lines.Add("bunker level settings found: " + (BunkerSettings != null));
            return lines;
        }

        /// <summary>Shifts everything in the bunker by the given amount.</summary>
        public static string Move(Vector3 by)
        {
            int moved = 0;
            foreach (var root in Roots())
            {
                root.transform.position += by;
                moved++;
            }
            Moved += by;
            Mark();
            Physics.SyncTransforms();
            return "moved " + moved + " root object(s) by " + by.ToString("F1");
        }

        private static readonly List<Light> SunsOff = new List<Light>();

        /// <summary>
        /// The map's sun and moon shine straight into the bunker, which has no ground above it to
        /// cast a shadow. Switches them off while the player is inside, and back on afterwards.
        /// </summary>
        public static string Sun(bool on)
        {
            if (on)
            {
                foreach (var light in SunsOff)
                {
                    if (light != null)
                    {
                        light.enabled = true;
                    }
                }
                int restored = SunsOff.Count;
                SunsOff.Clear();
                return "switched " + restored + " sky light(s) back on";
            }
            var mine = new HashSet<int>();
            foreach (var scene in BunkerScenes)
            {
                mine.Add(scene.handle);
            }
            var names = new List<string>();
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (light.type == LightType.Directional && light.enabled && !mine.Contains(light.gameObject.scene.handle))
                {
                    light.enabled = false;
                    SunsOff.Add(light);
                    names.Add(light.name);
                }
            }
            return "switched off " + names.Count + " sky light(s): " + string.Join(", ", names.ToArray());
        }

        public static async Task<string> Unload()
        {
            if (!Loaded)
            {
                return "not loaded";
            }
            Sun(true);
            var token = _token;
            _token = null;
            var marker = GameObject.Find(MarkerName);
            if (marker != null)
            {
                UnityEngine.Object.Destroy(marker);
            }
            if (token != null)
            {
                await ModernLoadScenesFromPreset.Unload(token);
            }
            else
            {
                _adopted = false;
                foreach (var scene in BunkerScenes.ToArray())
                {
                    var unloading = SceneManager.UnloadSceneAsync(scene);
                    while (unloading != null && !unloading.isDone)
                    {
                        await Task.Yield();
                    }
                }
            }
            BunkerScenes.Clear();
            BunkerSettings = null;
            if (RaidSettings != null && (!Singleton<LevelSettings>.Instantiated || Singleton<LevelSettings>.Instance != RaidSettings))
            {
                Singleton<LevelSettings>.Create(RaidSettings);
            }
            return "unloaded";
        }
    }

    /// <summary>
    /// The bunker's level settings would replace the map's the moment its scene wakes up, changing
    /// the look of the whole raid. Keep them aside instead.
    /// </summary>
    public class BunkerLevelSettingsAwakePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(LevelSettings), "Awake");
        }

        [PatchPrefix]
        public static bool Prefix(LevelSettings __instance)
        {
            if (!HideoutBunker.Loading)
            {
                return true;
            }
            HideoutBunker.BunkerSettings = __instance;
            Plugin.Log.LogInfo("Bunker level settings held back");
            return false;
        }
    }

    /// <summary>
    /// The bunker has its own keeper of indoor and outdoor areas. It stands down when it finds the
    /// map's already at work, but on being unloaded it would still clear the shared slot and leave
    /// the map without one.
    /// </summary>
    public class BunkerEnvironmentDestroyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.EnvironmentEffect.EnvironmentManager), "OnDestroy");
        }

        [PatchPrefix]
        public static bool Prefix(EFT.EnvironmentEffect.EnvironmentManager __instance)
        {
            return !HideoutBunker.Owns(__instance.gameObject);
        }
    }

    /// <summary>Settings that never took over must not give the map's settings up when they go.</summary>
    public class BunkerLevelSettingsDestroyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(LevelSettings), "OnDestroy");
        }

        [PatchPrefix]
        public static bool Prefix(LevelSettings __instance)
        {
            return HideoutBunker.RaidSettings == null || __instance == HideoutBunker.RaidSettings || !ReferenceEquals(__instance, HideoutBunker.BunkerSettings);
        }
    }
}
