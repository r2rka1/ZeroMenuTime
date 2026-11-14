using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Configuration;
using EFT;
using EFT.InventoryLogic;
using Diz.Jobs;
using EFT.UI;
using EFT.Trading;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// Testing aid: lets the running game be driven and inspected from outside, without anyone at the
    /// keyboard. Lines written to test/commands.txt next to the plugin's settings are carried out in
    /// order, and what happened is appended to test/results.log. Off unless switched on.
    ///
    /// Commands: status | check | enter NAME | leave | goto X Y Z | look YAW PITCH | wait SECONDS |
    /// from the menu: menu | continue | newgame | startraid | screenshot NAME; in a raid: save | undying on|off | die | recipes [STATION] | craft RECIPE_ID | collect STATION | clock [add HOURS] | vitals [hurt N] | sleep HOURS | use | target | stash [open|put TPL N|take TPL|save] | stations | build NUMBER | give TEMPLATE COUNT | hideout enter ID|leave | doors [R] | scenery [R] [WORD] | shot X Y Z NAME | aim | post ROUTE JSON | bunker load|where|move X Y Z|settings bunker|settings map|unload | lookat [NAME] | style NAME | savequit | screenshot NAME | lay TEMPLATE_ID [COUNT] | deliveries | table | quests | handin [QUEST_ID]
    /// </summary>
    public static class TestHarness
    {
        public static ConfigEntry<bool> Enabled;

        public static string Dir => Path.Combine(TraderConfig.Dir, "test");
        private static string CommandPath => Path.Combine(Dir, "commands.txt");
        private static string ResultPath => Path.Combine(Dir, "results.log");

        private static float _nextPoll;
        private static bool _running;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Testing", "Test harness", false,
                "Carry out commands written to the test folder and report the results there. For testing only.");
        }

        public static void Tick()
        {
            if (Enabled == null || !Enabled.Value || _running || Time.unscaledTime < _nextPoll)
            {
                return;
            }
            _nextPoll = Time.unscaledTime + 0.5f;
            try
            {
                if (!File.Exists(CommandPath))
                {
                    return;
                }
                var lines = File.ReadAllLines(CommandPath);
                File.Delete(CommandPath);
                if (Plugin.Instance != null)
                {
                    _running = true;
                    Plugin.Instance.StartCoroutine(RunAll(lines));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Test harness could not read commands: " + e.Message);
            }
        }

        public static void Report(string text)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(ResultPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine);
            }
            catch
            {
                // Reporting must never break the game.
            }
            Plugin.Log.LogInfo("[test] " + text);
        }

        private static IEnumerator RunAll(string[] lines)
        {
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }
                Report("> " + line);
                IEnumerator step = null;
                try
                {
                    step = Run(line);
                }
                catch (Exception e)
                {
                    Report("FAILED " + e.Message);
                }
                while (step != null)
                {
                    bool more;
                    try
                    {
                        more = step.MoveNext();
                    }
                    catch (Exception e)
                    {
                        Report("FAILED " + e);
                        break;
                    }
                    if (!more)
                    {
                        break;
                    }
                    yield return step.Current;
                }
            }
            Report("done");
            _running = false;
        }

        private static float Number(string text)
        {
            return float.Parse(text, CultureInfo.InvariantCulture);
        }

        private static IEnumerator Run(string line)
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts[0].ToLowerInvariant();
            var player = GamePlayerOwner.MyPlayer;
            if (player == null && verb == "menu")
            {
                // What the main menu offers right now.
                var screen = UnityEngine.Object.FindObjectOfType<MenuScreen>();
                if (screen == null)
                {
                    Report("the main menu is not showing");
                    yield break;
                }
                var shown = MainMenu.Items();
                foreach (var entry in MenuBackdrop.Describe())
                {
                    Report("  " + entry);
                }
                foreach (var button in screen.GetComponentsInChildren<DefaultUIButton>(false))
                {
                    var label = button.GetComponentInChildren<TMPro.TMP_Text>();
                    shown.Add(label != null ? label.text : button.name);
                }
                Report("menu buttons showing: " + string.Join(" | ", shown.ToArray()));
                var bars = new List<string>();
                foreach (var bar in UnityEngine.Object.FindObjectsOfType<MenuTaskBar>())
                {
                    foreach (var pair in bar._toggleButtons)
                    {
                        if (pair.Value != null && pair.Value.gameObject.activeInHierarchy)
                        {
                            bars.Add(pair.Key.ToString());
                        }
                    }
                }
                Report("bottom bar showing: " + string.Join(", ", bars.ToArray()));
                yield break;
            }
            if (player == null && verb == "press")
            {
                // press NAME: click one of the menu's own items.
                Report(MainMenu.Press(parts.Length > 1 ? parts[1] : "") ? "pressed " + parts[1] : "FAILED no such menu item");
                yield break;
            }
            if (player == null && verb == "backdrop")
            {
                // backdrop X Y Z YAW PITCH: hold the menu's view at one spot; "backdrop free" lets it move again.
                if (parts.Length > 2 && parts[1] == "lamp")
                {
                    // backdrop lamp POWER [RANGE]: brightness of the lamp that travels with the view.
                    foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
                    {
                        if (light.name == "lamp" && light.transform.parent != null && light.transform.parent.name.StartsWith("TradersInRaid menu"))
                        {
                            MenuBackdrop.LampPower = Number(parts[2]);
                            if (parts.Length > 3)
                            {
                                light.range = Number(parts[3]);
                            }
                        }
                    }
                    Report("lamp set");
                }
                else if (parts.Length < 6)
                {
                    MenuBackdrop.Fixed = null;
                    Report("backdrop moving freely");
                }
                else
                {
                    MenuBackdrop.Fixed = new MenuBackdrop.Framing { Position = new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])), Yaw = Number(parts[4]), Pitch = Number(parts[5]) };
                    Report("backdrop held");
                }
                yield break;
            }
            if (player == null && verb == "continue")
            {
                MainMenu.Continue();
                Report("Continue pressed");
                yield break;
            }
            if (player == null && verb == "newgame")
            {
                // What confirming New Game does. Replaces the character: only on a backed-up profile.
                Report("New Game confirmed");
                yield return MainMenu.NewGame();
                yield break;
            }
            if (player == null && verb == "screenshot")
            {
                yield return new WaitForEndOfFrame();
                Screenshot(parts.Length > 1 ? parts[1] : "shot");
                yield break;
            }
            if (verb == "startraid")
            {
                // From the main menu: start a raid where the character is, as the no-menu flow does.
                if (player != null)
                {
                    Report("already in a raid");
                    yield break;
                }
                Report("starting a raid from the menu");
                yield return Seamless.AfterMenuShown();
                yield break;
            }
            if (player == null && verb != "wait")
            {
                Report("not in a raid");
                yield break;
            }

            switch (verb)
            {
                case "status":
                    Status(player);
                    break;
                case "check":
                    Check(player);
                    break;
                case "wait":
                    yield return new WaitForSecondsRealtime(Number(parts[1]));
                    break;
                case "goto":
                    player.Teleport(new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])), false);
                    yield return new WaitForSecondsRealtime(0.5f);
                    Status(player);
                    break;
                case "style":
                    // Rebuilds the rooms in another trader's style (or "none" for their own).
                    TraderRoom.LookOverride = parts.Length < 2 || parts[1].ToLowerInvariant() == "none" ? null : parts[1];
                    SpawnTradersPatch.Postfix(Comfort.Common.Singleton<GameWorld>.Instance);
                    yield return new WaitForSecondsRealtime(1.5f);
                    Report("rooms rebuilt in style " + (TraderRoom.LookOverride ?? "their own"));
                    break;
                case "savequit":
                {
                    // Chooses "Save and go to menu" at the trader of the room the player is in.
                    var here = RoomHolding(player.Transform.position);
                    TraderInteractable keeper = null;
                    foreach (var trader in UnityEngine.Object.FindObjectsOfType<TraderInteractable>())
                    {
                        if (here != null && trader.TraderId == here.TraderId)
                        {
                            keeper = trader;
                        }
                    }
                    if (keeper == null || keeper.Exfil == null)
                    {
                        Report("FAILED no trader with an extract in this room");
                        break;
                    }
                    VagabondLink.Leave(keeper.Exfil);
                    Report("leave requested at " + keeper.Exfil.name + "; extract status " + keeper.Exfil.Status);
                    yield return new WaitForSecondsRealtime(3f);
                    Report("three seconds on: extract status " + keeper.Exfil.Status + ", player still in raid " + (GamePlayerOwner.MyPlayer != null));
                    break;
                }
                case "bunker":
                {
                    // Stage 1 of the hideout work: load the game's hideout interior into the raid and look at it.
                    string what = parts.Length > 1 ? parts[1].ToLowerInvariant() : "where";
                    Task<string> job = null;
                    if (what == "load")
                    {
                        job = HideoutBunker.Load();
                    }
                    else if (what == "unload")
                    {
                        job = HideoutBunker.Unload();
                    }
                    else if (what == "move")
                    {
                        Report(HideoutBunker.Move(new Vector3(Number(parts[2]), Number(parts[3]), Number(parts[4]))));
                    }
                    else if (what == "sun")
                    {
                        Report(HideoutBunker.Sun(parts.Length > 2 && parts[2] == "on"));
                    }
                    else if (what == "settings")
                    {
                        var use = parts.Length > 2 && parts[2] == "bunker" ? HideoutBunker.BunkerSettings : HideoutBunker.RaidSettings;
                        if (use != null)
                        {
                            use.ApplySettings();
                        }
                        Report(use != null ? "applied the " + (use == HideoutBunker.BunkerSettings ? "bunker's" : "map's") + " level settings" : "FAILED no such settings");
                    }
                    else
                    {
                        foreach (var entry in HideoutBunker.Describe())
                        {
                            Report(entry);
                        }
                    }
                    if (job != null)
                    {
                        while (!job.IsCompleted)
                        {
                            yield return null;
                        }
                        Report(job.IsFaulted ? "FAILED " + job.Exception.InnerException : job.Result);
                    }
                    break;
                }
                case "post":
                {
                    // Calls one of the server's routes: post ROUTE {json}
                    string body = parts.Length > 2 ? line.Substring(line.IndexOf(parts[1], StringComparison.Ordinal) + parts[1].Length).Trim() : "{}";
                    var call = SPT.Common.Http.RequestHandler.PostJsonAsync(parts[1], body);
                    while (!call.IsCompleted)
                    {
                        yield return null;
                    }
                    string reply = call.IsFaulted ? "FAILED " + call.Exception.InnerException.Message : call.Result;
                    Report(reply.Length > 1500 ? reply.Substring(0, 1500) + " ... (" + reply.Length + " characters)" : reply);
                    break;
                }
                case "hideout":
                {
                    // hideout enter ID | hideout leave: the trip through a hideout door, without the door.
                    string what = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
                    if (what == "enter" && parts.Length > 2)
                    {
                        HideoutDoors.Enter(parts[2].ToLowerInvariant(), player.Transform.position, player.Rotation.x);
                    }
                    else if (what == "leave")
                    {
                        HideoutDoors.Leave();
                    }
                    yield return new WaitForSecondsRealtime(5f);
                    float y1 = player.Transform.position.y;
                    yield return new WaitForSecondsRealtime(2f);
                    float y2 = player.Transform.position.y;
                    Report((Mathf.Abs(y2 - y1) < 0.05f ? "PASS" : "FAILED") + " standing still (y " + y1.ToString("F2") + " -> " + y2.ToString("F2") + "), inside hideout: " + (HideoutDoors.Inside ?? "none") + ", bunker loaded: " + HideoutBunker.Loaded);
                    Status(player);
                    break;
                }
                case "stash":
                {
                    // stash | stash open | stash put TEMPLATE [COUNT] | stash take TEMPLATE | stash save
                    string what = parts.Length > 1 ? parts[1].ToLowerInvariant() : "list";
                    var stash = HideoutStash.Current;
                    if (stash == null)
                    {
                        Report("FAILED no hideout stash (not inside a hideout?)");
                        break;
                    }
                    if (what == "open")
                    {
                        Report(HideoutStash.Open() ? "stash screen asked for" : "FAILED could not open");
                        yield return new WaitForSecondsRealtime(1.5f);
                    }
                    else if (what == "put" && parts.Length > 2)
                    {
                        // Moves carried items of that kind into the stash, the way dragging them would.
                        int wanted = parts.Length > 3 ? (int)Number(parts[3]) : 1, moved = 0;
                        foreach (var item in LocalTrade.CarriedItems(player).Where(x => x.TemplateId == parts[2]).ToList())
                        {
                            if (moved >= wanted)
                            {
                                break;
                            }
                            var place = stash.Grids[0].FindLocationForItem(item);
                            if (place == null)
                            {
                                Report("FAILED no room in the stash");
                                break;
                            }
                            var move = ItemManipulator.Move(item, place, player.InventoryController, false);
                            if (move.Failed)
                            {
                                Report("FAILED move: " + move.Error);
                                break;
                            }
                            move.Value.RaiseEvents(player.InventoryController, CommandStatus.Begin);
                            move.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
                            moved++;
                        }
                        Report("moved " + moved + " into the stash");
                    }
                    else if (what == "take" && parts.Length > 2)
                    {
                        var item = HideoutStash.Items().FirstOrDefault(x => x.TemplateId == parts[2]);
                        if (item == null)
                        {
                            Report("FAILED not in the stash");
                        }
                        else
                        {
                            // Moved, as dragging it out would; not created anew.
                            var room = LocalTrade.RoomFor(player, item);
                            var move = room == null ? default : ItemManipulator.Move(item, room, player.InventoryController, false);
                            if (room == null || move.Failed)
                            {
                                Report("FAILED could not take it: " + (room == null ? "no room in carried gear" : move.Error.ToString()));
                            }
                            else
                            {
                                move.Value.RaiseEvents(player.InventoryController, CommandStatus.Begin);
                                move.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
                                Report("took " + HideoutStations.ItemName(parts[2]) + " out of the stash");
                            }
                        }
                    }
                    else if (what == "close")
                    {
                        player.GetComponent<GamePlayerOwner>().CloseInventoryIfOpen();
                        yield return new WaitForSecondsRealtime(1.5f);
                        Report("gear screen closed");
                    }
                    else if (what == "save")
                    {
                        HideoutStash.Send("test");
                        yield return new WaitForSecondsRealtime(1.5f);
                    }
                    var names = new Dictionary<string, int>();
                    foreach (var item in HideoutStash.Items())
                    {
                        string name = HideoutStations.ItemName(item.TemplateId);
                        int count;
                        names.TryGetValue(name, out count);
                        names[name] = count + Mathf.Max(1, item.StackObjectsCount);
                    }
                    Report("stash of '" + HideoutDoors.Inside + "': " + HideoutStash.Count() + " item(s): "
                           + string.Join(", ", names.Select(pair => pair.Value + " " + pair.Key).Take(25).ToArray()));
                    break;
                }
                case "hideoutdata":
                {
                    // Stage 6 probe: is the game's own hideout data alive during a raid, and what does it hold?
                    if (!Comfort.Common.Singleton<EFT.Hideout.HideoutRepresentation>.Instantiated)
                    {
                        Report("the game's hideout data is NOT available in a raid");
                        break;
                    }
                    var data = Comfort.Common.Singleton<EFT.Hideout.HideoutRepresentation>.Instance;
                    int schemes = 0;
                    try
                    {
                        schemes = data.ProductionSchemesCollection.ProductionSchemes.Count();
                    }
                    catch (Exception e)
                    {
                        Report("recipes could not be read: " + e.Message);
                    }
                    Report("the game's hideout data is available: " + data.AreaDatas.Count + " station record(s), " + schemes + " crafting recipe(s)");
                    foreach (var area in data.AreaDatas.Take(6))
                    {
                        Report("  " + area.Template.Type + " level " + area.CurrentLevel + ", status " + area.Status);
                    }
                    Report("hideout screen objects present: overlay " + (Comfort.Common.Singleton<EFT.UI.CommonUI>.Instantiated && Comfort.Common.Singleton<EFT.UI.CommonUI>.Instance.HideoutScreenOverlay != null)
                           + ", rear " + (Comfort.Common.Singleton<EFT.UI.CommonUI>.Instantiated && Comfort.Common.Singleton<EFT.UI.CommonUI>.Instance.HideoutScreenRear != null));
                    break;
                }
                case "recipes":
                {
                    // recipes [STATION]: what this hideout can make, and what is under way everywhere.
                    int only = parts.Length > 1 ? (int)Number(parts[1]) : -1;
                    Report("hideout '" + HideoutDoors.Inside + "' can make " + HideoutCrafting.Recipes.Count + " recipe(s)");
                    foreach (var recipe in HideoutCrafting.Recipes.Where(r => only < 0 || r.Area == only).OrderBy(r => r.Area).ThenBy(r => r.Seconds).Take(40))
                    {
                        string missing = HideoutCrafting.Missing(player, recipe);
                        Report("  " + recipe.Id + " station " + recipe.Area + ": " + recipe.Count + " x " + HideoutStations.ItemName(recipe.Product) + " in " + WorldClock.Span(recipe.Seconds)
                               + " from " + HideoutCrafting.Needs(recipe) + (missing == null ? " | CAN MAKE" : " | missing " + missing));
                    }
                    foreach (var pair in HideoutCrafting.Crafts)
                    {
                        foreach (var craft in pair.Value)
                        {
                            Report("  under way in '" + pair.Key + "' at station " + craft.Area + ": " + craft.Count + " x " + HideoutStations.ItemName(craft.Product)
                                   + (craft.Due <= WorldClock.Now ? ", READY" : ", ready in " + WorldClock.Span(craft.Due - WorldClock.Now)));
                        }
                    }
                    break;
                }
                case "craft":
                {
                    // craft RECIPE_ID: what choosing that recipe at its station does.
                    var recipe = HideoutCrafting.Recipes.FirstOrDefault(r => r.Id == parts[1]);
                    if (recipe == null)
                    {
                        Report("FAILED no such recipe here");
                        break;
                    }
                    var job = HideoutCrafting.Start(recipe);
                    while (!job.IsCompleted)
                    {
                        yield return null;
                    }
                    Report(job.IsFaulted ? "FAILED " + job.Exception.InnerException : job.Result);
                    break;
                }
                case "collect":
                {
                    var job = HideoutCrafting.Collect((int)Number(parts[1]));
                    while (!job.IsCompleted)
                    {
                        yield return null;
                    }
                    Report(job.IsFaulted ? "FAILED " + job.Exception.InnerException : job.Result);
                    break;
                }
                case "carried":
                {
                    // carried [TEMPLATE]: how many items the character carries, or how many of one kind.
                    var all = LocalTrade.CarriedItems(player);
                    Report(parts.Length > 1
                        ? "carrying " + all.Where(x => x.TemplateId == parts[1]).Sum(x => Mathf.Max(1, x.StackObjectsCount)) + " " + HideoutStations.ItemName(parts[1])
                        : "carrying " + all.Count + " item(s)");
                    break;
                }
                case "save":
                {
                    var job = SaveGame.SaveNow("test");
                    while (!job.IsCompleted)
                    {
                        yield return null;
                    }
                    Report(job.IsFaulted ? "FAILED " + job.Exception.InnerException : job.Result);
                    break;
                }
                case "undying":
                {
                    Plugin.Undying.Value = parts.Length > 1 && parts[1].ToLowerInvariant() == "on";
                    yield return new WaitForSecondsRealtime(1f);
                    Report("undying is " + (Plugin.Undying.Value ? "on" : "off"));
                    break;
                }
                case "die":
                {
                    // Kills the character, to test what a death does. Undying must be off.
                    Report("killing the character");
                    player.ActiveHealthController.Kill(EDamageType.Bullet);
                    break;
                }
                case "sleep":
                {
                    // sleep HOURS: what choosing a Sleep action at the Rest Space does.
                    HideoutSleep.Sleep(parts.Length > 1 ? Number(parts[1]) : 8f);
                    yield return new WaitForSecondsRealtime(6f);
                    goto case "vitals";
                }
                case "vitals":
                {
                    // Health, energy and water, and what the hideout is adding. vitals hurt N lowers them first, for testing.
                    var hc = player.ActiveHealthController;
                    if (parts.Length > 2 && parts[1].ToLowerInvariant() == "hurt")
                    {
                        float amount = Number(parts[2]);
                        hc.ChangeEnergy(-amount);
                        hc.ChangeHydration(-amount);
                        hc.ChangeHealth(EBodyPart.Chest, -amount / 2f, new EFT.Ballistics.DamageInfo { DamageType = EDamageType.Existence });
                        hc.ChangeHealth(EBodyPart.LeftLeg, -amount / 2f, new EFT.Ballistics.DamageInfo { DamageType = EDamageType.Existence });
                    }
                    Report("health " + hc.GetBodyPartHealth(EBodyPart.Common).Current.ToString("F1") + "/" + hc.GetBodyPartHealth(EBodyPart.Common).Maximum.ToString("F0")
                           + ", energy " + hc.Energy.Current.ToString("F1") + ", water " + hc.Hydration.Current.ToString("F1")
                           + " | hideout: " + (HideoutDoors.Inside ?? "none") + " gives " + HideoutBoosts.Describe() + " | clock " + WorldClock.Describe(WorldClock.Now));
                    break;
                }
                case "clock":
                {
                    // clock | clock add HOURS
                    if (parts.Length > 2 && parts[1].ToLowerInvariant() == "add")
                    {
                        WorldClock.Add(Number(parts[2]) * 3600.0);
                        yield return new WaitForSecondsRealtime(1.5f);
                    }
                    var world = Comfort.Common.Singleton<GameWorld>.Instance;
                    Report("world clock: " + WorldClock.Describe(WorldClock.Now) + " (" + WorldClock.Now.ToString("F0") + " game seconds); the raid's own clock reads "
                           + world.GameDateTime.Calculate().ToString("yyyy-MM-dd HH:mm") + ", pace x" + world.GameDateTime.TimeFactor);
                    break;
                }
                case "stations":
                {
                    // Every station of the hideout the player is in: level, where it is, what the next level costs.
                    var controller = HideoutStations.Controller();
                    if (controller == null || HideoutDoors.Inside == null)
                    {
                        Report("FAILED not inside a hideout");
                        break;
                    }
                    Report("hideout '" + HideoutDoors.Inside + "':");
                    foreach (var pair in controller.Areas)
                    {
                        int level;
                        HideoutStations.Levels.TryGetValue((int)pair.Key, out level);
                        Bounds b;
                        bool has = HideoutStations.LevelBounds(pair.Value, Mathf.Max(level, 1), out b);
                        var tally = HideoutStations.Tally(player, (int)pair.Key);
                        Report("  " + (int)pair.Key + " " + pair.Key + " level " + level + "/" + (pair.Value.AreaLevels.Length - 1)
                               + (has ? " at " + b.center.ToString("F1") + " size " + b.size.ToString("F1") : " (nothing to see)")
                               + (HideoutStations.NextCosts.ContainsKey((int)pair.Key) ? " | next needs: " + HideoutStations.CostText(tally, false) + " | missing: " + HideoutStations.CostText(tally, true) : " | fully built"));
                    }
                    break;
                }
                case "build":
                {
                    // build NUMBER: what choosing Build or Upgrade at that station does.
                    int number = (int)Number(parts[1]);
                    var job = HideoutStations.Build(number, ((EFT.EAreaType)number).ToString());
                    while (!job.IsCompleted)
                    {
                        yield return null;
                    }
                    Report(job.IsFaulted ? "FAILED " + job.Exception.InnerException : job.Result);
                    break;
                }
                case "give":
                {
                    // give TEMPLATE COUNT: puts items into carried gear, for paying station costs in tests.
                    int count = parts.Length > 2 ? (int)Number(parts[2]) : 1;
                    var template = Comfort.Common.Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), parts[1], null);
                    int given = 0;
                    if (template.StackMaxSize > 1)
                    {
                        template.StackObjectsCount = Mathf.Min(count, template.StackMaxSize);
                        var one = LocalTrade.Give(player, template);
                        while (!one.IsCompleted)
                        {
                            yield return null;
                        }
                        given = one.Result ? template.StackObjectsCount : 0;
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var item = i == 0 ? template : Comfort.Common.Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), parts[1], null);
                            var one = LocalTrade.Give(player, item);
                            while (!one.IsCompleted)
                            {
                                yield return null;
                            }
                            if (one.Result)
                            {
                                given++;
                            }
                        }
                    }
                    Report("gave " + given + " of " + count + " " + HideoutStations.ItemName(parts[1]));
                    break;
                }
                case "doors":
                {
                    // Door objects near the player, nearest first: candidates for a hideout door.
                    float radius = parts.Length > 1 ? Number(parts[1]) : 60f;
                    var found = new List<KeyValuePair<float, string>>();
                    foreach (var door in UnityEngine.Object.FindObjectsOfType<EFT.Interactive.WorldInteractiveObject>())
                    {
                        float distance = Vector3.Distance(door.transform.position, player.Transform.position);
                        if (distance <= radius)
                        {
                            found.Add(new KeyValuePair<float, string>(distance, distance.ToString("F0") + " m  " + door.GetType().Name + " " + door.name + " id " + door.Id
                                + " at " + door.transform.position.ToString("F1") + " yaw " + door.transform.eulerAngles.y.ToString("F0") + " state " + door.DoorState
                                + (string.IsNullOrEmpty(door.KeyId) ? "" : " key " + door.KeyId) + (door.Operatable ? "" : " NOT OPERATABLE")));
                        }
                    }
                    found.Sort((a, b) => a.Key.CompareTo(b.Key));
                    Report("door objects within " + radius + " m: " + found.Count);
                    for (int i = 0; i < found.Count && i < 60; i++)
                    {
                        Report("  " + found[i].Value);
                    }
                    break;
                }
                case "layout":
                    // layout [STYLE]: furnish the rooms again from test/layout.txt, in a trader's style if one is named.
                    if (parts.Length > 1)
                    {
                        TraderRoom.LookOverride = parts[1].ToLowerInvariant() == "none" ? null : parts[1];
                    }
                    yield return HideoutDoors.Furnish();
                    break;
                case "props":
                    // props WORD: hideout pieces whose name contains the word.
                    foreach (var entry in RoomProps.Find(parts.Length > 1 ? parts[1] : ""))
                    {
                        Report("  " + entry);
                    }
                    break;
                case "survey":
                    // Catalogue of everything drawn in the hideout interior, for choosing room furniture.
                    Report(RoomProps.Survey(System.IO.Path.Combine(Dir, "bunker-objects.txt")));
                    Report(RoomProps.SurveyMaterials(System.IO.Path.Combine(Dir, "bunker-materials.txt")));
                    break;
                case "scenery":
                {
                    // Drawn objects near the player whose name contains a word (default "door") and that
                    // are about the size of a door: doors that are part of the map and never open.
                    float radius = parts.Length > 1 ? Number(parts[1]) : 60f;
                    string word = parts.Length > 2 ? parts[2].ToLowerInvariant() : "door";
                    var found = new List<KeyValuePair<float, string>>();
                    foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                    {
                        if (!renderer.enabled || renderer.name.ToLowerInvariant().IndexOf(word, StringComparison.Ordinal) < 0)
                        {
                            continue;
                        }
                        var b = renderer.bounds;
                        float distance = Vector3.Distance(b.center, player.Transform.position);
                        float wide = Mathf.Max(b.size.x, b.size.z);
                        if (distance > radius || b.size.y < 1.7f || b.size.y > 2.8f || wide < 0.6f || wide > 1.6f
                            || renderer.GetComponentInParent<EFT.Interactive.WorldInteractiveObject>() != null)
                        {
                            continue;
                        }
                        found.Add(new KeyValuePair<float, string>(distance, distance.ToString("F0") + " m  " + renderer.name + " centre " + b.center.ToString("F1")
                            + " foot " + new Vector3(b.center.x, b.min.y, b.center.z).ToString("F1") + " size " + b.size.ToString("F1")));
                    }
                    found.Sort((a, b) => a.Key.CompareTo(b.Key));
                    Report("door-sized '" + word + "' objects within " + radius + " m: " + found.Count);
                    for (int i = 0; i < found.Count && i < 60; i++)
                    {
                        Report("  " + found[i].Value);
                    }
                    break;
                }
                case "shot":
                {
                    // shot X Y Z NAME: stand a couple of metres from a point with a clear view, look at it, take a picture.
                    var target = new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])) + Vector3.up * 1.0f;
                    int solid = LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain", "Default");
                    bool placed = false;
                    foreach (float reach in new[] { 2.6f, 1.8f, 3.6f })
                    {
                        for (int step = 0; step < 16 && !placed; step++)
                        {
                            var direction = Quaternion.Euler(0f, step * 22.5f, 0f) * Vector3.forward;
                            var eye = target + direction * reach + Vector3.up * 0.5f;
                            RaycastHit floor;
                            if (Physics.Linecast(target + direction * 0.25f, eye, solid, QueryTriggerInteraction.Ignore)
                                || !Physics.Raycast(eye, Vector3.down, out floor, 3f, solid, QueryTriggerInteraction.Ignore))
                            {
                                continue;
                            }
                            player.Teleport(floor.point + Vector3.up * 0.05f, false);
                            var to = target - (floor.point + Vector3.up * 1.6f);
                            player.Rotation = new Vector2(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
                            placed = true;
                            Report("viewing from " + floor.point.ToString("F1") + ", looking " + player.Rotation.x.ToString("F0"));
                        }
                        if (placed)
                        {
                            break;
                        }
                    }
                    if (!placed)
                    {
                        Report("FAILED no clear place to stand near that point");
                        break;
                    }
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return new WaitForEndOfFrame();
                    Screenshot(parts.Length > 4 ? parts[4] : "shot");
                    break;
                }
                case "target":
                {
                    // What the game currently offers to interact with, and the actions we would give it.
                    var seen = player.InteractableObject;
                    if (seen == null)
                    {
                        Report("the game offers nothing to interact with here");
                        break;
                    }
                    string offered = "";
                    var station = seen as StationInteractable;
                    var door = seen as RoomDoor;
                    var trader = seen as TraderInteractable;
                    var actions = HideoutStations.ActionsForObject(seen) ?? (station != null ? station.GetActions() : door != null ? door.GetActions() : trader != null ? trader.GetActions() : null);
                    if (actions != null)
                    {
                        foreach (var action in actions.Actions)
                        {
                            offered += " [" + action.Name + (action.Disabled ? " (greyed out)" : "") + "]";
                        }
                    }
                    Report("the game offers: " + seen.name + offered);
                    break;
                }
                case "aim":
                {
                    // What the player is looking at: the exact point and the way the surface faces.
                    var eyes = player.Transform.position + Vector3.up * 1.6f;
                    var forward = Quaternion.Euler(-player.Rotation.y, player.Rotation.x, 0f) * Vector3.forward;
                    RaycastHit hit;
                    if (Physics.Raycast(eyes, forward, out hit, 30f, LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain", "Default"), QueryTriggerInteraction.Ignore))
                    {
                        Report("looking at " + hit.collider.name + " at " + hit.point.ToString("F2") + ", " + hit.distance.ToString("F1") + " m away, surface faces yaw "
                               + Quaternion.LookRotation(new Vector3(hit.normal.x, 0f, hit.normal.z)).eulerAngles.y.ToString("F0"));
                    }
                    else
                    {
                        Report("looking at nothing within 30 m");
                    }
                    break;
                }
                case "use":
                {
                    // Uses the nearest door of ours, as pressing the action key while looking at it would.
                    RoomDoor nearest = null;
                    float best = 6f;
                    foreach (var door in UnityEngine.Object.FindObjectsOfType<RoomDoor>())
                    {
                        float distance = Vector3.Distance(door.transform.position, player.Transform.position);
                        if (distance < best)
                        {
                            best = distance;
                            nearest = door;
                        }
                    }
                    if (nearest == null)
                    {
                        Report("FAILED no door of ours within 6 m");
                        break;
                    }
                    Report("using '" + nearest.Label + "' " + best.ToString("F1") + " m away at " + nearest.transform.position.ToString("F1"));
                    nearest.Use();
                    yield return new WaitForSecondsRealtime(5f);
                    float y1 = player.Transform.position.y;
                    yield return new WaitForSecondsRealtime(2f);
                    float y2 = player.Transform.position.y;
                    Report((Mathf.Abs(y2 - y1) < 0.05f ? "PASS" : "FAILED") + " standing still (y " + y1.ToString("F2") + " -> " + y2.ToString("F2") + "), inside hideout: " + (HideoutDoors.Inside ?? "none"));
                    Status(player);
                    break;
                }
                case "lookat":
                {
                    // Turns the player toward the door on the map of the first room, or the room named.
                    foreach (var room in TraderRoom.Rooms)
                    {
                        if (room.OutsideDoor == null || (parts.Length > 1 && !TraderConfig.NameOf(room.TraderId).ToLowerInvariant().StartsWith(parts[1].ToLowerInvariant())))
                        {
                            continue;
                        }
                        var to = room.OutsideDoor.transform.position - player.Transform.position;
                        player.Rotation = new Vector2(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
                        Report("door of " + TraderConfig.NameOf(room.TraderId) + " is " + new Vector2(to.x, to.z).magnitude.ToString("F1") + " m away at " + room.OutsideDoor.transform.position.ToString("F1"));
                        break;
                    }
                    break;
                }
                case "look":
                    player.Rotation = new Vector2(Number(parts[1]), parts.Length > 2 ? Number(parts[2]) : 0f);
                    break;
                case "enter":
                case "leave":
                {
                    RoomDoor door = null;
                    if (verb == "enter")
                    {
                        foreach (var room in TraderRoom.Rooms)
                        {
                            if (parts.Length < 2 || TraderConfig.NameOf(room.TraderId).ToLowerInvariant().StartsWith(parts[1].ToLowerInvariant()))
                            {
                                door = room.OutsideDoor;
                                break;
                            }
                        }
                    }
                    else
                    {
                        var inside = RoomHolding(player.Transform.position);
                        door = inside != null ? inside.ExitDoor : null;
                    }
                    if (door == null)
                    {
                        Report("FAILED no such door");
                        yield break;
                    }
                    float healthBefore = Health(player);
                    door.Use();
                    yield return new WaitForSecondsRealtime(1.2f);
                    float y1 = player.Transform.position.y;
                    yield return new WaitForSecondsRealtime(2f);
                    float y2 = player.Transform.position.y;
                    bool steady = Mathf.Abs(y2 - y1) < 0.05f;
                    bool unhurt = Health(player) >= healthBefore - 0.5f;
                    Report((steady && unhurt ? "PASS" : "FAILED") + " after door: standing still " + steady
                           + " (y " + y1.ToString("F2") + " -> " + y2.ToString("F2") + "), unhurt " + unhurt);
                    Status(player);
                    break;
                }
                case "lay":
                {
                    // A made-up item on the table of the room the player is in; not tied to any mail.
                    var here = RoomHolding(player.Transform.position);
                    if (here == null)
                    {
                        Report("FAILED not in a room");
                        break;
                    }
                    int copies = parts.Length > 2 ? (int)Number(parts[2]) : 1;
                    for (int i = 0; i < copies; i++)
                    {
                        var item = Comfort.Common.Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), parts[1], null);
                        var placing = RewardTable.Place(here, item, null);
                        while (!placing.IsCompleted)
                        {
                            yield return null;
                        }
                        if (placing.IsFaulted)
                        {
                            Report("FAILED " + placing.Exception.InnerException);
                            continue;
                        }
                        var loot = placing.Result;
                        Report(loot != null ? "laid " + item.ShortName.Localized() + " at " + loot.transform.position.ToString("F2") : "FAILED to lay item");
                    }
                    break;
                }
                case "deliveries":
                {
                    var here = RoomHolding(player.Transform.position);
                    if (here == null)
                    {
                        Report("FAILED not in a room");
                        break;
                    }
                    Mail.Collect(here.TraderId, TraderConfig.NameOf(here.TraderId));
                    yield return new WaitForSecondsRealtime(4f);
                    goto case "table";
                }
                case "table":
                {
                    var lines = RewardTable.Describe();
                    Report("on tables: " + lines.Count);
                    foreach (var entry in lines)
                    {
                        Report("  " + entry);
                    }
                    break;
                }
                case "handin":
                {
                    // Hands in a quest the player has in progress, exactly as the trade screen would,
                    // whether or not its tasks are done. Only for a profile that is backed up.
                    var here = RoomHolding(player.Transform.position);
                    EFT.Quests.Quest chosen = null;
                    foreach (var quest in player.QuestController.Quests)
                    {
                        if (quest.QuestStatus != EFT.Quests.EQuestStatus.Started && quest.QuestStatus != EFT.Quests.EQuestStatus.AvailableForFinish)
                        {
                            continue;
                        }
                        if (parts.Length > 1 ? quest.Id == parts[1] : (here == null || quest.Template.TraderId == here.TraderId))
                        {
                            chosen = quest;
                            break;
                        }
                    }
                    if (chosen == null)
                    {
                        Report("FAILED no quest in progress" + (here != null && parts.Length < 2 ? " for " + TraderConfig.NameOf(here.TraderId) : ""));
                        break;
                    }
                    Report("handing in quest " + chosen.Id + " of " + TraderConfig.NameOf(chosen.Template.TraderId));
                    var finishing = player.QuestController.FinishQuest(chosen, true);
                    while (!finishing.IsCompleted)
                    {
                        yield return null;
                    }
                    if (finishing.IsFaulted)
                    {
                        Report("FAILED " + finishing.Exception.InnerException);
                        break;
                    }
                    Report(finishing.Result.Failed ? "FAILED game refused: " + finishing.Result.Error : "PASS quest handed in, status " + chosen.QuestStatus);
                    yield return new WaitForSecondsRealtime(5f);
                    goto case "table";
                }
                case "quests":
                {
                    foreach (var quest in player.QuestController.Quests)
                    {
                        if (quest.QuestStatus == EFT.Quests.EQuestStatus.Started || quest.QuestStatus == EFT.Quests.EQuestStatus.AvailableForFinish
                            || quest.QuestStatus == EFT.Quests.EQuestStatus.AvailableForStart)
                        {
                            Report("  " + quest.Id + " " + TraderConfig.NameOf(quest.Template.TraderId) + " " + quest.QuestStatus);
                        }
                    }
                    break;
                }
                case "lootphysics":
                {
                    // Which layers does loose loot collide with? Looks at any loot object on the map.
                    var any = UnityEngine.Object.FindObjectOfType<EFT.Interactive.LootItem>();
                    if (any == null)
                    {
                        Report("FAILED no loot object found");
                        break;
                    }
                    foreach (var collider in any.GetComponentsInChildren<Collider>(true))
                    {
                        int layer = collider.gameObject.layer;
                        var hits = new List<string>();
                        for (int other = 0; other < 32; other++)
                        {
                            if (!Physics.GetIgnoreLayerCollision(layer, other) && LayerMask.LayerToName(other).Length > 0)
                            {
                                hits.Add(LayerMask.LayerToName(other));
                            }
                        }
                        Report("loot collider " + collider.GetType().Name + " trigger " + collider.isTrigger + " enabled " + collider.enabled
                               + " on layer " + LayerMask.LayerToName(layer) + ", collides with: " + string.Join(", ", hits.ToArray()));
                    }
                    var body = any.GetComponentInChildren<Rigidbody>();
                    Report("rigidbody: " + (body == null ? "none" : "kinematic " + body.isKinematic + ", gravity " + body.useGravity + ", detection " + body.collisionDetectionMode));
                    break;
                }
                case "drop":
                {
                    // Lets go of an item one metre above the player's feet, two metres ahead, under the
                    // game's own physics, and reports where it is a few seconds later.
                    var item = Comfort.Common.Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), parts[1], null);
                    var from = player.Transform.position + Quaternion.Euler(0f, player.Rotation.x, 0f) * new Vector3(0f, 1f, 2f);
                    var dropped = Comfort.Common.Singleton<GameWorld>.Instance.ThrowItem(item, player, from, Quaternion.identity, Vector3.zero, Vector3.zero, false);
                    yield return new WaitForSecondsRealtime(3f);
                    float fell = from.y - dropped.transform.position.y;
                    Report((fell < 1.5f ? "PASS" : "FAILED") + " dropped item fell " + fell.ToString("F2") + " m and is at " + dropped.transform.position.ToString("F2"));
                    break;
                }
                case "broadphase":
                {
                    // Rebuilds the physics engine's collision regions around the given box.
                    var centre = new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3]));
                    var size = new Vector3(Number(parts[4]), Number(parts[5]), Number(parts[6]));
                    int cells = (int)Number(parts[7]);
                    Physics.RebuildBroadphaseRegions(new Bounds(centre, size), cells);
                    Report("collision regions rebuilt: centre " + centre + ", size " + size + ", subdivisions " + cells);
                    break;
                }
                case "screenshot":
                    yield return new WaitForEndOfFrame();
                    Screenshot(parts.Length > 1 ? parts[1] : "shot");
                    break;
                default:
                    Report("FAILED unknown command");
                    break;
            }
        }

        private static float Health(Player player)
        {
            try
            {
                return player.ActiveHealthController.GetBodyPartHealth(EBodyPart.Common).Current;
            }
            catch
            {
                return -1f;
            }
        }

        private static TraderRoom RoomHolding(Vector3 position)
        {
            foreach (var room in TraderRoom.Rooms)
            {
                if (room.Indoor != null && room.Indoor.Check(position) != null)
                {
                    return room;
                }
            }
            return null;
        }

        private static void Status(Player player)
        {
            var at = player.Transform.position;
            var room = RoomHolding(at);
            string env = "?";
            try
            {
                env = EFT.EnvironmentEffect.EnvironmentManager.Instance.Environment.ToString();
            }
            catch
            {
                // Not every map has one.
            }
            string ground = "nothing within 3 m";
            RaycastHit hit;
            if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out hit, 3.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                ground = hit.collider.name + " on layer " + LayerMask.LayerToName(hit.collider.gameObject.layer)
                         + " " + (hit.distance - 0.5f).ToString("F2") + " m below";
            }
            Report("map " + player.Location + ", at " + at.ToString("F2") + ", facing " + player.Rotation.x.ToString("F0")
                   + ", alive " + player.HealthController.IsAlive + ", health " + Health(player).ToString("F0")
                   + ", in room " + (room != null ? TraderConfig.NameOf(room.TraderId) : "none")
                   + ", game says " + env + ", under feet: " + ground
                   + ", player layer " + LayerMask.LayerToName(player.gameObject.layer));
        }

        /// <summary>Checks every room without going there.</summary>
        private static void Check(Player player)
        {
            Report("rooms: " + TraderRoom.Rooms.Count + ", on by setting: " + TraderRoom.Enabled.Value);
            foreach (var room in TraderRoom.Rooms)
            {
                string name = TraderConfig.NameOf(room.TraderId);
                var hits = Physics.RaycastAll(room.Entry + Vector3.up, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
                var layers = new List<string>();
                foreach (var hit in hits)
                {
                    layers.Add(LayerMask.LayerToName(hit.collider.gameObject.layer));
                }
                bool floor = hits.Length > 0;
                bool indoor = TraderRoom.IndoorAt(room.Entry + Vector3.up) != null;
                bool doors = room.OutsideDoor != null && room.ExitDoor != null;
                string shader = "?";
                var renderer = room.Root != null ? room.Root.GetComponentInChildren<MeshRenderer>() : null;
                if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null)
                {
                    shader = renderer.sharedMaterial.shader.name;
                }
                Report((floor && indoor && doors ? "PASS " : "FAILED ") + name + ": floor under entry " + floor
                       + " [" + string.Join(", ", layers.ToArray()) + "], indoors " + indoor + ", doors " + doors + ", shader " + shader);
            }
        }

        private static void Screenshot(string name)
        {
            Texture2D full = null, small = null;
            RenderTexture target = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                int width = Mathf.Min(1280, full.width);
                int height = Mathf.RoundToInt(full.height * (width / (float)full.width));
                target = RenderTexture.GetTemporary(width, height, 0);
                Graphics.Blit(full, target);
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                small = new Texture2D(width, height, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                small.Apply();
                RenderTexture.active = previous;
                Directory.CreateDirectory(Dir);
                string path = Path.Combine(Dir, name + ".jpg");
                File.WriteAllBytes(path, small.EncodeToJPG(80));
                Report("screenshot saved: " + path);
            }
            catch (Exception e)
            {
                Report("FAILED screenshot: " + e.Message);
            }
            finally
            {
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (full != null) UnityEngine.Object.Destroy(full);
                if (small != null) UnityEngine.Object.Destroy(small);
            }
        }
    }
}
