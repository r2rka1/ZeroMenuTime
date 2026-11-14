using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using EFT;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// Testing aid: lets the running game be driven and inspected from outside, without anyone at the
    /// keyboard. Lines written to test/commands.txt next to the plugin's settings are carried out in
    /// order, and what happened is appended to test/results.log. Off unless switched on.
    ///
    /// Commands: status | check | enter NAME | leave | goto X Y Z | look YAW PITCH | wait SECONDS |
    /// lookat [NAME] | style NAME | savequit | screenshot NAME | lay TEMPLATE_ID [COUNT] | deliveries | table | quests | handin [QUEST_ID]
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
