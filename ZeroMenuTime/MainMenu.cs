using System;
using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using System.Reflection;
using EFT;
using EFT.Communications;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroMenuTime
{
    /// <summary>
    /// The main menu, made over: a title and a short column of words at the top left (Continue,
    /// New Game, Settings, Exit) over a view of the player's own hideout. Character, traders and
    /// hideout are all reached inside the world now, so the game's own buttons and bottom bar go.
    ///
    /// Continue starts a raid where the character is; after a death or a crash that is the bed of
    /// the last save. New Game makes the character afresh on the server (keeping a backup) and
    /// closes the game, because the game only reads the character when it starts.
    /// </summary>
    public static class MainMenu
    {
        private const string PanelName = "ZeroMenuTime menu";
        public const string Title = "ZERO MENU TIME";

        private static MenuScreen _screen;
        private static GameObject _panel;
        private static TextMeshProUGUI _font;
        private static TextMeshProUGUI _whereLabel;
        private static float _nextCheck;
        private static bool _working;

        /// <summary>The line under Continue: where and when the game picks up.</summary>
        public static string Where => When.Length > 0 && _place.Length > 0 ? _place + ", " + When : _place + When;
        public static string When = "";
        private static string _place = "";
        private static bool _askedPlace;

        private static readonly Dictionary<string, string> Places = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "bigmap", "Customs" }, { "Customs", "Customs" }, { "factory4_day", "Factory" }, { "factory4_night", "Factory" },
            { "FactoryDay", "Factory" }, { "FactoryNight", "Factory" }, { "TarkovStreets", "Streets of Tarkov" }, { "Streets", "Streets of Tarkov" },
            { "Shoreline", "Shoreline" }, { "Woods", "Woods" }, { "Interchange", "Interchange" }, { "RezervBase", "Reserve" }, { "Reserve", "Reserve" },
            { "Lighthouse", "Lighthouse" }, { "laboratory", "The Lab" }, { "Sandbox", "Ground Zero" },
            { "Sandbox_high", "Ground Zero" }, { "GroundZero", "Ground Zero" }, { "Labyrinth", "The Labyrinth" },
        };

        /// <summary>The menu has come up again: where the character is may have changed.</summary>
        public static void Refresh()
        {
            _askedPlace = false;
        }

        private static async void AskPlace()
        {
            try
            {
                var state = await Seamless.FetchVagabondState();
                string map = state != null ? state.CurrentMap : null;
                string known;
                // Vagabond names the map by its database id; the game knows the short name for it.
                var app = SPT.Reflection.Utils.ClientAppUtils.GetMainApp();
                if (!string.IsNullOrEmpty(map) && app != null && app.Session != null && app.Session.LocationSettings != null)
                {
                    foreach (var candidate in app.Session.LocationSettings.locations.Values)
                    {
                        if (string.Equals(candidate._Id, map, StringComparison.OrdinalIgnoreCase))
                        {
                            map = candidate.Id;
                            break;
                        }
                    }
                }
                _place = string.IsNullOrEmpty(map) ? "" : Places.TryGetValue(map, out known) ? known : map;
            }
            catch (Exception)
            {
            }
        }

        private static readonly Color Ink = new Color32(231, 229, 212, 255);
        private static readonly Color Dim = new Color32(231, 229, 212, 120);

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
                // Everything the game put on this screen goes; ours is drawn instead.
                foreach (var stock in new MonoBehaviour[] { screen._playButton, screen._playerButton, screen._tradeButton, screen._hideoutButton,
                             screen._exitButton, screen._disconnectButton, screen._toggleGameModeButton, screen._hideScreenButton })
                {
                    if (stock != null && stock.gameObject.activeSelf)
                    {
                        stock.gameObject.SetActive(false);
                    }
                }
                if (screen._alphaWarningGameObject != null && screen._alphaWarningGameObject.activeSelf)
                {
                    screen._alphaWarningGameObject.SetActive(false);
                }
                var preloader = MonoBehaviourSingleton<PreloaderUI>.Instance;
                if (preloader != null && preloader.MenuTaskBar != null && preloader.MenuTaskBar.gameObject.activeSelf)
                {
                    preloader.SetMenuTaskBarVisibility(false);
                }

                if (_panel == null)
                {
                    Build(screen);
                }
                if (!_askedPlace)
                {
                    _askedPlace = true;
                    AskPlace();
                }
                ForRaid(screen, GamePlayerOwner.MyPlayer != null);
                if (_whereLabel != null && _whereLabel.text != Where)
                {
                    _whereLabel.text = Where;
                }
                MenuBackdrop.KeepStockHidden();
                Guide.Offer(screen, _font);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Arranging the main menu failed: " + e);
            }
        }

        /// <summary>
        /// The same screen comes up when Esc is pressed in a raid. There nothing may be started or
        /// reset: Continue becomes the way back to the game, and Reset is not offered.
        /// </summary>
        private static void ForRaid(MenuScreen screen, bool inRaid)
        {
            if (_panel == null)
            {
                return;
            }
            foreach (var item in _panel.GetComponentsInChildren<MenuItem>(true))
            {
                if (item.name == "item Continue")
                {
                    string words = inRaid ? "BACK TO THE GAME" : "CONTINUE";
                    if (item.Label.text != words)
                    {
                        item.Label.text = words;
                    }
                }
                else if (item.name == "item Reset" && item.gameObject.activeSelf == inRaid)
                {
                    item.gameObject.SetActive(!inRaid);
                }
            }
            if (_whereLabel != null && _whereLabel.gameObject.activeSelf == inRaid)
            {
                _whereLabel.gameObject.SetActive(!inRaid);
            }
        }

        private static void Build(MenuScreen screen)
        {
            // A reload of this plugin leaves the panel made by the one before.
            var left = screen.transform.Find(PanelName);
            if (left != null)
            {
                left.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(left.gameObject);
            }
            foreach (var child in screen.GetComponentsInChildren<Transform>(true))
            {
                // The New Game button an earlier build of this mod cloned from Play.
                if (child != null && child.name == "ZeroMenuTime New Game")
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
            var font = screen._playButton != null ? screen._playButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            _font = font;

            _panel = new GameObject(PanelName, typeof(RectTransform));
            var panel = (RectTransform)_panel.transform;
            panel.SetParent(screen.transform, false);
            panel.SetAsFirstSibling();
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;

            // Darkens the left of the picture so the words read against anything.
            var shade = new GameObject("shade", typeof(RectTransform), typeof(RawImage));
            var shadeRect = (RectTransform)shade.transform;
            shadeRect.SetParent(panel, false);
            shadeRect.anchorMin = new Vector2(0f, 0f);
            shadeRect.anchorMax = new Vector2(0.62f, 1f);
            shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
            var gradient = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++)
            {
                float t = x / 63f;
                gradient.SetPixel(x, 0, new Color(0f, 0f, 0f, 0.88f * (1f - t) * (1f - t)));
            }
            gradient.Apply();
            var shadeImage = shade.GetComponent<RawImage>();
            shadeImage.texture = gradient;
            shadeImage.raycastTarget = false;

            const float left0 = 110f;
            var title = Text(panel, "title", Title, font, 76f, Ink, new Vector2(left0, -110f), new Vector2(900f, 90f));
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 6f;

            var rule = new GameObject("rule", typeof(RectTransform), typeof(Image));
            var ruleRect = (RectTransform)rule.transform;
            ruleRect.SetParent(panel, false);
            ruleRect.anchorMin = ruleRect.anchorMax = ruleRect.pivot = new Vector2(0f, 1f);
            ruleRect.anchoredPosition = new Vector2(left0 + 2f, -206f);
            ruleRect.sizeDelta = new Vector2(84f, 3f);
            var ruleImage = rule.GetComponent<Image>();
            ruleImage.color = new Color32(197, 160, 72, 255);
            ruleImage.raycastTarget = false;

            float y = -270f;
            Item(panel, "Continue", font, new Vector2(left0, y), Continue);
            _whereLabel = Text(panel, "where", Where, font, 17f, Dim, new Vector2(left0 + 2f, y - 50f), new Vector2(700f, 26f));
            y -= 96f;
            Item(panel, "Reset", font, new Vector2(left0, y), AskNewGame);
            y -= 66f;
            Item(panel, "Settings", font, new Vector2(left0, y), () => Open(EMenuType.Settings));
            y -= 66f;
            Item(panel, "Guide", font, new Vector2(left0, y), () => Guide.Open(_screen, _font));
            y -= 66f;
            Item(panel, "Exit", font, new Vector2(left0, y), () => Open(EMenuType.Exit));
            Plugin.Log.LogInfo("Main menu built: Continue, Reset, Settings, Guide, Exit");
        }

        private static TextMeshProUGUI Text(RectTransform parent, string name, string words, TextMeshProUGUI like, float size, Color colour, Vector2 at, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = at;
            rect.sizeDelta = box;
            var label = go.AddComponent<TextMeshProUGUI>();
            if (like != null)
            {
                label.font = like.font;
                label.fontSharedMaterial = like.fontSharedMaterial;
            }
            label.text = words;
            label.fontSize = size;
            label.color = colour;
            label.alignment = TextAlignmentOptions.Left;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            return label;
        }

        private static void Item(RectTransform parent, string words, TextMeshProUGUI like, Vector2 at, Action onClick)
        {
            var go = new GameObject("item " + words, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = at + new Vector2(-24f, 6f);
            rect.sizeDelta = new Vector2(420f, 58f);
            var hit = go.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);

            var bar = new GameObject("bar", typeof(RectTransform), typeof(Image));
            var barRect = (RectTransform)bar.transform;
            barRect.SetParent(rect, false);
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.anchoredPosition = new Vector2(6f, 0f);
            barRect.sizeDelta = new Vector2(4f, -18f);
            var barImage = bar.GetComponent<Image>();
            barImage.color = new Color32(197, 160, 72, 255);
            barImage.raycastTarget = false;
            bar.SetActive(false);

            var label = Text(rect, "label", words.ToUpperInvariant(), like, 36f, Dim, new Vector2(24f, -6f), new Vector2(390f, 46f));
            label.characterSpacing = 3f;

            var item = go.AddComponent<MenuItem>();
            item.Label = label;
            item.Bar = bar;
            item.Clicked = onClick;
        }

        public static void OpenGuide()
        {
            Guide.Open(_screen, _font);
        }

        private static void Open(EMenuType what)
        {
            try
            {
                var operation = Seamless.MenuOperation();
                if (operation != null)
                {
                    operation.ShowScreen(what, true);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Opening " + what + " from the menu failed: " + e);
            }
        }

        /// <summary>The names on the menu, for the test harness.</summary>
        public static List<string> Items()
        {
            var names = new List<string>();
            if (_panel != null)
            {
                foreach (var item in _panel.GetComponentsInChildren<MenuItem>(false))
                {
                    names.Add(item.Label.text);
                }
            }
            return names;
        }

        /// <summary>Presses a menu item by name, as a click would.</summary>
        public static bool Press(string name)
        {
            if (_panel == null)
            {
                return false;
            }
            foreach (var item in _panel.GetComponentsInChildren<MenuItem>(false))
            {
                if (string.Equals(item.Label.text.Replace(" ", ""), name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                {
                    item.Clicked();
                    return true;
                }
            }
            return false;
        }

        public static void Tick()
        {
            bool showing = _screen != null && _screen.gameObject.activeInHierarchy;
            MenuBackdrop.Tick(showing);
            if (_screen == null || Time.unscaledTime < _nextCheck)
            {
                return;
            }
            _nextCheck = Time.unscaledTime + 0.5f;
            if (showing)
            {
                Arrange(_screen);
            }
        }

        public static void Continue()
        {
            Guide.Close();
            if (_working || Plugin.Instance == null)
            {
                return;
            }
            if (GamePlayerOwner.MyPlayer != null)
            {
                // Esc in a raid: back to the game, as the game's own button there does.
                if (_screen != null && _screen._hideScreenButton != null)
                {
                    _screen._hideScreenButton.OnClick.Invoke();
                }
                return;
            }
            Plugin.Log.LogInfo("Continue chosen");
            Plugin.Instance.StartCoroutine(Seamless.ContinueNow());
        }

        public static void AskNewGame()
        {
            if (_working || GamePlayerOwner.MyPlayer != null)
            {
                return;
            }
            var ui = ItemUiContext.Instance;
            if (ui == null)
            {
                return;
            }
            ui.ShowMessageWindow(
                "Reset the game?\n\nYour character, the hideout and your save are replaced by a fresh start: a small kit, waking in the hideout. "
                + "A backup of everything is kept on disk.\n\nThe game closes afterwards; start it again to play.",
                () =>
                {
                    if (Plugin.Instance != null)
                    {
                        Plugin.Instance.StartCoroutine(NewGame());
                    }
                },
                () => { },
                "Reset");
        }

        /// <summary>Has the server make the character afresh, then restarts the game so it reads it.</summary>
        public static IEnumerator NewGame()
        {
            _working = true;
            NotificationManager.DisplayMessageNotification("Resetting the game...", ENotificationDurationType.Long);
            var call = RequestHandler.PostJsonAsync("/zeromenutime/newgame", "{\"Confirm\":\"yes\"}");
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
                NotificationManager.DisplayWarningNotification("The game could not be reset: " + why);
                _working = false;
                yield break;
            }
            Plugin.Log.LogInfo("New game made on the server; old character kept in " + reply["Backup"] + ". Closing the game.");
            bool acknowledged = false;
            var ui = ItemUiContext.Instance;
            if (ui != null)
            {
                ui.ShowMessageWindow("The game is reset.\n\nIt will now close. Start it again and choose Continue.",
                    () => acknowledged = true, () => acknowledged = true, "Reset");
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
    }

    /// <summary>One line of the main menu: lights up under the pointer, acts on a click.</summary>
    public class MenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public TextMeshProUGUI Label;
        public GameObject Bar;
        public Action Clicked;

        private static readonly Color Lit = new Color32(255, 252, 235, 255);
        private static readonly Color Unlit = new Color32(231, 229, 212, 120);

        public void OnPointerEnter(PointerEventData data)
        {
            Label.color = Lit;
            Bar.SetActive(true);
            Sound(EUISoundType.ButtonOver);
        }

        public void OnPointerExit(PointerEventData data)
        {
            Label.color = Unlit;
            Bar.SetActive(false);
        }

        public void OnPointerClick(PointerEventData data)
        {
            Sound(EUISoundType.ButtonClick);
            if (Clicked != null)
            {
                Clicked();
            }
        }

        private void OnDisable()
        {
            if (Label != null)
            {
                Label.color = Unlit;
            }
            if (Bar != null)
            {
                Bar.SetActive(false);
            }
        }

        private static void Sound(EUISoundType kind)
        {
            try
            {
                if (Singleton<GUISounds>.Instantiated)
                {
                    Singleton<GUISounds>.Instance.PlayUISound(kind);
                }
            }
            catch (Exception)
            {
            }
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
            MainMenu.Refresh();
            MainMenu.Arrange(__instance);
        }
    }
}
