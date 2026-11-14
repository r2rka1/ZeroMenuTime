using System;
using System.Collections;
using System.Reflection;
using EFT;
using EFT.Communications;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// The main menu, cut down to what this way of playing needs: Continue, New Game and Exit.
    /// Character, traders and hideout are all reached inside the world now, so their buttons go,
    /// here and on the bar along the bottom.
    ///
    /// Continue starts a raid where the character is; after a death or a crash that is the bed of
    /// the last save. New Game makes the character afresh on the server (keeping a backup) and
    /// closes the game, because the game only reads the character when it starts.
    /// </summary>
    public static class MainMenu
    {
        private const string NewGameName = "TradersInRaid New Game";

        private static MenuScreen _screen;
        private static DefaultUIButton _newGame;
        private static float _nextCheck;
        private static bool _working;

        /// <summary>Called when the menu is shown, and then twice a second while it stays up: other code switches buttons back on.</summary>
        public static void Arrange(MenuScreen screen)
        {
            if (screen == null)
            {
                return;
            }
            _screen = screen;
            try
            {
                foreach (var unwanted in new MonoBehaviour[] { screen._playerButton, screen._tradeButton, screen._hideoutButton, screen._toggleGameModeButton, screen._hideScreenButton })
                {
                    if (unwanted != null && unwanted.gameObject.activeSelf)
                    {
                        unwanted.gameObject.SetActive(false);
                    }
                }

                var play = screen._playButton;
                if (play != null && play.GetComponent<ContinueMark>() == null)
                {
                    play.gameObject.AddComponent<ContinueMark>();
                    play.OnClick.RemoveAllListeners();
                    play.OnClick.AddListener(Continue);
                }
                if (play != null)
                {
                    play.SetHeaderText("CONTINUE");
                }

                if (_newGame == null && play != null)
                {
                    // A reload of this plugin leaves the button made by the one before; take it over.
                    var left = play.transform.parent.Find(NewGameName);
                    if (left != null)
                    {
                        UnityEngine.Object.Destroy(left.gameObject);
                    }
                    var copy = UnityEngine.Object.Instantiate(play.gameObject, play.transform.parent);
                    copy.name = NewGameName;
                    copy.transform.SetSiblingIndex(play.transform.GetSiblingIndex() + 1);
                    var mark = copy.GetComponent<ContinueMark>();
                    if (mark != null)
                    {
                        UnityEngine.Object.Destroy(mark);
                    }
                    _newGame = copy.GetComponent<DefaultUIButton>();
                    _newGame.OnClick.RemoveAllListeners();
                    _newGame.OnClick.AddListener(AskNewGame);
                    Plugin.Log.LogInfo("Main menu arranged: Continue, New Game, Exit");
                }
                if (_newGame != null && play != null)
                {
                    // The menu's buttons are placed by hand, not by a layout, so the copy starts
                    // exactly on top of Continue. Put it one button's height below.
                    var above = play.GetComponent<RectTransform>();
                    var below = _newGame.GetComponent<RectTransform>();
                    if (above != null && below != null)
                    {
                        float height = Mathf.Max(above.rect.height, 50f);
                        below.anchoredPosition = above.anchoredPosition + new Vector2(0f, -(height + 24f));
                    }
                }
                if (_newGame != null)
                {
                    _newGame.SetHeaderText("NEW GAME");
                    if (!_newGame.gameObject.activeSelf)
                    {
                        _newGame.gameObject.SetActive(true);
                    }
                }

                // The bar along the bottom has its own ways into the same screens.
                foreach (var bar in UnityEngine.Object.FindObjectsOfType<MenuTaskBar>())
                {
                    if (bar._toggleButtons == null)
                    {
                        continue;
                    }
                    foreach (var kind in new[] { EMenuType.Player, EMenuType.Trade, EMenuType.Hideout, EMenuType.RagFair })
                    {
                        AnimatedToggle toggle;
                        if (bar._toggleButtons.TryGetValue(kind, out toggle) && toggle != null && toggle.gameObject.activeSelf)
                        {
                            toggle.gameObject.SetActive(false);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Arranging the main menu failed: " + e.Message);
            }
        }

        public static void Tick()
        {
            if (_screen == null || Time.unscaledTime < _nextCheck)
            {
                return;
            }
            _nextCheck = Time.unscaledTime + 0.5f;
            if (_screen.gameObject.activeInHierarchy)
            {
                Arrange(_screen);
            }
        }

        public static void Continue()
        {
            if (_working || Plugin.Instance == null)
            {
                return;
            }
            Plugin.Log.LogInfo("Continue chosen");
            Plugin.Instance.StartCoroutine(Seamless.ContinueNow());
        }

        public static void AskNewGame()
        {
            if (_working)
            {
                return;
            }
            var ui = ItemUiContext.Instance;
            if (ui == null)
            {
                return;
            }
            ui.ShowMessageWindow(
                "Start a new game?\n\nYour character, all three hideouts and your save are replaced by a fresh start. "
                + "A backup of everything is kept on disk.\n\nThe game closes afterwards; start it again to play.",
                () =>
                {
                    if (Plugin.Instance != null)
                    {
                        Plugin.Instance.StartCoroutine(NewGame());
                    }
                },
                () => { },
                "New game");
        }

        /// <summary>Has the server make the character afresh, then restarts the game so it reads it.</summary>
        public static IEnumerator NewGame()
        {
            _working = true;
            NotificationManager.DisplayMessageNotification("Starting a new game...", ENotificationDurationType.Long);
            var call = RequestHandler.PostJsonAsync("/tradersinraid/newgame", "{\"Confirm\":\"yes\"}");
            while (!call.IsCompleted)
            {
                yield return null;
            }
            JObject reply = null;
            try
            {
                reply = JObject.Parse(call.Result);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("New game: " + e.Message);
            }
            if (reply == null || !((bool?)reply["Ok"] ?? false))
            {
                string why = reply != null ? (string)reply["Error"] : "no answer from the server";
                Plugin.Log.LogWarning("New game failed: " + why);
                NotificationManager.DisplayWarningNotification("A new game could not be started: " + why);
                _working = false;
                yield break;
            }
            Plugin.Log.LogInfo("New game made on the server; old character kept in " + reply["Backup"] + ". Closing the game.");
            bool acknowledged = false;
            var ui = ItemUiContext.Instance;
            if (ui != null)
            {
                ui.ShowMessageWindow("Your new game is ready.\n\nThe game will now close. Start it again and choose Continue.",
                    () => acknowledged = true, () => acknowledged = true, "New game");
            }
            // Close anyway after a while, in case the window could not be shown or answered.
            for (float waited = 0f; waited < 12f && !acknowledged; waited += Time.unscaledDeltaTime)
            {
                yield return null;
            }
            Restart();
        }

        /// <summary>
        /// Closes the game. It only reads the character when it starts, so a new game needs a fresh
        /// start; starting a second copy from inside this one proved unreliable, so the player
        /// starts it again themselves.
        /// </summary>
        private static void Restart()
        {
            Application.Quit();
        }

        /// <summary>Marks the Play button once it has been turned into Continue.</summary>
        private class ContinueMark : MonoBehaviour
        {
        }
    }

    /// <summary>Every time the main menu is shown, cut it down.</summary>
    public class MainMenuPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // The overload that takes the profile; found by its first parameter so its other types need not be named.
            foreach (var method in typeof(MenuScreen).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var parameters = method.GetParameters();
                if (method.Name == "Show" && parameters.Length == 3 && parameters[0].ParameterType == typeof(Profile))
                {
                    return method;
                }
            }
            throw new MissingMethodException("MenuScreen.Show(Profile, ...)");
        }

        [PatchPostfix]
        public static void Postfix(MenuScreen __instance)
        {
            MainMenu.Arrange(__instance);
        }
    }
}
