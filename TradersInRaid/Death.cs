using System;
using System.Collections;
using BepInEx.Configuration;
using EFT.Communications;
using EFT.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// After a death the open world normally decides by itself where the player turns up again
    /// (Vagabond's OnDeathGoTo setting). This asks instead: carry on at your hideout, or stay where
    /// you died. The answer is written into the open-world state on the server.
    /// </summary>
    public static class Death
    {
        public static ConfigEntry<bool> Ask;

        /// <summary>A death has not been answered yet; ask once the main menu is up.</summary>
        public static bool Pending;

        private static bool _asking;

        public static void Bind(ConfigFile config)
        {
            Ask = config.Bind("Death", "Ask where to continue", true,
                "After dying, ask whether to continue at the hideout or where you died, instead of being moved automatically.");
        }

        /// <summary>Runs when the main menu appears after a death.</summary>
        public static IEnumerator AskNow()
        {
            if (_asking)
            {
                yield break;
            }
            _asking = true;
            Pending = false;

            // Let the menu finish building before putting a window on top of it.
            yield return new WaitForSecondsRealtime(1.5f);

            // With a save, there is nothing to ask: the character wakes up at the bed it saved at.
            var asking = SaveGame.Status(false);
            while (!asking.IsCompleted)
            {
                yield return null;
            }
            var status = asking.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? asking.Result : null;
            if (status != null && ((bool?)status["Has"] ?? false))
            {
                string bed = (string)status["Hideout"];
                Plugin.Log.LogInfo("Died with a save; the next raid begins at the bed of hideout '" + bed + "'");
                NotificationManager.DisplayMessageNotification("You died. Your last save was put back; you will wake up at your bed in the "
                    + char.ToUpper(bed[0]) + bed.Substring(1) + " hideout.", ENotificationDurationType.Long);
                _asking = false;
                yield break;
            }

            var ui = ItemUiContext.Instance;
            if (ui == null)
            {
                Plugin.Log.LogWarning("Cannot ask where to continue: no menu UI");
                _asking = false;
                yield break;
            }

            Plugin.Log.LogInfo("Asking where to continue after death");
            try
            {
                ui.ShowMessageWindow(
                    "Where do you want to continue?\n\nYes — go to your hideout.\nNo — stay where you died.",
                    () => Choose("hideout"),
                    () => Choose("stay"),
                    "You died");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not ask where to continue: " + e);
            }
            _asking = false;
        }

        private static async void Choose(string choice)
        {
            try
            {
                string body = JsonConvert.SerializeObject(new { Choice = choice });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/tradersinraid/death/goto", body));
                if ((bool?)reply["Ok"] ?? false)
                {
                    string map = (string)reply["Map"];
                    Plugin.Log.LogInfo("After death, continuing at " + choice + " (" + map + ")");
                    NotificationManager.DisplayMessageNotification(
                        choice == "hideout"
                            ? "You will wake up at your hideout."
                            : "You will wake up where you died (" + map + ").",
                        ENotificationDurationType.Long);
                }
                else
                {
                    string error = (string)reply["Error"] ?? "unknown reason";
                    Plugin.Log.LogWarning("Death choice '" + choice + "' not applied: " + error);
                    NotificationManager.DisplayWarningNotification("Could not move you there: " + error);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Death choice failed: " + e);
                NotificationManager.DisplayWarningNotification("Could not reach the server; the usual death rules apply.");
            }
        }
    }
}
