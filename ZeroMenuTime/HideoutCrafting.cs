using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// Making things at the stations of the hideout the player is in. Recipes and their durations are
    /// the game's own; the time is counted on the world clock, so a craft keeps going while the
    /// player is elsewhere, in a raid or asleep, and each hideout has its own crafts under way.
    /// Everything is chosen in first person: the station's action list becomes the recipe list.
    /// </summary>
    public static class HideoutCrafting
    {
        public class Recipe
        {
            public string Id, Product;
            public int Area, Count;
            public double Seconds;
            public List<HideoutStations.Cost> Items = new List<HideoutStations.Cost>();
            public List<string> Tools = new List<string>();
        }

        public class Craft
        {
            public string Recipe, Product;
            public int Area, Count;
            public double Started, Due;
            /// <summary>Waiting for power; its time is not running.</summary>
            public bool Paused;
        }

        private const int PerPage = 6;

        public static readonly List<Recipe> Recipes = new List<Recipe>();
        /// <summary>What is being made, by hideout.</summary>
        public static readonly Dictionary<string, List<Craft>> Crafts = new Dictionary<string, List<Craft>>();

        /// <summary>The station whose recipe list is showing, or -1.</summary>
        public static int MenuArea = -1;
        private static int _page;
        private static bool _busy;

        public static List<Craft> Here
        {
            get
            {
                // One hideout behind all three doors: whatever is being made is being made here.
                return HideoutDoors.Inside != null ? Crafts.Values.SelectMany(list => list).ToList() : new List<Craft>();
            }
        }

        /// <summary>Takes the crafts under way (and the recipes, if sent) from any of the server's hideout answers.</summary>
        public static void Read(JObject reply)
        {
            var crafts = reply["Crafts"] as JObject;
            if (crafts != null)
            {
                Crafts.Clear();
                foreach (var pair in crafts)
                {
                    var list = new List<Craft>();
                    foreach (var entry in pair.Value as JArray ?? new JArray())
                    {
                        list.Add(new Craft
                        {
                            Recipe = (string)entry["Recipe"], Product = (string)entry["Product"], Area = (int)entry["Area"],
                            Count = (int)entry["Count"], Started = (double)entry["Started"], Due = (double)entry["Due"],
                            Paused = (bool?)entry["Paused"] ?? false,
                        });
                    }
                    Crafts[pair.Key] = list;
                }
            }
            var recipes = reply["Recipes"] as JArray;
            if (recipes != null && recipes.Count > 0)
            {
                Recipes.Clear();
                foreach (var entry in recipes)
                {
                    var recipe = new Recipe
                    {
                        Id = (string)entry["Id"], Product = (string)entry["Product"], Area = (int)entry["Area"],
                        Count = (int)entry["Count"], Seconds = (double)entry["Seconds"],
                    };
                    foreach (var item in entry["Items"] as JArray ?? new JArray())
                    {
                        recipe.Items.Add(new HideoutStations.Cost { Tpl = (string)item["Tpl"], Count = (int)item["Count"] });
                    }
                    foreach (var tool in entry["Tools"] as JArray ?? new JArray())
                    {
                        recipe.Tools.Add((string)tool);
                    }
                    Recipes.Add(recipe);
                }
            }
        }

        /// <summary>Fetches what this hideout can make. Called on entering and after building.</summary>
        public static async Task Load()
        {
            if (HideoutDoors.Inside == null)
            {
                return;
            }
            try
            {
                Recipes.Clear();
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/hideout/recipes",
                    JsonConvert.SerializeObject(new { Id = HideoutDoors.Inside })));
                if ((bool?)reply["Ok"] ?? false)
                {
                    Read(reply);
                    Plugin.Log.LogInfo("Hideout '" + HideoutDoors.Inside + "' can make " + Recipes.Count + " recipe(s); " + Here.Count + " craft(s) under way here");
                }
                else
                {
                    Plugin.Log.LogWarning("Recipes not loaded: " + reply["Error"]);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Loading recipes failed: " + e);
            }
        }

        /// <summary>Says which hideouts have something finished and waiting.</summary>
        public static void AnnounceDue()
        {
            double now = WorldClock.Now;
            var ready = new List<string>();
            foreach (var pair in Crafts)
            {
                int due = pair.Value.Count(c => c.Due <= now);
                if (due > 0)
                {
                    ready.Add(due + " in the " + char.ToUpper(pair.Key[0]) + pair.Key.Substring(1) + " hideout");
                }
            }
            if (ready.Count > 0)
            {
                NotificationManager.DisplayMessageNotification("Finished and waiting to be collected: " + string.Join(", ", ready.ToArray()) + ".", ENotificationDurationType.Long);
            }
        }

        public static void Clear()
        {
            Recipes.Clear();
            MenuArea = -1;
            _page = 0;
        }

        private static void Refresh()
        {
            var player = GamePlayerOwner.MyPlayer;
            var owner = player != null ? player.GetComponent<GamePlayerOwner>() : null;
            if (owner != null)
            {
                owner.InteractionsChangedHandler();
            }
        }

        private static string Label(string product, int count)
        {
            return (count > 1 ? count + " x " : "") + HideoutStations.ItemName(product);
        }

        /// <summary>What a recipe still lacks, in words, or null if everything is at hand.</summary>
        public static string Missing(Player player, Recipe recipe)
        {
            if (HideoutStations.IsFree)
            {
                return null;
            }
            var spendable = HideoutStations.Spendable(player);
            var parts = new List<string>();
            foreach (var cost in recipe.Items)
            {
                int have = spendable.Where(item => item.TemplateId == cost.Tpl).Sum(item => item.StackObjectsCount);
                if (have < cost.Count)
                {
                    parts.Add((cost.Count - have) + " " + HideoutStations.ItemName(cost.Tpl));
                }
            }
            foreach (string tool in recipe.Tools)
            {
                if (!spendable.Any(item => item.TemplateId == tool))
                {
                    parts.Add(HideoutStations.ItemName(tool) + " (tool)");
                }
            }
            return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
        }

        public static string Needs(Recipe recipe)
        {
            var parts = recipe.Items.Select(cost => cost.Count + " " + HideoutStations.ItemName(cost.Tpl)).ToList();
            parts.AddRange(recipe.Tools.Select(tool => HideoutStations.ItemName(tool) + " (tool)"));
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts.ToArray());
        }

        /// <summary>The crafting lines for a station's ordinary action list.</summary>
        public static void AddActions(AvailableInteractionState state, int area, int level)
        {
            if (level < 1)
            {
                return;
            }
            var craft = Here.FirstOrDefault(c => c.Area == area);
            if (craft != null)
            {
                double left = craft.Due - WorldClock.Now;
                if (left <= 0)
                {
                    state.Actions.Add(new InteractionAction { Name = "Collect " + Label(craft.Product, craft.Count), Action = () => Collect(area), Disabled = false });
                }
                else
                {
                    state.Actions.Add(new InteractionAction
                    {
                        Name = "Making " + Label(craft.Product, craft.Count) + ", ready in " + WorldClock.Span(left),
                        Action = () => { },
                        Disabled = true,
                    });
                }
                return;
            }
            if (Recipes.Any(r => r.Area == area))
            {
                state.Actions.Add(new InteractionAction
                {
                    Name = "Craft...",
                    Action = () =>
                    {
                        MenuArea = area;
                        _page = 0;
                        Refresh();
                    },
                    Disabled = false,
                });
            }
        }

        /// <summary>The recipe list of the station whose menu is open, a page at a time.</summary>
        public static AvailableInteractionState Menu(int area)
        {
            var state = new AvailableInteractionState();
            var player = GamePlayerOwner.MyPlayer;
            var all = Recipes.Where(r => r.Area == area).ToList();
            // What can be made right now comes first.
            all = all.OrderBy(r => Missing(player, r) == null ? 0 : 1).ThenBy(r => r.Seconds).ToList();
            int pages = Mathf.Max(1, Mathf.CeilToInt(all.Count / (float)PerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            foreach (var recipe in all.Skip(_page * PerPage).Take(PerPage))
            {
                string missing = Missing(player, recipe);
                var chosen = recipe;
                state.Actions.Add(new InteractionAction
                {
                    Name = Label(recipe.Product, recipe.Count) + " (" + WorldClock.Span(recipe.Seconds) + ")"
                           + (missing == null ? " from " + Needs(recipe) : " - missing " + missing),
                    Action = () => Start(chosen),
                    Disabled = missing != null,
                });
            }
            if (pages > 1)
            {
                state.Actions.Add(new InteractionAction
                {
                    Name = "More... (page " + (_page + 1) + " of " + pages + ")",
                    Action = () =>
                    {
                        _page = (_page + 1) % pages;
                        Refresh();
                    },
                    Disabled = false,
                });
            }
            state.Actions.Add(new InteractionAction
            {
                Name = "Back",
                Action = () =>
                {
                    MenuArea = -1;
                    Refresh();
                },
                Disabled = false,
            });
            return state;
        }

        /// <summary>Pays for a recipe and sets it going. Returns a line saying what happened.</summary>
        public static async Task<string> Start(Recipe recipe)
        {
            if (_busy || HideoutDoors.Inside == null)
            {
                return "not possible right now";
            }
            _busy = true;
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                string missing = Missing(player, recipe);
                if (missing != null)
                {
                    NotificationManager.DisplayWarningNotification("Missing: " + missing);
                    return "missing " + missing;
                }
                string failure = await HideoutStations.Pay(player, recipe.Items);
                if (failure != null)
                {
                    return failure;
                }
                string body = JsonConvert.SerializeObject(new { Id = HideoutDoors.Inside, Recipe = recipe.Id, Seconds = WorldClock.Now });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/hideout/craft", body));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    NotificationManager.DisplayWarningNotification("Could not start: " + reply["Error"]);
                    return "server refused: " + reply["Error"];
                }
                Read(reply);
                Coop.HideoutChanged();
                HideoutStash.Send("after starting a craft");
                MenuArea = -1;
                Refresh();
                string text = "Making " + Label(recipe.Product, recipe.Count) + ", ready in " + WorldClock.Span(recipe.Seconds) + " of game time.";
                Plugin.Log.LogInfo(text + " (hideout '" + HideoutDoors.Inside + "', station " + recipe.Area + ")");
                NotificationManager.DisplayMessageNotification(text, ENotificationDurationType.Long);
                return text;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Starting a craft failed: " + e);
                return "failed: " + e.Message;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Takes what a station has finished into this hideout's stash.</summary>
        public static async Task<string> Collect(int area)
        {
            if (_busy || HideoutDoors.Inside == null)
            {
                return "not possible right now";
            }
            _busy = true;
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                string body = JsonConvert.SerializeObject(new { Id = HideoutDoors.Inside, Area = area, Seconds = WorldClock.Now });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/hideout/collect", body));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    return "server refused: " + reply["Error"];
                }
                Read(reply);
                var names = new List<string>();
                foreach (var done in reply["Collected"] as JArray ?? new JArray())
                {
                    string product = (string)done["Product"];
                    int count = (int)done["Count"];
                    int placed = await PutInStash(player, product, count);
                    names.Add(Label(product, placed) + (placed < count ? " (" + (count - placed) + " did not fit and went to your gear or were lost)" : ""));
                }
                if (names.Count == 0)
                {
                    return "nothing is ready at that station";
                }
                HideoutStash.Send("after collecting a craft");
                Refresh();
                string text = (Net.Coop ? "Collected, laid in front of you: " : "Collected into the stash: ") + string.Join(", ", names.ToArray()) + ".";
                Plugin.Log.LogInfo(text);
                NotificationManager.DisplayMessageNotification(text, ENotificationDurationType.Long);
                return text;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Collecting a craft failed: " + e);
                return "failed: " + e.Message;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Makes the finished items and puts them in the stash, or failing that in carried gear. Returns how many were placed.</summary>
        /// <summary>Puts one made-up item into the hideout's stash, or into the player's gear if the stash is full.</summary>
        public static async Task<bool> PutItem(Player player, Item item)
        {
            // A raid has only the models of what was in it when it began; something made here
            // since would otherwise be invisible in the hand or on the ground.
            try
            {
                var models = item.GetAllItems().SelectMany(part => part.Template.AllResources).ToArray();
                await Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                    ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Local, models, JobYieldPriority.Immediate);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not load the model of " + item.TemplateId + ": " + e.Message);
            }
            if (Net.Coop)
            {
                // The shared stash is the same on every machine only through moves everyone is told
                // of. A thing made here is laid in front of the player for all to see; they stow it.
                return await LocalTrade.Give(player, item);
            }
            var stash = HideoutStash.Current;
            var address = stash != null ? stash.Grids[0].FindLocationForItem(item) : null;
            if (address != null)
            {
                var add = ItemManipulator.Add(item, address, player.InventoryController, false);
                if (!add.Failed)
                {
                    add.Value.RaiseEvents(player.InventoryController, CommandStatus.Begin);
                    add.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
                    return true;
                }
            }
            return await LocalTrade.Give(player, item);
        }

        public static async Task<int> PutInStash(Player player, string template, int count)
        {
            var factory = Singleton<ItemFactory>.Instance;
            var stash = HideoutStash.Current;
            int placed = 0, left = count;
            while (left > 0)
            {
                var item = factory.CreateItem(MongoID.Generate(), template, null);
                int size = Mathf.Clamp(left, 1, Mathf.Max(1, item.StackMaxSize));
                if (item.StackMaxSize > 1)
                {
                    item.StackObjectsCount = size;
                }
                else
                {
                    size = 1;
                }
                bool done = await PutItem(player, item);
                if (done)
                {
                    placed += size;
                }
                left -= size;
            }
            return placed;
        }
    }
}
