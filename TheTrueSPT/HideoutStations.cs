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

namespace TheTrueSPT
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
        /// <summary>Testing aid: everything in the hideout is had for nothing.</summary>
        public static BepInEx.Configuration.ConfigEntry<bool> Free;
        public static bool IsFree => Free != null && Free.Value;

        public static readonly Dictionary<int, int> Levels = new Dictionary<int, int>();
        public static readonly Dictionary<int, List<Cost>> NextCosts = new Dictionary<int, List<Cost>>();

        private static readonly Dictionary<int, GameObject> Boxes = new Dictionary<int, GameObject>();
        private static bool _building;

        /// <summary>Takes levels and costs from any of the server's hideout answers.</summary>
        public static void Read(JObject reply)
        {
            HideoutPower.Read(reply);
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
            HideoutPower.ApplyLook();
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
            box.name = "TheTrueSPT station -- " + type;
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
        public static List<Item> Spendable(Player player)
        {
            var equipment = player.Profile.Inventory.Equipment;
            var worn = new HashSet<Item>(equipment.GetAllSlots().Select(slot => slot.ContainedItem).Where(item => item != null));
            var spendable = LocalTrade.CarriedItems(player).Where(item => !worn.Contains(item) && item != equipment).ToList();
            // Carried first, then whatever is in this hideout's stash.
            spendable.AddRange(HideoutStash.Items());
            return spendable;
        }

        /// <summary>An item's name in full, for where there is room for it.</summary>
        public static string FullName(string tpl)
        {
            string name = (tpl + " Name").Localized();
            if (string.IsNullOrEmpty(name) || name.EndsWith(" Name", StringComparison.Ordinal))
            {
                return ItemName(tpl);
            }
            name = System.Text.RegularExpressions.Regex.Replace(name, "<[^>]+>", "");
            return name.Replace("★", "").Replace("☆", "").Trim();
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
                result.Add(new KeyValuePair<Cost, int>(cost, IsFree ? Mathf.Max(have, cost.Count) : have));
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
            // One line opens the station's window, where building, crafting and resting are laid
            // out in full. The two things done most often keep a line of their own.
            var state = new AvailableInteractionState();
            int level;
            Levels.TryGetValue(stationArea, out level);
            var craft = HideoutCrafting.Here.FirstOrDefault(c => c.Area == stationArea);
            if (craft != null && craft.Due <= WorldClock.Now)
            {
                state.Actions.Add(new InteractionAction
                {
                    Name = "Collect " + (craft.Count > 1 ? craft.Count + " x " : "") + ItemName(craft.Product),
                    Action = () => HideoutCrafting.Collect(stationArea),
                    Disabled = false,
                });
            }
            if (stationArea == (int)EAreaType.Stash && HideoutStash.Current != null)
            {
                state.Actions.Add(new InteractionAction { Name = "Open stash", Action = () => HideoutStash.Open(), Disabled = false });
            }
            string what = level == 0 ? stationName + " (not built)" : stationName;
            if (craft != null && craft.Due > WorldClock.Now)
            {
                what += ", making " + ItemName(craft.Product) + ", " + WorldClock.Span(craft.Due - WorldClock.Now) + " left";
            }
            state.Actions.Add(new InteractionAction
            {
                Name = what,
                Action = () => HideoutPanel.Open(stationArea, stationName),
                Disabled = false,
            });
            return state;
        }

        /// <summary>
        /// Takes a list of materials out of carried gear, then the stash. Returns null when all of
        /// it was handed over, otherwise a line saying what could not be taken.
        /// </summary>
        public static async Task<string> Pay(Player player, List<Cost> costs)
        {
            if (IsFree)
            {
                return null;
            }
            foreach (var cost in costs)
            {
                int left = cost.Count;
                foreach (var item in Spendable(player).Where(item => item.TemplateId == cost.Tpl).ToList())
                {
                    if (left <= 0)
                    {
                        break;
                    }
                    int take = Mathf.Min(left, item.StackObjectsCount);
                    if (!await LocalTrade.Take(player, item, take))
                    {
                        NotificationManager.DisplayWarningNotification("Could not hand over " + ItemName(cost.Tpl) + ".");
                        return "could not take " + ItemName(cost.Tpl);
                    }
                    left -= take;
                }
            }
            return null;
        }

        /// <summary>A built Rest Space is somewhere to sleep.</summary>
        private static void AddSleep(AvailableInteractionState state, int area, int level)
        {
            if (area != HideoutSleep.RestSpace || level < 1)
            {
                return;
            }
            foreach (float hours in new[] { 2f, 4f, 8f })
            {
                float slept = hours;
                state.Actions.Add(new InteractionAction { Name = "Sleep " + hours.ToString("0") + " hours", Action = () => HideoutSleep.Sleep(slept), Disabled = false });
            }
            float untilMorning = HideoutSleep.HoursUntilMorning();
            state.Actions.Add(new InteractionAction
            {
                Name = "Sleep until morning (" + untilMorning.ToString("0.#") + " hours)",
                Action = () => HideoutSleep.Sleep(untilMorning),
                Disabled = false,
            });
            state.Actions.Add(new InteractionAction { Name = "Save", Action = () => SaveGame.Save("at the bed"), Disabled = false });
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
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/thetruespt/hideout/build", body));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Building " + name + " failed on the server: " + reply["Error"]);
                    NotificationManager.DisplayWarningNotification("Could not build " + name + ": " + reply["Error"]);
                    return "server refused: " + reply["Error"];
                }
                Read(reply);
                HideoutBoosts.Read(reply);
                HideoutStash.Send("after building");
                await HideoutCrafting.Load();
                int level;
                Levels.TryGetValue(area, out level);
                var controller = Controller();
                HideoutArea visual;
                if (controller != null && controller.Areas.TryGetValue((EAreaType)area, out visual))
                {
                    Show((EAreaType)area, visual, level, false);
                }
                Plugin.Log.LogInfo(name + " built to level " + level + " in hideout '" + HideoutDoors.Inside + "'");
                HideoutDoors.RefreshExits();
                HideoutPower.ApplyLook();
                // The wall comes down in several stages in the game's data; here it is one job.
                if (area == (int)EAreaType.EmergencyWall && NextCosts.ContainsKey(area))
                {
                    _building = false;
                    return await Build(area, name);
                }
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
