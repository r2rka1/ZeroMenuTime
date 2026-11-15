using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheTrueSPT
{
    [BepInPlugin("com.sandbox.thetruespt", "The True SPT", "0.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<bool> ShowMarkers;
        public static ConfigEntry<KeyboardShortcut> CaptureKey;

        /// <summary>The player's stash, saved before the raid loader clears it.</summary>
        public static Stash SavedStash;

        public static Plugin Instance;
        public static ConfigEntry<bool> UseCustomSpots;

        /// <summary>Show a message after a delay, and keep it on screen longer than the default.</summary>
        public static System.Collections.IEnumerator NotifyLater(string message, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);
            NotificationManager.DisplayMessageNotification(message, ENotificationDurationType.Long);
        }

        /// <summary>
        /// The mod used to be called TradersInRaid. Settings and data files saved under that name are
        /// copied to the new one, once, where the new one does not exist yet. Nothing old is removed.
        /// </summary>
        private void CarryOverOldName()
        {
            try
            {
                string oldSettings = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "com.sandbox.tradersinraid.cfg");
                if (System.IO.File.Exists(oldSettings) && !System.IO.File.Exists(Config.ConfigFilePath))
                {
                    System.IO.File.Copy(oldSettings, Config.ConfigFilePath);
                    Config.Reload();
                    Log.LogInfo("Settings carried over from " + oldSettings);
                }
                string oldData = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "TradersInRaid");
                if (System.IO.Directory.Exists(oldData))
                {
                    System.IO.Directory.CreateDirectory(TraderConfig.Dir);
                    int copied = 0;
                    foreach (string file in System.IO.Directory.GetFiles(oldData))
                    {
                        string name = System.IO.Path.GetFileName(file);
                        string target = System.IO.Path.Combine(TraderConfig.Dir, name);
                        if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && !System.IO.File.Exists(target))
                        {
                            System.IO.File.Copy(file, target);
                            copied++;
                        }
                    }
                    if (copied > 0)
                    {
                        Log.LogInfo("Carried " + copied + " data file(s) over from " + oldData);
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("Carrying over from the old name failed: " + e.Message);
            }
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            CarryOverOldName();
            ShowMarkers = Config.Bind("General", "Show trader markers", true,
                "Show a visible box where each trader stands. Turn off once real props are placed.");
            CaptureKey = Config.Bind("General", "Capture position key", new KeyboardShortcut(KeyCode.F10),
                "In raid: writes your current map and position to captured_positions.txt so a trader can be placed there.");

            HideoutStations.Free = Config.Bind("Testing", "Free hideout", false,
                "In a hideout: building, upgrading and crafting need no materials and no tools. For testing only; turn off for real play.");
            Undying = Config.Bind("Testing", "Undying", false,
                "In raid: heals you fully and switches off all damage to you. For testing only; turn off for real play.");
            SelfTestKey = Config.Bind("Testing", "Self-test key", new KeyboardShortcut(KeyCode.F8),
                "In raid: runs the inventory steps a trade is made of and reports which worked. For testing only.");
            UseCustomSpots = Config.Bind("General", "Also use traders.json spots", false,
                "When Vagabond is installed traders stand at Vagabond's trader extracts. Turn this on to add the spots from traders.json as well.");

            Seamless.Bind(Config);
            Death.Bind(Config);
            TraderModelBundle.Bind(Config);
            Metabolism.Bind(Config);
            TraderRoom.Bind(Config);
            RoomProps.Bind(Config);
            Photo.Bind(Config);
            TestHarness.Bind(Config);
            ShowFigures = Config.Bind("General", "Show trader figures", true,
                "Stand a character at each trader spot. The marker box is hidden once the figure has loaded.");
            _patches = new List<ModulePatch>
            {
                new SaveStashPatch(), new SpawnTradersPatch(), new TraderActionsPatch(),
                new TraderExfilEnterPatch(), new TraderExfilExitPatch(),
                new LocalPurchasePatch(), new LocalSellPatch(), new CarriedPaymentPatch(), new DrawPackItemsPatch(),
                new RaidStopPatch(), new SkipResultsPatch(), new MenuShownPatch(), new HideoutLeftPatch(), new QuestHandInNoticePatch(),
                new EnergyDrainPatch(), new HydrationDrainPatch(),
                new RoomIndoorPatch(), new RoomIndoorBasePatch(),
                new BunkerLevelSettingsAwakePatch(), new BunkerLevelSettingsDestroyPatch(), new BunkerEnvironmentDestroyPatch(), new MainMenuPatch(), new RangeBestScorePatch(), new HideoutMovePathPatch(), new HideoutModMovePatch(), new UiFixesInRaidPatch(),
            };
            foreach (var patch in _patches)
            {
                patch.Enable();
            }
            Log.LogInfo("The True SPT loaded (" + BuildStamp + ")");

            // Reloaded while the main menu is up: arrange it again.
            try
            {
                MainMenu.Arrange(FindObjectOfType<MenuScreen>());
            }
            catch (Exception e)
            {
                Log.LogWarning("Menu not arranged on load: " + e.Message);
            }

            // Reloaded in the middle of a raid (ScriptEngine): put the traders back straight away.
            try
            {
                if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance.MainPlayer != null)
                {
                    // An earlier copy of this plugin may have switched the sky off for a room or the hideout.
                    IndoorLight.Repair();
                    SpawnTradersPatch.Postfix(Singleton<GameWorld>.Instance);
                    NotificationManager.DisplayMessageNotification("The True SPT reloaded (" + BuildStamp + ")", ENotificationDurationType.Long);
                }
            }
            catch (Exception e)
            {
                Log.LogError("Respawn after reload failed: " + e);
            }
        }

        /// <summary>Shown in the log and on reload so it is clear which build is running.</summary>
        public static readonly string BuildStamp =
            "built " + (typeof(Plugin).Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false).Cast<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildTime")?.Value ?? "?");

        public static ConfigEntry<bool> ShowFigures;
        public static ConfigEntry<bool> Undying;
        private Player _undyingAppliedTo;
        private bool _undyingState;

        /// <summary>
        /// Testing aid: uses the game's own damage multiplier to make the player take no damage,
        /// and heals fully once when it switches on. Applied once per raid and whenever the setting changes.
        /// </summary>
        private void ApplyUndying()
        {
            if (Undying == null)
            {
                return;
            }
            var player = GamePlayerOwner.MyPlayer;
            if (player == null || player.ActiveHealthController == null)
            {
                _undyingAppliedTo = null;
                return;
            }
            if (ReferenceEquals(_undyingAppliedTo, player) && _undyingState == Undying.Value)
            {
                return;
            }
            try
            {
                var health = player.ActiveHealthController;
                if (Undying.Value)
                {
                    health.RestoreFullHealth();
                    health.SetDamageCoeff(0f);
                }
                else
                {
                    health.SetDamageCoeff(1f);
                }
                _undyingAppliedTo = player;
                _undyingState = Undying.Value;
                Log.LogInfo("Undying " + (Undying.Value ? "on (healed, damage off)" : "off"));
                NotificationManager.DisplayMessageNotification("Undying: " + (Undying.Value ? "ON" : "off"));
            }
            catch (Exception e)
            {
                _undyingAppliedTo = player;
                _undyingState = Undying.Value;
                Log.LogError("Undying failed: " + e);
            }
        }

        private List<ModulePatch> _patches;
        public static ConfigEntry<KeyboardShortcut> SelfTestKey;

        /// <summary>Undo everything, so a reload does not leave old patches or trader boxes behind.</summary>
        private void OnDestroy()
        {
            IndoorLight.Restore();
            HideoutPanel.Close();
            Photo.Restore();
            HideoutGunsmith.Close();
            try
            {
                LocalTrade.Active = false;
                if (_patches != null)
                {
                    foreach (var patch in _patches)
                    {
                        patch.Disable();
                    }
                }
                TraderInteractable.DestroyAll();
                HideoutDoors.Close();
                RoomBundle.Unload();
                Log.LogInfo("The True SPT unloaded");
            }
            catch (Exception e)
            {
                Log.LogError("Unload failed: " + e);
            }
        }

        /// <summary>The black screen while the player steps through a room door.</summary>
        private void OnGUI()
        {
            if (RoomDoor.Fade <= 0f)
            {
                return;
            }
            var before = GUI.color;
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(RoomDoor.Fade));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            if (!string.IsNullOrEmpty(RoomDoor.FadeText))
            {
                GUI.color = new Color(0.8f, 0.8f, 0.8f, Mathf.Clamp01(RoomDoor.Fade));
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(18, Screen.height / 40) };
                GUI.Label(new Rect(0f, Screen.height * 0.45f, Screen.width, Screen.height * 0.1f), RoomDoor.FadeText, style);
            }
            GUI.color = before;
        }

        private void Update()
        {
            if (SelfTestKey != null && SelfTestKey.Value.IsDown() && GamePlayerOwner.MyPlayer != null)
            {
                SelfTest.Run();
            }
            if (Input.GetKeyDown(KeyCode.F7) && GamePlayerOwner.MyPlayer != null)
            {
                InteractionProbe.Run();
            }
            ApplyUndying();
            TestHarness.Tick();
            RewardTable.Tick();
            WorldClock.Tick();
            HideoutBoosts.Tick();
            MainMenu.Tick();
            HideoutPanel.Tick();
            IndoorLight.Tick();
            MapMarkers.Tick();
            HideoutPower.Tick();
            BotFreeze.Tick();
            Photo.Tick();
            HideoutGunsmith.Tick();
            if (!CaptureKey.Value.IsDown())
            {
                return;
            }
            var player = GamePlayerOwner.MyPlayer;
            if (player == null)
            {
                return;
            }
            try
            {
                var p = player.Transform.position;
                var rot = player.Transform.rotation.eulerAngles.y;
                var line = string.Format(CultureInfo.InvariantCulture,
                    "{{ \"Name\": \"\", \"TraderId\": \"\", \"Maps\": [\"{0}\"], \"Position\": [{1:F2}, {2:F2}, {3:F2}], \"RotationY\": {4:F0}, \"Enabled\": true }}",
                    player.Location, p.x, p.y, p.z, rot);
                Directory.CreateDirectory(TraderConfig.Dir);
                File.AppendAllText(TraderConfig.CapturePath, line + Environment.NewLine);
                NotificationManager.DisplayMessageNotification("Position saved: " + player.Location + " " + p.ToString("F1"), ENotificationDurationType.Long);
                Log.LogInfo("Captured " + line);
            }
            catch (Exception e)
            {
                Log.LogError("Capture failed: " + e);
            }
        }
    }

    /// <summary>Opens the normal trader screen while in a raid.</summary>
    public static class TradeOpener
    {
        public static void Open(string traderId, string traderName)
        {
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                var session = ClientAppUtils.GetClientApp().Session;
                if (player == null || session == null)
                {
                    Plugin.Log.LogWarning("No player or session; cannot open trader");
                    return;
                }

                Profile.TraderInfo info;
                if (!player.Profile.TradersInfo.TryGetValue(traderId, out info) || !info.Unlocked)
                {
                    NotificationManager.DisplayWarningNotification(traderName + " will not deal with you yet.");
                    return;
                }

                Trader trader = session.GetTrader(traderId);
                if (trader == null)
                {
                    Plugin.Log.LogWarning("Trader not found in session: " + traderId);
                    return;
                }

                // The trade screen needs a stash to show. Lend it for as long as the screen is open,
                // and take it away again so the end-of-raid save never contains stash items.
                // In a raid the "stash" the screen shows is the player's backpack, and trades are
                // settled locally on carried items (see LocalTrade).
                var inventory = player.Profile.Inventory;
                bool lentStash = false;
                if (inventory.Stash == null)
                {
                    var packStash = LocalTrade.BuildPackStash(player);
                    inventory.Stash = packStash;
                    lentStash = true;
                }
                LocalTrade.Active = true;
                // Vagabond marks traders unavailable unless you extracted to them, and closes the trade
                // screen for unavailable traders. Standing at the trader counts: mark available while open.
                bool wasDisabled = false;
                if (trader.Info != null)
                {
                    wasDisabled = trader.Info.Disabled;
                    trader.Info.Disabled = false;
                }
                Plugin.Log.LogInfo("Opening " + traderName + " (stash lent: " + lentStash + ", stash present: " + (inventory.Stash != null) + ", was disabled: " + wasDisabled + ", available now: " + (trader.Info != null && trader.Info.Available) + ")");

                var controller = new TraderScreensGroup.TraderScreenController(
                    trader,
                    new[] { trader },
                    player.Profile,
                    player.InventoryController,
                    player.HealthController,
                    player.QuestController,
                    player.AchievementsController,
                    session);
                controller.OnClose += () =>
                {
                    LocalTrade.Active = false;
                    LocalTrade.ForgetPackView();
                    if (lentStash)
                    {
                        inventory.Stash = null;
                    }
                    if (trader.Info != null && wasDisabled)
                    {
                        trader.Info.Disabled = true;
                    }
                    Plugin.Log.LogInfo("Closed " + traderName);
                    QuestSync.Send("trader closed");
                };
                controller.ShowScreen(EScreenState.Queued);
            }
            catch (Exception e)
            {
                LocalTrade.Active = false;
                Plugin.Log.LogError("Opening trader failed: " + e);
                NotificationManager.DisplayWarningNotification("TheTrueSPT: could not open trader, see log.");
            }
        }
    }

    /// <summary>Remember the stash before the raid loader sets it to null.</summary>
    public class SaveStashPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.LocalGameCreate));
        }

        [PatchPrefix]
        public static void Prefix()
        {
            try
            {
                var stash = ClientAppUtils.GetClientApp().Session?.Profile?.Inventory?.Stash;
                if (stash != null)
                {
                    Plugin.SavedStash = stash;
                }
                Plugin.Log.LogInfo("Raid loading; stash saved: " + (Plugin.SavedStash != null));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("SaveStashPatch: " + e);
            }
        }
    }

    /// <summary>When the raid starts: make the trade screen available and put the traders on the map.</summary>
    public class SpawnTradersPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
        }

        [PatchPostfix]
        public static void Postfix(GameWorld __instance)
        {
            try
            {
                var player = __instance.MainPlayer;
                if (player == null)
                {
                    return;
                }

                // The trade screen lives in the menu UI scene, which the game unloads during raids.
                if (!MonoBehaviourSingleton<MenuUI>.Instantiated)
                {
                    var key = Scenes.MenuUIScene;
                    EFT.Assets.Manager.LoadScene(key.path, key.rcid, LoadSceneMode.Additive);
                    Plugin.Log.LogInfo("Menu UI scene requested for in-raid trading");
                }

                string map = player.Location ?? "";
                int count = 0;

                // Traders stand at Vagabond's trader extracts when Vagabond is installed.
                TraderInteractable.DestroyAll();
                VagabondLink.Reset();
                var extracts = VagabondLink.LoadTraderExtracts();
                foreach (var extract in extracts)
                {
                    var exfil = VagabondLink.FindExfil(__instance, extract.ExfilIdentifier);
                    // Vagabond lists Factory day and night separately for the same extract; place once.
                    if (exfil == null || VagabondLink.TraderExfils.Contains(exfil.GetInstanceID()))
                    {
                        continue;
                    }
                    string name = TraderConfig.NameOf(extract.TraderId);
                    float facing = exfil.transform.rotation.eulerAngles.y;
                    // In the open the trader stands on the extract; with rooms, a door stands there
                    // and the trader waits inside.
                    var ground = exfil.transform.position;
                    if (TraderRoom.Enabled.Value)
                    {
                        var room = TraderRoom.Build(extract.TraderId, name, exfil.transform.position);
                        // The door goes where doors.json says, else against the nearest wall, else on the spot.
                        var doorGround = exfil.transform.position;
                        float doorFacing = facing;
                        var chosen = TraderConfig.DoorFor(extract.TraderId, map);
                        string how = "on the trader's spot";
                        if (chosen != null)
                        {
                            doorGround = new Vector3(chosen.Position[0], chosen.Position[1], chosen.Position[2]);
                            doorFacing = chosen.RotationY;
                            how = "where it was placed by hand";
                            if (!chosen.Hidden || chosen.AtFeet)
                            {
                                RoomDoor.BackToWall(ref doorGround, ref doorFacing);
                            }
                        }
                        else if (RoomDoor.FindWall(exfil.transform.position, out doorGround, out doorFacing))
                        {
                            how = "against the nearest wall";
                        }
                        Plugin.Log.LogInfo("Door for " + name + " " + how + ": " + doorGround.ToString("F1") + " facing " + doorFacing.ToString("F0"));
                        RoomDoor door;
                        if (chosen != null && chosen.Hidden)
                        {
                            // Laid over a door that is already part of the map, and not drawn.
                            door = RoomDoor.Spawn("Enter " + name + "'s room", doorGround + Vector3.up * (RoomDoor.Height / 2f)
                                + Quaternion.Euler(0f, doorFacing, 0f) * new Vector3(0f, 0f, 0.08f), doorFacing);
                            var drawn = door.GetComponent<MeshRenderer>();
                            if (drawn != null)
                            {
                                drawn.enabled = false;
                            }
                        }
                        else
                        {
                            door = RoomDoor.SpawnOutside("Enter " + name + "'s room", doorGround, doorFacing);
                        }
                        room.OutsideDoor = door;
                        door.Destination = room.Entry;
                        door.DestinationYaw = room.EntryYaw;
                        // Coming back out, the player stands just in front of the door, facing away from it.
                        room.ExitDoor.Destination = doorGround + Quaternion.Euler(0f, doorFacing, 0f) * new Vector3(0f, 0.05f, 1.0f);
                        room.ExitDoor.DestinationYaw = doorFacing;
                        ground = room.TraderStand;
                        facing = room.TraderYaw;
                    }
                    var at = ground + new Vector3(0f, 0.9f, 0f);
                    var trader = TraderInteractable.Spawn(name, extract.TraderId, at, facing, Plugin.ShowMarkers.Value);
                    trader.Exfil = exfil;
                    if (Plugin.ShowFigures.Value)
                    {
                        TraderFigure.Spawn(extract.TraderId, name, ground, facing, trader.gameObject);
                    }
                    VagabondLink.TraderExfils.Add(exfil.GetInstanceID());
                    count++;
                    Plugin.Log.LogInfo("Placed " + name + " at Vagabond extract " + extract.ExfilIdentifier + " " + at.ToString("F1"));
                }

                bool useFile = extracts.Count == 0 || Plugin.UseCustomSpots.Value;
                foreach (var spot in useFile ? TraderConfig.Load() : new List<TraderSpot>())
                {
                    if (!spot.Enabled || spot.Position == null || spot.Position.Length < 3)
                    {
                        continue;
                    }
                    if (!spot.Maps.Any(m => string.Equals(m, map, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    TraderInteractable.Spawn(spot.Name, spot.TraderId,
                        new Vector3(spot.Position[0], spot.Position[1], spot.Position[2]), spot.RotationY, Plugin.ShowMarkers.Value);
                    count++;
                    Plugin.Log.LogInfo("Placed " + spot.Name + " on " + map + " at " + string.Join(", ", spot.Position));
                }
                int hideoutDoors = HideoutDoors.Spawn(map);
                WorldClock.Begin();
                TraderRoom.ExtendPhysics();
                Plugin.Log.LogInfo("Raid started on '" + map + "', traders placed: " + count);
                QuestSync.Send("raid start");
                if (count > 0 && Plugin.Instance != null)
                {
                    Plugin.Instance.StartCoroutine(Plugin.NotifyLater("Traders in this area: " + count, 12f));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("SpawnTradersPatch: " + e);
            }
        }
    }

    /// <summary>Give our trader spots a "Trade with ..." action when the player looks at them.</summary>
    public class TraderActionsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableActions),
                new[] { typeof(GamePlayerOwner), typeof(IInteractive) });
        }

        [PatchPrefix]
        public static bool Prefix(object interactive, ref AvailableInteractionState __result)
        {
            if (interactive is TraderInteractable trader)
            {
                __result = trader.GetActions();
                return false;
            }
            if (interactive is RoomDoor door)
            {
                __result = door.GetActions();
                return false;
            }
            var rangeActions = HideoutRange.ActionsFor(interactive);
            if (rangeActions != null)
            {
                __result = rangeActions;
                return false;
            }
            var stationActions = HideoutStations.ActionsForObject(interactive);
            if (stationActions != null)
            {
                __result = stationActions;
                return false;
            }
            return true;
        }
    }
}
