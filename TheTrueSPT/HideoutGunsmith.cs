using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.Screens;
using EFT.UI.WeaponModding;

namespace TheTrueSPT
{
    /// <summary>
    /// The game's own weapon modding screen, opened from the Workbench. The game allows it only in
    /// its menus; in the hideout it is the same screen, with parts from what the player carries and
    /// from the hideout's stash.
    /// </summary>
    public static class HideoutGunsmith
    {
        /// <summary>The weapons carried, at any depth, then those in the hideout's stash.</summary>
        public static List<Weapon> Weapons(Player player)
        {
            var found = new List<Weapon>();
            var equipment = player.Profile.Inventory.Equipment;
            foreach (var item in equipment.GetAllItems())
            {
                var weapon = item as Weapon;
                if (weapon != null && !(weapon.Parent?.Container?.ParentItem is Weapon))
                {
                    found.Add(weapon);
                }
            }
            foreach (var item in HideoutStash.Items())
            {
                var weapon = item as Weapon;
                if (weapon != null && !(weapon.Parent?.Container?.ParentItem is Weapon))
                {
                    found.Add(weapon);
                }
            }
            return found;
        }

        public static bool InStash(Weapon weapon)
        {
            var stash = HideoutStash.Current;
            return stash != null && weapon.GetAllParentItems().Contains(stash);
        }

        public static string Open(Weapon weapon)
        {
            var player = GamePlayerOwner.MyPlayer;
            if (player == null || weapon == null)
            {
                return "not possible right now";
            }
            try
            {
                var collections = player.Profile.Inventory.Equipment.GetCollections(InventoryEquipment.AllSlotNames).ToList();
                if (HideoutStash.Current != null)
                {
                    collections.Add(HideoutStash.Current);
                }
                new WeaponModdingScreen.WeaponModdingScreenController(weapon, player.InventoryController, collections.ToArray()).ShowScreen(EScreenState.Queued);
                Plugin.Log.LogInfo("Weapon modding opened for " + weapon.ShortName.Localized() + (InStash(weapon) ? " (in the stash)" : " (carried)"));
                return null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Opening weapon modding failed: " + e);
                return "The modding screen could not be opened: " + e.Message;
            }
        }
    }
}
