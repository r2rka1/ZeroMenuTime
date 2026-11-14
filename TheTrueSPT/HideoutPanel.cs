using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheTrueSPT
{
    /// <summary>
    /// The window a hideout station opens: what its next level needs and what of that you have,
    /// what it can make, from what, how long that takes, and what is being made now. It takes the
    /// place of the long action lists the stations had. Everything it does goes through the same
    /// code those lists used (HideoutStations, HideoutCrafting, HideoutSleep).
    /// </summary>
    public static class HideoutPanel
    {
        private const string CanvasName = "TheTrueSPT hideout panel";

        private static readonly Color Ink = new Color32(231, 229, 212, 255);
        private static readonly Color Dim = new Color32(231, 229, 212, 140);
        private static readonly Color Gold = new Color32(197, 160, 72, 255);
        private const string Good = "#9CCC65", Bad = "#E57373", Faint = "#8A897E";

        private static GameObject _canvas;
        private static RectTransform _window;
        private static int _area = -1;
        private static string _name = "";
        private static TextMeshProUGUI _status, _note;
        private static HideoutCrafting.Craft _shownCraft;
        private static bool _shownDue;
        private static float _nextRefresh;
        private static bool _busy;
        private static TMP_FontAsset _font;

        public static bool IsOpen => _canvas != null;
        public static int Area => _area;

        public static void Open(int area, string name)
        {
            if (HideoutDoors.Inside == null)
            {
                return;
            }
            _area = area;
            _name = name;
            Build();
            GamePlayerOwner.SetIgnoreInputInNPCDialog(true);
            // While the player walks about, the game keeps its interface deaf to the mouse.
            Listen(true);
        }

        public static void Close()
        {
            if (_canvas != null)
            {
                UnityEngine.Object.Destroy(_canvas);
                _canvas = null;
            }
            _window = null;
            _status = _note = null;
            _area = -1;
            GamePlayerOwner.SetIgnoreInputInNPCDialog(false);
            Listen(false);
            try
            {
                CursorSwitcher.SetCursorLockMode(false, Screen.fullScreenMode);
            }
            catch (Exception)
            {
            }
        }

        private static void Listen(bool on)
        {
            try
            {
                UIEventSystem.Instance.SetTemporaryStatus(on);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hideout panel: could not switch the interface's mouse on or off: " + e.Message);
            }
        }

        /// <summary>Called every frame.</summary>
        public static void Tick()
        {
            if (_canvas == null)
            {
                return;
            }
            if (GamePlayerOwner.MyPlayer == null || HideoutDoors.Inside == null || Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            // The game takes the pointer back whenever it thinks the player is walking about.
            if (Cursor.lockState == CursorLockMode.Locked || !Cursor.visible)
            {
                CursorSwitcher.SetCursorLockMode(true, Screen.fullScreenMode);
                Cursor.visible = true;
            }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                var craft = HideoutCrafting.Here.FirstOrDefault(c => c.Area == _area);
                bool due = craft != null && craft.Due <= WorldClock.Now;
                if (craft != _shownCraft || due != _shownDue)
                {
                    Build();
                }
                else if (craft != null && _status != null)
                {
                    _status.text = StatusText(craft);
                }
            }
        }

        private static string StatusText(HideoutCrafting.Craft craft)
        {
            double left = craft.Due - WorldClock.Now;
            return left <= 0 ? "<color=" + Good + ">Ready to collect</color>" : "Ready in " + WorldClock.Span(left) + "  <color=" + Faint + ">(" + Played(left) + ")</color>";
        }

        /// <summary>Game time put as time at the keyboard: the world runs seven times as fast as the clock on the wall.</summary>
        private static string Played(double gameSeconds)
        {
            double minutes = gameSeconds / 7.0 / 60.0;
            return minutes < 1 ? "under a minute of play" : minutes < 90 ? "about " + Math.Round(minutes) + " min of play" : "about " + (minutes / 60.0).ToString("0.#") + " h of play";
        }

        private static string Stock(int have, int need)
        {
            return "<color=" + (have >= need ? Good : Bad) + ">" + have + " / " + need + "</color>";
        }

        // ---------------------------------------------------------------- building the window

        private static void Build()
        {
            var player = GamePlayerOwner.MyPlayer;
            if (player == null)
            {
                return;
            }
            float scroll = 1f;
            if (_canvas != null)
            {
                var old = _canvas.GetComponentInChildren<ScrollRect>();
                if (old != null)
                {
                    scroll = old.verticalNormalizedPosition;
                }
                UnityEngine.Object.Destroy(_canvas);
            }
            var stale = GameObject.Find(CanvasName);
            if (stale != null)
            {
                UnityEngine.Object.Destroy(stale);
            }

            _canvas = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            // Dims the world behind, and swallows clicks that miss the window.
            var veil = Node(_canvas.transform, "veil");
            Stretch(veil);
            veil.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            _window = Node(_canvas.transform, "window");
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(1180f, 760f);
            _window.gameObject.AddComponent<Image>().color = new Color32(14, 14, 13, 245);
            var edge = Node(_window, "edge");
            edge.anchorMin = new Vector2(0f, 1f);
            edge.anchorMax = new Vector2(1f, 1f);
            edge.pivot = new Vector2(0.5f, 1f);
            edge.sizeDelta = new Vector2(0f, 3f);
            edge.gameObject.AddComponent<Image>().color = Gold;

            var column = Node(_window, "column");
            Stretch(column);
            var columnLayout = Vertical(column, 18f, new RectOffset(36, 36, 30, 26));

            int level;
            HideoutStations.Levels.TryGetValue(_area, out level);
            bool canGrow = HideoutStations.NextCosts.ContainsKey(_area);

            // Heading: the station, its level, and the way out of the window.
            var head = Node(column, "head");
            Horizontal(head, 16f);
            var title = Text(head, _name.ToUpperInvariant(), 40f, Ink, true);
            title.characterSpacing = 4f;
            Flexible(title.gameObject, 1f, 0f);
            Text(head, level == 0 ? "NOT BUILT" : "LEVEL " + level + (canGrow ? "" : "  ·  FULLY BUILT"), 20f, level == 0 ? Dim : Gold, false).alignment = TextAlignmentOptions.MidlineRight;
            Button(head, "CLOSE  [ESC]", Close, true, 190f);

            var body = Node(column, "body");
            Horizontal(body, 34f).childAlignment = TextAnchor.UpperLeft;
            Flexible(body.gameObject, 1f, 1f);

            // Left: building, and what this station does for you directly.
            var left = Node(body, "left");
            Vertical(left, 10f, new RectOffset(0, 0, 0, 0));
            Fixed(left.gameObject, 430f);

            if (canGrow)
            {
                Heading(left, level == 0 ? "BUILD" : "UPGRADE TO LEVEL " + (level + 1));
                var tally = HideoutStations.Tally(player, _area);
                bool affordable = true;
                foreach (var pair in tally)
                {
                    Row(left, pair.Key.Tpl, HideoutStations.FullName(pair.Key.Tpl), Stock(pair.Value, pair.Key.Count), 40f);
                    affordable &= pair.Value >= pair.Key.Count;
                }
                if (tally.Count == 0)
                {
                    Text(left, "Needs nothing.", 18f, Dim, false);
                }
                Text(left, HideoutStations.IsFree ? "Free hideout is switched on (F12, Testing): nothing is taken." : "Taken from what you carry, then from this hideout's stash. Built at once.", 15f, Dim, false);
                int area = _area;
                string name = _name;
                Button(left, level == 0 ? "BUILD" : "UPGRADE", () => Do(() => HideoutStations.Build(area, name)), affordable && !_busy, 220f);
            }
            else
            {
                Heading(left, "FULLY BUILT");
                Text(left, "There is no further level to build.", 18f, Dim, false);
            }

            if (_area == (int)EAreaType.Stash && HideoutStash.Current != null)
            {
                Space(left, 14f);
                Heading(left, "STASH");
                Text(left, HideoutStash.Count() + " item(s) kept in this hideout.", 18f, Dim, false);
                Button(left, "OPEN STASH", () => { Close(); HideoutStash.Open(); }, true, 220f);
            }
            if (_area == HideoutSleep.RestSpace && level >= 1)
            {
                Space(left, 14f);
                Heading(left, "REST");
                Text(left, "Sleeping heals " + HideoutSleep.HealPerHour(level).ToString("0") + " health an hour, moves the clock on and saves the game. It is now " + WorldClock.Describe(WorldClock.Now) + ".", 16f, Dim, false);
                var naps = Node(left, "naps");
                Horizontal(naps, 10f);
                foreach (float hours in new[] { 2f, 4f, 8f })
                {
                    float slept = hours;
                    Button(naps, hours.ToString("0") + " H", () => { Close(); HideoutSleep.Sleep(slept); }, true, 96f);
                }
                float untilMorning = HideoutSleep.HoursUntilMorning();
                Button(left, "UNTIL MORNING  (" + untilMorning.ToString("0.#") + " H)", () => { Close(); HideoutSleep.Sleep(untilMorning); }, true, 312f);
                Button(left, "SAVE", () => { SaveGame.Save("at the bed"); Note("Saved."); }, true, 220f);
            }

            // Right: what it makes.
            var right = Node(body, "right");
            Vertical(right, 10f, new RectOffset(0, 0, 0, 0));
            Flexible(right.gameObject, 1f, 1f);
            Heading(right, "CRAFTING");

            var craft = HideoutCrafting.Here.FirstOrDefault(c => c.Area == _area);
            _shownCraft = craft;
            _shownDue = craft != null && craft.Due <= WorldClock.Now;
            _status = null;
            var recipes = HideoutCrafting.Recipes.Where(r => r.Area == _area).ToList();
            if (level < 1)
            {
                Text(right, "Build this station to craft here.", 18f, Dim, false);
            }
            else if (craft != null)
            {
                var card = Card(right);
                var making = Node(card, "making");
                Horizontal(making, 14f);
                Icon(making, craft.Product, 72f);
                Flexible(Text(making, Amount(craft.Product, craft.Count), 24f, Ink, true).gameObject, 1f, 0f);
                _status = Text(card, StatusText(craft), 18f, Ink, false);
                Text(card, "The result goes into this hideout's stash.", 15f, Dim, false);
                int area = _area;
                Button(card, "COLLECT", () => Do(() => HideoutCrafting.Collect(area)), _shownDue && !_busy, 220f);
                Text(right, "One thing at a time: the other recipes are back when this is collected.", 15f, Dim, false);
            }
            else if (recipes.Count == 0)
            {
                Text(right, "Nothing is made at this station.", 18f, Dim, false);
            }
            else
            {
                var spendable = HideoutStations.Spendable(player);
                var list = Scroll(right);
                // What can be made right now comes first.
                foreach (var recipe in recipes.OrderBy(r => HideoutCrafting.Missing(player, r) == null ? 0 : 1).ThenBy(r => HideoutStations.FullName(r.Product)))
                {
                    var card = Card(list);
                    var top = Node(card, "top");
                    Horizontal(top, 14f);
                    Icon(top, recipe.Product, 64f);
                    Flexible(Text(top, Amount(recipe.Product, recipe.Count), 22f, Ink, true).gameObject, 1f, 0f);
                    Text(top, WorldClock.Span(recipe.Seconds) + "  <color=" + Faint + ">" + Played(recipe.Seconds) + "</color>", 16f, Ink, false).alignment = TextAlignmentOptions.MidlineRight;

                    foreach (var cost in recipe.Items)
                    {
                        int have = spendable.Where(item => item.TemplateId == cost.Tpl).Sum(item => item.StackObjectsCount);
                        have = HideoutStations.IsFree ? Mathf.Max(have, cost.Count) : have;
                        Row(card, cost.Tpl, HideoutStations.FullName(cost.Tpl), Stock(have, cost.Count), 32f);
                    }
                    foreach (string tool in recipe.Tools)
                    {
                        bool have = HideoutStations.IsFree || spendable.Any(item => item.TemplateId == tool);
                        Row(card, tool, HideoutStations.FullName(tool) + "  <color=" + Faint + ">tool, not used up</color>",
                            "<color=" + (have ? Good : Bad) + ">" + (have ? "at hand" : "missing") + "</color>", 32f);
                    }
                    if (recipe.Items.Count == 0 && recipe.Tools.Count == 0)
                    {
                        Text(card, "Needs nothing.", 16f, Dim, false);
                    }
                    var chosen = recipe;
                    Button(card, "CRAFT", () => Do(() => HideoutCrafting.Start(chosen)), HideoutCrafting.Missing(player, recipe) == null && !_busy, 180f);
                }
                var view = list.GetComponentInParent<ScrollRect>();
                if (view != null)
                {
                    Canvas.ForceUpdateCanvases();
                    view.verticalNormalizedPosition = scroll;
                }
            }

            _note = Text(column, "", 16f, Gold, false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_window);
        }

        private static string Amount(string product, int count)
        {
            return (count > 1 ? count + " × " : "") + HideoutStations.FullName(product);
        }

        private static void Note(string words)
        {
            if (_note != null)
            {
                _note.text = words ?? "";
            }
        }

        /// <summary>Runs one of the station's deeds, then draws the window afresh with what came of it.</summary>
        private static async void Do(Func<System.Threading.Tasks.Task<string>> deed)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            string said = null;
            try
            {
                said = await deed();
            }
            catch (Exception e)
            {
                said = "That did not work: " + e.Message;
                Plugin.Log.LogError("Hideout panel: " + e);
            }
            _busy = false;
            if (_canvas != null)
            {
                Build();
                Note(said);
            }
        }

        // ---------------------------------------------------------------- pieces

        private static TMP_FontAsset Font()
        {
            if (_font == null)
            {
                TMP_FontAsset any = null;
                foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    any = any ?? font;
                    if (font.name.IndexOf("Bender", StringComparison.OrdinalIgnoreCase) >= 0 && font.name.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _font = font;
                        break;
                    }
                }
                _font = _font ?? any;
            }
            return _font;
        }

        private static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static VerticalLayoutGroup Vertical(RectTransform rect, float spacing, RectOffset padding)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            return layout;
        }

        private static HorizontalLayoutGroup Horizontal(RectTransform rect, float spacing)
        {
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            return layout;
        }

        private static void Flexible(GameObject go, float width, float height)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.flexibleWidth = width;
            element.flexibleHeight = height;
        }

        private static void Fixed(GameObject go, float width)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = width;
            element.flexibleWidth = 0f;
        }

        private static void Space(RectTransform parent, float height)
        {
            var gap = Node(parent, "gap");
            var element = gap.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = height;
        }

        private static TextMeshProUGUI Text(RectTransform parent, string words, float size, Color colour, bool bold)
        {
            var rect = Node(parent, "text");
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var font = Font();
            if (font != null)
            {
                label.font = font;
            }
            label.text = words;
            label.fontSize = size;
            label.color = colour;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = true;
            label.richText = true;
            label.raycastTarget = false;
            return label;
        }

        private static void Heading(RectTransform parent, string words)
        {
            var label = Text(parent, words, 17f, Gold, true);
            label.characterSpacing = 6f;
            var rule = Node(parent, "rule");
            rule.gameObject.AddComponent<Image>().color = new Color32(231, 229, 212, 40);
            var element = rule.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = 1f;
        }

        /// <summary>An item's picture, its name on the left and a figure on the right.</summary>
        private static void Row(RectTransform parent, string template, string name, string figure, float picture)
        {
            var row = Node(parent, "row");
            Horizontal(row, 12f);
            Icon(row, template, picture);
            float size = picture >= 40f ? 19f : 17f;
            Flexible(Text(row, name, size, Ink, false).gameObject, 1f, 0f);
            Text(row, figure, size, Ink, false).alignment = TextAlignmentOptions.MidlineRight;
        }

        private static readonly Dictionary<string, ItemIcon> Icons = new Dictionary<string, ItemIcon>();

        /// <summary>The game's own picture of an item, in a dark square. It fills in when the game has drawn it.</summary>
        private static void Icon(RectTransform parent, string template, float size)
        {
            var frame = Node(parent, "icon");
            frame.gameObject.AddComponent<Image>().color = new Color32(0, 0, 0, 120);
            var element = frame.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = size;
            element.preferredHeight = element.minHeight = size;
            element.flexibleWidth = 0f;
            var picture = Node(frame, "picture");
            Stretch(picture);
            picture.offsetMin = new Vector2(3f, 3f);
            picture.offsetMax = new Vector2(-3f, -3f);
            var image = picture.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            try
            {
                ItemIcon icon;
                if (!Icons.TryGetValue(template, out icon) || icon == null)
                {
                    var item = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), template, null);
                    icon = EFT.UI.DragAndDrop.ItemViewFactory.LoadItemIcon(item);
                    Icons[template] = icon;
                }
                var view = picture.gameObject.AddComponent<PanelIcon>();
                view.Source = icon;
                view.Target = image;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hideout panel: no picture for " + template + ": " + e.Message);
            }
        }

        private static RectTransform Card(RectTransform parent)
        {
            var card = Node(parent, "card");
            card.gameObject.AddComponent<Image>().color = new Color32(255, 255, 255, 10);
            Vertical(card, 8f, new RectOffset(16, 16, 12, 14));
            return card;
        }

        /// <summary>A list that scrolls. Gives back the place to put its entries.</summary>
        private static RectTransform Scroll(RectTransform parent)
        {
            var scroll = Node(parent, "scroll");
            Flexible(scroll.gameObject, 1f, 1f);
            scroll.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
            var view = scroll.gameObject.AddComponent<ScrollRect>();
            var viewport = Node(scroll, "viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Node(viewport, "content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            Vertical(content, 10f, new RectOffset(0, 8, 0, 0));
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.viewport = viewport;
            view.content = content;
            view.horizontal = false;
            view.vertical = true;
            view.movementType = ScrollRect.MovementType.Clamped;
            view.scrollSensitivity = 40f;
            return content;
        }

        private static void Button(RectTransform parent, string words, Action onClick, bool enabled, float width)
        {
            var holder = Node(parent, "button " + words);
            var image = holder.gameObject.AddComponent<Image>();
            image.color = enabled ? new Color32(231, 229, 212, 28) : new Color32(231, 229, 212, 10);
            var element = holder.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = width;
            element.preferredHeight = element.minHeight = 42f;
            element.flexibleWidth = 0f;
            var label = Text(holder, words, 18f, enabled ? Ink : new Color32(231, 229, 212, 70), true);
            Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            label.characterSpacing = 3f;
            label.enableWordWrapping = false;
            var button = holder.gameObject.AddComponent<PanelButton>();
            button.Background = image;
            button.Label = label;
            button.Clicked = onClick;
            button.Usable = enabled;
            // A button in a column must not be stretched to the column's width.
            var group = parent.GetComponent<VerticalLayoutGroup>();
            if (group != null)
            {
                var wrap = Node(parent, "button row");
                Horizontal(wrap, 0f);
                holder.SetParent(wrap, false);
            }
        }

        /// <summary>Presses a button of the open window by its words; for the test harness.</summary>
        public static bool Press(string words)
        {
            if (_canvas == null)
            {
                return false;
            }
            foreach (var button in _canvas.GetComponentsInChildren<PanelButton>())
            {
                if (button.Usable && button.Label.text.Replace(" ", "").StartsWith(words.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                {
                    button.Clicked();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Everything the open window says, for the test harness.</summary>
        public static List<string> Lines()
        {
            var lines = new List<string>();
            if (_canvas != null)
            {
                foreach (var label in _canvas.GetComponentsInChildren<TextMeshProUGUI>())
                {
                    if (!string.IsNullOrEmpty(label.text))
                    {
                        lines.Add(System.Text.RegularExpressions.Regex.Replace(label.text, "<[^>]+>", ""));
                    }
                }
            }
            return lines;
        }
    }

    /// <summary>Shows an item's picture as soon as the game has drawn it.</summary>
    public class PanelIcon : MonoBehaviour
    {
        public ItemIcon Source;
        public Image Target;

        private void Update()
        {
            if (Source != null && Target != null && Source.Sprite != null && Target.sprite != Source.Sprite)
            {
                Target.sprite = Source.Sprite;
                Target.enabled = true;
            }
        }
    }

    /// <summary>A button of the hideout window.</summary>
    public class PanelButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Image Background;
        public TextMeshProUGUI Label;
        public Action Clicked;
        public bool Usable;

        public void OnPointerEnter(PointerEventData data)
        {
            if (Usable)
            {
                Background.color = new Color32(231, 229, 212, 235);
                Label.color = new Color32(14, 14, 13, 255);
                Sound(EUISoundType.ButtonOver);
            }
        }

        public void OnPointerExit(PointerEventData data)
        {
            if (Usable)
            {
                Background.color = new Color32(231, 229, 212, 28);
                Label.color = new Color32(231, 229, 212, 255);
            }
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (Usable && Clicked != null)
            {
                Sound(EUISoundType.ButtonClick);
                Clicked();
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
}
