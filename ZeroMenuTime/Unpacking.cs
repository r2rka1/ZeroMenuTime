using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using JsonType;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.Reflection.Patching;

namespace ZeroMenuTime
{
    /// <summary>
    /// Sealed cases (the weapon case of an airdrop and the like) in the hideout. The game only
    /// offers Unpack in the menus and has the server open the case on the saved character. In the
    /// hideout of this mod the case is in the game, so Unpack is offered there, the server is asked
    /// only what is inside, and the contents are put into the hideout's stash.
    /// </summary>
    public static class Unpacking
    {
        /// <summary>Only in the hideout: that is where there is a stash to unpack into.</summary>
        public static bool Possible => Repairs.InRaid && HideoutDoors.Inside != null && HideoutStash.Current != null;

        public static async Task<IResult> Open(Item box)
        {
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                if (box == null || player == null || !Possible)
                {
                    return new FailedResult("This can only be unpacked in the hideout");
                }
                string body = JsonConvert.SerializeObject(new { Tpl = box.TemplateId.ToString() });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/unpack", body));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Unpacking refused: " + reply["Error"]);
                    return new FailedResult("Could not unpack: " + reply["Error"]);
                }
                // The case goes first: what was inside may need the room it took.
                var gone = ItemManipulator.Discard(box, player.InventoryController, false);
                if (gone.Failed)
                {
                    return new FailedResult("Could not unpack: " + gone.Error);
                }
                gone.Value.RaiseEvents(player.InventoryController, CommandStatus.Begin);
                gone.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
                InventorySync.Gone(box);

                bool found = (bool?)reply["FoundInRaid"] ?? false;
                int made = 0, total = 0;
                foreach (var thing in reply["Loot"] as JArray ?? new JArray())
                {
                    total++;
                    try
                    {
                        var flat = thing.ToString().ParseJsonTo<FlatItem[]>();
                        if (flat.Length == 0)
                        {
                            continue;
                        }
                        var tree = Singleton<ItemFactory>.Instance.FlatItemsToTree(flat, true);
                        Item root;
                        // The thing itself is the first of its list; the rest are its parts.
                        if (tree.Items == null || !tree.Items.TryGetValue(flat[0]._id, out root))
                        {
                            continue;
                        }
                        foreach (var part in root.GetAllItems())
                        {
                            part.SpawnedInSession = found;
                        }
                        if (await HideoutCrafting.PutItem(player, root))
                        {
                            made++;
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning("Something from the case could not be made: " + e.Message);
                    }
                }
                HideoutStash.Send("after unpacking");
                Plugin.Log.LogInfo("Unpacked " + box.TemplateId + ": " + made + " of " + total + " thing(s) placed");
                NotificationManager.DisplayMessageNotification(made == total
                    ? "Unpacked: " + made + " thing(s), in the stash."
                    : "Unpacked, but only " + made + " of " + total + " thing(s) found room.", ENotificationDurationType.Long);
                return SuccessfulResult.New;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Unpacking failed: " + e);
                return new FailedResult("Could not unpack, see log");
            }
        }
    }

    /// <summary>In the hideout the case is unpacked here, not by the server on the saved character.</summary>
    public class UnpackInRaidPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.UnpackItem));
        }

        [PatchPrefix]
        public static bool Prefix(Item targetItem, ref Task<IResult> __result)
        {
            if (!Repairs.InRaid)
            {
                return true;
            }
            __result = Unpacking.Open(targetItem);
            return false;
        }
    }
}
