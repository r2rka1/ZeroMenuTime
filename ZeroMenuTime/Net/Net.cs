using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx.Bootstrap;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>What this game is in the raid.</summary>
    public enum Role
    {
        /// <summary>No co-op: one player, who owns the world.</summary>
        Single,
        /// <summary>A player who hosts the raid for others.</summary>
        Host,
        /// <summary>A player in someone else's raid.</summary>
        Client,
        /// <summary>The host with no player and no screen.</summary>
        HeadlessHost,
    }

    /// <summary>A human player in the raid, as this machine knows them.</summary>
    public class Human
    {
        public string ProfileId = "";
        public string Name = "";
        public bool IsLocal;
        public bool Alive;
        public Vector3 Position;
        public Player Player;
    }

    /// <summary>
    /// Everything the mod needs from a co-op layer. Without one, <see cref="SinglePlayer"/> answers:
    /// one human, messages delivered to this machine itself. The Fika add-on puts its own in place.
    /// Nothing outside the add-on names a Fika type.
    /// </summary>
    public interface IMultiplayer
    {
        Role Role { get; }

        /// <summary>The human players in the raid. Never bots.</summary>
        List<Human> Humans();

        /// <summary>
        /// Sends a message of this mod's own. To all: every other machine in the raid. Otherwise to
        /// the host only (on the host itself: handled here at once).
        /// </summary>
        void Send(string kind, byte[] body, bool toAll);

        /// <summary>
        /// Lays an item in the world so that every machine has the same one. Whoever creates an item
        /// calls this; nobody creates one on their own machine only.
        /// </summary>
        void LayItem(Item item, Vector3 at, Quaternion turned);

        /// <summary>
        /// The same, for an item this machine has already laid itself (and set down just so): every
        /// other machine lays the same item at exactly that place.
        /// </summary>
        void TellItem(Item item, Vector3 at, Quaternion turned);

        /// <summary>
        /// What must happen before the game's own raid start: nothing alone; with Fika, the match is
        /// created (or the one of <paramref name="joinHost"/> joined). False when it could not be.
        /// </summary>
        Task<bool> PrepareRaid(RaidSettings settings, string joinHost);

        /// <summary>
        /// A co-op raid waits in a lobby until someone starts it: the hosting player, or with a
        /// headless host any player. This is that button. Says what it did.
        /// </summary>
        string StartWaitingRaid();

        /// <summary>
        /// Takes this player out of the raid as an extraction by the named exit, where the game's
        /// own extraction does not end a co-op raid for them. False (with the reason) when it may
        /// not: a hosting player cannot leave while others are in.
        /// </summary>
        bool LeaveRaid(string exitName, out string why);

        /// <summary>
        /// The whole group is dead: this player's raid ends as a death. The hosting player goes
        /// last, once the others have gone (or after a wait).
        /// </summary>
        void EndRaidDead();
    }

    /// <summary>The seam itself: the layer in use, roles, events and message handlers.</summary>
    public static class Net
    {
        public static IMultiplayer Current = new SinglePlayer();

        public static Role Role => Current.Role;

        /// <summary>True where the world is decided: alone, or as the host (player or headless).</summary>
        public static bool IsHost => Current.Role != Role.Client;

        /// <summary>True in a raid others may be in, or may join: anything but playing alone.</summary>
        public static bool Coop => Current.Role != Role.Single;

        /// <summary>This process has no player and no screen, in the menu as much as in a raid.</summary>
        public static bool Headless => Application.isBatchMode;

        /// <summary>The co-op mod is installed, whether or not a raid is on.</summary>
        public static bool FikaInstalled => Chainloader.PluginInfos.ContainsKey("com.fika.core");

        /// <summary>The raid has begun on this machine (world and, unless headless, the player exist).</summary>
        public static event Action RaidStarted;

        /// <summary>The raid has ended for this machine: how, and by which exit.</summary>
        public static event Action<ExitStatus, string> RaidEnded;

        /// <summary>Another machine joined or left the raid (profile id where known, else a name).</summary>
        public static event Action<string> PeerJoined;
        public static event Action<string> PeerLeft;

        private static readonly Dictionary<string, Action<byte[], string>> Handlers = new Dictionary<string, Action<byte[], string>>();
        private static bool _raidOn;

        /// <summary>Handles a kind of message: its body and who sent it (profile id; "" when unknown).</summary>
        public static void On(string kind, Action<byte[], string> handler)
        {
            Handlers[kind] = handler;
        }

        /// <summary>Called by the layer when a message arrives (and by SinglePlayer for its own).</summary>
        public static void Deliver(string kind, byte[] body, string from)
        {
            Action<byte[], string> handler;
            if (!Handlers.TryGetValue(kind, out handler))
            {
                Plugin.Log.LogWarning("Net: no handler for message '" + kind + "'");
                return;
            }
            try
            {
                handler(body, from ?? "");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Net: handling '" + kind + "' failed: " + e);
            }
        }

        /// <summary>
        /// A player has left the raid for good (extracted, saved and left). After a moment, when the
        /// co-op layer has let the character go, the raid's own memory of "everyone who was ever
        /// here" forgets them too: otherwise the same player could not come into this raid again.
        /// </summary>
        public static void OnLeft(byte[] body, string from)
        {
            string profile = System.Text.Encoding.UTF8.GetString(body ?? new byte[0]);
            if (Plugin.Instance != null && profile != "")
            {
                Plugin.Instance.StartCoroutine(Forget(profile));
            }
        }

        private static System.Collections.IEnumerator Forget(string profile)
        {
            yield return new WaitForSecondsRealtime(4f);
            try
            {
                var world = Comfort.Common.Singleton<GameWorld>.Instance;
                var ever = world != null ? HarmonyLib.AccessTools.Field(typeof(GameWorld), "_allPlayersEverExisted").GetValue(world) as System.Collections.IDictionary : null;
                if (ever != null && ever.Contains(profile))
                {
                    ever.Remove(profile);
                    Plugin.Log.LogInfo("Net: " + profile + " has left; the raid no longer remembers their character");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Net: could not forget a player who left: " + e.Message);
            }
        }

        public static void RaiseRaidStarted()
        {
            if (_raidOn)
            {
                return;
            }
            _raidOn = true;
            Plugin.Log.LogInfo("Net: raid started as " + Role + (Headless ? " (headless)" : ""));
            Raise(RaidStarted, "RaidStarted");
        }

        /// <summary>Once per raid, whichever of the game's or the co-op layer's signals comes first.</summary>
        public static void RaiseRaidEnded(ExitStatus status, string exit)
        {
            if (!_raidOn)
            {
                return;
            }
            _raidOn = false;
            Plugin.Log.LogInfo("Net: raid ended as " + Role + ": " + status + " via '" + exit + "'");
            try
            {
                if (RaidEnded != null)
                {
                    RaidEnded(status, exit);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Net: RaidEnded handler failed: " + e);
            }
        }

        public static void RaisePeerJoined(string who)
        {
            Plugin.Log.LogInfo("Net: " + who + " joined");
            if (PeerJoined != null) { PeerJoined(who); }
        }

        public static void RaisePeerLeft(string who)
        {
            Plugin.Log.LogInfo("Net: " + who + " left");
            if (PeerLeft != null) { PeerLeft(who); }
        }

        private static void Raise(Action what, string name)
        {
            try
            {
                if (what != null)
                {
                    what();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Net: " + name + " handler failed: " + e);
            }
        }
    }

    /// <summary>Playing alone: the one local player, and messages that come straight back.</summary>
    public class SinglePlayer : IMultiplayer
    {
        public Role Role => Role.Single;

        public List<Human> Humans()
        {
            var list = new List<Human>();
            var me = GamePlayerOwner.MyPlayer;
            if (me != null)
            {
                list.Add(new Human
                {
                    ProfileId = me.ProfileId, Name = me.Profile.Nickname, IsLocal = true, Player = me,
                    Alive = me.HealthController != null && me.HealthController.IsAlive, Position = me.Transform.position,
                });
            }
            return list;
        }

        public void Send(string kind, byte[] body, bool toAll)
        {
            // To all: there is nobody else. To the host: that is this machine.
            if (!toAll)
            {
                var me = GamePlayerOwner.MyPlayer;
                Net.Deliver(kind, body, me != null ? me.ProfileId : "");
            }
        }

        public void LayItem(Item item, Vector3 at, Quaternion turned)
        {
            var world = Comfort.Common.Singleton<GameWorld>.Instance;
            if (world != null)
            {
                world.SetupItem(item, world.MainPlayer, at, turned);
            }
        }

        public void TellItem(Item item, Vector3 at, Quaternion turned)
        {
        }

        public Task<bool> PrepareRaid(RaidSettings settings, string joinHost)
        {
            return Task.FromResult(true);
        }

        public string StartWaitingRaid()
        {
            return "alone: nothing waits";
        }

        public void EndRaidDead()
        {
        }

        public bool LeaveRaid(string exitName, out string why)
        {
            // Alone the game's own extraction ends the raid; the caller does that.
            why = "";
            return false;
        }
    }

    /// <summary>
    /// Which features work in a co-op raid so far. A feature that is not ready is refused with a
    /// notice, not left half working. Each stage of plans/fika switches its own on.
    /// </summary>
    public static class Coop
    {
        public enum Feature { Rooms, Trade, Deliveries, QuestHandIn, Repair, Hideout, Sleep, Wait, Save, SaveAndLeave }

        private static readonly HashSet<Feature> Ready = new HashSet<Feature>
        {
            Feature.Rooms, Feature.Trade, Feature.Deliveries, Feature.QuestHandIn, Feature.Repair, Feature.Hideout, Feature.Sleep, Feature.Wait,
        };

        /// <summary>Whose world this raid is (profile id); "" until the server has said, and alone.</summary>
        public static string Owner = "";

        /// <summary>The local player owns the hideout everyone stands in. Alone: always.</summary>
        public static bool IsOwner
        {
            get
            {
                var me = GamePlayerOwner.MyPlayer;
                return !Net.Coop || (me != null && Owner == me.ProfileId);
            }
        }

        /// <summary>A raid that can be joined, as the server lists it.</summary>
        public class Raid
        {
            public string Match = "", Host = "", Owner = "", OwnerName = "", Location = "", Status = "";
            public bool Headless, Mine;
            public int Players;
        }

        /// <summary>The raids running or waiting now.</summary>
        public static async Task<List<Raid>> Raids()
        {
            try
            {
                var reply = Newtonsoft.Json.Linq.JObject.Parse(await SPT.Common.Http.RequestHandler.PostJsonAsync("/zeromenutime/coop/raids", "{}"));
                return reply["Raids"] != null ? reply["Raids"].ToObject<List<Raid>>() : new List<Raid>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Co-op: the list of raids could not be read: " + e.Message);
                return new List<Raid>();
            }
        }

        /// <summary>
        /// Joins a raid: the server moves this player to where that world's owner is, then the raid is
        /// started the usual way with the join in front of it. Someone who was in that raid before
        /// comes back as the character the host still holds.
        /// </summary>
        public static async Task Join(Raid raid)
        {
            try
            {
                await SPT.Common.Http.RequestHandler.PostJsonAsync("/zeromenutime/coop/join",
                    Newtonsoft.Json.JsonConvert.SerializeObject(new { TraderId = raid.Owner }));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Co-op: could not tell the server about the join: " + e.Message);
            }
            Plugin.Log.LogInfo("Co-op: joining " + (raid.OwnerName != "" ? raid.OwnerName : raid.Host) + " on " + raid.Location + (raid.Mine ? " (coming back)" : ""));
            Seamless.JoinHost = (raid.Mine ? "back:" : "") + raid.Match;
            Seamless.StartRaid(raid.Location);
        }

        /// <summary>At raid start: asks the server whose raid this is.</summary>
        public static async void AskOwner()
        {
            Owner = "";
            if (!Net.Coop || Net.Headless)
            {
                return;
            }
            try
            {
                var who = Newtonsoft.Json.Linq.JObject.Parse(await SPT.Common.Http.RequestHandler.PostJsonAsync("/zeromenutime/coop/who", "{}"));
                Owner = (string)who["Owner"] ?? "";
                Plugin.Log.LogInfo("Co-op: this world belongs to " + Owner + (IsOwner ? " (you)" : ""));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Co-op: could not ask whose raid this is: " + e.Message);
            }
        }

        /// <summary>The hideout's stations, power or crafts changed here: every machine inside looks again.</summary>
        public static void HideoutChanged()
        {
            if (Net.Coop)
            {
                Net.Current.Send("zmt.hideout", new byte[0], true);
            }
        }

        /// <summary>True alone, always. In co-op: only what is ready; otherwise says so on screen.</summary>
        public static bool Allowed(Feature feature, bool tell = true)
        {
            if (!Net.Coop || (Ready.Contains(feature) && !Handshake.Mismatch))
            {
                return true;
            }
            if (Handshake.Mismatch)
            {
                if (tell)
                {
                    NotificationManager.DisplayWarningNotification("Switched off: your Zero Menu Time version differs from the host's.", ENotificationDurationType.Long);
                }
                return false;
            }
            if (tell && !Net.Headless)
            {
                NotificationManager.DisplayWarningNotification("Not available in co-op yet: " + Words(feature) + ".", ENotificationDurationType.Long);
            }
            Plugin.Log.LogInfo("Co-op: '" + feature + "' refused, not ready yet");
            return false;
        }

        private static string Words(Feature feature)
        {
            switch (feature)
            {
                case Feature.Trade: return "trading";
                case Feature.Deliveries: return "collecting deliveries";
                case Feature.QuestHandIn: return "quest rewards at the counter";
                case Feature.Repair: return "repairs";
                case Feature.Hideout: return "the hideout";
                case Feature.Sleep: return "sleeping";
                case Feature.Wait: return "waiting until morning";
                case Feature.Save: return "saving";
                case Feature.SaveAndLeave: return "save and go to menu";
                default: return feature.ToString();
            }
        }
    }
}
