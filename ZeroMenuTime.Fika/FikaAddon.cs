using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.InventoryLogic;
using Fika.Core.Main.Components;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using Fika.Core.UI.Custom;
using SPT.Reflection.Utils;
using UnityEngine;

namespace ZeroMenuTime.Fika
{
    /// <summary>
    /// The part of Zero Menu Time that talks to Fika. It is loaded only where Fika is installed, and
    /// it is the only place a Fika type is named: the rest of the mod works through <see cref="Net"/>.
    /// </summary>
    [BepInPlugin("com.r2rka1.zeromenutime.fika", "r2rka1-ZeroMenuTime-Fika", "2.1.1")]
    [BepInDependency("com.fika.core")]
    [BepInDependency("com.r2rka1.zeromenutime")]
    public class FikaAddon : BaseUnityPlugin
    {
        /// <summary>The Fika versions this was built and tested against. Others get a warning in the log.</summary>
        private const string TestedFika = "2.4.6";

        private void Awake()
        {
            Net.Current = new FikaLayer();
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnManager);
            FikaEventDispatcher.SubscribeEvent<FikaGameEndedEvent>(ended => Net.RaiseRaidEnded(ended.ExitStatus, ended.ExitName ?? ""));
            FikaEventDispatcher.SubscribeEvent<PeerConnectedEvent>(joined =>
            {
                string name = joined.Peer != null && joined.Peer.Tag is string known ? known : "a player";
                FikaLayer.Gone.Remove(name);
                Net.RaisePeerJoined(name);
            });
            FikaEventDispatcher.SubscribeEvent<PeerDisconnectedEvent>(left =>
            {
                string name = left.Peer != null && left.Peer.Tag is string known ? known : "a player";
                FikaLayer.Gone.Add(name);
                Net.RaisePeerLeft(name);
            });
            Net.RaidStarted += () => FikaLayer.Gone.Clear();

            // SPIKE (stage 6): let a new player into a raid that has started. Off unless switched on.
            var harmony = new HarmonyLib.Harmony("com.r2rka1.zeromenutime.fika");
            foreach (string method in new[] { "OnConnectionRequest", "OnNetworkReceiveUnconnected" })
            {
                var target = HarmonyLib.AccessTools.Method(typeof(FikaServer), method);
                if (target == null)
                {
                    Logger.LogWarning("Late join: FikaServer." + method + " not found");
                    continue;
                }
                harmony.Patch(target, new HarmonyLib.HarmonyMethod(typeof(LateJoin), nameof(LateJoin.Before)), new HarmonyLib.HarmonyMethod(typeof(LateJoin), nameof(LateJoin.After)));
            }

            FikaEventDispatcher.SubscribeEvent<FikaGameCreatedEvent>(created => StartCoroutine(StartWhenLoaded()));
            string fika = typeof(FikaBackendUtils).Assembly.GetName().Version.ToString(3);
            Logger.LogInfo("Zero Menu Time: Fika add-on loaded; Fika " + fika + (fika == TestedFika ? "" : " (tested with " + TestedFika + ")")
                           + (Net.Headless ? "; this is a headless host" : ""));
        }

        /// <summary>
        /// A raid this game hosts, or asked a headless host for, does not wait in the lobby: it starts
        /// once this player's character is in it. Others come in as it runs.
        /// </summary>
        private System.Collections.IEnumerator StartWhenLoaded()
        {
            if (Net.Headless || Plugin.AutoStartCoop == null || !Plugin.AutoStartCoop.Value)
            {
                yield break;
            }
            float until = Time.realtimeSinceStartup + 600f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(1f);
                var game = Singleton<global::Fika.Core.Main.GameMode.IFikaGame>.Instance;
                if (game == null || game.GameController == null)
                {
                    continue;
                }
                if (game.GameController.RaidStarted)
                {
                    yield break;
                }
                if (!FikaBackendUtils.IsServer && !FikaBackendUtils.IsHeadlessRequester)
                {
                    // Someone else's raid: it starts when they start it.
                    yield break;
                }
                CoopHandler coop;
                if (!CoopHandler.TryGetCoopHandler(out coop) || coop.MyPlayer == null)
                {
                    continue;
                }
                yield return new WaitForSecondsRealtime(3f);
                Plugin.Log.LogInfo("Net: the raid has loaded; starting it: " + Net.Current.StartWaitingRaid());
                yield return new WaitForSecondsRealtime(5f);
            }
        }

        /// <summary>Doors and other things opened or shut since the raid began, lamps switched, windows broken.</summary>
        private void SendWorldTo(FikaServer server, NetPeer peer)
        {
            try
            {
                var world = Singleton<GameWorld>.Instance;
                if (server == null || world == null || world.World == null)
                {
                    return;
                }
                int doors = 0, lamps = 0, windows = 0;
                var forSync = HarmonyLib.AccessTools.Field(typeof(World), "_interactableObjectsForNetSync").GetValue(world.World) as EFT.Interactive.WorldInteractiveObject[];
                var changed = new List<EFT.Interactive.WorldInteractiveObject.InteractiveObjectStatusInfo>();
                foreach (var thing in forSync ?? new EFT.Interactive.WorldInteractiveObject[0])
                {
                    if (thing == null)
                    {
                        continue;
                    }
                    var door = thing as EFT.Interactive.Door;
                    if ((thing.DoorState != thing.InitialDoorState && thing.DoorState != EFT.Interactive.EDoorState.Interacting) || (door != null && door.IsBroken))
                    {
                        changed.Add(thing.GetStatusInfo(true));
                    }
                }
                if (changed.Count > 0)
                {
                    var packet = new global::Fika.Core.Networking.Packets.World.ReconnectPacket
                    {
                        Type = global::Fika.Core.Networking.Packets.World.ReconnectPacket.EReconnectDataType.Interactives, InteractivesData = changed,
                    };
                    server.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
                    doors = changed.Count;
                }
                var lampStates = new Dictionary<int, byte>();
                foreach (var lamp in LocationScene.GetAllObjects<EFT.Interactive.LampController>(false))
                {
                    if (lamp.NetId != 0 && !lampStates.ContainsKey(lamp.NetId))
                    {
                        lampStates.Add(lamp.NetId, (byte)lamp.LampState);
                    }
                }
                if (lampStates.Count > 0)
                {
                    var packet = new global::Fika.Core.Networking.Packets.World.ReconnectPacket
                    {
                        Type = global::Fika.Core.Networking.Packets.World.ReconnectPacket.EReconnectDataType.LampControllers, LampStates = lampStates,
                    };
                    server.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
                    lamps = lampStates.Count;
                }
                var broken = new Dictionary<int, Vector3>();
                foreach (var window in world.Windows.GetValuesEnumerator())
                {
                    if (window.AvailableToSync && window.IsDamaged && window.FirstHitPosition.HasValue)
                    {
                        broken[window.NetId] = window.FirstHitPosition.Value;
                    }
                }
                if (broken.Count > 0)
                {
                    var packet = new global::Fika.Core.Networking.Packets.World.ReconnectPacket
                    {
                        Type = global::Fika.Core.Networking.Packets.World.ReconnectPacket.EReconnectDataType.Windows, WindowBreakerStates = broken,
                    };
                    server.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
                    windows = broken.Count;
                }
                Plugin.Log.LogInfo("Net: a newcomer was told of " + doors + " door(s) and the like changed, " + lamps + " lamp(s), " + windows + " broken window(s)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Net: telling a newcomer about doors, lamps and windows failed: " + e);
            }
        }

        private float _nextTransitLook;
        private bool _transitStopAsked;

        /// <summary>
        /// After a transit Fika holds each player until they press its "leave" key. Here the step is
        /// taken for them, clients first and the hosting player once the others have gone, so a
        /// group moves to the next map by standing in the transit and nothing more.
        /// </summary>
        private void Update()
        {
            if (Net.Headless || Time.unscaledTime < _nextTransitLook)
            {
                return;
            }
            _nextTransitLook = Time.unscaledTime + 1f;
            CoopHandler coop;
            var game = Singleton<global::Fika.Core.Main.GameMode.IFikaGame>.Instance;
            if (game == null || !CoopHandler.TryGetCoopHandler(out coop) || coop.MyPlayer == null)
            {
                _transitStopAsked = false;
                return;
            }
            if (_transitStopAsked || game.ExitStatus != ExitStatus.Transit || coop.QuitState != CoopHandler.EQuitState.Extracted)
            {
                return;
            }
            if (FikaBackendUtils.IsServer && coop.HumanPlayers.Count > 1)
            {
                // The hosting player goes last.
                return;
            }
            _transitStopAsked = true;
            Plugin.Log.LogInfo("Net: through the transit; on to " + game.ExitLocation);
            game.Stop(coop.MyPlayer.ProfileId, ExitStatus.Transit, game.ExitLocation, 0f);
        }

        private void OnManager(FikaNetworkManagerCreatedEvent created)
        {
            var manager = created.Manager;
            if (manager is FikaServer)
            {
                manager.RegisterPacket<ModMessagePacket, NetPeer>((packet, peer) =>
                {
                    if (packet.Kind == "zmt.world?")
                    {
                        // A player who came into a raid already running: Fika tells such things only to
                        // someone reconnecting. The same packets are sent to the newcomer.
                        SendWorldTo(manager as FikaServer, peer);
                        return;
                    }
                    // A client's message "to all" has been passed on to the other clients by Fika's host
                    // already (the sender marks it so); passing it on here as well would deliver it twice.
                    Net.Deliver(packet.Kind, packet.Body, packet.From);
                });
                manager.RegisterPacket<WorldItemPacket, NetPeer>((packet, peer) =>
                {
                    FikaLayer.Lay(packet);
                });
            }
            else
            {
                manager.RegisterPacket<ModMessagePacket>(packet => Net.Deliver(packet.Kind, packet.Body, packet.From));
                manager.RegisterPacket<WorldItemPacket>(FikaLayer.Lay);
            }
            Logger.LogInfo("Zero Menu Time: packets registered on " + manager.GetType().Name);
        }
    }

    /// <summary>The co-op layer over Fika.</summary>
    public class FikaLayer : IMultiplayer
    {
        public Role Role
        {
            get
            {
                if (FikaBackendUtils.IsHeadless)
                {
                    return Role.HeadlessHost;
                }
                // Outside a match Fika is neither; the mod then behaves as it does alone.
                return FikaBackendUtils.IsServer ? Role.Host : FikaBackendUtils.IsClient ? Role.Client : Role.Single;
            }
        }

        /// <summary>
        /// On the host: the names of players whose game has gone. Fika keeps their character standing
        /// in the world (they may come back); for this mod they are not in the raid.
        /// </summary>
        public static readonly HashSet<string> Gone = new HashSet<string>();

        public List<Human> Humans()
        {
            var list = new List<Human>();
            CoopHandler coop;
            if (!CoopHandler.TryGetCoopHandler(out coop) || coop.HumanPlayers == null)
            {
                return list;
            }
            // On the host: whose game is connected right now. A character whose player has gone stays
            // standing in the world for Fika; for this mod they are not in the raid.
            HashSet<string> connected = null;
            var server = FikaBackendUtils.IsServer ? Singleton<FikaServer>.Instance : null;
            if (server != null && server.NetServer != null)
            {
                connected = new HashSet<string>();
                foreach (var peer in server.NetServer)
                {
                    if (peer.Tag is string tag)
                    {
                        connected.Add(tag);
                    }
                }
            }
            foreach (var player in coop.HumanPlayers)
            {
                // Someone who has left the raid stays on Fika's list; they are not in it any more.
                // (Fika takes a player who extracted off this list itself. Its list of extracted ids is
                // not looked at: an id is used again when the same player comes back.)
                if (player == null
                    || (!player.IsYourPlayer && connected != null
                        && !connected.Contains(player.Profile.Nickname) && !connected.Contains(player.Profile.Info.MainProfileNickname ?? "")))
                {
                    continue;
                }
                list.Add(new Human
                {
                    ProfileId = player.ProfileId, Name = player.Profile.Nickname, IsLocal = player.IsYourPlayer, Player = player,
                    Alive = player.HealthController != null && player.HealthController.IsAlive, Position = player.Transform.position,
                });
            }
            return list;
        }

        private static string MyProfile()
        {
            var profile = FikaBackendUtils.Profile;
            return profile != null ? profile.ProfileId : "";
        }

        public void Send(string kind, byte[] body, bool toAll)
        {
            var manager = Singleton<IFikaNetworkManager>.Instance;
            bool host = Role != Role.Client;
            if (host && !toAll)
            {
                // To the host, on the host.
                Net.Deliver(kind, body, MyProfile());
                return;
            }
            if (manager == null)
            {
                Plugin.Log.LogWarning("Net: '" + kind + "' not sent, no network yet");
                return;
            }
            var packet = new ModMessagePacket { Kind = kind, Body = body ?? new byte[0], From = MyProfile(), ToAll = toAll };
            manager.SendData(ref packet, DeliveryMethod.ReliableOrdered, toAll);
        }

        public void LayItem(Item item, Vector3 at, Quaternion turned)
        {
            var packet = new WorldItemPacket { Item = item, Position = at, Rotation = turned };
            Lay(packet);
            var manager = Singleton<IFikaNetworkManager>.Instance;
            if (manager != null)
            {
                manager.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
            }
        }

        public void TellItem(Item item, Vector3 at, Quaternion turned)
        {
            var packet = new WorldItemPacket { Item = item, Position = at, Rotation = turned };
            var manager = Singleton<IFikaNetworkManager>.Instance;
            if (manager != null)
            {
                manager.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
            }
        }

        /// <summary>Lays a received (or own) item in this machine's world.</summary>
        public static async void Lay(WorldItemPacket packet)
        {
            try
            {
                var world = Singleton<GameWorld>.Instance;
                if (world == null || packet.Item == null)
                {
                    return;
                }
                var models = packet.Item.GetAllItems().SelectMany(part => part.Template.AllResources).ToArray();
                await Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                    ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Online, models, JobYieldPriority.Immediate);
                world.SetupItem(packet.Item, world.MainPlayer, packet.Position, packet.Rotation);
                Plugin.Log.LogInfo("Net: item " + packet.Item.Id + " laid at " + packet.Position.ToString("F1"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Net: laying an item failed: " + e);
            }
        }

        /// <summary>
        /// Asks a free headless host for a raid with these settings, as Fika's "use headless" does.
        /// Fika's own listener then joins this game to that raid when the headless is ready; this
        /// waits for that, and the caller presses the game's start.
        /// </summary>
        private static async Task<bool> AskHeadless(RaidSettings settings)
        {
            var free = global::Fika.Core.Networking.Http.FikaRequestHandler.GetAvailableHeadlesses();
            if (free == null || free.Length == 0)
            {
                Plugin.Log.LogWarning("Net: no headless host is free");
                return false;
            }
            global::Fika.Core.FikaPlugin.HeadlessRequesterWebSocket ??= new global::Fika.Core.Networking.Websocket.HeadlessRequesterWebSocket();
            if (!global::Fika.Core.FikaPlugin.HeadlessRequesterWebSocket.Connected)
            {
                global::Fika.Core.FikaPlugin.HeadlessRequesterWebSocket.Connect();
            }
            var request = new global::Fika.Core.Networking.Models.Headless.StartHeadlessRequest
            {
                HeadlessSessionID = free[0].HeadlessSessionID,
                Time = settings.SelectedDateTime,
                LocationId = settings.SelectedLocation._Id,
                SpawnPlace = settings.PlayersSpawnPlace,
                MetabolismDisabled = settings.MetabolismDisabled,
                BotSettings = settings.BotSettings,
                Side = settings.Side,
                TimeAndWeatherSettings = settings.TimeAndWeatherSettings,
                WavesSettings = settings.WavesSettings,
                CustomRaidSettings = FikaBackendUtils.CustomRaidSettings,
                UseEvent = false,
            };
            var answer = await global::Fika.Core.Networking.Http.FikaRequestHandler.StartHeadless(request);
            if (!string.IsNullOrEmpty(answer.Error))
            {
                Plugin.Log.LogWarning("Net: the headless host refused: " + answer.Error);
                return false;
            }
            FikaBackendUtils.IsHeadlessRequester = true;
            Plugin.Log.LogInfo("Net: headless host " + free[0].HeadlessSessionID + " asked for a raid; waiting for it");
            for (int waited = 0; waited < 300 && !FikaBackendUtils.IsClient; waited++)
            {
                await Task.Delay(1000);
            }
            Plugin.Log.LogInfo("Net: " + (FikaBackendUtils.IsClient ? "joined the headless host's raid" : "the headless host's raid never came"));
            return FikaBackendUtils.IsClient;
        }

        public bool LeaveRaid(string exitName, out string why)
        {
            why = "";
            CoopHandler coop;
            var game = Singleton<global::Fika.Core.Main.GameMode.IFikaGame>.Instance;
            if (game == null || !CoopHandler.TryGetCoopHandler(out coop) || coop.MyPlayer == null)
            {
                why = "no co-op raid is running";
                return false;
            }
            var server = Singleton<FikaServer>.Instance;
            if (FikaBackendUtils.IsServer && server != null && server.NetServer != null && server.NetServer.ConnectedPeersCount > 0)
            {
                why = "you host this raid, and others are still in it";
                return false;
            }
            // Said to the others the way an extraction is, so each lets this character go; and said in
            // this mod's own words too, so that coming back later is a fresh arrival, not a clash with
            // a character the others still remember.
            Net.Current.Send("zmt.left", System.Text.Encoding.UTF8.GetBytes(coop.MyPlayer.ProfileId), true);
            if (!FikaBackendUtils.IsServer)
            {
                var gone = new global::Fika.Core.Networking.Packets.Generic.SubPackets.ClientExtractPacket(coop.MyPlayer.NetId);
                Singleton<IFikaNetworkManager>.Instance.SendGenericPacket(in gone, DeliveryMethod.ReliableOrdered, true);
            }
            game.ExitStatus = ExitStatus.Survived;
            game.ExitLocation = exitName;
            game.Stop(coop.MyPlayer.ProfileId, ExitStatus.Survived, exitName, 0f);
            return true;
        }

        public async void EndRaidDead()
        {
            try
            {
                // A moment to see what happened; then out. The host waits for the others to go first.
                await Task.Delay(6000);
                var server = Singleton<FikaServer>.Instance;
                for (int waited = 0; waited < 30 && FikaBackendUtils.IsServer && server != null && server.NetServer != null && server.NetServer.ConnectedPeersCount > 0; waited++)
                {
                    await Task.Delay(1000);
                }
                CoopHandler coop;
                var game = Singleton<global::Fika.Core.Main.GameMode.IFikaGame>.Instance;
                if (game == null || !CoopHandler.TryGetCoopHandler(out coop) || coop.MyPlayer == null)
                {
                    return;
                }
                game.ExitStatus = ExitStatus.Killed;
                game.Stop(coop.MyPlayer.ProfileId, ExitStatus.Killed, null, 0f);
                Plugin.Log.LogInfo("Net: the raid ends for this player, dead with the whole group");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Net: ending the raid after the group's death failed: " + e);
            }
        }

        public string StartWaitingRaid()
        {
            var game = Singleton<global::Fika.Core.Main.GameMode.IFikaGame>.Instance;
            if (game == null || game.GameController == null)
            {
                return "no co-op raid is waiting";
            }
            if (game.GameController.RaidStarted)
            {
                return "the raid has already started";
            }
            if (FikaBackendUtils.IsServer)
            {
                HarmonyLib.AccessTools.Property(game.GameController.GetType(), "RaidStarted").SetValue(game.GameController, true);
                return "started (this game hosts)";
            }
            // Not the host: ask it, as Fika's own start button does for the player who asked a headless host.
            var packet = new global::Fika.Core.Networking.Packets.Backend.InformationPacket { RequestStart = true };
            Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered);
            return "the host was asked to start";
        }

        /// <summary>What Fika's host or join button does before the game's own start is pressed.</summary>
        public async Task<bool> PrepareRaid(RaidSettings settings, string joinHost)
        {
            var profile = ClientAppUtils.GetMainApp().Session.Profile;
            // Fika's pre-raid screen sets this; this mod goes past that screen.
            HarmonyLib.AccessTools.Property(typeof(FikaBackendUtils), "Profile").SetValue(null, profile);
            if (string.IsNullOrEmpty(joinHost))
            {
                await FikaBackendUtils.CreateMatch(profile.ProfileId, profile.Nickname, settings);
                Plugin.Log.LogInfo("Net: match created; this game hosts");
                return true;
            }
            if (joinHost == "headless")
            {
                return await AskHeadless(settings);
            }
            // "back:" in front: this player was in that raid before, and the host still holds the character.
            bool back = joinHost.StartsWith("back:", StringComparison.Ordinal);
            if (back)
            {
                joinHost = joinHost.Substring(5);
            }
            bool joined = await MatchMakerUIScript.JoinMatch(profile.Id, joinHost, null, back);
            Plugin.Log.LogInfo("Net: joining the match of " + joinHost + ": " + (joined ? "accepted" : "refused"));
            return joined;
        }
    }

    /// <summary>Any message of this mod's own: what kind, its body, who sent it, and whether it goes to everyone.</summary>
    public struct ModMessagePacket : INetSerializable
    {
        public string Kind;
        public byte[] Body;
        public string From;
        public bool ToAll;

        public void Deserialize(NetDataReader reader)
        {
            Kind = reader.GetString();
            From = reader.GetString();
            ToAll = reader.GetBool();
            Body = reader.GetBytesWithLength();
        }

        public readonly void Serialize(NetDataWriter writer)
        {
            writer.Put(Kind ?? "");
            writer.Put(From ?? "");
            writer.Put(ToAll);
            writer.PutBytesWithLength(Body ?? new byte[0]);
        }
    }

    /// <summary>An item for every machine to lay in its world: the same item, so the same ids.</summary>
    public struct WorldItemPacket : INetSerializable
    {
        public Item Item;
        public Vector3 Position;
        public Quaternion Rotation;

        public void Deserialize(NetDataReader reader)
        {
            Item = reader.GetItem();
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Rotation = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }

        public readonly void Serialize(NetDataWriter writer)
        {
            writer.PutItem(Item);
            writer.Put(Position.x);
            writer.Put(Position.y);
            writer.Put(Position.z);
            writer.Put(Rotation.x);
            writer.Put(Rotation.y);
            writer.Put(Rotation.z);
            writer.Put(Rotation.w);
        }
    }

    /// <summary>
    /// SPIKE (stage 6). Fika's host refuses new players once the raid has started, in two places,
    /// both by reading the "raid started" flag. While either of them runs, the flag reads false.
    /// </summary>
    public static class LateJoin
    {
        private static System.Reflection.PropertyInfo _flag;
        private static object _controller;

        public static void Before(object[] __args, out bool __state)
        {
            __state = false;
            if (Plugin.LateJoin == null || !Plugin.LateJoin.Value)
            {
                return;
            }
            // Someone whose game died is let back in by Fika itself, under its own word; with the raid
            // made to look not started, Fika would ask for the other word and refuse them.
            try
            {
                object request = __args != null && __args.Length > 0 ? __args[0] : null;
                object data = request != null ? HarmonyLib.AccessTools.Property(request.GetType(), "Data")?.GetValue(request) : null;
                var peek = data != null ? HarmonyLib.AccessTools.Method(data.GetType(), "PeekString", Type.EmptyTypes) : null;
                if (peek != null && peek.Invoke(data, null) as string == "fika.reconnect")
                {
                    return;
                }
            }
            catch (Exception)
            {
            }
            var game = Singleton<global::Fika.Core.Main.GameMode.IFikaGame>.Instance;
            if (game == null || game.GameController == null || !game.GameController.RaidStarted)
            {
                return;
            }
            _controller = game.GameController;
            _flag = HarmonyLib.AccessTools.Property(_controller.GetType(), "RaidStarted");
            _flag.SetValue(_controller, false);
            __state = true;
        }

        public static void After(bool __state)
        {
            if (__state)
            {
                _flag.SetValue(_controller, true);
            }
        }
    }
}
