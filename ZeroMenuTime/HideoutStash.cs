using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using JsonType;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// The stash of the hideout the player is standing in. Its contents come from the server when
    /// the hideout is entered; it is opened beside the player's gear like a container being looted,
    /// and whenever it is closed its whole contents are sent back, so the server always has what
    /// the player last saw. Each hideout has its own.
    /// </summary>
    public static class HideoutStash
    {
        private static CompoundItem _stash;
        private static string _hideout;
        private static bool _sending;

        public static CompoundItem Current => _stash;

        /// <summary>Builds the stash from the server's answer on entering a hideout.</summary>
        public static void Read(string hideout, JObject reply)
        {
            if (Net.Coop)
            {
                // In co-op there is one stash, the raid owner's, and it is the same item tree on every
                // machine. It is not built from this machine's own answer: the host says what is in it.
                _hideout = hideout;
                if (_stash == null)
                {
                    Net.Current.Send("zmt.stash?", new byte[0], false);
                }
                return;
            }
            _stash = null;
            _hideout = hideout;
            Build(hideout, reply);
        }

        // ---- Co-op: one stash for everyone ----

        private static object _registered;

        /// <summary>On the host: someone is entering the hideout and needs the stash. Made here first if need be.</summary>
        public static async void OnAsked(byte[] body, string from)
        {
            if (!Net.IsHost)
            {
                return;
            }
            try
            {
                if (_stash == null)
                {
                    // The host asks as itself; the server answers with the raid owner's hideout.
                    var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/hideout/enter",
                        "{\"Id\":\"" + (_hideout ?? (HideoutDoors.Fronts.Count > 0 ? HideoutDoors.Fronts.Keys.First() : "customs")) + "\"}"));
                    if (!((bool?)reply["Ok"] ?? false))
                    {
                        Plugin.Log.LogWarning("Shared stash: the server gave none: " + reply["Error"]);
                        return;
                    }
                    if (_stash == null)
                    {
                        _hideout = _hideout ?? (string)reply["Active"] ?? "customs";
                        Build(_hideout, reply);
                    }
                }
                Tell();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Shared stash: " + e);
            }
        }

        /// <summary>On the host: a machine that has just come into the raid asks whether a stash exists yet.</summary>
        public static void OnAskedIfAny(byte[] body, string from)
        {
            if (Net.IsHost && _stash != null)
            {
                Tell();
            }
        }

        /// <summary>On the host: says to every machine what the stash holds now.</summary>
        private static void Tell()
        {
            if (_stash == null)
            {
                return;
            }
            var items = JArray.Parse(Singleton<ItemFactory>.Instance.TreeToFlatItems(_stash).ToJson());
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if ((string)items[i]["_id"] == _stash.Id.ToString())
                {
                    items.RemoveAt(i);
                }
            }
            var told = new JObject { ["StashId"] = _stash.Id.ToString(), ["StashTemplate"] = _stash.TemplateId.ToString(), ["Stash"] = items, ["Hideout"] = _hideout };
            Net.Current.Send("zmt.stash", System.Text.Encoding.UTF8.GetBytes(told.ToString(Newtonsoft.Json.Formatting.None)), true);
            Plugin.Log.LogInfo("Shared stash: told everyone (" + items.Count + " item(s))");
        }

        /// <summary>On a client: the host says what the stash holds. Built once; after that the game's own moves keep it true.</summary>
        public static void OnTold(byte[] body, string from)
        {
            if (Net.IsHost || _stash != null)
            {
                return;
            }
            try
            {
                var reply = JObject.Parse(System.Text.Encoding.UTF8.GetString(body));
                _hideout = _hideout ?? (string)reply["Hideout"];
                Build(_hideout, reply);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Shared stash: the host's answer could not be used: " + e);
            }
        }

        /// <summary>
        /// Makes the stash known to the raid the way a crate on the map is, under its own id, so that
        /// moves in and out of it are ordinary moves every machine can follow.
        /// </summary>
        private static void Register()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == null || _stash == null)
            {
                return;
            }
            var owner = new ItemController(_stash, _stash.Id, "Stash");
            var holder = new GameObject("ZeroMenuTime shared stash");
            holder.transform.position = HideoutBunker.Moved;
            world.ItemOwners[owner] = new GameWorld.ItemOwnerWorldData
            {
                Transform = holder.transform,
                DoNotPerformPickUpValidation = true,
                ItemOwner = owner,
            };
            _registered = owner;
            Plugin.Log.LogInfo("Shared stash " + _stash.Id + " registered with the raid: " + Count() + " item(s)");
        }

        private static void Build(string hideout, JObject reply)
        {
            try
            {
                string id = (string)reply["StashId"];
                string template = (string)reply["StashTemplate"];
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(template))
                {
                    Plugin.Log.LogWarning("The server sent no stash for hideout '" + hideout + "'");
                    return;
                }
                var all = new JArray { new JObject { ["_id"] = id, ["_tpl"] = template } };
                foreach (var item in reply["Stash"] as JArray ?? new JArray())
                {
                    all.Add(item);
                }
                var flat = all.ToString().ParseJsonTo<FlatItem[]>();
                // Built as an ordinary item tree with the stash at its root. (The factory's own
                // "flat items to stash" wraps everything in a new, different stash.)
                var tree = Singleton<ItemFactory>.Instance.FlatItemsToTree(flat, true);
                Item root;
                if (tree.Items == null || !tree.Items.TryGetValue(id, out root) || !(root is CompoundItem))
                {
                    Plugin.Log.LogWarning("The stash of hideout '" + hideout + "' could not be built");
                    return;
                }
                _stash = (CompoundItem)root;
                var player = GamePlayerOwner.MyPlayer;
                if (Net.Coop)
                {
                    Register();
                }
                else
                {
                    new ItemController(_stash, player.ProfileId, player.Profile.Nickname);
                }
                if (player != null)
                {
                    MarkSearched(player);
                }
                Plugin.Log.LogInfo("Stash of hideout '" + hideout + "' holds " + Count() + " item(s)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Building the stash of hideout '" + hideout + "' failed: " + e);
                _stash = null;
            }
        }

        /// <summary>
        /// The stash is built anew on every visit, and new containers count as "not searched yet".
        /// What lies in your own stash is known to you, so nothing in it needs searching again.
        /// </summary>
        private static void MarkSearched(Player player)
        {
            int containers = 0, stillShut = 0;
            try
            {
                var search = player.InventoryController.SearchController as IPlayerSearchController;
                if (search == null || _stash == null)
                {
                    return;
                }
                foreach (var part in _stash.GetAllItems().ToArray())
                {
                    if (part is SearchableItem searchable)
                    {
                        search.SetItemAsSearched(searchable);
                        containers++;
                    }
                    search.SetItemAsKnown(part, false);
                }
                var asked = search as ISearchController;
                foreach (var part in _stash.GetAllItems().OfType<SearchableItem>())
                {
                    if (asked != null && (!asked.IsSearched(part) || asked.ContainsUnknownItems(part)))
                    {
                        stillShut++;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Marking the stash as searched failed: " + e.Message);
            }
            Plugin.Log.LogInfo("Stash containers marked as searched: " + containers + (stillShut > 0 ? ", still to be searched: " + stillShut : ""));
        }

        /// <summary>Everything in the stash at any depth, not counting the stash itself.</summary>
        public static List<Item> Items()
        {
            return _stash == null ? new List<Item>() : _stash.GetAllItems().Where(item => item.Id != _stash.Id).ToList();
        }

        public static int Count()
        {
            return Items().Count;
        }

        /// <summary>Opens the stash beside the player's gear.</summary>
        public static bool Open()
        {
            var player = GamePlayerOwner.MyPlayer;
            if (_stash == null && Net.Coop)
            {
                NotificationManager.DisplayWarningNotification("The stash is not here yet; try again in a moment.");
                Net.Current.Send("zmt.stash?", new byte[0], false);
                return false;
            }
            if (_stash == null || player == null)
            {
                return false;
            }
            var owner = player.GetComponent<GamePlayerOwner>();
            if (owner == null)
            {
                Plugin.Log.LogWarning("No player owner to open the stash with");
                return false;
            }
            // Shown as a container being looted: its own grid, beside the player's gear.
            owner.ShowInventoryScreenLoot(_stash, () => Send("closed"), false);
            Plugin.Log.LogInfo("Stash of hideout '" + _hideout + "' opened");
            return true;
        }

        private static readonly HashSet<System.Reflection.MethodBase> SortAllowed = new HashSet<System.Reflection.MethodBase>();

        /// <summary>
        /// In a raid the game sorts nothing. Its check is given one exception, this hideout's stash,
        /// and is otherwise left to answer as it always has.
        /// </summary>
        private static void AllowSorting(InventoryController controller)
        {
            var check = controller.GetType().GetMethod("IsAllowedToSort", new[] { typeof(CompoundItem) });
            if (check == null || !SortAllowed.Add(check))
            {
                return;
            }
            new HarmonyLib.Harmony("com.r2rka1.zeromenutime.stashsort").Patch(check,
                postfix: new HarmonyLib.HarmonyMethod(typeof(HideoutStash).GetMethod(nameof(SortCheck), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
        }

        private static void SortCheck(CompoundItem item, ref bool __result)
        {
            if (item != null && item == _stash)
            {
                __result = true;
            }
        }

        /// <summary>Packs the stash tight, the way the Sort button of the old stash screen did.</summary>
        public static bool Sort()
        {
            if (Net.Coop)
            {
                NotificationManager.DisplayWarningNotification("The stash cannot be sorted in co-op yet.");
                return false;
            }
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                if (_stash == null || player == null)
                {
                    return false;
                }
                AllowSorting(player.InventoryController);
                var sorted = ItemManipulator.Sort(_stash, player.InventoryController, false);
                if (sorted.Failed)
                {
                    Plugin.Log.LogWarning("Stash not sorted: " + sorted.Error);
                    NotificationManager.DisplayWarningNotification("The stash could not be sorted.");
                    return false;
                }
                sorted.Value.RaiseEvents(player.InventoryController, CommandStatus.Begin);
                sorted.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
                NotificationManager.DisplayMessageNotification("Stash sorted.");
                Send("sorted");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Sorting the stash failed: " + e);
                return false;
            }
        }

        /// <summary>Tells the server what the stash holds now.</summary>
        public static async void Send(string reason)
        {
            await SendAsync(reason);
        }

        public static async Task SendAsync(string reason)
        {
            if (_stash == null || _hideout == null || _sending)
            {
                return;
            }
            _sending = true;
            try
            {
                var flat = Singleton<ItemFactory>.Instance.TreeToFlatItems(_stash);
                var items = JArray.Parse(flat.ToJson());
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if ((string)items[i]["_id"] == _stash.Id.ToString())
                    {
                        items.RemoveAt(i);
                    }
                }
                var body = new JObject { ["Id"] = _hideout, ["Items"] = items };
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/hideout/stash", body.ToString(Newtonsoft.Json.Formatting.None)));
                bool ok = (bool?)reply["Ok"] ?? false;
                Plugin.Log.LogInfo("Stash of hideout '" + _hideout + "' saved (" + reason + "): " + items.Count + " item(s), server said " + (ok ? "ok" : (string)reply["Error"]));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Saving the stash failed: " + e);
            }
            finally
            {
                _sending = false;
            }
        }

        public static void Clear()
        {
            if (Net.Coop && _stash != null && _hideout != null)
            {
                // Leaving the hideout in co-op: the stash stays, it is everyone's for the whole raid.
                return;
            }
            Forget();
        }

        /// <summary>The raid is over: the stash goes with it.</summary>
        public static void Forget()
        {
            try
            {
                var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
                if (world != null && _registered is IItemOwner known)
                {
                    world.ItemOwners.Remove(known);
                }
            }
            catch (Exception)
            {
            }
            _registered = null;
            _stash = null;
            _hideout = null;
        }
    }
}
