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
        private static WeaponModdingScreen.WeaponModdingScreenController _open;
        private static WeaponModdingScreen _screen;
        private static Weapon _weapon;
        private static int _shape;
        private static float _nextScreenLook;

        /// <summary>Which parts the weapon is made of right now, as one number.</summary>
        private static int Shape(Weapon weapon)
        {
            int shape = 17;
            foreach (var part in weapon.GetAllItems())
            {
                shape = unchecked(shape * 31 + part.Id.GetHashCode() + (part.Parent != null && part.Parent.Container != null ? part.Parent.Container.ID.GetHashCode() : 0));
            }
            return shape;
        }

        /// <summary>Inside the hideout, the in-raid rules on vital parts do not apply. Returns whether they were lifted.</summary>
        public static bool Lift()
        {
#pragma warning disable 618
            if (HideoutDoors.Inside == null || !InGameStatus.InRaid)
            {
                return false;
            }
            InGameStatus.InRaid = false;
            return true;
        }

        public static void Restore(bool lifted)
        {
            if (lifted)
            {
                InGameStatus.InRaid = true;
            }
#pragma warning restore 618
        }

        private static UnityEngine.Camera _world;
        private static int _worldMask;
        private static UnityEngine.CameraClearFlags _worldClear;
        private static UnityEngine.Color _worldColour;
        private static bool _menuRules;

        /// <summary>
        /// While the screen is open it behaves as it does in the menus: the in-raid rules are off
        /// (vital parts stay locked only on a weapon in the hands, as in the menus), and the world
        /// behind it is not drawn (in a raid it showed through upside down), only a dark backdrop.
        /// </summary>
        private static void MenuLike(bool on)
        {
#pragma warning disable 618
            if (on && !_menuRules && InGameStatus.InRaid)
            {
                InGameStatus.InRaid = false;
                _menuRules = true;
            }
            else if (!on && _menuRules)
            {
                InGameStatus.InRaid = true;
                _menuRules = false;
            }
#pragma warning restore 618
            try
            {
                if (on && _world == null && EFT.CameraControl.CameraManager.Instance != null)
                {
                    _world = EFT.CameraControl.CameraManager.Instance.Camera;
                    if (_world != null)
                    {
                        _worldMask = _world.cullingMask;
                        _worldClear = _world.clearFlags;
                        _worldColour = _world.backgroundColor;
                        _world.cullingMask = 0;
                        _world.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
                        _world.backgroundColor = new UnityEngine.Color(0.055f, 0.055f, 0.05f, 1f);
                    }
                }
                else if (!on && _world != null)
                {
                    _world.cullingMask = _worldMask;
                    _world.clearFlags = _worldClear;
                    _world.backgroundColor = _worldColour;
                    _world = null;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Modding backdrop: " + e.Message);
            }
        }

        /// <summary>
        /// Called every frame. The screen brings the menu's bottom bar with it (main menu, traders,
        /// flea market), which must not be used from a raid; it is kept hidden while the screen is
        /// open. When the screen closes, the stash is saved, since parts may have left it.
        /// </summary>
        public static void Tick()
        {
            if (_open == null)
            {
                return;
            }
            if (_open.Closed)
            {
                _open = null;
                _screen = null;
                _weapon = null;
                MenuLike(false);
                HideoutStash.Send("after modding a weapon");
                return;
            }
            // In a raid a change of part is carried out a moment after the screen has redrawn the
            // gun, so the screen showed each change one step late. Redraw once the change is in.
            if (_screen == null && UnityEngine.Time.unscaledTime >= _nextScreenLook)
            {
                _nextScreenLook = UnityEngine.Time.unscaledTime + 0.25f;
                _screen = UnityEngine.Object.FindObjectOfType<WeaponModdingScreen>();
            }
            if (_screen != null && _weapon != null)
            {
                int shape = Shape(_weapon);
                if (shape != _shape)
                {
                    _shape = shape;
                    try
                    {
                        _screen.WeaponUpdate();
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log.LogWarning("Redrawing the modding screen failed: " + e.Message);
                    }
                }
            }
            var preloader = MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance;
            if (preloader != null && preloader.MenuTaskBar != null && preloader.MenuTaskBar.gameObject.activeSelf)
            {
                preloader.SetMenuTaskBarVisibility(false);
            }
        }

        public static void Close()
        {
            if (_open != null && !_open.Closed)
            {
                _open.CloseScreen();
            }
            _open = null;
            MenuLike(false);
        }

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

        /// <summary>True if the part being moved belongs to, or goes onto, the weapon the player is holding.</summary>
        public static bool InHands(Item item, ItemAddress to)
        {
            var player = GamePlayerOwner.MyPlayer;
            var held = player != null && player.HandsController != null ? player.HandsController.Item : null;
            if (held == null)
            {
                return false;
            }
            if (item.GetAllParentItems().Contains(held))
            {
                return true;
            }
            var onto = to != null && to.Container != null ? to.Container.ParentItem : null;
            return onto != null && (onto == held || onto.GetAllParentItems().Contains(held));
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
                _open = new WeaponModdingScreen.WeaponModdingScreenController(weapon, player.InventoryController, collections.ToArray());
                _open.ShowScreen(EScreenState.Queued);
                _weapon = weapon;
                _shape = Shape(weapon);
                _screen = null;
                MenuLike(true);
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

namespace TheTrueSPT
{
    /// <summary>
    /// In a raid the game refuses to take off or put on a weapon's vital parts, and parts marked as
    /// not moddable in a raid. In the hideout it is the workshop the menus used to be, so those two
    /// rules are lifted while the player is inside.
    /// </summary>
    public class HideoutMovePathPatch : SPT.Reflection.Patching.ModulePatch
    {
        protected override System.Reflection.MethodBase GetTargetMethod()
        {
            return HarmonyLib.AccessTools.Method(typeof(EFT.InventoryLogic.ItemManipulator), nameof(EFT.InventoryLogic.ItemManipulator.MovePathCheck));
        }

        [SPT.Reflection.Patching.PatchPrefix]
        public static void Prefix(ref bool __state)
        {
            __state = HideoutGunsmith.Lift();
        }

        /// <summary>
        /// Runs after the other mods' checks. The weapon in the hands still keeps its vital parts:
        /// taking the receiver off a gun being held is not something the game can cope with.
        /// </summary>
        [SPT.Reflection.Patching.PatchPostfix]
        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.Last)]
        public static void Postfix(bool __state, EFT.InventoryLogic.Item item, EFT.InventoryLogic.ItemAddress to, ref Diz.LanguageExtensions.Option<Diz.LanguageExtensions.None> __result)
        {
            HideoutGunsmith.Restore(__state);
            if (HideoutDoors.Inside == null || __result.Failed)
            {
                return;
            }
            bool vital = (to is EFT.InventoryLogic.SlotItemAddress into && into.Slot.Required)
                         || (item.Parent is EFT.InventoryLogic.SlotItemAddress from && from.Slot.Required);
            if (vital && HideoutGunsmith.InHands(item, to))
            {
                __result = new Diz.LanguageExtensions.StringError("Put the weapon away first: its vital parts cannot be changed while it is in your hands.");
            }
        }
    }

    /// <summary>
    /// The UI Fixes mod keeps its own rule on modding in a raid. Inside the hideout it is told the
    /// player is not in a raid, as for the game's rule.
    /// </summary>
    public class UiFixesInRaidPatch : SPT.Reflection.Patching.ModulePatch
    {
        protected override System.Reflection.MethodBase GetTargetMethod()
        {
            var plugin = HarmonyLib.AccessTools.TypeByName("UIFixes.Plugin");
            return plugin != null ? HarmonyLib.AccessTools.Method(plugin, "InRaid") : null;
        }

        [SPT.Reflection.Patching.PatchPostfix]
        public static void Postfix(ref bool __result)
        {
            if (__result && HideoutDoors.Inside != null)
            {
                __result = false;
            }
        }
    }

    /// <summary>The same rule, as a part checks it for itself.</summary>
    public class HideoutModMovePatch : SPT.Reflection.Patching.ModulePatch
    {
        protected override System.Reflection.MethodBase GetTargetMethod()
        {
            return HarmonyLib.AccessTools.Method(typeof(EFT.InventoryLogic.Mod), nameof(EFT.InventoryLogic.Mod.CanBeMoved));
        }

        [SPT.Reflection.Patching.PatchPrefix]
        public static void Prefix(ref bool __state)
        {
            __state = HideoutGunsmith.Lift();
        }

        [SPT.Reflection.Patching.PatchPostfix]
        public static void Postfix(bool __state)
        {
            HideoutGunsmith.Restore(__state);
        }
    }
}
