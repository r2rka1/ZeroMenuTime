using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroMenuTime
{
    /// <summary>
    /// A short guide shown over the main menu when the game starts: where the first trader is, how
    /// trading, the hideout, crafting, repairs and saving work, and a choice of what a death takes.
    /// It can be stepped through or closed at any point, and switched off with "Don't show again".
    /// The pictures are short loops of the mod in use, kept as plain picture files beside the plugin.
    /// </summary>
    public static class Guide
    {
        public static ConfigEntry<bool> Show;

        private class Page
        {
            public string Title, Text, Pictures;
            public bool DeathChoice, ModelsStatus;
            /// <summary>Shown only when the co-op mod is installed.</summary>
            public bool CoopOnly;
        }

        private static Page[] _pages;

        /// <summary>The pages for this install: the co-op page only where co-op is possible.</summary>
        private static Page[] Pages
        {
            get
            {
                if (_pages == null)
                {
                    var shown = new System.Collections.Generic.List<Page>();
                    foreach (var page in AllPages)
                    {
                        if (!page.CoopOnly || Net.FikaInstalled)
                        {
                            shown.Add(page);
                        }
                    }
                    _pages = shown.ToArray();
                }
                return _pages;
            }
        }

        private static readonly Page[] AllPages =
        {
            new Page
            {
                Title = "Welcome to Zero Menu Time",
                Pictures = "skier",
                Text = "There are no menus between raids here. You trade, take quests, build your hideout, sleep and save <b>inside the world</b>, and the next raid starts where you stood.\n\n"
                       + "This guide takes a minute. You can close it at any step, and open it again from the main menu.",
            },
            new Page
            {
                Title = "Traders live on the maps",
                Pictures = "skier",
                Text = "Every trader has a <b>door</b> on one map. Walk up to it, look at it and choose <b>Enter</b>: the trader's own room is behind it, and nothing can follow you in.\n\n"
                       + "A new character wakes on <b>Customs</b>. <b>Skier</b> is there: the trailer row just north of the Crackhouse, between the Crackhouse and the Repair Shop. His is the trailer with the wooden pallet in front of its door, next to the metal stairs.\n\n"
                       + "The other traders and their doors are listed with pictures on the mod's page, under <b>Where the doors are</b>. With the Dynamic Maps mod the doors are marked on your map.",
            },
            new Page
            {
                Title = "Trading and quests",
                Pictures = "trade",
                Text = "Look at the trader and choose <b>Trade</b>. You pay from what you carry, and you can sell what you carry or wear.\n\n"
                       + "What you buy is <b>laid on the table</b> beside you; pick it up by hand. Quest rewards, the items a quest gives you at the start, and mail from traders arrive on the same table. Anything you leave there waits for your next visit.\n\n"
                       + "Quests are taken and handed in at the trader, in the <b>Tasks</b> tab.",
            },
            new Page
            {
                Title = "Your hideout",
                Pictures = "hideout",
                Text = "The hideout is behind a real door on <b>Customs</b>, <b>Streets</b> and <b>Shoreline</b>: one hideout, three ways in. On Customs it is the ZB-1011 bunker, a small concrete hut on the east edge of the map, past Warehouse 7: go down the stairs, the steel door at the end of the tunnel.\n\n"
                       + "Inside you are safe, and the raid above stands still. It is your stash, your workshop and your bed.\n\n"
                       + "To leave, use the door you came in by.",
            },
            new Page
            {
                Title = "Building stations",
                Pictures = "build",
                Text = "Walk up to the place of a station, look at it and open its window. It lists what the next level needs; the items are taken from what you carry, then from the stash.\n\n"
                       + "Start with the <b>Generator</b> and put fuel in: without power the lights are out and nothing electric works. The <b>Stash</b> station opens your stash and can sort it.",
            },
            new Page
            {
                Title = "Crafting",
                Pictures = "craft",
                Text = "A built station's window also lists what it can <b>craft</b>. Crafts take game time and keep running while you raid or sleep; collect the result at the station.\n\n"
                       + "At the Workbench you can also modify your weapons.",
            },
            new Page
            {
                Title = "Repairs",
                Pictures = "repair",
                Text = "<b>Repair kits</b> work in a raid on what you carry: open your inventory, right-click the weapon or armour and choose <b>Repair</b>.\n\n"
                       + "The kits counted are the ones you carry, and those in the stash while you are in the hideout.\n\n"
                       + "As always, weapons need the <b>Workbench</b> built and armour needs the <b>Lavatory</b>.",
            },
            new Page
            {
                Title = "Moving traders, and traders of other mods",
                Text = "Every trader's place is a setting. Press <b>F12</b> in game, open <b>r2rka1-ZeroMenuTime</b> and find the group <b>Trader - NAME</b> (Ref, whom Vagabond gives no place, has one too): pick the <b>Map</b>, then go through that map's doors with the arrows at <b>Door</b>. Each door is one the map already has, shut for good. The change applies from the next raid." + "\n\n"
                       + "A trader added by <b>another mod</b> has no place until you give them one. They are listed as <b>Custom trader - NAME</b> with the map set to <b>Not placed</b>:" + "\n"
                       + "1. Set <b>Map</b> to the map they should be on.\n"
                       + "2. Pick a <b>Door</b> with the arrows (a door already taken says by whom).\n"
                       + "3. Choose the <b>Room</b> (plain, or furnished like one of the game's traders) and the <b>Figure</b> that stands in it.\n"
                       + "4. Start the next raid on that map: the door is marked on the map like the others." + "\n\n"
                       + "Playing with friends, the host's settings count for everyone.",
            },
            new Page
            {
                Title = "Real trader models and voices",
                Pictures = "models",
                ModelsStatus = true,
                Text = "Seven traders can stand in their rooms as their <b>real models</b> and say a line when you start to trade. The mod does not contain them: it reads them from <b>your own installed copy of the 1.0 game</b>, and only reads." + "\n\n"
                       + "If you own it and it is installed, there is usually nothing to do. If the models do not show, press <b>F12</b> in game, open <b>r2rka1-ZeroMenuTime</b>, and under <b>Trader models</b> fill in <b>Folder of the 1.0 game</b>: the folder the game is installed in. That is needed for a Steam install or a copy moved by hand.\n\n"
                       + "Without the 1.0 game nothing is missing: the traders are stand-in figures and do not speak.",
            },
            new Page
            {
                Title = "Saving, sleeping and dying",
                Text = "You save by <b>sleeping in your hideout's bed</b>, or by choosing <b>Save and go to menu</b> at a trader. A death puts you back at your last save.\n\n"
                       + "Choose what a death takes from you. You can change this at any time in the settings (F12, Death).",
                DeathChoice = true,
            },
            new Page
            {
                Title = "Playing with friends",
                CoopOnly = true,
                Text = "<b>Continue</b> hosts your own world; friends come into it with <b>Join a friend</b>, at any time. <b>Continue on headless</b> hosts it on a headless host instead, so you can leave and come back while others play on.\n\n"
                       + "It is the host's world: their clock, their hideout, one stash for everyone. <b>What you put in the stash is the host's.</b> Only the host builds.\n\n"
                       + "The raid stands still only while <b>everyone</b> is in a trader's room or the hideout. Sleeping and waiting need everyone's yes.\n\n"
                       + "If you die, your gear drops where you fell and you watch the others. A teammate can <b>revive</b> you at a trader for 100,000 roubles, or free at the hideout's medical station. You come back where you came into the map, with nothing. If everyone dies, the raid is over.",
            },
            new Page
            {
                Title = "That is all",
                Text = "Press <b>Continue</b> in the main menu to carry on where you left off.\n\n"
                       + "Every part of the mod has a setting: press <b>F12</b> in game and open r2rka1-ZeroMenuTime. The mod's page lists all the doors, the settings and the known limits.\n\n"
                       + "This guide can be opened again from the main menu.",
            },
        };

        private static readonly Color Ink = new Color32(231, 229, 212, 255);
        private static readonly Color Dim = new Color32(231, 229, 212, 140);
        private static readonly Color Gold = new Color32(197, 160, 72, 255);
        private static readonly Color Panel = new Color32(17, 19, 21, 252);
        private static readonly Color Plate = new Color32(40, 43, 46, 255);

        private static GameObject _root;
        private static TextMeshProUGUI _font, _title, _text, _count, _nextLabel, _tick;
        private static RawImage _picture;
        private static GuideFilm _film;
        private static RectTransform _choices;
        private static GuideButton _back;
        private static readonly List<GuideButton> ChoiceButtons = new List<GuideButton>();
        private static int _page;
        private static bool _offered, _dontShow;

        public static bool IsOpen => _root != null;
        public static int PageNumber => _page + 1;

        public static void Bind(ConfigFile config)
        {
            Show = config.Bind("General", "Show the guide at start", true,
                "When the game starts, a short guide is shown over the main menu. It can also be opened from the main menu at any time.");
        }

        /// <summary>Called when the main menu is drawn: shows the guide once per start of the game, if wanted.</summary>
        public static void Offer(MenuScreen screen, TextMeshProUGUI font)
        {
            if (_offered || Show == null || !Show.Value)
            {
                return;
            }
            _offered = true;
            Open(screen, font);
        }

        public static void Open(MenuScreen screen, TextMeshProUGUI font)
        {
            try
            {
                Close(false);
                if (screen == null)
                {
                    return;
                }
                _font = font;
                _dontShow = Show != null && !Show.Value;
                Build((RectTransform)screen.transform);
                Go(0);
                Plugin.Log.LogInfo("Guide opened");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("The guide could not be shown: " + e);
                Close(false);
            }
        }

        public static void Close(bool keepChoice = true)
        {
            if (_root != null)
            {
                if (keepChoice && Show != null && Show.Value == _dontShow)
                {
                    Show.Value = !_dontShow;
                    Plugin.Log.LogInfo("Guide at start: " + (Show.Value ? "on" : "off"));
                }
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            ChoiceButtons.Clear();
        }

        public static void Next()
        {
            if (_page >= Pages.Length - 1)
            {
                Close();
            }
            else
            {
                Go(_page + 1);
            }
        }

        public static void Back()
        {
            if (_page > 0)
            {
                Go(_page - 1);
            }
        }

        public static void Go(int page)
        {
            if (_root == null)
            {
                return;
            }
            _page = Mathf.Clamp(page, 0, Pages.Length - 1);
            var shown = Pages[_page];
            _title.text = shown.Title.ToUpperInvariant();
            _text.text = shown.Text + (shown.ModelsStatus ? ModelsLine() : "");
            _count.text = (_page + 1) + " / " + Pages.Length;
            _nextLabel.text = _page == Pages.Length - 1 ? "DONE" : "NEXT";
            _back.gameObject.SetActive(_page > 0);
            _choices.gameObject.SetActive(shown.DeathChoice);
            if (shown.DeathChoice)
            {
                MarkChoice();
            }
            _film.Load(shown.Pictures == null ? null : Path.Combine(TraderConfig.Dir, "guide", shown.Pictures));
            // Without a picture the words take the whole width.
            bool pictured = _film.Has;
            _picture.gameObject.SetActive(pictured);
            var words = (RectTransform)_text.transform;
            words.anchoredPosition = new Vector2(pictured ? 640f : 44f, -112f);
            words.sizeDelta = new Vector2(pictured ? 556f : 1152f, shown.DeathChoice ? 190f : 420f);
        }

        /// <summary>Whether the player's copy of the 1.0 game has been found, said in a line.</summary>
        private static string ModelsLine()
        {
            string found = TraderModelBundle.FoundAt();
            return "\n\n" + (found != null
                ? "<color=#8FBF6A>Found on this PC:</color> " + found
                : "<color=#C8A050>Not found on this PC.</color> Fill in the folder if you have it installed.");
        }

        public static void SetDontShow(bool dontShow)
        {
            _dontShow = dontShow;
            if (_tick != null)
            {
                _tick.text = dontShow ? "X" : "";
            }
        }

        private static void Choose(Death.Loss what)
        {
            if (Death.OnDeath != null)
            {
                Death.OnDeath.Value = what;
            }
            MarkChoice();
        }

        private static void MarkChoice()
        {
            for (int i = 0; i < ChoiceButtons.Count; i++)
            {
                ChoiceButtons[i].SetChosen(Death.OnDeath != null && (int)Death.OnDeath.Value == i);
            }
        }

        private static void Build(RectTransform parent)
        {
            _root = new GameObject("ZeroMenuTime guide", typeof(RectTransform), typeof(Image));
            var root = (RectTransform)_root.transform;
            root.SetParent(parent, false);
            root.SetAsLastSibling();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            // Darkens the menu and takes the clicks meant for it.
            _root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.74f);

            var window = Box(root, "window", Panel, Vector2.zero, new Vector2(1240f, 690f));
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            var rule = Box(window, "rule", Gold, new Vector2(0f, 0f), new Vector2(1240f, 3f));
            rule.GetComponent<Image>().raycastTarget = false;

            _title = Words(window, "title", "", 34f, Ink, new Vector2(44f, -34f), new Vector2(900f, 46f), false);
            _title.fontStyle = FontStyles.Bold;
            _title.characterSpacing = 4f;
            _count = Words(window, "count", "", 20f, Dim, new Vector2(1010f, -42f), new Vector2(110f, 30f), false);
            _count.alignment = TextAlignmentOptions.Right;
            Button(window, "close", "X", new Vector2(1150f, -28f), new Vector2(56f, 50f), () => Close());

            var picture = new GameObject("picture", typeof(RectTransform), typeof(RawImage));
            var pictureRect = (RectTransform)picture.transform;
            pictureRect.SetParent(window, false);
            pictureRect.anchorMin = pictureRect.anchorMax = pictureRect.pivot = new Vector2(0f, 1f);
            pictureRect.anchoredPosition = new Vector2(44f, -112f);
            pictureRect.sizeDelta = new Vector2(560f, 315f);
            _picture = picture.GetComponent<RawImage>();
            _picture.raycastTarget = false;
            _film = picture.AddComponent<GuideFilm>();
            _film.Screen = _picture;

            _text = Words(window, "text", "", 22f, Ink, new Vector2(640f, -112f), new Vector2(556f, 420f), true);
            _text.lineSpacing = 8f;

            // What a death takes: one of three.
            var choices = new GameObject("choices", typeof(RectTransform));
            _choices = (RectTransform)choices.transform;
            _choices.SetParent(window, false);
            _choices.anchorMin = _choices.anchorMax = _choices.pivot = new Vector2(0f, 1f);
            _choices.anchoredPosition = new Vector2(44f, -318f);
            _choices.sizeDelta = new Vector2(1152f, 250f);
            ChoiceButtons.Clear();
            string[] names = { "KEEP EVERYTHING", "LOSE WHAT YOU CARRIED", "LOSE EVERYTHING" };
            string[] notes =
            {
                "Your last save is put back with the gear you had then. The gentlest choice, and the one the mod starts with.",
                "Your last save is put back, but what you wore and carried is lost. The secure container is kept and insurance works.",
                "The same, and the secure container is emptied too. Only what is in the hideout is safe.",
            };
            for (int i = 0; i < 3; i++)
            {
                var what = (Death.Loss)i;
                var choice = Button(_choices, "choice " + i, names[i], new Vector2(i * 388f, 0f), new Vector2(376f, 56f), () => Choose(what));
                ChoiceButtons.Add(choice);
                var note = Words(_choices, "note " + i, notes[i], 18f, Dim, new Vector2(i * 388f + 4f, -70f), new Vector2(368f, 150f), true);
                note.lineSpacing = 4f;
            }

            // Don't show again.
            var check = Button(window, "check", "", new Vector2(44f, -622f), new Vector2(34f, 34f), () => SetDontShow(!_dontShow));
            _tick = check.Label;
            _tick.fontSize = 22f;
            Words(window, "check words", "Don't show again", 20f, Ink, new Vector2(92f, -626f), new Vector2(320f, 30f), false);
            SetDontShow(_dontShow);

            _back = Button(window, "back", "BACK", new Vector2(884f, -612f), new Vector2(150f, 52f), Back);
            var next = Button(window, "next", "NEXT", new Vector2(1046f, -612f), new Vector2(150f, 52f), Next);
            _nextLabel = next.Label;
            next.SetChosen(true);
        }

        private static RectTransform Box(RectTransform parent, string name, Color colour, Vector2 at, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = at;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = colour;
            return rect;
        }

        private static TextMeshProUGUI Words(RectTransform parent, string name, string words, float size, Color colour, Vector2 at, Vector2 box, bool wrap)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = at;
            rect.sizeDelta = box;
            var label = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                label.font = _font.font;
                label.fontSharedMaterial = _font.fontSharedMaterial;
            }
            label.text = words;
            label.fontSize = size;
            label.color = colour;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.enableWordWrapping = wrap;
            label.richText = true;
            label.raycastTarget = false;
            return label;
        }

        private static GuideButton Button(RectTransform parent, string name, string words, Vector2 at, Vector2 size, Action onClick)
        {
            var rect = Box(parent, name, Plate, at, size);
            var label = Words(rect, "label", words, 20f, Ink, Vector2.zero, size, false);
            label.alignment = TextAlignmentOptions.Center;
            label.characterSpacing = 2f;
            var button = rect.gameObject.AddComponent<GuideButton>();
            button.Back = rect.GetComponent<Image>();
            button.Label = label;
            button.Clicked = onClick;
            return button;
        }
    }

    /// <summary>A plain button of the guide: lights up under the pointer, and can be shown as the chosen one.</summary>
    public class GuideButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Image Back;
        public TextMeshProUGUI Label;
        public Action Clicked;

        private static readonly Color Plain = new Color32(40, 43, 46, 255);
        private static readonly Color Over = new Color32(66, 70, 74, 255);
        private static readonly Color Chosen = new Color32(197, 160, 72, 255);
        private static readonly Color Ink = new Color32(231, 229, 212, 255);
        private static readonly Color Dark = new Color32(20, 20, 18, 255);
        private bool _chosen, _over;

        public void SetChosen(bool chosen)
        {
            _chosen = chosen;
            Paint();
        }

        private void Paint()
        {
            if (Back != null)
            {
                Back.color = _chosen ? Chosen : _over ? Over : Plain;
            }
            if (Label != null)
            {
                Label.color = _chosen ? Dark : Ink;
            }
        }

        public void OnPointerEnter(PointerEventData data)
        {
            _over = true;
            Paint();
        }

        public void OnPointerExit(PointerEventData data)
        {
            _over = false;
            Paint();
        }

        public void OnPointerClick(PointerEventData data)
        {
            try
            {
                Comfort.Common.Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.ButtonClick);
            }
            catch (Exception)
            {
                // No sound is no reason not to act.
            }
            Clicked?.Invoke();
        }
    }

    /// <summary>Shows a folder of numbered pictures one after another, as a short loop.</summary>
    public class GuideFilm : MonoBehaviour
    {
        public RawImage Screen;
        private readonly List<Texture2D> _frames = new List<Texture2D>();
        private float _next;
        private int _at;

        public bool Has => _frames.Count > 0;

        public void Load(string folder)
        {
            foreach (var frame in _frames)
            {
                Destroy(frame);
            }
            _frames.Clear();
            _at = 0;
            try
            {
                if (folder != null && Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder, "*.jpg");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string file in files)
                    {
                        var picture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        if (picture.LoadImage(File.ReadAllBytes(file), true))
                        {
                            _frames.Add(picture);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Guide pictures in " + folder + " not read: " + e.Message);
            }
            if (Screen != null && _frames.Count > 0)
            {
                Screen.texture = _frames[0];
                // Kept at the picture's own shape inside the space given to it.
                var rect = (RectTransform)Screen.transform;
                float shape = _frames[0].height / (float)_frames[0].width;
                rect.sizeDelta = shape > 0.5625f + 0.01f ? new Vector2(Mathf.Min(560f, 440f / shape), 440f) : new Vector2(560f, 560f * shape);
            }
        }

        private void Update()
        {
            if (_frames.Count < 2 || Time.unscaledTime < _next)
            {
                return;
            }
            // The last picture is held a little, so the loop reads as a loop.
            _at = (_at + 1) % _frames.Count;
            _next = Time.unscaledTime + (_at == _frames.Count - 1 ? 1.2f : 0.14f);
            Screen.texture = _frames[_at];
        }

        private void OnDestroy()
        {
            foreach (var frame in _frames)
            {
                Destroy(frame);
            }
            _frames.Clear();
        }
    }
}
