using System;
using System.Collections;
using BepInEx.Configuration;
using EFT.Communications;
using EFT.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// After a death the open world normally decides by itself where the player turns up again
    /// (Vagabond's OnDeathGoTo setting). This asks instead: carry on at your hideout, or stay where
    /// you died. The answer is written into the open-world state on the server.
    /// </summary>
    public static class Death
    {
        public static ConfigEntry<bool> Ask;

        public enum Loss
        {
            /// <summary>The save is put back as it was, gear and all.</summary>
            KeepItems,
            /// <summary>The save is put back, but the gear is what the game leaves after a death.</summary>
            LoseCarriedGear,
            /// <summary>As above, and the secure container is emptied too.</summary>
            LoseEverything,
        }

        public static ConfigEntry<Loss> OnDeath;

        private static string ModeName(Loss loss)
        {
            return loss == Loss.LoseEverything ? "everything" : loss == Loss.LoseCarriedGear ? "tarkov" : "keep";
        }

        /// <summary>Tells the server what a death takes; it applies that when the raid ends.</summary>
        public static async void SendMode()
        {
            try
            {
                string body = JsonConvert.SerializeObject(new { Mode = ModeName(OnDeath.Value) });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/death/mode", body));
                if (!((bool?)reply["Ok"] ?? false))
                {
                    Plugin.Log.LogWarning("Death setting not taken by the server: " + reply["Error"]);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Death setting not sent: " + e.Message);
            }
        }

        /// <summary>A death has not been answered yet; ask once the main menu is up.</summary>
        public static bool Pending;

        private static bool _asking;

        public static void Bind(ConfigFile config)
        {
            Ask = config.Bind("Death", "Ask where to continue", true,
                "After dying, ask whether to continue at the hideout or where you died, instead of being moved automatically.");
            OnDeath = config.Bind("Death", "When you die", Loss.KeepItems,
                "What a death takes. KeepItems: your last save is put back with the gear you had then. "
                + "LoseCarriedGear: the save is put back, but what you wore and carried is lost (the secure container is kept, insurance works). "
                + "LoseEverything: the same, and the secure container is emptied too. Gear kept in the hideout is never lost.");
            OnDeath.SettingChanged += (_, __) => SendMode();
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
                if (string.IsNullOrEmpty(bed))
                {
                    Plugin.Log.LogInfo("Died with a save made at a trader (" + (string)status["Exit"] + ")");
                    NotificationManager.DisplayMessageNotification("You died. Your last save was put back" + LossText() + "; you will continue at the trader where you saved.",
                        ENotificationDurationType.Long);
                    _asking = false;
                    yield break;
                }
                Plugin.Log.LogInfo("Died with a save; the next raid begins at the bed of hideout '" + bed + "'");
                NotificationManager.DisplayMessageNotification("You died. Your last save was put back" + LossText() + "; you will wake up at your bed in the "
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

        private static string LossText()
        {
            switch (OnDeath.Value)
            {
                case Loss.LoseCarriedGear:
                    return ", but the gear you carried is lost";
                case Loss.LoseEverything:
                    return ", but everything you carried is lost, the secure container too";
                default:
                    return "";
            }
        }

        private static async void Choose(string choice)
        {
            try
            {
                string body = JsonConvert.SerializeObject(new { Choice = choice });
                var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/zeromenutime/death/goto", body));
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
