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

namespace ZeroMenuTime
{
    [BepInPlugin("com.r2rka1.zeromenutime", "r2rka1-ZeroMenuTime", "1.2.4")]
    [BepInDependency("dev.oogabooga.spt-vagabond", BepInDependency.DependencyFlags.SoftDependency)]
    // Loaded after UI Fixes, so its in-raid modding rule can be lifted inside the hideout.
    [BepInDependency("com.tyfon.uifixes", BepInDependency.DependencyFlags.SoftDependency)]
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

        private void Awake()
        {
            Instance = this;
            Log = Logger;
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
            Guide.Bind(Config);
            VagabondLink.RemoveHideoutExits = Config.Bind("General", "Remove the hideout exits of Vagabond", true,
                "Vagabond's own exits to its hideout serve no purpose with this mod's hideout. On: they are switched off in the raid.");
            Metabolism.Bind(Config);
            TraderRoom.Bind(Config);
            RoomProps.Bind(Config);
            Photo.Bind(Config);
            TestHarness.Bind(Config);
            ShowFigures = Config.Bind("General", "Show trader figures", true,
                "Stand a character at each trader spot. The marker box is hidden once the figure has loaded.");
            _patches = new List<ModulePatch>
            {
                new SaveStashPatch(), new SpawnTradersPatch(), new TraderActionsPatch(), new DoorLookPatch(),
                new TraderExfilEnterPatch(), new TraderExfilExitPatch(),
                new LocalPurchasePatch(), new LocalSellPatch(), new CarriedPaymentPatch(), new DrawPackItemsPatch(),
                new RaidStopPatch(), new SkipResultsPatch(), new MenuShownPatch(), new HideoutLeftPatch(), new QuestHandInNoticePatch(), new QuestRewardItemsPatch(), new IndoorAmbientPatch(), new IndoorReflectionPatch(), new QuestStartItemsPatch(), new RepairMenuPatch(),
                new EnergyDrainPatch(), new HydrationDrainPatch(),
                new RoomIndoorPatch(), new RoomIndoorBasePatch(),
                new BunkerLevelSettingsAwakePatch(), new BunkerLevelSettingsDestroyPatch(), new BunkerEnvironmentDestroyPatch(), new MainMenuPatch(), new RangeBestScorePatch(), new HideoutMovePathPatch(), new HideoutModMovePatch(), new UiFixesInRaidPatch(),
            };
            foreach (var patch in _patches.ToArray())
            {
                // One patch that cannot be applied (another mod missing or changed) must not stop the rest.
                try
                {
                    patch.Enable();
                }
                catch (Exception e)
                {
                    _patches.Remove(patch);
                    Log.LogWarning(patch.GetType().Name + " not applied: " + e.Message);
                }
            }
            Log.LogInfo("Zero Menu Time loaded (" + BuildStamp + ")");

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
                    NotificationManager.DisplayMessageNotification("Zero Menu Time reloaded (" + BuildStamp + ")", ENotificationDurationType.Long);
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
                Log.LogInfo("Zero Menu Time unloaded");
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
            FallGuard.Tick();
            HideoutSound.Tick();
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
        /// <summary>The trade screen that is open now, if any.</summary>
        public static TraderScreensGroup.TraderScreenController Screen;

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
                    Screen = null;
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
                Screen = controller;
                controller.ShowScreen(EScreenState.Queued);
                TraderModelBundle.Speak(traderId, player.Position);
            }
            catch (Exception e)
            {
                LocalTrade.Active = false;
                Plugin.Log.LogError("Opening trader failed: " + e);
                NotificationManager.DisplayWarningNotification("ZeroMenuTime: could not open trader, see log.");
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
                Repairs.EnablePatches();
                var extracts = VagabondLink.LoadTraderExtracts();
                VagabondLink.SwitchOffHideoutExits(__instance);
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
                        var outside = doorGround + Quaternion.Euler(0f, doorFacing, 0f) * new Vector3(0f, 0.05f, 1.0f);
                        float outsideYaw = doorFacing;
                        // The trader's spot is where the player is meant to stand, so it is outside. When
                        // it lies close by on the other side of the door from the way the door was
                        // recorded as facing, that record is the wrong way round: come out on the
                        // spot's side (Jaeger's bunker door and Prapor's office door were such).
                        var spot = exfil.transform.position;
                        var behind = doorGround + Quaternion.Euler(0f, doorFacing + 180f, 0f) * new Vector3(0f, 0.05f, 1.0f);
                        float toFront = Vector2.Distance(new Vector2(outside.x, outside.z), new Vector2(spot.x, spot.z));
                        float toBehind = Vector2.Distance(new Vector2(behind.x, behind.z), new Vector2(spot.x, spot.z));
                        if (Mathf.Abs(spot.y - doorGround.y) < 2.5f && toBehind < 6f && toBehind + 0.5f < toFront)
                        {
                            outside = behind;
                            outsideYaw = doorFacing + 180f;
                            Plugin.Log.LogInfo("Door for " + name + ": the way out is on the trader's side, facing " + (outsideYaw % 360f).ToString("F0"));
                        }
                        RoomDoor.KeepClear(ref outside);
                        room.ExitDoor.Destination = outside;
                        room.ExitDoor.DestinationYaw = outsideYaw;
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
                Death.SendMode();
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

    /// <summary>
    /// Our doors are laid over the map's own doors, and the game takes the first interactive thing
    /// its look ray meets: the map's door leaf, a door left open, or the frame in front could take
    /// the place of ours, and from the far side ours could not be met at all. After the game's own
    /// look, a door of ours in reach along the same ray, with nothing solid in between, is chosen
    /// over the map's door there and over anything farther away.
    /// </summary>
    public class DoorLookPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.FindInteractable));
        }

        [PatchPostfix]
        public static void Postfix(Ray ray, ref RaycastHit hit, ref GameObject __result)
        {
            try
            {
                if (RoomDoor.All.Count == 0)
                {
                    return;
                }
                var settings = EFTHardSettings.Instance;
                float reach = settings != null ? settings.LOOT_RAYCAST_DISTANCE + settings.BEHIND_CAST : 1.5f;
                float distance;
                var door = RoomDoor.Seen(ray, reach, out distance);
                if (door == null)
                {
                    return;
                }
                if (__result != null)
                {
                    var other = __result.GetComponentInParent<InteractableObject>();
                    if (other == door)
                    {
                        return;
                    }
                    // Loot, a body or a trader in front of the door keeps its place. The map's own
                    // door at ours does not, nor a hideout station's box, which can reach through a
                    // wall up to a door (the cultists' circle does, at the way out).
                    bool mapDoorHere = other is WorldInteractiveObject && Vector3.Distance(other.transform.position, door.transform.position) < 2.0f;
                    if (!mapDoorHere && !(other is StationInteractable) && hit.distance < distance - 0.05f)
                    {
                        return;
                    }
                }
                __result = door.gameObject;
                hit.distance = distance;
                hit.point = ray.GetPoint(distance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("DoorLookPatch: " + e.Message);
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
