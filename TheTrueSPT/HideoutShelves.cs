using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using JsonType;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace TheTrueSPT
{
    /// <summary>
    /// What stations keep on them: the guns on the weapon stand, the trophies and dogtags of the
    /// place of fame. The game throws these containers away on the client when a raid starts, as
    /// it does the stash, so they are rebuilt here from the server's answer, opened beside the
    /// player's gear, saved when closed, and shown on the stand by the game's own display code.
    /// </summary>
    public static class HideoutShelves
    {
        private static readonly Dictionary<int, CompoundItem> Shelves = new Dictionary<int, CompoundItem>();
        private static readonly Dictionary<int, HideoutAreaStashController> Displays = new Dictionary<int, HideoutAreaStashController>();
        private static bool _sending;

        public static bool Has(int area)
        {
            return Shelves.ContainsKey(area);
        }

        public static int Count(int area)
        {
            CompoundItem shelf;
            return Shelves.TryGetValue(area, out shelf) ? shelf.GetAllItems().Count(item => item.Id != shelf.Id && item.Parent != null && item.Parent.Container.ParentItem == shelf) : 0;
        }

        /// <summary>Builds every station's container from the server's answer on entering the hideout.</summary>
        public static void Read(JObject reply)
        {
            Clear();
            var shelves = reply != null ? reply["Shelves"] as JObject : null;
            var player = GamePlayerOwner.MyPlayer;
            if (shelves == null || player == null)
            {
                return;
            }
            foreach (var pair in shelves)
            {
                try
                {
                    int area = int.Parse(pair.Key);
                    string id = (string)pair.Value["Id"];
                    var all = new JArray { new JObject { ["_id"] = id, ["_tpl"] = (string)pair.Value["Template"] } };
                    foreach (var item in pair.Value["Items"] as JArray ?? new JArray())
                    {
                        all.Add(item);
                    }
                    var tree = Singleton<ItemFactory>.Instance.FlatItemsToTree(all.ToString().ParseJsonTo<FlatItem[]>(), true);
                    Item root;
                    if (tree.Items == null || !tree.Items.TryGetValue(id, out root) || !(root is CompoundItem))
                    {
                        Plugin.Log.LogWarning("Station " + area + ": its container could not be built");
                        continue;
                    }
                    Shelves[area] = (CompoundItem)root;
                    new ItemController(root, player.ProfileId, player.Profile.Nickname);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Station " + pair.Key + ": building its container failed: " + e.Message);
                }
            }
            Plugin.Log.LogInfo("Station storage: " + string.Join(", ", Shelves.Select(s => ((EAreaType)s.Key) + " " + Count(s.Key)).ToArray()));
        }

        /// <summary>Hangs what is kept on each stand where it can be seen. Called once the stations are shown.</summary>
        public static void ShowAll()
        {
            var controller = HideoutStations.Controller();
            var player = GamePlayerOwner.MyPlayer;
            if (controller == null || player == null)
            {
                return;
            }
            foreach (var pair in Shelves)
            {
                try
                {
                    HideoutArea area;
                    if (!controller.Areas.TryGetValue((EAreaType)pair.Key, out area) || area == null || area.AreaLevels == null)
                    {
                        continue;
                    }
                    int level;
                    HideoutStations.Levels.TryGetValue(pair.Key, out level);
                    level = Mathf.Clamp(level, 0, area.AreaLevels.Length - 1);
                    var points = area.AreaLevels[level] != null ? area.AreaLevels[level].StashObjectsSpawnPoints : null;
                    if (points == null || points.Count == 0)
                    {
                        continue;
                    }
                    HideoutAreaStashController old;
                    if (Displays.TryGetValue(pair.Key, out old))
                    {
                        old.Dispose();
                    }
                    var display = new HideoutAreaStashController();
                    display.Init(null, player.InventoryController, pair.Value, 0.25f, points, null);
                    Displays[pair.Key] = display;
                    display.UpdateStash(true);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Station " + (EAreaType)pair.Key + ": showing what it keeps failed: " + e.Message);
                }
            }
        }

        /// <summary>Opens a station's container beside the player's gear.</summary>
        public static bool Open(int area)
        {
            var player = GamePlayerOwner.MyPlayer;
            CompoundItem shelf;
            if (player == null || !Shelves.TryGetValue(area, out shelf))
            {
                return false;
            }
            var owner = player.GetComponent<GamePlayerOwner>();
            if (owner == null)
            {
                return false;
            }
            owner.ShowInventoryScreenLoot(shelf, () => Send(area), false);
            if (Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(Fit());
            }
            return true;
        }

        /// <summary>
        /// The game's looting screen was laid out for small containers. A station's container is
        /// drawn growing upwards from the top of the panel, out of sight, and is wider than the
        /// panel. This sets it at the panel's top left and shrinks it to the panel's width.
        /// </summary>
        private static System.Collections.IEnumerator Fit()
        {
            // The screen builds the container's grid a moment after it opens.
            EFT.UI.DragAndDrop.TemplatedGridsView view = null;
            RectTransform inner = null;
            for (float waited = 0f; waited < 2f && inner == null; waited += Time.unscaledDeltaTime)
            {
                yield return null;
                EFT.UI.DragAndDrop.GridView best = null;
                foreach (var found in UnityEngine.Object.FindObjectsOfType<EFT.UI.DragAndDrop.GridView>())
                {
                    // The player's own rig and pockets are drawn the same way, on the left; the
                    // looted container is the one furthest to the right.
                    if (found.isActiveAndEnabled && found.GetComponentInParent<EFT.UI.DragAndDrop.TemplatedGridsView>() != null
                        && found.GetComponentInParent<UnityEngine.UI.ScrollRect>() != null
                        && (best == null || found.transform.position.x > best.transform.position.x))
                    {
                        best = found;
                    }
                }
                if (best == null)
                {
                    continue;
                }
                view = best.GetComponentInParent<EFT.UI.DragAndDrop.TemplatedGridsView>();
                // The frame that holds the grid: the child of the view that the grid sits under.
                for (var at = best.transform; at != null && at != view.transform; at = at.parent)
                {
                    inner = at as RectTransform;
                }
            }
            if (view == null || inner == null)
            {
                Plugin.Log.LogWarning("Station storage screen: the container's grid was not found; left as the game drew it");
                yield break;
            }
            var scroll = (RectTransform)view.GetComponentInParent<UnityEngine.UI.ScrollRect>().transform;
            Plugin.Log.LogInfo("Station storage screen: fitting '" + view.name + "' (child '" + (inner != null ? inner.name : "?") + "' " + (inner != null ? inner.rect.size.ToString("F0") : "") + " at "
                               + (inner != null ? inner.position.ToString("F0") : "") + ") into '" + scroll.name + "' " + scroll.rect.size.ToString("F0") + " at " + scroll.position.ToString("F0"));
            var corners = new Vector3[4];
            // The layout settles over a few frames; hold the place until it has.
            for (float held = 0f; held < 0.6f && view != null && inner != null; held += Time.unscaledDeltaTime)
            {
                float fit = inner.rect.width > 1f ? Mathf.Min(1f, (scroll.rect.width - 8f) / inner.rect.width) : 1f;
                inner.localScale = new Vector3(fit, fit, 1f);
                scroll.GetWorldCorners(corners);   // bottom left, top left, top right, bottom right
                var topLeft = corners[1];
                // Its pivot may be at its bottom or its top; place it by where its own top left ends up.
                var own = new Vector3[4];
                inner.GetWorldCorners(own);
                inner.position += new Vector3(topLeft.x + 36f - own[1].x, topLeft.y - own[1].y, 0f);
                yield return null;
            }
            if (inner != null)
            {
                Plugin.Log.LogInfo("Station storage screen: left at " + inner.position.ToString("F0") + ", scale " + inner.lossyScale.x.ToString("F2"));
            }
        }

        public static async void Send(int area)
        {
            await SendAsync(area);
        }

        public static async Task SendAsync(int area)
        {
            CompoundItem shelf;
            if (_sending || HideoutDoors.Inside == null || !Shelves.TryGetValue(area, out shelf))
            {
                return;
            }
            _sending = true;
            try
            {
                var items = JArray.Parse(Singleton<ItemFactory>.Instance.TreeToFlatItems(shelf).ToJson());
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if ((string)items[i]["_id"] == shelf.Id.ToString())
                    {
                        items.RemoveAt(i);
                    }
                }
                var body = new JObject { ["Id"] = HideoutDoors.Inside, ["Area"] = area, ["Items"] = items };
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/thetruespt/hideout/shelf", body.ToString(Newtonsoft.Json.Formatting.None)));
                Plugin.Log.LogInfo("Station " + (EAreaType)area + " saved: " + items.Count + " item(s), server said " + (((bool?)reply["Ok"] ?? false) ? "ok" : (string)reply["Error"]));
                HideoutAreaStashController display;
                if (Displays.TryGetValue(area, out display))
                {
                    await display.UpdateStash(true);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Saving station " + area + " failed: " + e);
            }
            finally
            {
                _sending = false;
            }
        }

        public static void Clear()
        {
            foreach (var display in Displays.Values)
            {
                try
                {
                    display.Dispose();
                }
                catch (Exception)
                {
                }
            }
            Displays.Clear();
            Shelves.Clear();
        }
    }
}
