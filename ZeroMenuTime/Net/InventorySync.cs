using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EFT;
using EFT.InventoryLogic;

namespace ZeroMenuTime
{
    /// <summary>
    /// Trading, building and crafting take things out of what a player carries without the player
    /// moving anything: money paid, goods sold, materials used. The game has no network form for
    /// that, so in co-op every other machine would go on believing the player still has them. Here
    /// the machine that made the change says exactly what it did, by item id, and the others do the
    /// same to their copy of that player.
    /// </summary>
    public static class InventorySync
    {
        /// <summary>The whole item is gone from what the local player carries.</summary>
        public static void Gone(Item item)
        {
            Tell("gone " + item.Id);
        }

        /// <summary>A stack the local player carries now holds this many.</summary>
        public static void Count(Item item)
        {
            Tell("count " + item.Id + " " + item.StackObjectsCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void Tell(string what)
        {
            if (Net.Coop)
            {
                Net.Current.Send("zmt.inv", Encoding.UTF8.GetBytes(what), true);
            }
        }

        /// <summary>On every other machine: do to the sender's inventory what the sender did.</summary>
        public static void OnTold(byte[] body, string from)
        {
            string[] parts = Encoding.UTF8.GetString(body ?? new byte[0]).Split(' ');
            if (parts.Length < 2)
            {
                return;
            }
            Player owner = null;
            foreach (var human in Net.Current.Humans())
            {
                if (human.ProfileId == from && !human.IsLocal)
                {
                    owner = human.Player;
                }
            }
            if (owner == null)
            {
                Plugin.Log.LogWarning("Inventory sync: no player " + from + " here for '" + parts[0] + " " + parts[1] + "'");
                return;
            }
            Item item = null;
            foreach (var carried in owner.Profile.Inventory.Equipment.GetAllItems())
            {
                if (carried.Id == parts[1])
                {
                    item = carried;
                    break;
                }
            }
            if (item == null && HideoutStash.Current != null)
            {
                // Building and crafting also use up what lies in the shared stash.
                foreach (var stored in HideoutStash.Current.GetAllItems())
                {
                    if (stored.Id == parts[1])
                    {
                        item = stored;
                        break;
                    }
                }
            }
            if (item == null)
            {
                Plugin.Log.LogWarning("Inventory sync: " + owner.Profile.Nickname + " has no item " + parts[1] + " here");
                return;
            }
            var controller = owner.InventoryController;
            try
            {
                if (parts[0] == "gone")
                {
                    var discard = ItemManipulator.Discard(item, controller, false);
                    if (discard.Failed)
                    {
                        Plugin.Log.LogWarning("Inventory sync: could not remove " + item.Id + ": " + discard.Error);
                        return;
                    }
                    discard.Value.RaiseEvents(controller, CommandStatus.Begin);
                    discard.Value.RaiseEvents(controller, CommandStatus.Succeed);
                }
                else if (parts[0] == "count" && parts.Length > 2)
                {
                    item.StackObjectsCount = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    item.RaiseRefreshEvent();
                }
                Plugin.Log.LogInfo("Inventory sync: " + owner.Profile.Nickname + ": " + parts[0] + " " + item.ShortName + (parts.Length > 2 ? " " + parts[2] : ""));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Inventory sync failed: " + e);
            }
        }
    }
}
