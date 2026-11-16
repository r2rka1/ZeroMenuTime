using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ZeroMenuTime
{
    /// <summary>
    /// Trading that happens entirely on what the player carries. Nothing goes through the stash:
    /// payment is taken from carried items, purchases land in carried containers, and the result is
    /// saved with the rest of the raid inventory when the raid ends.
    /// </summary>
    public static class LocalTrade
    {
        /// <summary>True while a trader screen opened by this mod is on screen.</summary>
        public static bool Active;

        private static readonly EquipmentSlot[] ContainerSlots =
        {
            EquipmentSlot.Backpack, EquipmentSlot.TacticalVest, EquipmentSlot.Pockets, EquipmentSlot.SecuredContainer,
        };

        /// <summary>Worn things that can be sold straight off the body: weapons and what protects.</summary>
        private static readonly EquipmentSlot[] SellableWornSlots =
        {
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster, EquipmentSlot.Scabbard,
            EquipmentSlot.Headwear, EquipmentSlot.Earpiece, EquipmentSlot.FaceCover, EquipmentSlot.Eyewear, EquipmentSlot.ArmorVest,
            EquipmentSlot.TacticalVest, EquipmentSlot.Backpack,
        };

        /// <summary>True for a container (pack, rig, case) that has something in one of its grids.</summary>
        public static bool HasThingsInside(Item item)
        {
            var holder = item as CompoundItem;
            if (holder?.Grids == null)
            {
                return false;
            }
            foreach (var grid in holder.Grids)
            {
                if (grid.Items.Any())
                {
                    return true;
                }
            }
            return false;
        }

        private static readonly EquipmentSlot[] WearSlots =
        {
            EquipmentSlot.Backpack, EquipmentSlot.TacticalVest, EquipmentSlot.ArmorVest, EquipmentSlot.Headwear,
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster,
        };

        /// <summary>
        /// The trade screen is built around one "stash" grid. Give it a stand-in stash whose grid is the
        /// player's backpack, so the player side of the screen shows what is in the pack.
        /// </summary>
        public static Stash BuildPackStash(Player player)
        {
            var fake = Singleton<ItemFactory>.Instance.CreateFakeStash();
            fake.Grids[0] = BuildCarriedList(player, fake);
            new ItemController(fake, player.ProfileId, player.Profile.Nickname);
            _packStash = fake;
            return fake;
        }

        private static Stash _packStash;

        /// <summary>
        /// One list of everything loose the player carries: the contents of the backpack, rig, pockets
        /// and secure container. It is a reference list, the kind the trader's side of the screen uses:
        /// items are shown in it without being moved out of the container they are really in.
        /// </summary>
        private static Grid BuildCarriedList(Player player, Stash owner)
        {
            var list = new ReferenceGrid("hideout", 10, 25, true, false, new ItemFilter[0], owner);
            foreach (var grid in CarriedGrids(player))
            {
                foreach (var item in grid.Items.ToList())
                {
                    // A case or pouch with things in it is not for sale as it is: it would take its
                    // contents with it. Emptied, it is listed.
                    if (HasThingsInside(item))
                    {
                        continue;
                    }
                    var added = list.AddAnywhere(item, EErrorHandlingType.Ignore);
                    if (added.Failed)
                    {
                        Plugin.Log.LogWarning("Could not list " + item.TemplateId + " for trading: " + added.Error);
                    }
                }
            }
            // What is worn can be sold too: a rifle picked up and slung is not in any pack. The
            // weapon in the hands is left out: it cannot be taken away while it is held.
            var inHands = player.HandsController != null ? player.HandsController.Item : null;
            foreach (var slot in SellableWornSlots)
            {
                var worn = player.Profile.Inventory.Equipment.GetSlot(slot).ContainedItem;
                // A pack or rig is only for sale once it is empty. The secure container is never listed.
                if (worn == null || worn == inHands || HasThingsInside(worn))
                {
                    continue;
                }
                var added = list.AddAnywhere(worn, EErrorHandlingType.Ignore);
                if (added.Failed)
                {
                    Plugin.Log.LogWarning("Could not list worn " + worn.TemplateId + " for trading: " + added.Error);
                }
            }
            return list;
        }

        // The player-side grid view of the open trade screen and what it was shown with,
        // kept so the list can be rebuilt after a trade changes what the player carries.
        private static EFT.UI.DragAndDrop.TradingGridView _packView;
        private static EFT.InventoryLogic.ItemContext _packContext;
        private static Assortment _packAssortment;
        private static InventoryController _packController;
        private static ItemUiContext _packUi;

        public static void RememberPackView(EFT.UI.DragAndDrop.TradingGridView view, EFT.InventoryLogic.ItemContext context,
            Assortment assortment, InventoryController controller, ItemUiContext ui)
        {
            _packView = view;
            _packContext = context;
            _packAssortment = assortment;
            _packController = controller;
            _packUi = ui;
        }

        public static void ForgetPackView()
        {
            _packView = null;
            _packStash = null;
        }

        /// <summary>After a purchase or sale, list what the player carries now.</summary>
        public static void RefreshPackView()
        {
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                if (!Active || _packView == null || _packStash == null || player == null)
                {
                    return;
                }
                var list = BuildCarriedList(player, _packStash);
                _packStash.Grids[0] = list;
                var view = _packView;
                view.Close();
                view.Show(list, _packContext, _packAssortment, _packController, _packUi);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Refreshing the carried list failed: " + e);
            }
        }

        /// <summary>Everything the player carries, including contents of containers.</summary>
        public static List<Item> CarriedItems(Player player)
        {
            return player.Profile.Inventory.Equipment.GetAllItems().ToList();
        }

        private static IEnumerable<Grid> CarriedGrids(Player player)
        {
            var equipment = player.Profile.Inventory.Equipment;
            foreach (var slot in ContainerSlots)
            {
                var container = equipment.GetSlot(slot).ContainedItem as CompoundItem;
                if (container?.Grids == null)
                {
                    continue;
                }
                foreach (var grid in container.Grids)
                {
                    yield return grid;
                }
            }
        }

        /// <summary>A free place for the item in what the player carries, or null.</summary>
        public static ItemAddress RoomFor(Player player, Item item)
        {
            return FindRoom(player, item);
        }

        private static ItemAddress FindRoom(Player player, Item item)
        {
            foreach (var grid in CarriedGrids(player))
            {
                var address = grid.FindLocationForItem(item);
                if (address != null)
                {
                    return address;
                }
            }
            // Nothing to put it in: wear or sling it if the matching gear slot is empty
            // (this is how a first backpack or rig gets onto the player).
            var equipment = player.Profile.Inventory.Equipment;
            foreach (var slotName in WearSlots)
            {
                var slot = equipment.GetSlot(slotName);
                if (slot == null || slot.ContainedItem != null)
                {
                    continue;
                }
                var address = slot.CreateItemAddress();
                if (!ItemManipulator.Add(item, address, player.InventoryController, true).Failed)
                {
                    return address;
                }
            }
            return null;
        }

        public static async Task<bool> Give(Player player, Item item)
        {
            var controller = player.InventoryController;

            // The raid only has models loaded for items that were in it at the start. A new item needs
            // its models loaded first, or using it breaks the player's hands and camera.
            var resources = item.GetAllItems().SelectMany(x => x.Template.AllResources).ToArray();
            if (resources.Length > 0)
            {
                await Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                    ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Local, resources, JobYieldPriority.General);
            }

            var address = FindRoom(player, item);

            // Stackable things (money, ammo) first top up stacks the player already carries, so they
            // need no free slot at all when those stacks have room. This is all-or-nothing: if the
            // whole amount cannot be placed, nothing is changed and the caller keeps the item.
            if (item.StackMaxSize > 1)
            {
                var stacks = CarriedItems(player)
                    .Where(x => x.TemplateId == item.TemplateId && x.StackObjectsCount < x.StackMaxSize)
                    .ToList();
                int capacity = stacks.Sum(x => x.StackMaxSize - x.StackObjectsCount);
                if (capacity < item.StackObjectsCount && address == null)
                {
                    return false;
                }
                foreach (var stack in stacks)
                {
                    if (item.StackObjectsCount <= 0)
                    {
                        break;
                    }
                    int moved = Math.Min(stack.StackMaxSize - stack.StackObjectsCount, item.StackObjectsCount);
                    stack.StackObjectsCount += moved;
                    item.StackObjectsCount -= moved;
                    stack.RaiseRefreshEvent();
                }
                if (item.StackObjectsCount <= 0)
                {
                    return true;
                }
            }

            if (address == null)
            {
                return false;
            }
            // A brand-new item has no "transaction" form in the game (it only knows moves, splits and
            // removals), so apply the add directly and raise the inventory events ourselves, the same
            // way the game's own trading code does for the selling table.
            var add = ItemManipulator.Add(item, address, controller, false);
            if (add.Failed)
            {
                Plugin.Log.LogWarning("Add failed: " + add.Error);
                return false;
            }
            add.Value.RaiseEvents(controller, CommandStatus.Begin);
            add.Value.RaiseEvents(controller, CommandStatus.Succeed);

            // New containers count as "not searched yet", which blocks putting anything in them.
            // This can only be done once the item has a place in the inventory.
            var search = controller.SearchController as IPlayerSearchController;
            foreach (var part in search == null ? new Item[0] : item.GetAllItems().ToArray())
            {
                try
                {
                    if (part is SearchableItem searchable)
                    {
                        search.SetItemAsSearched(searchable);
                    }
                    search.SetItemAsKnown(part, false);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Could not mark " + part.TemplateId + " as known: " + e.Message);
                }
            }
            await Task.Yield();
            return true;
        }

        public static async Task<bool> Take(Player player, Item item, int count)
        {
            var controller = player.InventoryController;
            if (item.StackObjectsCount > count)
            {
                // Taking part of a stack: apply it directly (the game has no transaction form for this
                // in a raid) and tell the inventory views the stack changed.
                var split = ItemManipulator.SplitToNowhere(item, count, controller, controller, false);
                if (split.Failed)
                {
                    Plugin.Log.LogWarning("Split failed: " + split.Error);
                    return false;
                }
                split.Value.RaiseEvents(controller, CommandStatus.Begin);
                split.Value.RaiseEvents(controller, CommandStatus.Succeed);
                await Task.Yield();
                return true;
            }
            var discard = ItemManipulator.Discard(item, controller, false);
            if (discard.Failed)
            {
                Plugin.Log.LogWarning("Discard failed: " + discard.Error);
                return false;
            }
            discard.Value.RaiseEvents(controller, CommandStatus.Begin);
            discard.Value.RaiseEvents(controller, CommandStatus.Succeed);
            await Task.Yield();
            return true;
        }

        private static readonly HashSet<string> CurrencyTpls = new HashSet<string>
        {
            "5449016a4bdc2d6f028b456f", // roubles
            "5696686a4bdc2da3298b456a", // dollars
            "569668774bdc2da2298b4568", // euros
        };

        private class MoneyAmount
        {
            public string Tpl;
            public double Count;
        }

        private class TradeReportResponse
        {
            public bool Ok;
            public double SalesSum;
            public int LoyaltyLevel;
            public string Error;
        }

        /// <summary>
        /// Tell the server how much money changed hands, so the trader's sales total and loyalty
        /// level grow as they would for a normal trade. Needs the ZeroMenuTime server mod.
        /// </summary>
        private static async Task Report(string traderId, List<MoneyAmount> money)
        {
            try
            {
                if (money.Count == 0)
                {
                    return;
                }
                string body = Newtonsoft.Json.JsonConvert.SerializeObject(new { TraderId = traderId, Money = money });
                string reply = await SPT.Common.Http.RequestHandler.PostJsonAsync("/zeromenutime/trade", body);
                var response = Newtonsoft.Json.JsonConvert.DeserializeObject<TradeReportResponse>(reply ?? "");
                if (response == null || !response.Ok)
                {
                    Plugin.Log.LogWarning("Trade not recorded for loyalty: " + (response?.Error ?? "no reply from server mod"));
                    return;
                }
                var player = GamePlayerOwner.MyPlayer;
                if (player != null && player.Profile.TradersInfo.TryGetValue(traderId, out var info))
                {
                    info.SalesSum = (long)response.SalesSum;
                }
                Plugin.Log.LogInfo("Trade recorded: sales total " + response.SalesSum.ToString("F0") + ", loyalty level " + response.LoyaltyLevel);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Trade not recorded for loyalty: " + e.Message);
            }
        }

        public static async Task<IResult> Purchase(Assortment assortment)
        {
            var player = GamePlayerOwner.MyPlayer;
            int quantity = assortment.CurrentQuantity;
            Item selected = assortment.SelectedItem;
            var payment = assortment.CurrentRequisites.SelectMany(x => x.PreparedItems).ToArray();
            assortment.TransactionInProgress = true;
            try
            {
                if (player == null || selected == null)
                {
                    return new FailedResult("Nothing selected");
                }

                // Build what the player will receive before taking any payment.
                var goods = new List<Item>();
                int stackMax = Math.Max(1, selected.StackMaxSize);
                int remaining = quantity;
                while (remaining > 0)
                {
                    int amount = Math.Min(stackMax, remaining);
                    var copy = selected.CloneItem(player.InventoryController);
                    copy.StackObjectsCount = amount;
                    copy.UnlimitedCount = false;
                    goods.Add(copy);
                    remaining -= amount;
                }

                // With rooms, what is bought is put on the trader's table, as quest rewards are.
                string traderId = assortment._traderId;
                bool toTable = TraderRoom.For(traderId) != null;
                if (!toTable && FindRoom(player, goods[0]) == null)
                {
                    NotificationManager.DisplayWarningNotification("No room in your gear for that.");
                    return new FailedResult("No room");
                }

                var paid = payment
                    .Where(x => CurrencyTpls.Contains(x.Item.TemplateId))
                    .Select(x => new MoneyAmount { Tpl = x.Item.TemplateId, Count = x.Count })
                    .ToList();
                foreach (var reference in payment)
                {
                    if (!await Take(player, reference.Item, reference.Count))
                    {
                        NotificationManager.DisplayWarningNotification("Payment failed.");
                        return new FailedResult("Payment failed");
                    }
                }

                int delivered = 0;
                if (toTable && await LeaveOnTable(traderId, goods))
                {
                    delivered = goods.Count;
                }
                else
                {
                    foreach (var item in goods)
                    {
                        if (await Give(player, item))
                        {
                            delivered++;
                        }
                    }
                }
                if (delivered < goods.Count)
                {
                    NotificationManager.DisplayWarningNotification("Ran out of room: " + delivered + " of " + goods.Count + " delivered.");
                }

                RefreshPackView();
                Plugin.Log.LogInfo("Bought " + quantity + " x " + selected.TemplateId + ", paid with " + payment.Length + " item(s), delivered " + delivered + "/" + goods.Count);
                selected.BuyRestrictionCurrent += quantity;
                assortment.ClearPreparedItems();
                _ = Report(assortment._traderId, paid);
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.TradeOperationComplete);
                return SuccessfulResult.New;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Local purchase failed: " + e);
                return new FailedResult(e.Message);
            }
            finally
            {
                assortment.TransactionInProgress = false;
            }
        }

        /// <summary>
        /// Leaves bought items with the trader (in their mail, on the server) and lays them on the
        /// table in the trader's room. Whatever is not picked up waits there for the next visit.
        /// False if the server could not take them; the caller then hands them over directly.
        /// </summary>
        private static async Task<bool> LeaveOnTable(string traderId, List<Item> goods)
        {
            try
            {
                var flat = Newtonsoft.Json.Linq.JArray.Parse(Singleton<ItemFactory>.Instance.TreeToFlatItems(goods.ToArray()).ToJson());
                var body = new Newtonsoft.Json.Linq.JObject { ["TraderId"] = traderId, ["Items"] = flat };
                var reply = Newtonsoft.Json.Linq.JObject.Parse(await SPT.Common.Http.RequestHandler.PostJsonAsync(
                    "/zeromenutime/mail/purchase", body.ToString(Newtonsoft.Json.Formatting.None)));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Bought items not left with the trader: " + reply["Error"]);
                    return false;
                }
                Mail.Collect(traderId, TraderConfig.NameOf(traderId));
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Bought items not left with the trader: " + e.Message);
                return false;
            }
        }

        public static async Task Sell(Assortment assortment)
        {
            var player = GamePlayerOwner.MyPlayer;
            if (player == null)
            {
                return;
            }
            // The items on the selling table are copies; _clones maps each copy to the real item.
            var originals = assortment.SellingTableGrid.ContainedItems.Keys
                .Select(copy => assortment._clones.TryGetValue(copy, out var original) ? original : null)
                .Where(x => x != null)
                .ToList();
            if (originals.Count == 0)
            {
                return;
            }
            var price = assortment.PreparedSum;
            assortment.TransactionInProgress = true;
            try
            {
                // Remember what is being sold for "sell N of item X" quest objectives.
                var soldReferences = originals
                    .Select(x => new TradingItemReference { Item = x, Count = x.StackObjectsCount })
                    .ToArray();
                int sold = 0;
                foreach (var item in originals)
                {
                    if (await Take(player, item, item.StackObjectsCount))
                    {
                        sold++;
                    }
                }
                assortment.ClearSellItems();

                if (sold == originals.Count && price.CurrencyId.HasValue && price.Amount > 0)
                {
                    string currency = price.CurrencyId.Value;
                    int remaining = price.Amount;
                    int undelivered = 0;
                    while (remaining > 0)
                    {
                        var money = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), currency, null);
                        int amount = Math.Min(Math.Max(1, money.StackMaxSize), remaining);
                        money.StackObjectsCount = amount;
                        if (!await Give(player, money))
                        {
                            undelivered += remaining;
                            break;
                        }
                        remaining -= amount;
                    }
                    if (undelivered > 0)
                    {
                        NotificationManager.DisplayWarningNotification("No room for " + undelivered + " of the payment.");
                    }
                    try
                    {
                        assortment.UpdateQuestsBackendCounters(soldReferences);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning("Quest counters not updated: " + e.Message);
                    }
                    _ = Report(assortment._traderId, new List<MoneyAmount> { new MoneyAmount { Tpl = currency, Count = price.Amount } });
                }
                else if (sold != originals.Count)
                {
                    NotificationManager.DisplayWarningNotification("Some items could not be sold; no payment made.");
                }

                RefreshPackView();
                Plugin.Log.LogInfo("Sold " + sold + "/" + originals.Count + " item(s) for " + price.Amount);
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.TradeOperationComplete);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Local sale failed: " + e);
            }
            finally
            {
                assortment.TransactionInProgress = false;
            }
        }
    }

    public class LocalPurchasePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Assortment), nameof(Assortment.Purchase));
        }

        [PatchPrefix]
        public static bool Prefix(Assortment __instance, ref Task<IResult> __result)
        {
            if (!LocalTrade.Active)
            {
                return true;
            }
            __result = LocalTrade.Purchase(__instance);
            return false;
        }
    }

    public class LocalSellPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Assortment), nameof(Assortment.Sell));
        }

        [PatchPrefix]
        public static bool Prefix(Assortment __instance)
        {
            if (!LocalTrade.Active)
            {
                return true;
            }
            if (!__instance.TransactionInProgress)
            {
                LocalTrade.Sell(__instance);
            }
            return false;
        }
    }

    /// <summary>
    /// The player side of the trade screen draws items lazily, only for the part of the grid the
    /// scroll area reports as visible. A backpack is too small to ever scroll, so nothing gets drawn.
    /// Draw every item up front instead.
    /// </summary>
    public class DrawPackItemsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.FirstMethod(typeof(EFT.UI.DragAndDrop.TradingGridView), m =>
                m.Name == "Show" && m.GetParameters().Length == 5 && m.GetParameters()[0].ParameterType == typeof(Grid));
        }

        [PatchPostfix]
        public static void Postfix(EFT.UI.DragAndDrop.TradingGridView __instance, Grid grid, EFT.InventoryLogic.ItemContext sourceContext, Assortment assortment, InventoryController inventoryController, ItemUiContext itemUiContext)
        {
            if (!LocalTrade.Active || grid == null)
            {
                return;
            }
            try
            {
                LocalTrade.RememberPackView(__instance, sourceContext, assortment, inventoryController, itemUiContext);
                Traverse.Create(__instance).Property("IsMagnified").SetValue(false);
                int drawn = 0;
                foreach (var entry in grid.ContainedItems.ToArray())
                {
                    if (__instance.GridItemViews.Any(view => view.Item == entry.Key))
                    {
                        continue;
                    }
                    __instance.CreateItemView(entry.Key, entry.Value, itemUiContext);
                    drawn++;
                }
                Plugin.Log.LogInfo("Trade screen: drew " + drawn + " of " + grid.ContainedItems.Count + " carried item(s)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("DrawPackItemsPatch: " + e);
            }
        }
    }

    /// <summary>Let "auto fill" pay with anything carried, not only what is in the backpack.</summary>
    public class CarriedPaymentPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HandoverExtensions), nameof(HandoverExtensions.AutoFill));
        }

        [PatchPrefix]
        public static void Prefix(ref IEnumerable<Item> allItems)
        {
            var player = GamePlayerOwner.MyPlayer;
            if (LocalTrade.Active && player != null)
            {
                allItems = LocalTrade.CarriedItems(player);
            }
        }
    }
}
