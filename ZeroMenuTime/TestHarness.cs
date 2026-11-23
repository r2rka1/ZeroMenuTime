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

namespace ZeroMenuTime
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
        private static readonly Dictionary<string, Vector3> _botWas = new Dictionary<string, Vector3>();
        private static float _botLook;

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

        /// <summary>While frames are being saved for the mod's page, the test notices in windows are left out.</summary>
        public static bool Filming => _film != null;

        private static Coroutine _film;
        private static string _filmName = "";
        private static int _filmFps = 12, _filmWidth = 960, _filmFrames;

        private static void FilmStop()
        {
            if (_film != null && Plugin.Instance != null)
            {
                Plugin.Instance.StopCoroutine(_film);
            }
            _film = null;
            Time.captureFramerate = 0;
        }

        /// <summary>Saves each frame once it is fully drawn. The game's clock is tied to the frame rate meanwhile, so motion stays even.</summary>
        private static IEnumerator Film(string folder)
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                Texture2D full = null, small = null;
                RenderTexture target = null;
                try
                {
                    full = ScreenCapture.CaptureScreenshotAsTexture();
                    int width = Mathf.Min(_filmWidth, full.width);
                    int height = Mathf.RoundToInt(full.height * (width / (float)full.width));
                    target = RenderTexture.GetTemporary(width, height, 0);
                    Graphics.Blit(full, target);
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    small = new Texture2D(width, height, TextureFormat.RGB24, false);
                    small.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    small.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(folder, (++_filmFrames).ToString("D5") + ".jpg"), small.EncodeToJPG(88));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Film frame failed: " + e.Message);
                }
                finally
                {
                    if (full != null) UnityEngine.Object.Destroy(full);
                    if (small != null) UnityEngine.Object.Destroy(small);
                    if (target != null) RenderTexture.ReleaseTemporary(target);
                }
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
                        if (light.name == "lamp" && light.transform.parent != null && light.transform.parent.name.StartsWith("ZeroMenuTime menu"))
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
            if (verb == "prompt")
            {
                // prompt: the yes/no question on screen now. prompt yes | prompt no: answers it.
                if (parts.Length > 1)
                {
                    Report(Prompt.Answer(parts[1] == "yes"));
                }
                else
                {
                    Report(Prompt.Text != null ? "asked: " + Prompt.Text.Replace("\n", " ") : "nothing is asked");
                }
                yield break;
            }
            if (verb == "menupress")
            {
                // menupress WORDS: clicks an entry of this mod's menu by its words (main menu, or the Esc menu in a raid).
                string words = string.Join(" ", parts, 1, parts.Length - 1);
                Report(MainMenu.Press(words) ? "pressed '" + words + "'" : "FAILED no menu entry '" + words + "'; entries: " + string.Join(" | ", MainMenu.Items().ToArray()));
                yield break;
            }
            if (verb == "fika")
            {
                // fika: what Fika says this game is. fika host | fika join HOSTPROFILEID: from the menu, start a raid through Fika.
                if (parts.Length > 2 && parts[1] == "spawn")
                {
                    // fika spawn TEMPLATE: a new item laid in front of the player on every machine.
                    var made = Comfort.Common.Singleton<ItemFactory>.Instance.GetPresetItem(parts[2]);
                    var where = player.Transform.position + player.Transform.forward * 1.2f + Vector3.up * 0.8f;
                    Net.Current.LayItem(made, where, Quaternion.identity);
                    Report("item " + made.Id + " laid for all at " + where.ToString("F1"));
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "loot")
                {
                    // fika loot [R]: loose items within R metres of the player, with their ids.
                    float r = parts.Length > 2 ? Number(parts[2]) : 4f;
                    int n = 0;
                    foreach (var loot in UnityEngine.Object.FindObjectsOfType<EFT.Interactive.LootItem>())
                    {
                        if (loot.Item != null && Vector3.Distance(loot.transform.position, player.Transform.position) <= r)
                        {
                            n++;
                            Report("  " + loot.Item.ShortName + " id " + loot.Item.Id + " at " + loot.transform.position.ToString("F1") + " " + loot.GetType().Name);
                        }
                    }
                    Report(n + " loose item(s) within " + r + " m");
                    yield break;
                }
                if (parts.Length > 2 && parts[1] == "take")
                {
                    // fika take ITEMID: the first thing the game's own menu offers for that loose item (Take), without aiming.
                    EFT.Interactive.LootItem wanted = null;
                    foreach (var loot in UnityEngine.Object.FindObjectsOfType<EFT.Interactive.LootItem>())
                    {
                        if (loot.Item != null && loot.Item.Id == parts[2])
                        {
                            wanted = loot;
                        }
                    }
                    if (wanted == null)
                    {
                        Report("FAILED no loose item with that id here");
                        yield break;
                    }
                    var offered = InteractionContextHelper.GetAvailableActions(player.GetComponent<GamePlayerOwner>(), wanted);
                    if (offered == null || offered.Actions.Count == 0)
                    {
                        Report("FAILED the game offers nothing for it");
                        yield break;
                    }
                    var names = new List<string>();
                    foreach (var act in offered.Actions)
                    {
                        names.Add(act.Name + (act.Disabled ? " (off)" : ""));
                    }
                    Report("offered: " + string.Join(", ", names.ToArray()) + "; doing the first");
                    offered.Actions[0].Action();
                    yield return new WaitForSecondsRealtime(3f);
                    Report("after: carrying it " + (wanted == null || wanted.Item == null ? "?" : (wanted.Item.Parent != null && wanted.Item.Parent.GetOwnerOrNull() == player.InventoryController).ToString()));
                    yield break;
                }
                if (parts.Length > 2 && parts[1] == "answer")
                {
                    // fika answer yes|no: answers the sleep or wait question asked by another player.
                    Report(Rest.AnswerLast(parts[2] == "yes"));
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "layout")
                {
                    // fika layout: where this machine's rooms and doors stand; whose settings they were built from.
                    foreach (var built in Layout.Built())
                    {
                        Report("  " + built);
                    }
                    Report("built from " + (Layout.Active != null ? "the host's settings" : "this game's own settings") + "; bots switched off now: " + BotFreeze.Count);
                    yield break;
                }
                if (parts.Length > 2 && parts[1] == "say")
                {
                    // fika say WORDS: a test message to every other machine; each logs it.
                    Net.Current.Send("zmt.test", System.Text.Encoding.UTF8.GetBytes(string.Join(" ", parts, 2, parts.Length - 2)), true);
                    Report("said");
                    yield break;
                }
                if (parts.Length > 2 && parts[1] == "tellhost")
                {
                    Net.Current.Send("zmt.test", System.Text.Encoding.UTF8.GetBytes(string.Join(" ", parts, 2, parts.Length - 2)), false);
                    Report("told the host");
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "humans")
                {
                    foreach (var human in Net.Current.Humans())
                    {
                        Report("  " + human.Name + (human.IsLocal ? " (me)" : "") + " alive " + human.Alive + " at " + human.Position.ToString("F1"));
                    }
                    Report("humans listed");
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "transits")
                {
                    // fika transits: the map's transits: id, name, where to, where it is, whether it can be used.
                    foreach (var transit in UnityEngine.Object.FindObjectsOfType<EFT.Interactive.TransitPoint>())
                    {
                        var zone = transit.GetComponentInChildren<Collider>();
                        Report("  " + transit.parameters.id + " '" + transit.parameters.name + "' to " + transit.parameters.location + " at " + transit.transform.position.ToString("F1")
                               + (zone != null ? " zone " + zone.bounds.center.ToString("F1") + " size " + zone.bounds.size.ToString("F1") : " no zone")
                               + " enabled " + transit.Enabled + " active " + transit.IsActive + " controller " + (transit.Controller != null ? transit.Controller.GetType().Name : "none"));
                    }
                    // What the transit controller itself believes: who stands in which transit, who has asked to go.
                    object controller = null;
                    foreach (var any in UnityEngine.Object.FindObjectsOfType<EFT.Interactive.TransitPoint>())
                    {
                        controller = controller ?? any.Controller;
                    }
                    if (controller != null)
                    {
                        for (var type = controller.GetType(); type != null && type != typeof(object); type = type.BaseType)
                        {
                            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                            {
                                var held = field.GetValue(controller) as System.Collections.IDictionary;
                                if (held == null)
                                {
                                    continue;
                                }
                                var shown = new List<string>();
                                foreach (System.Collections.DictionaryEntry entry in held)
                                {
                                    var who = entry.Key as Player;
                                    shown.Add((who != null ? who.Profile.Nickname : entry.Key + "") + "=" + (entry.Value is Player ? ((Player)entry.Value).Profile.Nickname : entry.Value + ""));
                                    if (shown.Count >= 6)
                                    {
                                        break;
                                    }
                                }
                                Report("  controller " + type.Name + "." + field.Name + " (" + held.Count + "): " + string.Join(", ", shown.ToArray()));
                            }
                        }
                    }
                    Report("transits listed");
                    yield break;
                }
                if (parts.Length > 2 && parts[1] == "transit")
                {
                    // fika transit ID [stand]: go and stand in that transit and ask to go through, as the game's own prompt does.
                    EFT.Interactive.TransitPoint chosen = null;
                    foreach (var transit in UnityEngine.Object.FindObjectsOfType<EFT.Interactive.TransitPoint>())
                    {
                        if (transit.parameters.id.ToString() == parts[2])
                        {
                            chosen = transit;
                        }
                    }
                    if (chosen == null)
                    {
                        Report("FAILED no transit with that id");
                        yield break;
                    }
                    var zoneOf = chosen.GetComponentInChildren<Collider>();
                    var centre = zoneOf != null ? zoneOf.bounds.center : chosen.transform.position;
                    RaycastHit under;
                    var stand = Physics.Raycast(centre + Vector3.up * 2f, Vector3.down, out under, 30f, LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain"), QueryTriggerInteraction.Ignore)
                        ? under.point + Vector3.up * 0.1f : centre;
                    player.Teleport(stand, false);
                    yield return new WaitForSecondsRealtime(2.5f);
                    if (parts.Length > 3 && parts[3] == "stand")
                    {
                        Report("standing in transit " + parts[2] + " at " + player.Transform.position.ToString("F1"));
                        yield break;
                    }
                    if (chosen.Controller == null)
                    {
                        Report("FAILED the transit has no controller");
                        yield break;
                    }
                    // The call the game's own "go through" prompt makes; in co-op it reaches the host from any machine.
                    player.TransitInteraction(chosen.Controller, chosen.parameters.id, "", JsonType.EDateTime.CURR);
                    Report("asked to go through transit " + parts[2] + " to " + chosen.parameters.location + " from " + player.Transform.position.ToString("F1"));
                    yield break;
                }
                if (parts.Length > 3 && parts[1] == "hurt")
                {
                    // fika hurt PART AMOUNT [KIND]: real damage to the local player, as a hit would do it.
                    EBodyPart part;
                    Enum.TryParse(parts[2], true, out part);
                    EDamageType kind = EDamageType.Bullet;
                    if (parts.Length > 4)
                    {
                        Enum.TryParse(parts[4], true, out kind);
                    }
                    player.ActiveHealthController.ApplyDamage(part, Number(parts[3]), new EFT.Ballistics.DamageInfo { DamageType = kind, Damage = Number(parts[3]) });
                    Report("hurt " + part + " by " + parts[3] + " (" + kind + "); alive " + player.HealthController.IsAlive + ", ghost " + Afterlife.Dead);
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "bots")
                {
                    // fika bots [R]: bots within R metres: shown or switched off, and whom each is after.
                    float reach = parts.Length > 2 ? Number(parts[2]) : 80f;
                    int n = 0, after = 0;
                    var world = Comfort.Common.Singleton<GameWorld>.Instance;
                    foreach (var other in world.AllPlayersEverExisted)
                    {
                        if (other == null || !other.IsAI || other.HealthController == null || !other.HealthController.IsAlive
                            || Vector3.Distance(other.Transform.position, player.Transform.position) > reach)
                        {
                            continue;
                        }
                        n++;
                        string goal = "nobody";
                        var owner = other.AIData != null ? other.AIData.BotOwner : null;
                        if (owner != null && owner.Memory != null && owner.Memory.GoalEnemy != null && owner.Memory.GoalEnemy.Person != null)
                        {
                            goal = owner.Memory.GoalEnemy.Person.Profile.Nickname + (Afterlife.Ghosts.Contains(owner.Memory.GoalEnemy.Person.ProfileId) ? " (A GHOST)" : "");
                            after++;
                        }
                        if (n <= 12)
                        {
                            Report("  " + other.Profile.Nickname + " " + Vector3.Distance(other.Transform.position, player.Transform.position).ToString("F0") + " m, "
                                   + (other.gameObject.activeInHierarchy ? "active" : "switched off") + ", after " + goal);
                        }
                    }
                    Report(n + " bot(s) within " + reach + " m, " + after + " after someone");
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "killbot")
                {
                    // fika killbot: the nearest living bot dies where it stands (on the host), leaving a body.
                    Player nearest = null;
                    foreach (var other in Comfort.Common.Singleton<GameWorld>.Instance.AllAlivePlayersList)
                    {
                        if (other != null && other.IsAI && (nearest == null || Vector3.Distance(other.Transform.position, player.Transform.position) < Vector3.Distance(nearest.Transform.position, player.Transform.position)))
                        {
                            nearest = other;
                        }
                    }
                    if (nearest == null)
                    {
                        Report("FAILED no bot alive");
                        yield break;
                    }
                    var where = nearest.Transform.position;
                    nearest.ActiveHealthController.Kill(EDamageType.Bullet);
                    Report("killed " + nearest.Profile.Nickname + " at " + where.ToString("F1"));
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "bodies")
                {
                    int bodies = 0;
                    foreach (var corpse in UnityEngine.Object.FindObjectsOfType<EFT.Interactive.Corpse>())
                    {
                        bodies++;
                        if (bodies <= 6)
                        {
                            Report("  body at " + corpse.transform.position.ToString("F1") + " " + corpse.GetType().Name);
                        }
                    }
                    Report(bodies + " body/bodies in the raid");
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "door")
                {
                    // fika door [open|list]: the nearest door of the map (not one of this mod's): its state; "open" uses it as the game's menu would.
                    EFT.Interactive.Door near = null;
                    foreach (var door in UnityEngine.Object.FindObjectsOfType<EFT.Interactive.Door>())
                    {
                        if (door.Operatable && door.DoorState != EFT.Interactive.EDoorState.Locked
                            && (near == null || Vector3.Distance(door.transform.position, player.Transform.position) < Vector3.Distance(near.transform.position, player.Transform.position)))
                        {
                            near = door;
                        }
                    }
                    if (near == null)
                    {
                        Report("FAILED no usable door");
                        yield break;
                    }
                    if (parts.Length > 2 && parts[2] == "open")
                    {
                        player.Teleport(near.transform.position + near.transform.forward * 0.9f, false);
                        yield return new WaitForSecondsRealtime(1.5f);
                        var offeredDoor = InteractionContextHelper.GetAvailableActions(player.GetComponent<GamePlayerOwner>(), near);
                        if (offeredDoor == null || offeredDoor.Actions.Count == 0)
                        {
                            Report("FAILED the game offers nothing for door " + near.Id);
                            yield break;
                        }
                        Report("doing '" + offeredDoor.Actions[0].Name + "' on door " + near.Id);
                        offeredDoor.Actions[0].Action();
                        yield return new WaitForSecondsRealtime(3f);
                    }
                    Report("door " + near.Id + " at " + near.transform.position.ToString("F1") + " is " + near.DoorState);
                    yield break;
                }
                if (parts.Length > 2 && parts[1] == "doorstate")
                {
                    foreach (var door in UnityEngine.Object.FindObjectsOfType<EFT.Interactive.Door>())
                    {
                        // Some door ids hold a space.
                        if (door.Id == string.Join(" ", parts.Skip(2)))
                        {
                            Report("door " + door.Id + " is " + door.DoorState);
                            yield break;
                        }
                    }
                    Report("FAILED no such door");
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "camfx")
                {
                    // fika camfx: every switched-on component on the main camera, with its numbers, to compare two states.
                    var main = Camera.main;
                    if (main == null) { Report("FAILED no main camera"); yield break; }
                    if (parts.Length > 3 && (parts[2] == "off" || parts[2] == "on"))
                    {
                        // fika camfx off|on NAME: switch one of the camera's components, to find which one spoils a picture.
                        foreach (var part in main.GetComponents<Behaviour>())
                        {
                            if (part != null && part.GetType().Name == parts[3])
                            {
                                part.enabled = parts[2] == "on";
                                Report(parts[3] + " is now " + (part.enabled ? "on" : "off"));
                                yield break;
                            }
                        }
                        Report("FAILED no such component on the camera");
                        yield break;
                    }
                    Report("camera at " + main.transform.position.ToString("F1") + " hdr " + main.allowHDR + " far " + main.farClipPlane.ToString("F0"));
                    foreach (var part in main.GetComponents<Behaviour>())
                    {
                        if (part == null || !part.enabled) { continue; }
                        var numbers = new List<string>();
                        foreach (var field in part.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                        {
                            if (field.FieldType == typeof(float) || field.FieldType == typeof(bool) || field.FieldType == typeof(int) || field.FieldType == typeof(Color))
                            {
                                object value = null;
                                try { value = field.GetValue(part); } catch (Exception) { }
                                numbers.Add(field.Name + "=" + (value is float ? ((float)value).ToString("F3") : value + ""));
                            }
                        }
                        Report("  " + part.GetType().Name + ": " + string.Join(" ", numbers.Take(40).ToArray()));
                    }
                    yield break;
                }
                if (parts.Length > 2 && parts[1] == "shot")
                {
                    // fika shot NAME: a picture of this game's own screen, in a raid.
                    yield return new WaitForEndOfFrame();
                    Screenshot(parts[2]);
                    yield break;
                }
                if (parts.Length > 3 && parts[1] == "set" && parts[2] == "death")
                {
                    Death.OnDeath.Value = parts[3] == "everything" ? Death.Loss.LoseEverything : parts[3] == "gear" ? Death.Loss.LoseCarriedGear : Death.Loss.KeepItems;
                    Report("when you die: " + Death.OnDeath.Value);
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "secure")
                {
                    var secure = player.Profile.Inventory.Equipment.GetSlot(EFT.InventoryLogic.EquipmentSlot.SecuredContainer).ContainedItem as EFT.InventoryLogic.CompoundItem;
                    int inside = 0;
                    foreach (var grid in secure != null ? secure.Grids : new EFT.InventoryLogic.Grid[0])
                    {
                        inside += System.Linq.Enumerable.Count(grid.Items);
                    }
                    Report("secure container holds " + inside + " thing(s)");
                    yield break;
                }
                if (parts.Length > 3 && parts[1] == "wear")
                {
                    // fika wear WORD POINTS: wears down the carried thing whose name holds WORD, so there is something to repair.
                    foreach (var carried in LocalTrade.CarriedItems(player))
                    {
                        var repairable = carried.GetItemComponent<EFT.InventoryLogic.RepairableComponent>();
                        if (repairable != null && carried.ShortName.Localized().IndexOf(parts[2], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            repairable.Durability = Mathf.Max(1f, repairable.Durability - Number(parts[3]));
                            carried.RaiseRefreshEvent();
                            Report("worn: " + carried.ShortName.Localized() + " now " + repairable.Durability.ToString("F1") + "/" + repairable.MaxDurability.ToString("F1"));
                            yield break;
                        }
                    }
                    Report("FAILED nothing repairable named like that");
                    yield break;
                }
                if (parts.Length > 2 && parts[1] == "endforall")
                {
                    // fika endforall EXIT: what the hosting player's yes to "end the raid for everyone" does.
                    Regroup.EndForAll(parts[2]);
                    Report("ending the raid for everyone by " + parts[2]);
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "arrive")
                {
                    // fika arrive: what happens to someone who was dead when the group reached a new map.
                    yield return Regroup.ArriveAfterDeath();
                    Report("arrived after death; carrying " + LocalTrade.CarriedItems(player).Count + " item(s)");
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "dead")
                {
                    // fika dead: who is a ghost, who can be brought back, what the local player has to pay with.
                    Report("ghost now " + Afterlife.Dead + "; ghosts: " + Afterlife.Ghosts.Count + "; living: " + Afterlife.Living().Count
                           + "; roubles carried: " + (player != null ? Afterlife.CarriedRoubles(player) : 0));
                    foreach (var ghost in Afterlife.Revivable())
                    {
                        Report("  can bring back " + ghost.Value + " (" + ghost.Key + ")");
                    }
                    yield break;
                }
                if (parts.Length > 2 && parts[1] == "revive")
                {
                    // fika revive paid|free: bring back the first dead teammate, as the trader's or the medical station's entry does.
                    var ghosts = Afterlife.Revivable();
                    if (ghosts.Count == 0)
                    {
                        Report("FAILED nobody to bring back");
                        yield break;
                    }
                    if (parts[2] == "paid")
                    {
                        var paying = Afterlife.ReviveForMoney(ghosts[0].Key, ghosts[0].Value);
                        while (!paying.IsCompleted)
                        {
                            yield return null;
                        }
                        Report(paying.Result);
                    }
                    else
                    {
                        Report(Afterlife.ReviveFree(ghosts[0].Key, ghosts[0].Value));
                    }
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "start")
                {
                    Report(Net.Current.StartWaitingRaid());
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "raids")
                {
                    var listing = Coop.Raids();
                    while (!listing.IsCompleted)
                    {
                        yield return null;
                    }
                    foreach (var raid in listing.Result)
                    {
                        Report("  " + raid.Match + " host " + raid.Host + " owner " + raid.OwnerName + " on " + raid.Location + " " + raid.Status + " players " + raid.Players
                               + (raid.Headless ? " headless" : "") + (raid.Mine ? " (I was in it)" : ""));
                    }
                    Report(listing.Result.Count + " raid(s)");
                    yield break;
                }
                if (parts.Length > 1 && parts[1] == "join" && player == null && (parts.Length == 2 || parts[2] == "any"))
                {
                    // fika join: the first raid on the server's list, as the menu's Join does.
                    var listing = Coop.Raids();
                    while (!listing.IsCompleted)
                    {
                        yield return null;
                    }
                    if (listing.Result.Count == 0)
                    {
                        Report("FAILED no raid to join");
                        yield break;
                    }
                    _ = Coop.Join(listing.Result[0]);
                    Report("joining " + listing.Result[0].Match + " on " + listing.Result[0].Location);
                    yield break;
                }
                if (parts.Length > 1 && player == null)
                {
                    Seamless.JoinHost = parts[1] == "join" ? parts[2] : null;
                    MainMenu.Continue();
                    Report("Continue pressed through Fika as " + parts[1]);
                    yield break;
                }
                Report("role " + Net.Role + ", host " + Net.IsHost + ", co-op " + Net.Coop + ", headless " + Net.Headless + ", Fika installed " + Net.FikaInstalled
                       + ", humans " + Net.Current.Humans().Count + ", local player " + (player != null));
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
            if (verb == "guide")
            {
                // guide open | next | back | close | page N | dontshow on|off: the guide over the main menu.
                string what = parts.Length > 1 ? parts[1] : "";
                if (what == "open") { MainMenu.OpenGuide(); }
                else if (what == "next") { Guide.Next(); }
                else if (what == "back") { Guide.Back(); }
                else if (what == "close") { Guide.Close(); }
                else if (what == "page" && parts.Length > 2) { Guide.Go((int)Number(parts[2]) - 1); }
                else if (what == "dontshow" && parts.Length > 2) { Guide.SetDontShow(parts[2] == "on"); }
                Report("guide " + (Guide.IsOpen ? "open at page " + Guide.PageNumber : "closed") + "; shown at start: " + Guide.Show.Value + "; when you die: " + Death.OnDeath.Value);
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
            if (player == null && verb != "wait" && verb != "post" && verb != "settings" && verb != "menukey" && verb != "physicsold" && verb != "place" && verb != "screenshot")
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
                            // In co-op: as a move the game sends to the others, which is what dragging does.
                            var move = ItemManipulator.Move(item, place, player.InventoryController, Net.Coop);
                            if (move.Failed)
                            {
                                Report("FAILED move: " + move.Error);
                                break;
                            }
                            if (Net.Coop)
                            {
                                player.InventoryController.TryRunNetworkTransaction(move, null);
                            }
                            else
                            {
                                move.Value.RaiseEvents(player.InventoryController, CommandStatus.Begin);
                                move.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
                            }
                            moved++;
                        }
                        Report("moved " + moved + " into the stash");
                    }
                    else if (what == "add" && parts.Length > 2)
                    {
                        // stash add TEMPLATE: a new thing of that kind, made straight into the stash. For tests only.
                        var made = Comfort.Common.Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), parts[2], null);
                        var spot = stash.Grids[0].FindLocationForItem(made);
                        var put = spot == null ? default : ItemManipulator.Add(made, spot, player.InventoryController, false);
                        if (spot == null || put.Failed)
                        {
                            Report("FAILED could not add it: " + (spot == null ? "no room in the stash" : put.Error.ToString()));
                        }
                        else
                        {
                            put.Value.RaiseEvents(player.InventoryController, CommandStatus.Begin);
                            put.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
                            Report("added " + HideoutStations.ItemName(parts[2]) + " to the stash");
                        }
                    }
                    else if (what == "sort")
                    {
                        Report(HideoutStash.Sort() ? "stash sorted" : "FAILED stash not sorted");
                    }
                    else if (what == "close")
                    {
                        player.GetComponent<GamePlayerOwner>().CloseInventoryIfOpen();
                        Report("stash screen closed");
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
                            var move = room == null ? default : ItemManipulator.Move(item, room, player.InventoryController, Net.Coop);
                            if (room == null || move.Failed)
                            {
                                Report("FAILED could not take it: " + (room == null ? "no room in carried gear" : move.Error.ToString()));
                            }
                            else
                            {
                                if (Net.Coop)
                                {
                                    player.InventoryController.TryRunNetworkTransaction(move, null);
                                }
                                else
                                {
                                    move.Value.RaiseEvents(player.InventoryController, CommandStatus.Begin);
                                    move.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
                                }
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
                case "weather":
                {
                    // weather rain R [fog F] [cloud C] | weather off | weather letrain on|off: forces the
                    // weather through the game's own debug switch, and says what the rain is doing now.
                    var weather = EFT.Weather.WeatherController.Instance;
                    if (weather == null)
                    {
                        Report("FAILED no weather on this map");
                        break;
                    }
                    if (parts.Length > 1 && parts[1] == "off")
                    {
                        weather.WeatherDebug.Enabled = false;
                    }
                    else if (parts.Length > 2 && parts[1] == "letrain")
                    {
                        IndoorRain.LetRain = parts[2] == "on";
                    }
                    else if (parts.Length > 2 && parts[1] == "owncalendar")
                    {
                        WorldClock.WeatherByOwnCalendar = parts[2] == "on";
                    }
                    else if (parts.Length > 2)
                    {
                        weather.WeatherDebug.Enabled = true;
                        for (int i = 1; i + 1 < parts.Length; i += 2)
                        {
                            if (parts[i] == "rain") weather.WeatherDebug.Rain = Number(parts[i + 1]);
                            if (parts[i] == "fog") weather.WeatherDebug.Fog = Number(parts[i + 1]);
                            if (parts[i] == "cloud") weather.WeatherDebug.CloudDensity = Number(parts[i + 1]);
                        }
                    }
                    yield return new WaitForSecondsRealtime(2f);
                    Report("weather: forced " + weather.WeatherDebug.Enabled + ", curve rain " + weather.WeatherCurve.Rain.ToString("F2") + " fog " + weather.WeatherCurve.Fog.ToString("F3")
                           + "; rain drawn " + RainController.Intensity.ToString("F2") + ", wetting " + RainController.Wetting.ToString("F2") + ", camera under rain " + RainController.IsCameraUnderRain
                           + "; shader rain " + Shader.GetGlobalFloat("_RainIntensity").ToString("F2") + "; indoors " + IndoorLight.Inside + ", let rain " + IndoorRain.LetRain
                           + "; read at " + weather.WeatherCurve.CurrentNormalizedTimeValue.ToString("F3") + " of the server's weather, calendar shift " + WorldClock.DescribeWeatherShift() + ", own calendar " + WorldClock.WeatherByOwnCalendar);
                    break;
                }
                case "airdrop":
                {
                    // airdrop [in SECONDS]: what the map's airdrop clock says; "in" has the next plane due
                    // that many seconds from now, certain to come.
                    var drops = AirdropHoldPatch.Seen;
                    if (drops == null)
                    {
                        Report("FAILED no airdrop clock has ticked on this map");
                        break;
                    }
                    if (parts.Length > 2 && parts[1] == "in")
                    {
                        drops._nextSpawnAirdropTime = drops._spawnAirdropTimer.Time + Number(parts[2]);
                        drops.CachedAirdropParameters.PlaneAirdropChance = 1f;
                        drops.CachedAirdropParameters.PlaneAirdropEnd = 100000;
                        if (drops._planeAirdropMax < 5f) drops._planeAirdropMax = 5f;
                    }
                    Report("airdrop: can come " + drops.CanSummonAirdrop + ", clock " + drops._spawnAirdropTimer.Time.ToString("F0") + " s, held " + drops._spawnAirdropTimer.IsPaused
                           + ", next due at " + drops._nextSpawnAirdropTime.ToString("F0") + " s, sent so far " + drops._currentAirdropsCount + " of " + drops._planeAirdropMax
                           + ", chance " + drops.PlaneAirdropChance + ", ends " + drops.PlaneAirdropEnd + ", raid standing still " + BotFreeze.On);
                    break;
                }
                case "sky":
                {
                    // sky set ROOM POWERED DARK: try other indoor levels for the sky's ambient colour.
                    if (parts.Length > 4 && parts[1] == "set")
                    {
                        float r = Number(parts[2]), h = Number(parts[3]), d = Number(parts[4]);
                        IndoorLight.RoomSky = new Color(r, r, r * 1.1f);
                        IndoorLight.HideoutSky = new Color(h, h, h * 1.1f);
                        IndoorLight.HideoutSkyDark = new Color(d, d, d * 1.1f);
                        if (parts.Length > 6)
                        {
                            IndoorLight.Mirror = Number(parts[5]);
                            IndoorLight.MirrorDark = Number(parts[6]);
                        }
                        Report("indoor sky levels set");
                        break;
                    }
                    // What lights the scene from outside: sky lights and the fill light.
                    foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>(true))
                    {
                        if (light.type == LightType.Directional && light.enabled && light.gameObject.activeInHierarchy)
                        {
                            Report("directional '" + light.name + "' scene " + light.gameObject.scene.name + " enabled " + light.enabled + " active " + light.gameObject.activeInHierarchy
                                   + " intensity " + light.intensity.ToString("F2") + " colour " + light.color);
                        }
                    }
                    Report("ambient mode " + RenderSettings.ambientMode + " light " + RenderSettings.ambientLight + " intensity " + RenderSettings.ambientIntensity.ToString("F2")
                           + " reflection " + RenderSettings.reflectionIntensity.ToString("F2") + " | inside " + (HideoutDoors.Inside ?? "no") + " powered " + HideoutPower.Powered);
                    foreach (string name in new[] { "TOD_AmbientColor", "TOD_LightColor", "TOD_SunLightColor", "TOD_MoonLightColor", "TOD_GroundColor", "TOD_FogColor", "TOD_SunSkyColor", "_AmbientColor", "unity_AmbientSky", "_SunColor", "_LightColor0" })
                    {
                        Report("global " + name + " " + Shader.GetGlobalColor(name));
                    }
                    Report("global floats: TOD_Brightness " + Shader.GetGlobalFloat("TOD_Brightness") + " TOD_Contrast " + Shader.GetGlobalFloat("TOD_Contrast") + " TOD_Fogginess " + Shader.GetGlobalFloat("TOD_Fogginess"));
                    int on = 0, total = 0;
                    string kinds = "";
                    foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
                    {
                        total++;
                        if (light.enabled && light.intensity > 0.01f)
                        {
                            on++;
                            if (on <= 12)
                            {
                                kinds += " [" + light.type + " '" + light.name + "' " + light.intensity.ToString("F1") + " r" + light.range.ToString("F0") + " " + light.gameObject.scene.name + "]";
                            }
                        }
                    }
                    Report("lights on " + on + " of " + total + ":" + kinds);
                    foreach (var probe in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>())
                    {
                        if (probe.enabled && Vector3.Distance(probe.transform.position, player.Transform.position) < 60f)
                        {
                            Report("reflection probe '" + probe.name + "' " + probe.mode + " intensity " + probe.intensity.ToString("F2") + " size " + probe.size + " scene " + probe.gameObject.scene.name);
                        }
                    }
                    break;
                }
                case "waitat":
                {
                    // waitat [HOURS]: what "Wait until morning" at a trader does (until morning with no number).
                    HideoutSleep.Wait(parts.Length > 1 ? Number(parts[1]) : HideoutSleep.HoursUntilMorning());
                    yield return new WaitForSecondsRealtime(6f);
                    Report("clock: " + WorldClock.Describe(WorldClock.Now));
                    goto case "vitals";
                }
                case "tradersave":
                {
                    // Chooses "Save" at the trader of the room the player is in.
                    var room = RoomHolding(player.Transform.position);
                    TraderInteractable owner = null;
                    foreach (var trader in UnityEngine.Object.FindObjectsOfType<TraderInteractable>())
                    {
                        if (room != null && trader.TraderId == room.TraderId)
                        {
                            owner = trader;
                        }
                    }
                    if (owner != null)
                    {
                        Report("the trader offers: " + string.Join(" | ", owner.GetActions().Actions.Select(x => x.Name)));
                    }
                    if (owner == null || (owner.Exfil == null && string.IsNullOrEmpty(owner.SaveExit)))
                    {
                        Report("not in a trader's room where a save can be made");
                        break;
                    }
                    var saved = SaveGame.SaveNow("harness, at a trader", owner.Exfil != null ? owner.Exfil.gameObject.name : owner.SaveExit);
                    while (!saved.IsCompleted)
                    {
                        yield return null;
                    }
                    Report("trader save: " + saved.Result);
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
                case "panel":
                {
                    // panel NUMBER: open a station's window; panel close; panel press WORDS; panel lines.
                    string what = parts.Length > 1 ? parts[1].ToLowerInvariant() : "lines";
                    int number;
                    if (what == "close")
                    {
                        HideoutPanel.Close();
                        Report("window closed");
                    }
                    else if (what == "filter")
                    {
                        // panel filter KIND [WORDS]: KIND is ALL, CAN (for CAN MAKE) or a kind's name with _ for spaces.
                        string kind = parts.Length > 2 ? parts[2].Replace('_', ' ').ToUpperInvariant() : "ALL";
                        HideoutPanel.Filter(kind == "CAN" ? "CAN MAKE" : kind, parts.Length > 3 ? string.Join(" ", parts, 3, parts.Length - 3) : "");
                        yield return null;
                        what = "lines";
                    }
                    else if (what == "press")
                    {
                        string words = string.Join(" ", parts, 2, parts.Length - 2);
                        Report(HideoutPanel.Press(words) ? "pressed " + words : "FAILED no usable button starts with " + words);
                    }
                    else if (int.TryParse(what, out number))
                    {
                        HideoutPanel.Open(number, ((EAreaType)number).ToString());
                        yield return null;
                        Report(HideoutPanel.IsOpen ? "window open for station " + number : "FAILED window did not open");
                    }
                    if (what == "lines" || int.TryParse(what, out number))
                    {
                        foreach (var entry in HideoutPanel.Lines())
                        {
                            Report("  " + entry);
                        }
                    }
                    break;
                }
                case "unpack":
                {
                    // unpack WORD_OR_TEMPLATE: unpack a sealed case at hand, as the item's menu would.
                    string word = parts.Length > 1 ? parts[1] : "";
                    var held = player.Profile.Inventory.Equipment.GetAllItems().ToList();
                    if (HideoutStash.Current != null)
                    {
                        held.AddRange(HideoutStash.Current.GetAllItems());
                    }
                    var box = held.FirstOrDefault(i => i.TemplateId.ToString() == word)
                              ?? held.FirstOrDefault(i => i.ItemInteractionButtons.Contains(EItemInfoButton.Unpack) && HideoutStations.FullName(i.TemplateId.ToString()).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (box == null)
                    {
                        Report("FAILED nothing to unpack matches '" + word + "'");
                        break;
                    }
                    int before = HideoutStash.Count();
                    var opening = EFT.UI.ItemUiContext.Instance.UnpackItem(box);
                    while (!opening.IsCompleted)
                    {
                        yield return null;
                    }
                    Report((opening.Result.Succeed ? "unpacked " : "FAILED " + opening.Result.Error + " ") + HideoutStations.FullName(box.TemplateId.ToString())
                           + "; stash " + before + " -> " + HideoutStash.Count() + " item(s)");
                    break;
                }
                case "modify":
                {
                    // modify [N]: open the weapon modding screen for the Nth weapon the Workbench would list.
                    var guns = HideoutGunsmith.Weapons(player);
                    int n = parts.Length > 1 ? (int)Number(parts[1]) : 0;
                    if (n >= guns.Count)
                    {
                        Report("FAILED only " + guns.Count + " weapon(s)");
                        break;
                    }
                    string failed = HideoutGunsmith.Open(guns[n]);
                    yield return new WaitForSecondsRealtime(1.5f);
                    Report(failed ?? "modding opened for " + guns[n].ShortName.Localized() + (HideoutGunsmith.InStash(guns[n]) ? " (in the stash)" : " (carried)"));
                    break;
                }
                case "vitalcheck":
                {
                    // Would the game let a vital part of the first listed weapon be taken off into the stash?
                    var guns = HideoutGunsmith.Weapons(player);
                    int which = parts.Length > 1 ? (int)Number(parts[1]) : 0;
                    if (which < guns.Count)
                    {
                        guns = new List<Weapon> { guns[which] };
                    }
                    var stash = HideoutStash.Current;
                    Item part = null;
                    foreach (var slot in guns.Count > 0 ? ((CompoundItem)guns[0]).AllSlots : new Slot[0])
                    {
                        if (slot.Required && slot.ContainedItem != null)
                        {
                            part = slot.ContainedItem;
                            break;
                        }
                    }
                    if (part == null || stash == null)
                    {
                        Report("FAILED no weapon with a vital part, or no stash");
                        break;
                    }
                    var to = stash.Grids[0].FindLocationForItem(part);
                    bool lifted = HideoutGunsmith.Lift();
                    var check = ItemManipulator.MovePathCheck(part, to, player.InventoryController);
                    HideoutGunsmith.Restore(lifted);
                    Report("taking " + part.ShortName.Localized() + " off " + guns[0].ShortName.Localized() + ": " + (check.Failed ? "refused: " + check.Error.GetType().Name + " " + check.Error : "allowed"));
                    break;
                }
                case "modclose":
                    HideoutGunsmith.Close();
                    yield return new WaitForSecondsRealtime(1f);
                    Report("modding closed");
                    break;
                case "relight":
                    Report(IndoorLight.Repair());
                    break;
                case "near":
                {
                    // near X Y Z [R]: what is drawn around a point, and whether it is ours. Does not move the player.
                    var at = new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3]));
                    float r = parts.Length > 4 ? Number(parts[4]) : 2.5f;
                    var seen = new HashSet<string>();
                    foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    {
                        if (Vector3.Distance(renderer.bounds.center, at) > r)
                        {
                            continue;
                        }
                        string found = renderer.name + (renderer.enabled && renderer.gameObject.activeInHierarchy ? "" : " (not drawn)") + " under " + renderer.transform.root.name + " centre " + renderer.bounds.center.ToString("F1");
                        if (seen.Add(found) && seen.Count <= 30)
                        {
                            Report("  " + found);
                        }
                    }
                    Report(seen.Count + " drawn thing(s) within " + r + " m");
                    break;
                }
                case "shelfput":
                    // shelfput STATION TEMPLATE: lay a made-up item in a station's container.
                    Report(HideoutShelves.PutTest((int)Number(parts[1]), parts[2]));
                    break;
                case "photo":
                    // Takes a clean picture, as the picture key does.
                    Photo.Take();
                    yield return new WaitForSecondsRealtime(1.5f);
                    Report("picture asked for; see " + Photo.Folder);
                    break;
                case "shelves":
                    foreach (var entry in HideoutShelves.Describe())
                    {
                        Report(entry);
                    }
                    break;
                case "uigrid":
                {
                    // Every item grid on screen, biggest first, with where it sits: for fixing a screen's layout.
                    foreach (var grid in UnityEngine.Object.FindObjectsOfType<EFT.UI.DragAndDrop.GridView>())
                    {
                        var rect = (RectTransform)grid.transform;
                        if (!grid.isActiveAndEnabled || rect.rect.width < 500f)
                        {
                            continue;
                        }
                        Report("grid magnified " + grid.IsMagnified + ", has grid " + (grid.Grid != null) + ", items in it " + (grid.Grid != null ? grid.Grid.ItemCollection.Count : -1)
                               + ", pictures " + grid.GetComponentsInChildren<EFT.UI.DragAndDrop.ItemView>(true).Length + ", children " + grid.transform.childCount);
                        Report("grid " + rect.rect.size.ToString("F0") + " at " + rect.position.ToString("F0") + " anchors " + rect.anchorMin + "-" + rect.anchorMax + " pivot " + rect.pivot + " scale " + rect.lossyScale.x.ToString("F2"));
                        var up = rect.parent;
                        for (int i = 0; i < 6 && up != null; i++, up = up.parent)
                        {
                            var r = up as RectTransform;
                            var kinds = new List<string>();
                            foreach (var c in up.GetComponents<Component>())
                            {
                                if (!(c is RectTransform) && !(c is CanvasRenderer))
                                {
                                    kinds.Add(c.GetType().Name);
                                }
                            }
                            Report("   in " + up.name + (r != null ? " " + r.rect.size.ToString("F0") + " at " + r.position.ToString("F0") + " pivot " + r.pivot : "") + " [" + string.Join(",", kinds.ToArray()) + "]");
                        }
                    }
                    break;
                }
                case "fps":
                {
                    // fps [SECONDS]: the frame rate averaged over a few seconds, and what the interior is doing.
                    float span = parts.Length > 1 ? Number(parts[1]) : 3f;
                    int frames = 0;
                    float began = Time.unscaledTime;
                    while (Time.unscaledTime - began < span)
                    {
                        frames++;
                        yield return null;
                    }
                    int lights = 0;
                    foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
                    {
                        if (light.isActiveAndEnabled)
                        {
                            lights++;
                        }
                    }
                    Report((frames / (Time.unscaledTime - began)).ToString("F0") + " frames a second over " + span.ToString("0") + " s; lights on " + lights
                           + "; bots switched off " + BotFreeze.Count + "; interior loaded " + HideoutBunker.Loaded + ", parked " + HideoutBunker.Parked + ", inside " + (HideoutDoors.Inside ?? "no"));
                    break;
                }
                case "lights":
                {
                    // lights: what the interior's lamps cost. lights shadows off|on, lights far METRES.
                    int on = 0, shadowed = 0, changed = 0;
                    string what = parts.Length > 1 ? parts[1] : "";
                    foreach (var root in HideoutBunker.Roots())
                    {
                        foreach (var light in root.GetComponentsInChildren<Light>(true))
                        {
                            if (!light.isActiveAndEnabled)
                            {
                                continue;
                            }
                            on++;
                            if (what == "shadows")
                            {
                                light.shadows = parts.Length > 2 && parts[2] == "on" ? LightShadows.Soft : LightShadows.None;
                                changed++;
                            }
                            if (light.shadows != LightShadows.None)
                            {
                                shadowed++;
                            }
                        }
                    }
                    Report("interior lamps lit " + on + ", casting shadows " + shadowed + ", changed " + changed);
                    break;
                }
                case "park":
                    HideoutBunker.Park(parts.Length < 2 || parts[1] != "off");
                    Report("interior parked: " + HideoutBunker.Parked);
                    break;
                case "locate":
                    // locate WORD: where hideout objects of that name are.
                    foreach (var entry in RoomProps.Locate(parts.Length > 1 ? parts[1] : "door"))
                    {
                        Report("  " + entry);
                    }
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
                case "frame":
                {
                    // frame trader NAME DIST [TURN] | frame hideout N DIST [TURN] | frame point X Y Z YAW DIST [TURN]:
                    // stand about DIST metres off a door, on the ground, with a clear view of it, look at it
                    // and take a clean picture. TURN (degrees) starts the search off to one side.
                    Vector3 target = Vector3.zero;
                    float yaw = 0f;
                    int at = 0;
                    bool known = false;
                    string kind = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
                    if (kind == "trader" && parts.Length > 3)
                    {
                        foreach (var room in TraderRoom.Rooms)
                        {
                            if (room.OutsideDoor != null && TraderConfig.NameOf(room.TraderId).ToLowerInvariant().StartsWith(parts[2].ToLowerInvariant()))
                            {
                                target = room.OutsideDoor.transform.position;
                                yaw = room.OutsideDoor.transform.eulerAngles.y;
                                known = true;
                                break;
                            }
                        }
                        at = 3;
                    }
                    else if (kind == "hideout" && parts.Length > 3)
                    {
                        int wanted = (int)Number(parts[2]), index = 0;
                        foreach (var front in HideoutDoors.Fronts.Values)
                        {
                            if (index++ == wanted)
                            {
                                target = front.Key + Vector3.up * 1.0f;
                                yaw = front.Value;
                                known = true;
                            }
                        }
                        at = 3;
                    }
                    else if (kind == "point" && parts.Length > 6)
                    {
                        target = new Vector3(Number(parts[2]), Number(parts[3]), Number(parts[4]));
                        yaw = Number(parts[5]);
                        known = true;
                        at = 6;
                    }
                    if (!known)
                    {
                        Report("FAILED no such door");
                        break;
                    }
                    float wantedReach = Number(parts[at]);
                    float turn = parts.Length > at + 1 ? Number(parts[at + 1]) : 0f;
                    int solid = LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain");
                    bool placed = false;
                    Vector3 eye = Vector3.zero;
                    foreach (float share in new[] { 1f, 0.8f, 0.6f, 0.45f, 0.3f })
                    {
                        foreach (float side in new[] { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 180f, 160f, -160f, 140f, -140f, 90f, -90f })
                        {
                            var direction = Quaternion.Euler(0f, yaw + turn + side, 0f) * Vector3.forward;
                            var spot = target + direction * (wantedReach * share);
                            RaycastHit floor;
                            // Look for ground from just above the door's height first, so roofs and branches are passed by.
                            var low = new Vector3(spot.x, target.y + 1.2f, spot.z);
                            bool ground = Physics.Raycast(low, Vector3.down, out floor, 15f, solid, QueryTriggerInteraction.Ignore)
                                          || Physics.Raycast(spot + Vector3.up * 25f, Vector3.down, out floor, 45f, solid, QueryTriggerInteraction.Ignore);
                            if (!ground || floor.point.y < target.y - 10f || floor.point.y > target.y + 12f)
                            {
                                continue;
                            }
                            var candidate = floor.point + Vector3.up * 1.6f;
                            var toDoor = target - candidate;
                            // A clear view up to the door; its own frame does not count.
                            if (Physics.Linecast(candidate, target - toDoor.normalized * 0.8f, solid, QueryTriggerInteraction.Ignore))
                            {
                                continue;
                            }
                            player.Teleport(floor.point + Vector3.up * 0.05f, false);
                            eye = candidate;
                            placed = true;
                            Report("standing " + toDoor.magnitude.ToString("F0") + " m off at " + floor.point.ToString("F1") + " (turned " + (turn + side).ToString("F0") + ")");
                            break;
                        }
                        if (placed)
                        {
                            break;
                        }
                    }
                    if (!placed)
                    {
                        Report("FAILED no clear place to stand");
                        break;
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        // Turn again once the player has settled on the ground.
                        var to = target - (player.Transform.position + Vector3.up * 1.6f);
                        player.Rotation = new Vector2(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
                        yield return new WaitForSecondsRealtime(0.25f);
                    }
                    ReportOnScreen(target);
                    break;
                }
                case "body":
                {
                    // body hide | body show: the player's own arms and gun, out of pictures.
                    bool show = parts.Length > 1 && parts[1].ToLowerInvariant() == "show";
                    if (!show)
                    {
                        // Put the gun away first; the hands are drawn again with every weapon change.
                        try
                        {
                            player.SetEmptyHands(null);
                        }
                        catch (Exception e)
                        {
                            Report("hands not emptied: " + e.Message);
                        }
                        yield return new WaitForSecondsRealtime(2f);
                    }
                    int count = 0;
                    foreach (var renderer in player.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.forceRenderingOff = !show;
                        count++;
                    }
                    Report((show ? "shown " : "hidden ") + count + " parts of the player");
                    break;
                }
                case "film":
                {
                    // film start NAME [FPS] [WIDTH] | film stop: saves every frame as the screen shows it,
                    // interface and all, to test/film/NAME, while other commands go on.
                    if (parts.Length > 2 && parts[1] == "start")
                    {
                        FilmStop();
                        _filmName = parts[2];
                        _filmFps = parts.Length > 3 ? (int)Number(parts[3]) : 12;
                        _filmWidth = parts.Length > 4 ? (int)Number(parts[4]) : 960;
                        _filmFrames = 0;
                        string folder = Path.Combine(Dir, "film", _filmName);
                        if (Directory.Exists(folder))
                        {
                            Directory.Delete(folder, true);
                        }
                        Directory.CreateDirectory(folder);
                        Time.captureFramerate = _filmFps;
                        _film = Plugin.Instance.StartCoroutine(Film(folder));
                        Report("filming " + _filmName + " at " + _filmFps + " frames a second");
                    }
                    else
                    {
                        Report("film " + _filmName + " stopped: " + _filmFrames + " frame(s)");
                        FilmStop();
                    }
                    break;
                }
                case "walk":
                {
                    // walk X Y Z [SECONDS]: really walk to a point, turning towards it, as a player would.
                    var goal = new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3]));
                    float until = Time.time + (parts.Length > 4 ? Number(parts[4]) : 12f);
                    float left = 99f;
                    while (Time.time < until)
                    {
                        var to = goal - player.Transform.position;
                        to.y = 0f;
                        left = to.magnitude;
                        if (left < 0.35f)
                        {
                            break;
                        }
                        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                        var now = player.Rotation;
                        float yaw = Mathf.MoveTowardsAngle(now.x, want, 200f * Time.deltaTime);
                        player.Rotation = new Vector2(yaw, Mathf.MoveTowards(now.y, 0f, 60f * Time.deltaTime));
                        // Only step off once roughly facing the way to go.
                        player.Move(Mathf.Abs(Mathf.DeltaAngle(yaw, want)) < 35f ? new Vector2(0f, 1f) : Vector2.zero);
                        yield return null;
                    }
                    player.Move(Vector2.zero);
                    Report((left < 0.35f ? "walked to " : "FAILED walk stopped " + left.ToString("F1") + " m short of ") + goal.ToString("F1")
                           + ", now at " + player.Transform.position.ToString("F1"));
                    break;
                }
                case "turn":
                {
                    // turn YAW PITCH [SECONDS]: look round smoothly.
                    float wantYaw = Number(parts[1]), wantPitch = parts.Length > 2 ? Number(parts[2]) : 0f;
                    float seconds = parts.Length > 3 ? Number(parts[3]) : 1f;
                    var from = player.Rotation;
                    for (float t = 0f; t < seconds; t += Time.deltaTime)
                    {
                        float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                        player.Rotation = new Vector2(Mathf.LerpAngle(from.x, wantYaw, k), Mathf.Lerp(from.y, wantPitch, k));
                        yield return null;
                    }
                    player.Rotation = new Vector2(wantYaw, wantPitch);
                    break;
                }
                case "act":
                {
                    // act [N]: the Nth action (first by default) the game offers for the thing looked at.
                    var seenNow = player.InteractableObject;
                    if (seenNow == null)
                    {
                        Report("FAILED nothing offered");
                        break;
                    }
                    var offered = InteractionContextHelper.GetAvailableActions(player.GetComponent<GamePlayerOwner>(), seenNow);
                    int which = parts.Length > 1 ? (int)Number(parts[1]) - 1 : 0;
                    if (offered == null || offered.Actions.Count <= which || offered.Actions[which].Disabled)
                    {
                        Report("FAILED no such action on " + seenNow.name);
                        break;
                    }
                    Report("doing '" + offered.Actions[which].Name + "' on " + seenNow.name + " (offered: "
                           + string.Join(", ", offered.Actions.Select(x => x.Name)) + ")");
                    offered.Actions[which].Action();
                    break;
                }
                case "pick":
                {
                    // pick [WORD] [SECONDS]: look at the thing on the table whose name holds WORD, and take it
                    // through the game's own menu, as a player would.
                    var thing = RewardTable.Find(parts.Length > 1 ? parts[1].Replace('_', ' ') : "");
                    if (thing == null)
                    {
                        Report("FAILED nothing like that on a table");
                        break;
                    }
                    // Where the eye may rest on it: the middle of what can be touched, then of what can be
                    // seen, then where the object itself says it is, each also a little lower.
                    var spots = new List<Vector3>();
                    foreach (var solidPart in thing.GetComponentsInChildren<Collider>())
                    {
                        if (solidPart.enabled)
                        {
                            spots.Add(solidPart.bounds.center);
                        }
                    }
                    foreach (var shown in thing.GetComponentsInChildren<Renderer>())
                    {
                        spots.Add(shown.bounds.center);
                    }
                    spots.Add(thing.transform.position);
                    foreach (var spot in spots.ToArray())
                    {
                        spots.Add(spot + Vector3.down * 0.03f);
                    }
                    float took = parts.Length > 2 ? Number(parts[2]) : 0.8f;
                    var cam = Camera.main;
                    bool found = false;
                    for (int n = 0; n < spots.Count && !found; n++)
                    {
                        var first = player.Rotation;
                        float turnTime = n == 0 ? took : 0.25f;
                        for (float t = 0f; t < turnTime + 0.7f; t += Time.deltaTime)
                        {
                            // Aimed afresh each frame: the eyes move as the head tilts.
                            var from = cam != null ? cam.transform.position : player.Transform.position + Vector3.up * 1.55f;
                            var sight = spots[n] - from;
                            float aimYaw = Mathf.Atan2(sight.x, sight.z) * Mathf.Rad2Deg;
                            float aimPitch = -Mathf.Atan2(sight.y, new Vector2(sight.x, sight.z).magnitude) * Mathf.Rad2Deg;
                            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / turnTime));
                            player.Rotation = new Vector2(Mathf.LerpAngle(first.x, aimYaw, k), Mathf.Lerp(first.y, aimPitch, k));
                            if (t > turnTime + 0.3f && player.InteractableObject != null
                                && player.InteractableObject.transform.IsChildOf(thing.transform.root))
                            {
                                found = true;
                                break;
                            }
                            yield return null;
                        }
                    }
                    var got = player.InteractableObject;
                    var menu = found && got != null ? InteractionContextHelper.GetAvailableActions(player.GetComponent<GamePlayerOwner>(), got) : null;
                    if (menu == null || menu.Actions.Count == 0 || menu.Actions[0].Disabled)
                    {
                        Report("FAILED the game offers nothing usable for " + thing.name + " from here ("
                               + Vector3.Distance(player.Transform.position, thing.transform.position).ToString("F1") + " m, tried " + spots.Count + " spots, "
                               + (found ? "seen but " + (menu == null || menu.Actions.Count == 0 ? "no actions" : "'" + menu.Actions[0].Name + "' is off") : "never under the eye") + ")");
                        break;
                    }
                    yield return new WaitForSecondsRealtime(0.6f);
                    Report("doing '" + menu.Actions[0].Name + "' on " + got.name);
                    menu.Actions[0].Action();
                    break;
                }
                case "turnto":
                {
                    // turnto X Y Z [SECONDS]: look smoothly at a point.
                    var point = new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3]));
                    float time = parts.Length > 4 ? Number(parts[4]) : 0.8f;
                    var view = Camera.main;
                    var eyes = view != null ? view.transform.position : player.Transform.position + Vector3.up * 1.55f;
                    var way = point - eyes;
                    float toYaw = Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg;
                    float toPitch = -Mathf.Atan2(way.y, new Vector2(way.x, way.z).magnitude) * Mathf.Rad2Deg;
                    var start = player.Rotation;
                    for (float t = 0f; t < time; t += Time.deltaTime)
                    {
                        float k = Mathf.SmoothStep(0f, 1f, t / time);
                        player.Rotation = new Vector2(Mathf.LerpAngle(start.x, toYaw, k), Mathf.Lerp(start.y, toPitch, k));
                        yield return null;
                    }
                    player.Rotation = new Vector2(toYaw, toPitch);
                    break;
                }
                case "trade":
                {
                    // trade open | trade close | trade list [WORD] | trade buy WORD [COUNT]: the trade screen of the
                    // room's trader, and a purchase made the way the screen makes it: select, pay from carried money, deal.
                    var roomNow = RoomHolding(player.Transform.position);
                    string what = parts.Length > 1 ? parts[1] : "";
                    if (what == "close")
                    {
                        if (TradeOpener.Screen != null)
                        {
                            TradeOpener.Screen.CloseScreen();
                        }
                        Report("trade screen closed");
                        break;
                    }
                    if (roomNow == null)
                    {
                        Report("FAILED not in a trader's room");
                        break;
                    }
                    if (what == "open")
                    {
                        TradeOpener.Open(roomNow.TraderId, TraderConfig.NameOf(roomNow.TraderId));
                        yield return new WaitForSecondsRealtime(1.5f);
                        Report(TradeOpener.Screen != null ? "trade screen open" : "FAILED trade screen did not open");
                        break;
                    }
                    var dealer = SPT.Reflection.Utils.ClientAppUtils.GetClientApp().Session.GetTrader(roomNow.TraderId);
                    var goods = dealer?.CurrentAssortment;
                    if (goods == null)
                    {
                        Report("FAILED the trader's goods are not loaded; trade open first");
                        break;
                    }
                    var onSale = ((CompoundItem)goods.TraderController.RootItem).Grids.SelectMany(g => g.Items).ToList();
                    string word = parts.Length > 2 ? parts[2].Replace('_', ' ') : "";
                    var matching = onSale.Where(i => i.ShortName.Localized().IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
                                                     || i.TemplateId.ToString() == word).ToList();
                    if (what == "sell")
                    {
                        // trade sell WORD: sells a carried or worn thing, the way dragging it to the table and pressing Deal would.
                        var mine = LocalTrade.CarriedItems(player).FirstOrDefault(i => i.ShortName.Localized().IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (mine == null)
                        {
                            Report("FAILED nothing carried matches '" + word + "'");
                            break;
                        }
                        if (!goods.QuickFindTradingAppropriatePlace(mine))
                        {
                            Report("FAILED " + mine.ShortName.Localized() + " could not be put on the selling table");
                            break;
                        }
                        yield return new WaitForSecondsRealtime(1f);
                        int price = goods.PreparedSum.Amount;
                        goods.Sell();
                        yield return new WaitForSecondsRealtime(2f);
                        bool still = LocalTrade.CarriedItems(player).Contains(mine);
                        Report((still ? "FAILED still carrying " : "sold ") + mine.ShortName.Localized() + " for " + price);
                        break;
                    }
                    if (what == "list")
                    {
                        Report(onSale.Count + " on sale; matching: " + string.Join(", ", matching.Take(40).Select(i => i.ShortName.Localized())));
                        break;
                    }
                    if (what != "buy" || matching.Count == 0)
                    {
                        Report("FAILED nothing on sale matches '" + word + "'");
                        break;
                    }
                    // Of several with the same name, the whole weapon rather than a part of one.
                    matching = matching.OrderByDescending(i => i is Weapon).ToList();
                    goods.SelectItem(matching[0]);
                    goods.CurrentQuantity = parts.Length > 3 ? (int)Number(parts[3]) : 1;
                    yield return new WaitForSecondsRealtime(0.3f);
                    foreach (var need in goods.CurrentRequisites)
                    {
                        foreach (var carried in LocalTrade.CarriedItems(player))
                        {
                            if (need.Enough)
                            {
                                break;
                            }
                            if (need.IsRequired(carried))
                            {
                                need.PrepareItem(carried);
                            }
                        }
                    }
                    yield return new WaitForSecondsRealtime(1.2f);
                    if (!goods.IsValid)
                    {
                        Report("FAILED cannot pay for " + matching[0].ShortName.Localized() + ": needs "
                               + string.Join(", ", goods.CurrentRequisites.Select(r => r.RequiredItemsCount + " x " + r.RequiredItem.ShortName.Localized())));
                        break;
                    }
                    var deal = goods.Purchase();
                    while (!deal.IsCompleted)
                    {
                        yield return null;
                    }
                    Report((deal.Result != null && deal.Result.Succeed ? "bought " : "FAILED buying ") + matching[0].ShortName.Localized()
                           + (deal.Result != null && !deal.Result.Succeed ? ": " + deal.Result.Error : ""));
                    break;
                }
                case "sounds":
                {
                    // sounds [WORD]: every sound source that is playing now: where from, how loud, through what.
                    var playing = UnityEngine.Object.FindObjectsOfType<AudioSource>().Where(x => x.isPlaying && !x.mute).ToList();
                    float hideoutDb;
                    Report("mixer HideoutVolume: " + (EFT.Settings.Sound.SoundSettingsController.MasterMixer.GetFloat("HideoutVolume", out hideoutDb) ? hideoutDb.ToString("F0") + " dB" : "?"));
                    Report(playing.Count + " source(s) playing; listener volume " + AudioListener.volume.ToString("F2"));
                    foreach (var group in playing.GroupBy(x => (x.outputAudioMixerGroup != null ? x.outputAudioMixerGroup.name : "NO MIXER") + " | scene " + x.gameObject.scene.name)
                                 .OrderByDescending(g => g.Count()))
                    {
                        Report("  " + group.Count() + " x " + group.Key + ": " + string.Join(", ", group.Take(6).Select(x =>
                            (x.clip != null ? x.clip.name : "?") + "@" + x.volume.ToString("F2") + (x.spatialBlend < 0.5f ? " 2D" : " " + Vector3.Distance(x.transform.position, player.Transform.position).ToString("F0") + "m")
                            + " on " + x.gameObject.name)));
                    }
                    break;
                }
                case "settings":
                {
                    // settings [WORD]: opens the settings window (the Configuration Manager), searching for WORD. settings close: shuts it.
                    object manager = null;
                    foreach (var plugin in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                    {
                        if (plugin.Instance != null && plugin.Instance.GetType().Name == "ConfigurationManager")
                        {
                            manager = plugin.Instance;
                        }
                    }
                    if (manager == null)
                    {
                        Report("FAILED no Configuration Manager among the plugins");
                        break;
                    }
                    bool open = !(parts.Length > 1 && parts[1] == "close");
                    var shown = HarmonyLib.AccessTools.Property(manager.GetType(), "DisplayingWindow");
                    shown.SetValue(manager, open);
                    if (open && parts.Length > 1)
                    {
                        var search = HarmonyLib.AccessTools.Property(manager.GetType(), "SearchString");
                        if (search != null && search.CanWrite)
                        {
                            search.SetValue(manager, parts[1].Replace('_', ' '));
                        }
                        else
                        {
                            Report("the window has no search that can be set");
                        }
                    }
                    yield return new WaitForSecondsRealtime(1.5f);
                    Report("settings window " + (open ? "open" : "closed"));
                    break;
                }
                case "physicsold":
                    // physicsold on|off: the physics box is redrawn after the raid has begun, as before 2.1.0, to compare.
                    LevelPhysicsPatch.OldWay = parts.Length > 1 && parts[1] == "on";
                    LevelPhysicsPatch.TestCells = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                    Report("physics box the old way: " + LevelPhysicsPatch.OldWay + ", cells for a test: " + LevelPhysicsPatch.TestCells);
                    break;
                case "menukey":
                {
                    // menukey: gives the main menu the inventory key, as the keyboard would, and says what came of it.
                    var screen = UnityEngine.Object.FindObjectOfType<EFT.UI.MenuScreen>();
                    var bar = UnityEngine.Object.FindObjectOfType<EFT.UI.MenuTaskBar>();
                    string outcome = "";
                    if (screen != null)
                    {
                        outcome += "menu screen: " + screen.TranslateCommand(EFT.InputSystem.ECommand.ToggleInventory);
                    }
                    if (bar != null)
                    {
                        outcome += "; bar: " + bar.TranslateCommand(EFT.InputSystem.ECommand.ToggleInventory);
                    }
                    yield return new WaitForSecondsRealtime(2f);
                    var inventory = UnityEngine.Object.FindObjectOfType<EFT.UI.InventoryScreen>();
                    Report((inventory != null && inventory.gameObject.activeInHierarchy ? "FAILED the character screen opened" : "PASS the character screen stayed shut") + " (" + outcome + ")");
                    break;
                }
                case "bots":
                {
                    // bots: how many bots there are, how many are switched on, and what state the game has each in.
                    // bots letrun on|off: lets ORBIT run during the pause, to compare with it held.
                    if (parts.Length > 2 && parts[1] == "letrun")
                    {
                        OrbitLink.LetRun = parts[2] == "on";
                    }
                    float walked = 0f, furthest = 0f;
                    var states = new Dictionary<string, int>();
                    int all = 0, on = 0, moving = 0;
                    foreach (var other in Comfort.Common.Singleton<GameWorld>.Instance.AllAlivePlayersList)
                    {
                        if (other == null || other.IsYourPlayer || !other.IsAI)
                        {
                            continue;
                        }
                        all++;
                        if (other.gameObject.activeSelf)
                        {
                            on++;
                        }
                        var owner = other.gameObject.GetComponent<BotOwner>();
                        string state = owner != null ? owner.BotState.ToString() : "no owner";
                        int n;
                        states.TryGetValue(state, out n);
                        states[state] = n + 1;
                        if (other.Velocity.sqrMagnitude > 0.05f)
                        {
                            moving++;
                        }
                        Vector3 before;
                        if (_botWas.TryGetValue(other.ProfileId, out before))
                        {
                            float d = Vector3.Distance(before, other.Transform.position);
                            walked += d;
                            furthest = Mathf.Max(furthest, d);
                        }
                        _botWas[other.ProfileId] = other.Transform.position;
                    }
                    Report("bots " + all + ", switched on " + on + ", moving " + moving + ", states: " + string.Join(", ", states.Select(x => x.Key + " " + x.Value))
                           + "; raid standing still " + BotFreeze.On + ", ORBIT held " + (BotFreeze.On && OrbitLink.Hooked && !OrbitLink.LetRun)
                           + "; since the last look " + (Time.realtimeSinceStartup - _botLook).ToString("0") + " s: all walked " + walked.ToString("0.0") + " m, the furthest " + furthest.ToString("0.0") + " m");
                    _botLook = Time.realtimeSinceStartup;
                    break;
                }
                case "treat":
                {
                    // treat: what treatment at Therapist would cost now, and buys it.
                    int price = Treatment.Cost();
                    Report("treatment would cost " + price + "; roubles carried " + Afterlife.CarriedRoubles(player));
                    if (price > 0)
                    {
                        var bought = Treatment.Buy();
                        while (!bought.IsCompleted)
                        {
                            yield return null;
                        }
                        yield return new WaitForSecondsRealtime(1f);
                        Report("treatment: " + bought.Result + "; cost now " + Treatment.Cost() + "; roubles carried " + Afterlife.CarriedRoubles(player));
                    }
                    break;
                }
                case "place":
                    // place: the settings of every trader's place. place WORD MAP [DOOR [ROOM [FIGURE]]]: sets one (MAP with _ for a space; DOOR - for the first free).
                    Report(parts.Length < 3 ? TraderPlaces.Report()
                        : TraderPlaces.Set(parts[1], parts[2].Replace('_', ' '), parts.Length > 3 ? (parts[3] == "-" ? "" : parts[3]) : null,
                            parts.Length > 4 ? parts[4] : null, parts.Length > 5 ? parts[5] : null));
                    break;
                case "doorsurvey":
                    // doorsurvey: looks through the map for scenery doors a trader's room could be put behind; writes survey/doors_MAP.json.
                    // doorsurvey X Y Z: the same, and says of everything door-like within four metres of the point why it was taken or not.
                    DoorSurvey.Probe = parts.Length > 3 ? new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])) : (Vector3?)null;
                    Report(DoorSurvey.Run(Layout.Map.Length > 0 ? Layout.Map : player.Location));
                    break;
                case "exits":
                {
                    // exits: every way out the game knows on this map, and its state.
                    var points = Comfort.Common.Singleton<GameWorld>.Instance.ExfiltrationController.ExfiltrationPoints;
                    foreach (var point in points)
                    {
                        Report("  " + point.gameObject.name + " '" + point.Settings.Name + "' status " + point.Status + " active " + point.gameObject.activeInHierarchy
                               + " at " + point.transform.position.ToString("F0"));
                    }
                    break;
                }
                case "repair":
                {
                    // repair WORD [POINTS|window]: the carried thing whose name holds WORD: how worn, which kits would do;
                    // with POINTS, repaired by that many kit points the way the Repair window does it; with "window", the window is opened.
                    string named = parts.Length > 1 ? parts[1].Replace('_', ' ') : "";
                    var worn = LocalTrade.CarriedItems(player).FirstOrDefault(i => i.GetItemComponent<RepairableComponent>() != null
                                   && i.ShortName.Localized().IndexOf(named, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (worn == null)
                    {
                        Report("FAILED nothing repairable carried matches '" + named + "'");
                        break;
                    }
                    var part = worn.GetItemComponent<RepairableComponent>();
                    var repairs = SPT.Reflection.Utils.ClientAppUtils.GetClientApp().Session.RepairController;
                    var kinds = repairs.GetSuitableRepairersCollections(worn).ToList();
                    Report(worn.ShortName.Localized() + " " + part.Durability.ToString("F1") + "/" + part.MaxDurability.ToString("F1") + " of " + part.TemplateDurability
                           + "; kits that fit: " + (kinds.Count == 0 ? "none" : string.Join(", ", kinds.Select(k => k.LocalizedName + " " + k.GetRepairPoints().ToString("F0") + " pts")))
                           + "; kits at hand: " + Repairs.KitsAtHand().Count() + "; traders offered: " + repairs.TraderRepairers.Count());
                    if (parts.Length > 2 && parts[2] == "window")
                    {
                        EFT.UI.ItemUiContext.Instance.OpenRepairWindow(repairs, new DefaultItemContext(worn, EItemViewType.Inventory));
                        Report("repair window asked for");
                    }
                    else if (parts.Length > 2 && kinds.Count > 0)
                    {
                        var job = kinds[0].RepairItems(new RepairItem(worn.Id, Number(parts[2])), null);
                        while (!job.IsCompleted)
                        {
                            yield return null;
                        }
                        Report((job.Result != null && job.Result.Succeed ? "repaired: now " : "FAILED repair (" + (job.Result != null ? job.Result.Error : "no result") + "): now ")
                               + part.Durability.ToString("F1") + "/" + part.MaxDurability.ToString("F1") + "; kit points left " + kinds[0].GetRepairPoints().ToString("F1"));
                    }
                    break;
                }
                case "traderbundle":
                    // traderbundle [again]: what the 1.0 game's trader bundle gave; "again" opens it afresh.
                    if (parts.Length > 1 && parts[1] == "again")
                    {
                        TraderModelBundle.Forget();
                    }
                    Report(TraderModelBundle.Report());
                    break;
                case "say":
                {
                    // say WORD [greeting]: the trader whose name holds WORD speaks.
                    string who = TraderConfig.IdOf(parts[1]);
                    TraderModelBundle.Speak(who, player.Position, parts.Length > 2);
                    Report("asked " + parts[1] + " (" + who + ") to speak");
                    break;
                }
                case "findobj":
                {
                    // findobj WORD [LIMIT]: scene objects whose name contains the word, with where they are and their collider.
                    string word = parts.Length > 1 ? parts[1].ToLowerInvariant() : "water";
                    int limit = parts.Length > 2 ? (int)Number(parts[2]) : 25, shown = 0;
                    foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                    {
                        if (t.name.ToLowerInvariant().IndexOf(word, StringComparison.Ordinal) < 0
                            || (parts.Length > 3 && Vector3.Distance(t.position, player.Transform.position) > Number(parts[3]))
                            || (parts.Length > 3 && t.GetComponent<Renderer>() == null))
                        {
                            continue;
                        }
                        var c = t.GetComponent<Collider>();
                        var r = t.GetComponent<Renderer>();
                        if (++shown <= limit)
                        {
                            Report("  " + Vector3.Distance(t.position, player.Transform.position).ToString("F0") + " m " + t.name + " at " + t.position.ToString("F1") + " turned " + t.eulerAngles.y.ToString("F0") + " layer " + LayerMask.LayerToName(t.gameObject.layer)
                                   + (c != null ? " collider " + c.GetType().Name + (c.isTrigger ? " (trigger)" : "") + " bounds " + c.bounds.center.ToString("F1") + " size " + c.bounds.size.ToString("F1") : "")
                                   + (r != null ? " drawn bounds " + r.bounds.center.ToString("F1") + " size " + r.bounds.size.ToString("F1") : ""));
                        }
                    }
                    Report(shown + " object(s) named like " + word);
                    break;
                }
                case "rays":
                {
                    // rays [X Y Z]: everything a line straight down meets, from 2 m above to 160 m below, any layer.
                    var from = parts.Length > 3 ? new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])) : player.Transform.position;
                    var hits = Physics.RaycastAll(from + Vector3.up * 2f, Vector3.down, 162f, ~0, QueryTriggerInteraction.Collide);
                    Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (var h in hits)
                    {
                        Report("  " + (h.distance - 2f).ToString("F2") + " m below: " + h.collider.name + " layer " + LayerMask.LayerToName(h.collider.gameObject.layer)
                               + " " + h.collider.GetType().Name + (h.collider.isTrigger ? " (trigger)" : ""));
                    }
                    Report(hits.Length + " thing(s) under " + from.ToString("F1") + "; fall guard sees " + FallGuard.Below(from).ToString("F1"));
                    break;
                }
                case "reach":
                {
                    // reach X Y Z [MARGIN] [STEP] [FROMX FROMY FROMZ]: can someone on foot get from here to that point?
                    // Draws the ground between as a chart: # blocked, . open but not reached, + reached, S start, D goal.
                    var goal = new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3]));
                    float margin = parts.Length > 4 && parts[4] != "open" ? Number(parts[4]) : 15f, step = parts.Length > 5 && parts[5] != "open" ? Number(parts[5]) : 1f;
                    var start = parts.Length > 8 ? new Vector3(Number(parts[6]), Number(parts[7]), Number(parts[8])) : player.Transform.position;
                    int body = 0, me = LayerMask.NameToLayer("Player");
                    for (int layer = 0; layer < 32; layer++)
                    {
                        if (!Physics.GetIgnoreLayerCollision(me, layer) && layer != me && LayerMask.LayerToName(layer) != "PlayerCollisionTest"
                            && LayerMask.LayerToName(layer) != "HitCollider" && LayerMask.LayerToName(layer) != "PlayerSpiritAura")
                        {
                            body |= 1 << layer;
                        }
                    }
                    if (parts[parts.Length - 1] == "open")
                    {
                        // As if the map's border walls were not there.
                        body &= ~LayerMask.GetMask("LevelBorder");
                    }
                    float x0 = Mathf.Min(start.x, goal.x) - margin, x1 = Mathf.Max(start.x, goal.x) + margin;
                    float z0 = Mathf.Min(start.z, goal.z) - margin, z1 = Mathf.Max(start.z, goal.z) + margin;
                    int w = Mathf.CeilToInt((x1 - x0) / step) + 1, h = Mathf.CeilToInt((z1 - z0) / step) + 1;
                    var height = new float[w, h];
                    var open = new bool[w, h];
                    var seen = new bool[w, h];
                    float top = Mathf.Max(start.y, goal.y) + 2.2f;
                    for (int i = 0; i < w; i++)
                    {
                        for (int j = 0; j < h; j++)
                        {
                            var p = new Vector3(x0 + i * step, top, z0 + j * step);
                            RaycastHit hit;
                            if (!Physics.Raycast(p, Vector3.down, out hit, 12f, body, QueryTriggerInteraction.Ignore))
                            {
                                height[i, j] = float.NaN;
                                continue;
                            }
                            height[i, j] = hit.point.y;
                            open[i, j] = !Physics.CheckCapsule(hit.point + Vector3.up * 0.75f, hit.point + Vector3.up * 1.5f, 0.3f, body, QueryTriggerInteraction.Ignore);
                        }
                    }
                    int si = Mathf.RoundToInt((start.x - x0) / step), sj = Mathf.RoundToInt((start.z - z0) / step);
                    int gi = Mathf.RoundToInt((goal.x - x0) / step), gj = Mathf.RoundToInt((goal.z - z0) / step);
                    var queue = new Queue<int>();
                    open[si, sj] = true;
                    seen[si, sj] = true;
                    queue.Enqueue(si * 100000 + sj);
                    while (queue.Count > 0)
                    {
                        int at = queue.Dequeue(), ai = at / 100000, aj = at % 100000;
                        for (int d = 0; d < 4; d++)
                        {
                            int ni = ai + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = aj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                            if (ni < 0 || nj < 0 || ni >= w || nj >= h || seen[ni, nj] || !open[ni, nj]
                                || float.IsNaN(height[ai, aj]) || Mathf.Abs(height[ni, nj] - height[ai, aj]) > 0.55f * step)
                            {
                                continue;
                            }
                            seen[ni, nj] = true;
                            queue.Enqueue(ni * 100000 + nj);
                        }
                    }
                    bool got = false;
                    for (int i = Mathf.Max(0, gi - 2); i <= Mathf.Min(w - 1, gi + 2); i++)
                    {
                        for (int j = Mathf.Max(0, gj - 2); j <= Mathf.Min(h - 1, gj + 2); j++)
                        {
                            got |= seen[i, j];
                        }
                    }
                    Report("chart: west is left, north is up; x " + x0.ToString("F0") + " to " + x1.ToString("F0") + ", z " + z1.ToString("F0") + " down to " + z0.ToString("F0") + ", " + step + " m a square");
                    for (int j = h - 1; j >= 0; j--)
                    {
                        var row = new System.Text.StringBuilder();
                        for (int i = 0; i < w; i++)
                        {
                            row.Append(i == si && j == sj ? 'S' : i == gi && j == gj ? 'D' : seen[i, j] ? '+' : float.IsNaN(height[i, j]) ? ' ' : open[i, j] ? '.' : '#');
                        }
                        Report("  |" + row + "|");
                    }
                    Report(got ? "PASS the goal can be reached on foot" : "FAILED no way on foot to the goal");
                    break;
                }
                case "touch":
                {
                    // touch X Y Z [R]: every collider reaching into a ball around a point, any layer.
                    var mid = new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3]));
                    foreach (var c in Physics.OverlapSphere(mid, parts.Length > 4 ? Number(parts[4]) : 0.5f, ~0, QueryTriggerInteraction.Collide))
                    {
                        string path = c.name;
                        for (var up = c.transform.parent; up != null; up = up.parent)
                        {
                            path = up.name + "/" + path;
                        }
                        Report("  " + path + " layer " + LayerMask.LayerToName(c.gameObject.layer) + " " + c.GetType().Name + (c.isTrigger ? " (trigger)" : "")
                               + " bounds " + c.bounds.center.ToString("F1") + " size " + c.bounds.size.ToString("F1") + " turned " + c.transform.eulerAngles.ToString("F0"));
                    }
                    Report("touched at " + mid.ToString("F1"));
                    break;
                }
                case "where":
                {
                    // where X Y Z: where that point is on the screen now, as fractions from the top left.
                    ReportOnScreen(new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])));
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
                case "doorcheck":
                {
                    // doorcheck [WORD]: for every door of ours (or those whose label has WORD), stand
                    // on each side of it at a few angles, look at it, and report whether the game itself
                    // offers its menu there. A side with no floor or no clear view is skipped.
                    string word = parts.Length > 1 ? parts[1].ToLowerInvariant().Replace("_", " ") : null;
                    var start = player.Transform.position;
                    var startLook = player.Rotation;
                    int good = 0, bad = 0;
                    foreach (var door in RoomDoor.All.ToArray())
                    {
                        if (door == null || (word != null && (door.Label ?? "").ToLowerInvariant().IndexOf(word) < 0))
                        {
                            continue;
                        }
                        string report = "'" + door.Label + "' at " + door.transform.position.ToString("F1") + ":";
                        foreach (int side in new[] { 1, -1 })
                        {
                            string sideText = side > 0 ? " front" : " back";
                            int sideGood = 0, sideBad = 0, tried = 0;
                            foreach (float angle in new[] { 0f, 35f, -35f })
                            {
                                foreach (float reach in new[] { 1.1f, 0.7f })
                                {
                                    var result = new string[1];
                                    yield return LookAtDoor(player, door, side, angle, reach, result);
                                    if (result[0] == null)
                                    {
                                        continue;
                                    }
                                    tried++;
                                    if (result[0] == "ok")
                                    {
                                        sideGood++;
                                    }
                                    else
                                    {
                                        sideBad++;
                                        sideText += " [" + angle.ToString("F0") + "deg " + reach.ToString("F1") + "m: " + result[0] + "]";
                                    }
                                }
                            }
                            report += sideText + (tried == 0 ? " no place to stand" : " " + sideGood + "/" + tried);
                            good += sideGood;
                            bad += sideBad;
                        }
                        Report(report);
                    }
                    player.Teleport(start, false);
                    player.Rotation = startLook;
                    Report((bad == 0 ? "PASS" : "FAILED") + " door menus: " + good + " shown, " + bad + " missing");
                    break;
                }
                case "doorlook":
                {
                    // doorlook WORD SIDE: stand in front (1) or behind (-1) the door whose label has WORD and look at it.
                    RoomDoor found = null;
                    foreach (var door in RoomDoor.All)
                    {
                        if (door != null && (door.Label ?? "").ToLowerInvariant().IndexOf(parts[1].ToLowerInvariant().Replace("_", " ")) >= 0)
                        {
                            found = door;
                            break;
                        }
                    }
                    if (found == null)
                    {
                        Report("FAILED no such door");
                        break;
                    }
                    var result = new string[1];
                    yield return LookAtDoor(player, found, parts.Length > 2 ? (int)Number(parts[2]) : 1, 0f, 1.1f, result);
                    Report("looking at '" + found.Label + "': " + (result[0] ?? "no place to stand"));
                    break;
                }
                case "press":
                {
                    // press: what pressing the action key does now, through the game's own menu for
                    // the thing looked at (its first action).
                    var seen = player.InteractableObject;
                    if (seen == null)
                    {
                        Report("FAILED nothing offered");
                        break;
                    }
                    var owner = player.GetComponent<GamePlayerOwner>();
                    var actions = InteractionContextHelper.GetAvailableActions(owner, seen);
                    if (actions == null || actions.Actions.Count == 0 || actions.Actions[0].Disabled)
                    {
                        Report("FAILED no usable action on " + seen.name);
                        break;
                    }
                    var before = player.Transform.position;
                    Report("pressing '" + actions.Actions[0].Name + "'");
                    actions.Actions[0].Action();
                    for (int i = 0; i < 60 && Vector3.Distance(before, player.Transform.position) < 3f; i++)
                    {
                        yield return new WaitForSecondsRealtime(0.5f);
                    }
                    yield return new WaitForSecondsRealtime(2f);
                    bool moved = Vector3.Distance(before, player.Transform.position) >= 3f;
                    Report((moved ? "PASS" : "FAILED") + " went through: now at " + player.Transform.position.ToString("F1") + ", inside hideout: " + (HideoutDoors.Inside ?? "none") + ", screen " + (RoomDoor.Fade > 0.5f ? "dark" : "clear"));
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
                case "accept":
                {
                    // accept QUEST_ID: takes a quest that is on offer, as the trade screen would.
                    EFT.Quests.Quest offered = null;
                    foreach (var quest in player.QuestController.Quests)
                    {
                        if (parts.Length > 1 && quest.Id == parts[1])
                        {
                            offered = quest;
                        }
                    }
                    if (offered == null)
                    {
                        Report("FAILED no such quest");
                        break;
                    }
                    var accepting = player.QuestController.AcceptQuest(offered, true);
                    while (!accepting.IsCompleted)
                    {
                        yield return null;
                    }
                    Report("quest " + offered.Id + " is now " + offered.QuestStatus);
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
                case "throw":
                {
                    // throw WORD: drops a worn or carried thing whose name holds WORD, as the player's own
                    // Discard does, and says where it is a few seconds later (or that it is gone).
                    string wanted = parts.Length > 1 ? parts[1].Replace('_', ' ') : "";
                    var mine = LocalTrade.CarriedItems(player).Concat(player.Profile.Inventory.Equipment.GetAllSlots().Select(sl => sl.ContainedItem).Where(i => i != null))
                        .FirstOrDefault(i => i.ShortName.Localized().IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (mine == null)
                    {
                        Report("FAILED nothing worn or carried matches '" + wanted + "'");
                        break;
                    }
                    string thrownId = mine.Id;
                    int inside = mine.GetAllItems().Count() - 1;
                    bool thrown = false;
                    player.InventoryController.ThrowItem(mine, false, result => { thrown = true; });
                    yield return new WaitForSecondsRealtime(4f);
                    var lying = UnityEngine.Object.FindObjectsOfType<EFT.Interactive.LootItem>().FirstOrDefault(l => l.Item != null && l.Item.Id == thrownId);
                    Report((lying != null ? "PASS " : "FAILED GONE: ") + mine.ShortName.Localized() + " with " + inside + " thing(s) inside, thrown " + thrown
                           + (lying != null ? ", lies at " + lying.transform.position.ToString("F2") + ", player at " + player.Transform.position.ToString("F2") : ", no loot object holds it"));
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

        /// <summary>
        /// Stands REACH metres off a door on one side of it (1 in front, -1 behind), turned ANGLE
        /// degrees round it, looks at its middle and says what the game offers: "ok" for the door's
        /// own menu, else what came instead. Null when there is nowhere to stand there.
        /// </summary>
        private static IEnumerator LookAtDoor(Player player, RoomDoor door, int side, float angle, float reach, string[] result)
        {
            result[0] = null;
            var collider = door.GetComponent<Collider>();
            var centre = collider != null ? collider.bounds.center : door.transform.position;
            // The way through the door: its thinnest side.
            var box = door.GetComponent<BoxCollider>();
            var size = box != null ? Vector3.Scale(box.size, door.transform.lossyScale) : Vector3.one;
            Vector3 through = size.x <= size.y && size.x <= size.z ? door.transform.right : size.z <= size.y ? door.transform.forward : door.transform.up;
            through.y = 0f;
            through = through.normalized * side;
            var direction = Quaternion.Euler(0f, angle, 0f) * through;
            int solid = LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain");
            var spot = centre + direction * (reach + 0.3f);
            RaycastHit floor;
            if (!Physics.Raycast(spot + Vector3.up * 0.6f, Vector3.down, out floor, 2.6f, solid, QueryTriggerInteraction.Ignore)
                || Physics.CheckCapsule(floor.point + Vector3.up * 0.4f, floor.point + Vector3.up * 1.6f, 0.25f, solid, QueryTriggerInteraction.Ignore))
            {
                yield break;
            }
            var eye = floor.point + Vector3.up * 1.55f;
            var toDoor = centre - eye;
            // The view to the door must be open up to its own block.
            if (Physics.Linecast(eye, centre - toDoor.normalized * 0.9f, solid, QueryTriggerInteraction.Ignore))
            {
                yield break;
            }
            player.Teleport(floor.point + Vector3.up * 0.05f, false);
            for (int i = 0; i < 4; i++)
            {
                var to = centre - (player.Transform.position + Vector3.up * 1.55f);
                player.Rotation = new Vector2(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
                yield return new WaitForSecondsRealtime(0.15f);
            }
            var seen = player.InteractableObject;
            if (seen == door)
            {
                var owner = player.GetComponent<GamePlayerOwner>();
                var actions = InteractionContextHelper.GetAvailableActions(owner, seen);
                result[0] = actions != null && actions.Actions.Count > 0 && !actions.Actions[0].Disabled ? "ok" : "no action";
            }
            else
            {
                result[0] = seen == null ? "nothing" : seen.name;
            }
        }

        private static void ReportOnScreen(Vector3 point)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Report("no camera");
                return;
            }
            var screen = camera.WorldToScreenPoint(point);
            Report("on screen at " + (screen.x / Screen.width).ToString("F3", CultureInfo.InvariantCulture) + " "
                   + (1f - screen.y / Screen.height).ToString("F3", CultureInfo.InvariantCulture) + (screen.z > 0f ? "" : " (behind)")
                   + ", " + screen.z.ToString("F0") + " m");
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
                   + ", alive " + player.HealthController.IsAlive + ", solid below " + FallGuard.Below(at).ToString("F1") + " m, health " + Health(player).ToString("F0")
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
                int width = Mathf.Min(1920, full.width);
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
