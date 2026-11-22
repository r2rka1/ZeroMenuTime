using System;
using System.Threading.Tasks;
using EFT;
using EFT.Communications;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace ZeroMenuTime
{
    /// <summary>
    /// Saving at a bed. The character is sent to the server as it would be if the raid ended now,
    /// together with the stash and the clock; the server writes it into the profile and keeps a copy.
    /// Dying puts that copy back, and after a death or a crash the next raid begins at the bed.
    /// </summary>
    public static class SaveGame
    {
        private static bool _saving;

        /// <summary>
        /// Drops damage records that have no kind of damage set. The server cannot read such a
        /// record, and one of them makes it refuse the whole character, at a save and at raid end.
        /// Returns how many were dropped.
        /// </summary>
        public static int CleanDamageRecords(Profile profile)
        {
            int dropped = 0;
            var history = profile != null && profile.Stats != null && profile.Stats.Eft != null ? profile.Stats.Eft.DamageHistory : null;
            if (history == null)
            {
                return 0;
            }
            foreach (var list in history.BodyParts.Values)
            {
                dropped += list.RemoveAll(record => !Enum.IsDefined(typeof(EDamageType), record.Type));
            }
            return dropped;
        }

        /// <summary>Saves the game at the bed of the hideout the player is in. Returns a line saying what happened.</summary>
        public static Task<string> SaveNow(string reason)
        {
            return SaveNow(reason, null);
        }

        /// <summary>
        /// Saves at the bed of the hideout the player is in, or, given a trader's extract, at that
        /// trader: the raid goes on, and a death puts the character back at the trader.
        /// </summary>
        public static async Task<string> SaveNow(string reason, string traderExit)
        {
            var player = GamePlayerOwner.MyPlayer;
            bool atTrader = !string.IsNullOrEmpty(traderExit);
            if (_saving || player == null || (!atTrader && HideoutDoors.Inside == null))
            {
                return "not possible right now";
            }
            _saving = true;
            try
            {
                // The server must hold the stash and the time as they are now before it copies them.
                if (!atTrader)
                {
                    await HideoutStash.SendAsync("saving");
                }
                await WorldClock.SendAsync("saving");

                // Written out the way the game itself writes the character for the server.
                int dropped = CleanDamageRecords(player.Profile);
                if (dropped > 0)
                {
                    Plugin.Log.LogInfo("Dropped " + dropped + " damage record(s) with no kind of damage");
                }
                var descriptor = new ProfileDescriptor(player.Profile, VisualsOnlySearchController.Instance);
                var body = new JObject { ["Hideout"] = atTrader ? "" : HideoutDoors.Inside, ["Exit"] = atTrader ? traderExit : "", ["Map"] = Layout.Map.Length > 0 ? Layout.Map : (player.Location ?? ""), ["Profile"] = descriptor.ToUnparsedData(new Newtonsoft.Json.JsonConverter[0]).JObject };
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/save", body.ToString(Newtonsoft.Json.Formatting.None)));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Save (" + reason + ") failed: " + reply["Error"]);
                    NotificationManager.DisplayWarningNotification("The game could not be saved: " + reply["Error"]);
                    return "server refused: " + reply["Error"];
                }
                if (atTrader)
                {
                    Plugin.Log.LogInfo("Game saved (" + reason + ") at the trader's extract " + traderExit + ", " + WorldClock.Describe(WorldClock.Now));
                    NotificationManager.DisplayMessageNotification("Game saved. If you die, you are back here.", ENotificationDurationType.Long);
                    return "saved at " + traderExit;
                }
                Plugin.Log.LogInfo("Game saved (" + reason + ") at the bed of hideout '" + HideoutDoors.Inside + "', " + WorldClock.Describe(WorldClock.Now));
                NotificationManager.DisplayMessageNotification("Game saved. If you die, you wake up here.", ENotificationDurationType.Long);
                return "saved at " + HideoutDoors.Inside;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Saving failed: " + e);
                NotificationManager.DisplayWarningNotification("The game could not be saved, see log.");
                return "failed: " + e.Message;
            }
            finally
            {
                _saving = false;
            }
        }

        public static async void Save(string reason)
        {
            await SaveNow(reason);
        }

        /// <summary>Tells the server the raid is ending by "Save and go to menu" at this trader's extract, so it keeps that as the save.</summary>
        public static async Task TraderSave(string exit)
        {
            try
            {
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/save/trader", new JObject { ["Exit"] = exit }.ToString()));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Trader save not taken: " + (string)reply["Error"]);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Trader save not sent: " + e.Message);
            }
        }

        /// <summary>Asks the server whether there is a save and whether the next raid should begin at its bed.</summary>
        public static async Task<JObject> Status(bool consume)
        {
            try
            {
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/save/status", consume ? "{\"Consume\":true}" : "{}"));
                return ((bool?)reply["Ok"] ?? false) ? reply : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Save status not read: " + e.Message);
                return null;
            }
        }
    }
}
