using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.Repairing;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.Reflection.Patching;

namespace ZeroMenuTime
{
    /// <summary>
    /// Repair kits in a raid. The game only offers Repair in the menus, looks for kits only in the
    /// stash, and has the server do the work on the saved character. In a raid there is no stash and
    /// the gear is in the game, not on the server, so none of that reaches a kit in the pack.
    ///
    /// Here the game's own Repair window is opened from an item's menu as in the menus; the kits are
    /// the ones the player carries (and those in the hideout's stash, when inside); and the repair is
    /// worked out by the server's own rules on the numbers sent to it, then applied in the game.
    /// Traders are not offered as repairers in a raid.
    /// </summary>
    public static class Repairs
    {
        public static bool InRaid => InGameStatus.InRaid && GamePlayerOwner.MyPlayer != null;

        private static bool _patched;

        /// <summary>
        /// The game's repair class cannot be touched while the game is starting: doing so then
        /// breaks it for good and the profile never loads. Its changes are made when the first raid
        /// starts, by which time the game has long set it up.
        /// </summary>
        public static void EnablePatches()
        {
            if (_patched)
            {
                return;
            }
            _patched = true;
            foreach (var patch in new ModulePatch[] { new RepairKitsAtHandPatch(), new RepairByKitPatch(), new NoTraderRepairInRaidPatch(), new RepairUnlockedPatch(), new UnpackInRaidPatch() })
            {
                try
                {
                    patch.Enable();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Repair kits in a raid are off: " + patch.GetType().Name + " could not be applied: " + e.Message);
                }
            }
        }

        /// <summary>Every kit within reach: what is carried, and the hideout's stash when standing in the hideout.</summary>
        public static IEnumerable<RepairKit> KitsAtHand()
        {
            var player = GamePlayerOwner.MyPlayer;
            var seen = new HashSet<Item>();
            foreach (var kit in player.Profile.Inventory.Equipment.GetAllItems().OfType<RepairKit>())
            {
                if (seen.Add(kit))
                {
                    yield return kit;
                }
            }
            if (HideoutDoors.Inside != null && HideoutStash.Current != null)
            {
                foreach (var kit in HideoutStash.Current.GetAllItems().OfType<RepairKit>())
                {
                    if (seen.Add(kit))
                    {
                        yield return kit;
                    }
                }
            }
        }

        private static Item Find(string id)
        {
            var player = GamePlayerOwner.MyPlayer;
            foreach (var item in player.Profile.Inventory.Equipment.GetAllItems())
            {
                if (item.Id == id)
                {
                    return item;
                }
            }
            if (HideoutStash.Current != null)
            {
                foreach (var item in HideoutStash.Current.GetAllItems())
                {
                    if (item.Id == id)
                    {
                        return item;
                    }
                }
            }
            return null;
        }

        /// <summary>The repair itself: the server works out the new durability by its own rules, the game is then told.</summary>
        public static async Task<IResult> ByKit(RepairController controller, RepairItem[] kits, string targetId)
        {
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                var target = Find(targetId);
                var repairable = target?.GetItemComponent<RepairableComponent>();
                if (repairable == null)
                {
                    return new FailedResult("The item to repair was not found");
                }
                float points = kits.Sum(k => k.Count);
                string body = JsonConvert.SerializeObject(new
                {
                    Tpl = target.TemplateId.ToString(),
                    Durability = (double)repairable.Durability,
                    MaxDurability = (double)repairable.MaxDurability,
                    Points = (double)points,
                });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/repair/kit", body));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Repair by kit refused: " + reply["Error"]);
                    return new FailedResult("Repair failed: " + reply["Error"]);
                }
                float before = repairable.Durability, beforeMax = repairable.MaxDurability;
                repairable.MaxDurability = (float)reply["MaxDurability"];
                repairable.Durability = (float)reply["Durability"];
                target.RaiseRefreshEvent(false, true);

                // The window has already taken the points off the last kit used; a kit used up
                // entirely is still there and is thrown away now.
                foreach (var used in kits)
                {
                    var kit = Find(used.Id) as RepairKit;
                    if (kit == null)
                    {
                        continue;
                    }
                    if (kit.Resource <= used.Count + 0.001f || kit.Resource <= 0.001f)
                    {
                        var gone = ItemManipulator.Discard(kit, player.InventoryController, false);
                        if (gone.Succeeded)
                        {
                            gone.Value.RaiseEvents(player.InventoryController, CommandStatus.Begin);
                            gone.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
                        }
                    }
                    else
                    {
                        kit.RaiseRefreshEvent(false, true);
                    }
                }
                if (HideoutDoors.Inside != null)
                {
                    HideoutStash.Send("repair");
                }
                var changed = Traverse.Create(controller).Field("_onSuccessfulRepairChangedEvent").GetValue<Action>();
                changed?.Invoke();
                Plugin.Log.LogInfo("Repaired " + target.TemplateId + " with " + points.ToString("F1") + " kit point(s): " + before.ToString("F1") + "/" + beforeMax.ToString("F1")
                                   + " -> " + repairable.Durability.ToString("F1") + "/" + repairable.MaxDurability.ToString("F1"));
                return SuccessfulResult.New;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Repair by kit failed: " + e);
                return new FailedResult("Repair failed, see log");
            }
        }
    }

    /// <summary>In a raid, Repair is offered in an item's menu as it is in the menus.</summary>
    public class RepairMenuPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var type = AccessTools.TypeByName("EFT.UI.ItemContextInteractionsSwitcher");
            return type != null ? AccessTools.Method(type, "IsActive") : null;
        }

        [PatchPostfix]
        public static void Postfix(object __instance, EItemInfoButton button, ref bool __result)
        {
            try
            {
                if (__result || !Repairs.InRaid)
                {
                    return;
                }
                if (button == EItemInfoButton.Unpack)
                {
                    // A sealed case can be unpacked in the hideout, where there is a stash for what is inside.
                    var item = Traverse.Create(__instance).Field("_item").GetValue<Item>();
                    __result = Unpacking.Possible && item != null && item.ItemInteractionButtons.Contains(EItemInfoButton.Unpack)
                               && !Traverse.Create(__instance).Property("BadContextType").GetValue<bool>();
                    return;
                }
                if (button != EItemInfoButton.Repair)
                {
                    return;
                }
                // Only where there is something to repair it with.
                if (!Traverse.Create(__instance).Property("BadContextType").GetValue<bool>() && Repairs.KitsAtHand().Any(k => k.Resource > 0f))
                {
                    __result = true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Repair menu: " + e.Message);
            }
        }
    }

    /// <summary>In a raid the kits are the ones at hand, not those of a stash that is not there.</summary>
    public class RepairKitsAtHandPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(RepairController), nameof(RepairController.FormRepairKitsCollection));
        }

        [PatchPrefix]
        public static bool Prefix(RepairController __instance, Predicate<RepairKit> predicate, ref IEnumerable<RepairKitsCollection> __result)
        {
            if (!Repairs.InRaid)
            {
                return true;
            }
            var byKind = new Dictionary<RepairKitTemplate, RepairKitsCollection>();
            foreach (var kit in Repairs.KitsAtHand())
            {
                if (kit.Resource <= 0f || (predicate != null && !predicate(kit)))
                {
                    continue;
                }
                RepairKitsCollection kind;
                if (byKind.TryGetValue(kit.RepairKitTemplate, out kind))
                {
                    kind.AddRepairKit(kit);
                }
                else
                {
                    byKind[kit.RepairKitTemplate] = __instance.CreateRepairKitsCollection(kit);
                }
            }
            __result = byKind.Values.ToList();
            return false;
        }
    }

    /// <summary>In a raid the repair is done here, not asked of the server's saved character.</summary>
    public class RepairByKitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(RepairController), nameof(RepairController.RepairItemsByRepairKit));
        }

        [PatchPrefix]
        public static bool Prefix(RepairController __instance, RepairItem[] repairKitsInfo, string targetItem, ref Task<IResult> __result)
        {
            if (!Repairs.InRaid)
            {
                return true;
            }
            __result = Repairs.ByKit(__instance, repairKitsInfo, targetItem);
            return false;
        }
    }

    /// <summary>Traders do not repair in a raid (yet): only kits are offered in the window.</summary>
    public class NoTraderRepairInRaidPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(RepairController), nameof(RepairController.TraderRepairers));
        }

        [PatchPostfix]
        public static void Postfix(ref IEnumerable<EFT.Trading.Trader> __result)
        {
            if (Repairs.InRaid)
            {
                __result = Enumerable.Empty<EFT.Trading.Trader>();
            }
        }
    }

    /// <summary>
    /// The game lets kits be used once the hideout has the station for it: the Workbench for
    /// weapons, the Lavatory for armour. In a raid it does not know what this mod's hideout has
    /// built, so the answer is taken from the hideout's own levels.
    /// </summary>
    public class RepairUnlockedPatch : ModulePatch
    {
        private const string Weapons = "5422acb9af1c889c16000029";

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(RepairController), nameof(RepairController.IsRepairUnlocked));
        }

        [PatchPostfix]
        public static void Postfix(Item item, ref bool __result)
        {
            if (__result || !Repairs.InRaid || item == null)
            {
                return;
            }
            int station = item.Template.IsChildOf(Weapons) ? (int)EAreaType.Workbench : (int)EAreaType.WaterCloset;
            int level;
            __result = HideoutStations.Levels.TryGetValue(station, out level) && level >= 1;
        }
    }
}
