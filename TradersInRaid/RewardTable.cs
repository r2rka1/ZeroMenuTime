using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diz.Jobs;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using JsonType;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// Lays what a trader is holding for the player out on the table in the trader's room, as
    /// ordinary loose loot. An item stays in the trader's mail until the player actually picks it
    /// up, so whatever is left on the table is laid out again on the next visit.
    /// </summary>
    public static class RewardTable
    {
        private class Laid
        {
            public string RootId;
            public Item Item;
            public LootItem Loot;
            public TraderRoom Room;
        }

        private static readonly List<Laid> OnTables = new List<Laid>();
        private static readonly Dictionary<TraderRoom, int> NextSlot = new Dictionary<TraderRoom, int>();
        private const float DropHeight = 0.08f;
        private static float _nextCheck;
        private static bool _reporting;

        public static int Count => OnTables.Count;

        public static bool IsLaidOut(string rootId)
        {
            foreach (var laid in OnTables)
            {
                if (laid.RootId == rootId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Where the n-th item goes: across the table first, then on the floor in front of it.</summary>
        private static Vector3 Slot(TraderRoom room, int index)
        {
            const float step = 0.3f;
            // The table's long side runs along z; two items fit across its width.
            int rows = Mathf.Max(1, Mathf.FloorToInt(room.TableSize.y / step));
            int onTable = rows * 2;
            if (index < onTable)
            {
                int row = index / 2, column = index % 2;
                float x = (column - 0.5f) * step;
                float z = (row - (rows - 1) / 2f) * step;
                return room.TableTop + new Vector3(x, 0.03f, z);
            }
            int overflow = index - onTable;
            int floorRow = overflow % rows, floorColumn = overflow / rows;
            float floorY = room.Entry.y;
            return new Vector3(
                room.TableTop.x - room.TableSize.x / 2f - 0.35f - floorColumn * step,
                floorY,
                room.TableTop.z + (floorRow - (rows - 1) / 2f) * step);
        }

        /// <summary>Puts one item on the room's table. The root id ties it to the trader's mail; null for a test item.</summary>
        public static async Task<LootItem> Place(TraderRoom room, Item item, string rootId)
        {
            var player = GamePlayerOwner.MyPlayer;
            var world = Singleton<GameWorld>.Instance;
            if (player == null || world == null || room == null || item == null)
            {
                return null;
            }
            int index;
            NextSlot.TryGetValue(room, out index);
            NextSlot[room] = index + 1;
            var at = Slot(room, index);

            // An item's model is only in memory if something on this map already uses it. Load it
            // (and the models of everything attached to it) before asking the game to show it.
            var models = item.GetAllItems().SelectMany(part => part.Template.AllResources).ToArray();
            await Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Local, models, JobYieldPriority.Immediate);
            if (room.Root == null)
            {
                // The raid ended while the model was loading.
                return null;
            }
            // Let go a little above the slot and left to the game's physics, so things come to rest
            // the way they would if someone had put them down.
            var above = at + Vector3.up * DropHeight;
            var turned = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            var loot = world.ThrowItem(item, player, above, turned, Vector3.zero, Vector3.zero, false);
            // Models have their pivots in all sorts of places (a vest's is far off to one side), so
            // shift the object until what is actually visible hangs over the slot.
            Bounds bounds;
            if (VisibleBounds(loot, out bounds))
            {
                loot.transform.position += above - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                var body = loot.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = loot.transform.position;
                }
            }
            OnTables.Add(new Laid { RootId = rootId, Item = item, Loot = loot, Room = room });
            Plugin.Log.LogInfo("Laid " + item.ShortName.Localized() + " on the table at " + at.ToString("F2"));
            return loot;
        }

        private static bool VisibleBounds(LootItem loot, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (var renderer in loot.GetComponentsInChildren<Renderer>())
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

        /// <summary>Lays out every parcel that is not already lying there. Returns how many were added.</summary>
        public static async Task<int> LayOut(TraderRoom room, JArray parcels)
        {
            var factory = Singleton<ItemFactory>.Instance;
            int added = 0;
            foreach (var parcel in parcels)
            {
                string rootId = (string)parcel["RootId"];
                if (IsLaidOut(rootId))
                {
                    continue;
                }
                try
                {
                    var flat = parcel["Items"].ToString().ParseJsonTo<FlatItem[]>();
                    var tree = factory.FlatItemsToTree(flat, true);
                    Item root;
                    if (tree.Items == null || !tree.Items.TryGetValue(rootId, out root) || root == null)
                    {
                        Plugin.Log.LogWarning("Mail item " + rootId + " could not be built");
                        continue;
                    }
                    if (await Place(room, root, rootId) != null)
                    {
                        added++;
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Laying out mail item " + rootId + " failed: " + e);
                }
            }
            return added;
        }

        /// <summary>
        /// Called every frame. About once a second, looks for table items the player now carries and
        /// tells the server they were taken, so the trader stops holding them.
        /// </summary>
        public static void Tick()
        {
            if (OnTables.Count == 0 || _reporting || Time.unscaledTime < _nextCheck)
            {
                return;
            }
            _nextCheck = Time.unscaledTime + 1f;
            var player = GamePlayerOwner.MyPlayer;
            if (player == null)
            {
                return;
            }
            try
            {
                var carried = new HashSet<Item>(LocalTrade.CarriedItems(player));
                var taken = new List<string>();
                for (int i = OnTables.Count - 1; i >= 0; i--)
                {
                    var laid = OnTables[i];
                    if (carried.Contains(laid.Item))
                    {
                        if (laid.RootId != null)
                        {
                            taken.Add(laid.RootId);
                        }
                        OnTables.RemoveAt(i);
                    }
                    else if (laid.Loot == null)
                    {
                        // Gone without being picked up; the trader still holds it.
                        OnTables.RemoveAt(i);
                    }
                }
                // An emptied table starts filling from the first slot again.
                foreach (var room in new List<TraderRoom>(NextSlot.Keys))
                {
                    bool any = false;
                    foreach (var laid in OnTables)
                    {
                        any |= laid.Room == room;
                    }
                    if (!any)
                    {
                        NextSlot.Remove(room);
                    }
                }
                if (taken.Count > 0)
                {
                    Report(taken);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Checking the reward table failed: " + e.Message);
            }
        }

        private static async void Report(List<string> rootIds)
        {
            _reporting = true;
            try
            {
                string body = JsonConvert.SerializeObject(new { TraderId = "", RootIds = rootIds });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/tradersinraid/mail/taken", body));
                bool ok = (bool?)reply["Ok"] ?? false;
                Plugin.Log.LogInfo("Picked up " + rootIds.Count + " item(s) from a table; trader told: " + ok);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Reporting picked-up table items failed: " + e.Message);
            }
            finally
            {
                _reporting = false;
            }
        }

        /// <summary>What lies on the tables, for the test harness.</summary>
        public static List<string> Describe()
        {
            var lines = new List<string>();
            foreach (var laid in OnTables)
            {
                lines.Add(laid.Item.ShortName.Localized() + (laid.Loot != null
                    ? " at " + laid.Loot.transform.position.ToString("F2")
                    : " (object gone)") + (laid.RootId == null ? " [test item]" : ""));
            }
            return lines;
        }

        public static void Clear()
        {
            OnTables.Clear();
            NextSlot.Clear();
        }
    }
}
