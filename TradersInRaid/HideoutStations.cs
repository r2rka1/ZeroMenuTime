using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Hideout;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>Something to look at where a station stands, offering to build or upgrade it.</summary>
    public class StationInteractable : InteractableObject
    {
        public int Area;
        public string StationName;

        public AvailableInteractionState GetActions()
        {
            return HideoutStations.ActionsFor(this);
        }
    }

    /// <summary>
    /// The stations of the hideout the player is standing in: showing each at its level, and
    /// building and upgrading them in first person. Levels and costs come from the server, which
    /// keeps a separate set for each hideout; the models are the game's own, in the bunker.
    /// </summary>
    public static class HideoutStations
    {
        public class Cost
        {
            public string Tpl;
            public int Count;
        }

        /// <summary>Station number to level, and to what the next level costs, for the hideout in use.</summary>
        public static readonly Dictionary<int, int> Levels = new Dictionary<int, int>();
        public static readonly Dictionary<int, List<Cost>> NextCosts = new Dictionary<int, List<Cost>>();

        private static readonly Dictionary<int, GameObject> Boxes = new Dictionary<int, GameObject>();
        private static bool _building;

        /// <summary>Takes levels and costs from any of the server's hideout answers.</summary>
        public static void Read(JObject reply)
        {
            Levels.Clear();
            NextCosts.Clear();
            var levels = reply["Levels"] as JObject;
            if (levels != null)
            {
                foreach (var pair in levels)
                {
                    Levels[int.Parse(pair.Key)] = (int)pair.Value;
                }
            }
            var costs = reply["NextCosts"] as JObject;
            if (costs != null)
            {
                foreach (var pair in costs)
                {
                    var list = new List<Cost>();
                    foreach (var entry in (JArray)pair.Value)
                    {
                        list.Add(new Cost { Tpl = (string)entry["Tpl"], Count = (int)entry["Count"] });
                    }
                    NextCosts[int.Parse(pair.Key)] = list;
                }
            }
        }

        public static HideoutController Controller()
        {
            foreach (var root in HideoutBunker.Roots())
            {
                var controller = root.GetComponentInChildren<HideoutController>(true);
                if (controller != null)
                {
                    return controller;
                }
            }
            return null;
        }

        private static string Name(EAreaType type)
        {
            // "IntelligenceCenter" -> "Intelligence Center"
            return System.Text.RegularExpressions.Regex.Replace(type.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
        }

        /// <summary>Shows every station at its level and puts something to look at on each.</summary>
        public static void ShowAll()
        {
            var controller = Controller();
            if (controller == null)
            {
                Plugin.Log.LogWarning("No hideout controller in the bunker; stations not shown");
                return;
            }
            int shown = 0;
            foreach (var pair in controller.Areas)
            {
                try
                {
                    int level;
                    Levels.TryGetValue((int)pair.Key, out level);
                    Show(pair.Key, pair.Value, level, true);
                    shown++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Station " + pair.Key + " could not be shown: " + e.Message);
                }
            }
            Plugin.Log.LogInfo("Stations shown: " + shown);
        }

        private static void Show(EAreaType type, HideoutArea area, int level, bool immediate)
        {
            if (area == null || area.AreaLevels == null || area.AreaLevels.Length == 0)
            {
                return;
            }
            level = Mathf.Clamp(level, 0, area.AreaLevels.Length - 1);
            for (int i = 0; i < area.AreaLevels.Length; i++)
            {
                if (area.AreaLevels[i] != null && i != level)
                {
                    area.AreaLevels[i].Disable();
                }
            }
            if (area.AreaDefaultLevel != null)
            {
                area.AreaDefaultLevel.Disable();
            }
            if (area.AreaLevels[level] != null)
            {
                // Not "immediate" plays the game's own construction effect.
                area.AreaLevels[level].Enable(immediate);
            }
            PlaceBox(type, area, level);
        }

        /// <summary>
        /// The box the player looks at to use a station. It is laid around what the station looks
        /// like now, or, for one not built yet, around what its first level will look like.
        /// </summary>
        private static void PlaceBox(EAreaType type, HideoutArea area, int level)
        {
            GameObject old;
            if (Boxes.TryGetValue((int)type, out old) && old != null)
            {
                UnityEngine.Object.Destroy(old);
            }
            Boxes.Remove((int)type);

            Bounds bounds;
            int around = level;
            if (!LevelBounds(area, around, out bounds) && !(area.AreaLevels.Length > 1 && LevelBounds(area, around = 1, out bounds)))
            {
                return;
            }
            // Some stations run the length of the bunker (lighting, ventilation). Keep the box to
            // something a person can walk up to.
            var size = Vector3.Min(bounds.size + Vector3.one * 0.1f, new Vector3(3f, 2.2f, 3f));
            size = Vector3.Max(size, new Vector3(0.5f, 0.5f, 0.5f));

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "TradersInRaid station -- " + type;
            box.transform.position = bounds.center;
            box.transform.localScale = size;
            box.layer = LayerMask.NameToLayer("Interactive");
            var renderer = box.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
            var station = box.AddComponent<StationInteractable>();
            station.Area = (int)type;
            station.StationName = Name(type);
            Boxes[(int)type] = box;
        }

        public static bool LevelBounds(HideoutArea area, int level, out Bounds bounds)
        {
            bounds = new Bounds();
            if (level < 0 || level >= area.AreaLevels.Length || area.AreaLevels[level] == null)
            {
                return false;
            }
            bool any = false;
            // The part of a level the game itself outlines when the station is pointed at is the
            // station proper; the rest of the level can include walls and pipes across the bunker.
            var core = area.AreaLevels[level].HighlightTransform;
            var renderers = core != null ? core.GetComponentsInChildren<MeshRenderer>(true) : new MeshRenderer[0];
            if (renderers.Length == 0)
            {
                renderers = area.AreaLevels[level].GetComponentsInChildren<MeshRenderer>(true);
            }
            foreach (var renderer in renderers)
            {
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
            return any;
        }

        public static void Clear()
        {
            foreach (var box in Boxes.Values)
            {
                if (box != null)
                {
                    UnityEngine.Object.Destroy(box);
                }
            }
            Boxes.Clear();
            Levels.Clear();
            NextCosts.Clear();
        }

        // ---------------------------------------------------------------- building

        /// <summary>Items that may be spent: whatever sits in a carried pack, rig, pockets or case (not what is worn or held), and the hideout's stash.</summary>
        private static List<Item> Spendable(Player player)
        {
            var equipment = player.Profile.Inventory.Equipment;
            var worn = new HashSet<Item>(equipment.GetAllSlots().Select(slot => slot.ContainedItem).Where(item => item != null));
            var spendable = LocalTrade.CarriedItems(player).Where(item => !worn.Contains(item) && item != equipment).ToList();
            // Carried first, then whatever is in this hideout's stash.
            spendable.AddRange(HideoutStash.Items());
            return spendable;
        }

        public static string ItemName(string tpl)
        {
            string name = (tpl + " ShortName").Localized();
            if (string.IsNullOrEmpty(name) || name.EndsWith(" ShortName", StringComparison.Ordinal))
            {
                return tpl;
            }
            // Other mods decorate item names with colour tags and stars; an action label wants plain text.
            name = System.Text.RegularExpressions.Regex.Replace(name, "<[^>]+>", "");
            return name.Replace("★", "").Replace("☆", "").Trim();
        }

        /// <summary>For each material of the station's next level: how many are needed and how many are carried.</summary>
        public static List<KeyValuePair<Cost, int>> Tally(Player player, int area)
        {
            var result = new List<KeyValuePair<Cost, int>>();
            List<Cost> costs;
            if (!NextCosts.TryGetValue(area, out costs))
            {
                return result;
            }
            var carried = Spendable(player);
            foreach (var cost in costs)
            {
                int have = carried.Where(item => item.TemplateId == cost.Tpl).Sum(item => item.StackObjectsCount);
                result.Add(new KeyValuePair<Cost, int>(cost, have));
            }
            return result;
        }

        public static string CostText(List<KeyValuePair<Cost, int>> tally, bool onlyMissing)
        {
            var parts = new List<string>();
            foreach (var pair in tally)
            {
                if (onlyMissing && pair.Value >= pair.Key.Count)
                {
                    continue;
                }
                int count = onlyMissing ? pair.Key.Count - pair.Value : pair.Key.Count;
                parts.Add(count + " " + ItemName(pair.Key.Tpl));
            }
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// The actions for whatever the player is looking at, if it is a station of the hideout they
        /// are in: one of our own boxes, or one of the colliders the game's hideout already has on
        /// its stations. Null if it is neither.
        /// </summary>
        public static AvailableInteractionState ActionsForObject(object interactive)
        {
            if (HideoutDoors.Inside == null)
            {
                return null;
            }
            var ours = interactive as StationInteractable;
            if (ours != null)
            {
                return ActionsFor(ours.Area, ours.StationName);
            }
            var component = interactive as Component;
            if (component == null || !HideoutBunker.Owns(component.gameObject))
            {
                return null;
            }
            var area = component.GetComponentInParent<HideoutArea>();
            if (area == null || area.AreaTemplate == null)
            {
                return null;
            }
            return ActionsFor((int)area.AreaTemplate.Type, Name(area.AreaTemplate.Type));
        }

        public static AvailableInteractionState ActionsFor(StationInteractable station)
        {
            return ActionsFor(station.Area, station.StationName);
        }

        public static AvailableInteractionState ActionsFor(int stationArea, string stationName)
        {
            var station = new { Area = stationArea, StationName = stationName };
            var state = new AvailableInteractionState();
            var player = GamePlayerOwner.MyPlayer;
            int level;
            Levels.TryGetValue(station.Area, out level);
            if (player == null || !NextCosts.ContainsKey(station.Area))
            {
                if (station.Area == (int)EAreaType.Stash && HideoutStash.Current != null)
                {
                    state.Actions.Add(new InteractionAction { Name = "Open stash", Action = () => HideoutStash.Open(), Disabled = false });
                }
                state.Actions.Add(new InteractionAction
                {
                    Name = station.StationName + " (level " + level + ", fully built)",
                    Action = () => { },
                    Disabled = true,
                });
                return state;
            }
            if (station.Area == (int)EAreaType.Stash && HideoutStash.Current != null)
            {
                state.Actions.Add(new InteractionAction
                {
                    Name = "Open stash",
                    Action = () => HideoutStash.Open(),
                    Disabled = false,
                });
            }
            var tally = Tally(player, station.Area);
            bool affordable = tally.All(pair => pair.Value >= pair.Key.Count);
            string verb = level == 0 ? "Build " + station.StationName : "Upgrade " + station.StationName + " to level " + (level + 1);
            int area = station.Area;
            string name = station.StationName;
            state.Actions.Add(new InteractionAction
            {
                Name = affordable ? verb : verb + " (missing " + CostText(tally, true) + ")",
                Action = () => Build(area, name),
                Disabled = !affordable,
            });
            state.Actions.Add(new InteractionAction
            {
                Name = "What it needs",
                Action = () => NotificationManager.DisplayMessageNotification(
                    verb + " needs: " + CostText(tally, false) + ". You are missing: " + CostText(tally, true) + ".", ENotificationDurationType.Long),
                Disabled = false,
            });
            return state;
        }

        /// <summary>Pays for and builds the next level of a station. Returns a line saying what happened.</summary>
        public static async Task<string> Build(int area, string name)
        {
            if (_building || HideoutDoors.Inside == null)
            {
                return "not possible right now";
            }
            _building = true;
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                var tally = Tally(player, area);
                if (!NextCosts.ContainsKey(area))
                {
                    return name + " is fully built";
                }
                if (tally.Any(pair => pair.Value < pair.Key.Count))
                {
                    string missing = CostText(tally, true);
                    NotificationManager.DisplayWarningNotification("Missing: " + missing);
                    return "missing " + missing;
                }

                // Materials first, out of what is carried.
                foreach (var pair in tally)
                {
                    int left = pair.Key.Count;
                    foreach (var item in Spendable(player).Where(item => item.TemplateId == pair.Key.Tpl).ToList())
                    {
                        if (left <= 0)
                        {
                            break;
                        }
                        int take = Mathf.Min(left, item.StackObjectsCount);
                        if (!await LocalTrade.Take(player, item, take))
                        {
                            NotificationManager.DisplayWarningNotification("Could not hand over " + ItemName(pair.Key.Tpl) + ".");
                            return "could not take " + ItemName(pair.Key.Tpl);
                        }
                        left -= take;
                    }
                }

                string body = JsonConvert.SerializeObject(new { Id = HideoutDoors.Inside, Area = area });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/tradersinraid/hideout/build", body));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Building " + name + " failed on the server: " + reply["Error"]);
                    NotificationManager.DisplayWarningNotification("Could not build " + name + ": " + reply["Error"]);
                    return "server refused: " + reply["Error"];
                }
                Read(reply);
                HideoutStash.Send("after building");
                int level;
                Levels.TryGetValue(area, out level);
                var controller = Controller();
                HideoutArea visual;
                if (controller != null && controller.Areas.TryGetValue((EAreaType)area, out visual))
                {
                    Show((EAreaType)area, visual, level, false);
                }
                Plugin.Log.LogInfo(name + " built to level " + level + " in hideout '" + HideoutDoors.Inside + "'");
                NotificationManager.DisplayMessageNotification(name + " is now level " + level + ".", ENotificationDurationType.Long);
                return name + " is now level " + level;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Building " + name + " failed: " + e);
                return "failed: " + e.Message;
            }
            finally
            {
                _building = false;
            }
        }
    }
}
