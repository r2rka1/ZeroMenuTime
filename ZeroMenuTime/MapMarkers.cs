using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// Puts the hideout's door and the traders' doors on the map of the Dynamic Maps mod, when that
    /// mod is installed. Everything is done by name at run time, so nothing here depends on it:
    /// without Dynamic Maps there are simply no markers.
    /// </summary>
    public static class MapMarkers
    {
        private const string Category = "Zero Menu Time";

        private static bool _looked;
        private static Type _viewType, _defType;
        private static MethodInfo _add, _byCategory, _toMap;
        private static PropertyInfo _mapDef;
        private static float _nextCheck, _nextSearch;
        // Searching a whole map's scene for the map view is slow enough to feel; it is done
        // rarely, and the views found are remembered.
        private static readonly List<UnityEngine.Object> Views = new List<UnityEngine.Object>();
        private static readonly HashSet<string> Added = new HashSet<string>();

        private static bool Ready()
        {
            if (_looked)
            {
                return _viewType != null;
            }
            _looked = true;
            try
            {
                Assembly maps = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == "DynamicMaps")
                    {
                        maps = assembly;
                        break;
                    }
                }
                if (maps == null)
                {
                    Plugin.Log.LogInfo("Dynamic Maps is not installed; no door markers on the map");
                    return false;
                }
                var view = maps.GetType("DynamicMaps.UI.Components.MapView");
                _defType = maps.GetType("DynamicMaps.Data.MapMarkerDef");
                var math = maps.GetType("DynamicMaps.Utils.MathUtils");
                _add = view.GetMethod("AddMapMarker", new[] { _defType });
                _byCategory = view.GetMethod("GetMapMarkersByCategory", new[] { typeof(string) });
                _mapDef = view.GetProperty("CurrentMapDef");
                _toMap = math.GetMethod("ConvertToMapPosition", new[] { typeof(Vector3) });
                if (_add == null || _byCategory == null || _mapDef == null || _toMap == null)
                {
                    Plugin.Log.LogWarning("Dynamic Maps has changed; no door markers on the map");
                    return false;
                }
                _viewType = view;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Door markers: " + e.Message);
            }
            return _viewType != null;
        }

        /// <summary>Called every frame; looks once a second for a map that is showing this raid's map without our markers.</summary>
        public static void Tick()
        {
            if (Time.unscaledTime < _nextCheck)
            {
                return;
            }
            _nextCheck = Time.unscaledTime + 1f;
            var player = GamePlayerOwner.MyPlayer;
            if (player == null || !Ready())
            {
                return;
            }
            try
            {
                Views.RemoveAll(view => view == null);
                if (Views.Count == 0 && Time.unscaledTime >= _nextSearch)
                {
                    _nextSearch = Time.unscaledTime + 20f;
                    Views.AddRange(UnityEngine.Object.FindObjectsOfType(_viewType));
                }
                foreach (var found in Views)
                {
                    var def = _mapDef.GetValue(found, null);
                    if (def == null || !IsThisMap(def, Layout.Map != "" ? Layout.Map : player.Location))
                    {
                        continue;
                    }
                    // Each door is added once per map view; a door that appears later (the rooms are
                    // built a little after the raid starts) is added when it does.
                    int viewId = found.GetInstanceID();
                    int added = 0;
                    foreach (var place in HideoutDoors.DoorPlaces())
                    {
                        if (Added.Add(viewId + "|hideout|" + place.ToString("F0")))
                        {
                            Add(found, "Hideout", place, new Color(0.77f, 0.63f, 0.28f), "Markers/door_with_key.png");
                            added++;
                        }
                    }
                    foreach (var room in TraderRoom.Rooms)
                    {
                        if (room.OutsideDoor != null && Added.Add(viewId + "|" + room.TraderId + "|" + room.OutsideDoor.transform.position.ToString("F0")))
                        {
                            Add(found, room.TraderName, room.OutsideDoor.transform.position, new Color(0.91f, 0.90f, 0.83f), "Markers/door_with_key.png");
                            added++;
                        }
                    }
                    if (added > 0)
                    {
                        Plugin.Log.LogInfo("Door markers put on the map: " + added);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Door markers: " + (e.InnerException ?? e).Message);
                _viewType = null;   // do not try again this session
            }
        }

        private static bool IsThisMap(object mapDef, string location)
        {
            var names = mapDef.GetType().GetProperty("MapInternalNames").GetValue(mapDef, null) as IEnumerable;
            if (names == null || location == null)
            {
                return false;
            }
            foreach (var name in names)
            {
                if (string.Equals(name as string, location, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void Add(object view, string text, Vector3 at, Color colour, string image)
        {
            var def = Activator.CreateInstance(_defType);
            Set(def, "Text", text);
            Set(def, "ImagePath", image);
            Set(def, "Position", _toMap.Invoke(null, new object[] { at }));
            Set(def, "Category", Category);
            Set(def, "Color", colour);
            _add.Invoke(view, new[] { def });
        }

        private static void Set(object target, string property, object value)
        {
            var info = target.GetType().GetProperty(property);
            if (info != null)
            {
                info.SetValue(target, value, null);
            }
        }
    }
}
