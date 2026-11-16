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

namespace ZeroMenuTime
{
    /// <summary>
    /// The window a hideout station opens: what its next level needs and what of that you have,
    /// what it can make, from what, how long that takes, and what is being made now. It takes the
    /// place of the long action lists the stations had. Everything it does goes through the same
    /// code those lists used (HideoutStations, HideoutCrafting, HideoutSleep).
    /// </summary>
    public static class HideoutPanel
    {
        private const string CanvasName = "ZeroMenuTime hideout panel";

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
        private static bool _shownDue, _shownPowered;
        private static float _nextRefresh, _nextRedraw;
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
            // What the stash holds may have changed behind the window's back (a part fitted to a gun).
            HideoutStash.Send("station window opened");
            _area = area;
            _name = name;
            _nextRedraw = Time.unscaledTime + 10f;
            Build();
            Refresh();
            GamePlayerOwner.SetIgnoreInputInNPCDialog(true);
            // While the player walks about, the game keeps its interface deaf to the mouse.
            Listen(true);
        }

        /// <summary>Asks the server how things stand right now, and draws the window again with the answer.</summary>
        private static async void Refresh()
        {
            int area = _area;
            await WorldClock.SendAsync(null);
            if (_canvas != null && _area == area && !_busy)
            {
                Build();
            }
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
            if (Time.unscaledTime >= _nextRedraw && !_busy && (_area == HideoutPower.Generator || _area == HideoutPower.BitcoinFarm
                                                              || _area == HideoutPower.WaterCollector || (_area == HideoutPower.ScavCase && !string.IsNullOrEmpty(HideoutPower.ScavRecipe))))
            {
                // Fuel and mining tick along with the clock.
                _nextRedraw = Time.unscaledTime + 10f;
                Refresh();
            }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                var craft = HideoutCrafting.Here.FirstOrDefault(c => c.Area == _area);
                bool due = craft != null && craft.Due <= WorldClock.Now;
                if (craft != _shownCraft || due != _shownDue || HideoutPower.Powered != _shownPowered)
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
            if (left > 0 && craft.Paused)
            {
                return "<color=" + Bad + ">Paused: no power.</color>  " + WorldClock.Span(left) + " of work left";
            }
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
            Text(head, HideoutPower.Powered ? "<color=" + Good + ">POWER ON</color>" : "<color=" + Faint + ">NO POWER</color>", 16f, Ink, true).alignment = TextAlignmentOptions.MidlineRight;
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
                Text(left, HideoutStations.IsFree && !TestHarness.Filming ? "Free hideout is switched on (F12, Testing): nothing is taken." : "Taken from what you carry, then from this hideout's stash. Built at once.", 15f, Dim, false);
                int area = _area;
                string name = _name;
                Button(left, level == 0 ? "BUILD" : "UPGRADE", () => Do(() => HideoutStations.Build(area, name)), affordable && !_busy, 220f);
            }
            else
            {
                Heading(left, "FULLY BUILT");
                Text(left, "There is no further level to build.", 18f, Dim, false);
            }

            if (_area == HideoutPower.Generator && level >= 1)
            {
                Space(left, 14f);
                Heading(left, "FUEL");
                if (HideoutPower.FuelFactor < 1)
                {
                    Text(left, "<color=" + Good + ">Solar Power is built: the generator burns half the fuel.</color>", 15f, Ink, false);
                }
                Text(left, HideoutPower.Powered ? "Running. The fuel lasts another " + WorldClock.Span(HideoutPower.FuelLasts) + "  <color=" + Faint + ">(" + Played(HideoutPower.FuelLasts) + ")</color>"
                    : HideoutPower.Fuel.Count == 0 ? "Off, and empty. Put a fuel can in." : "Off.", 17f, Ink, false);
                for (int i = 0; i < HideoutPower.Fuel.Count; i++)
                {
                    var can = HideoutPower.Fuel[i];
                    int index = i;
                    var row = Node(left, "can");
                    Horizontal(row, 12f);
                    Icon(row, can.Tpl, 40f);
                    Flexible(Text(row, HideoutStations.FullName(can.Tpl) + "  <color=" + Faint + ">" + can.Left.ToString("0") + " / " + can.Max.ToString("0") + "</color>", 17f, Ink, false).gameObject, 1f, 0f);
                    Button(row, "TAKE OUT", () => Do(() => HideoutPower.TakeFuel(index)), !_busy, 120f);
                }
                int cans = HideoutStations.IsFree ? 1 : HideoutPower.FuelAtHand(player).Count;
                Text(left, HideoutPower.Fuel.Count + " of " + HideoutPower.FuelSlots + " places taken. You have " + (HideoutStations.IsFree ? "free fuel" : cans + " fuel can(s) at hand") + ".", 15f, Dim, false);
                var fuelRow = Node(left, "fuel buttons");
                Horizontal(fuelRow, 10f);
                Button(fuelRow, "ADD FUEL", () => Do(HideoutPower.AddFuel), cans > 0 && HideoutPower.Fuel.Count < HideoutPower.FuelSlots && !_busy, 150f);
                Button(fuelRow, HideoutPower.On ? "SWITCH OFF" : "SWITCH ON", () => Do(() => HideoutPower.Switch(!HideoutPower.On)), (HideoutPower.On || HideoutPower.Fuel.Count > 0) && !_busy, 170f);
                Text(left, "It burns only while switched on, and goes on burning while you are away or asleep. Without power the lights are out and nothing electric works: crafts wait, the bitcoin farm stops. Only the Lavatory is worked by hand.", 15f, Dim, false);
            }
            if (_area == HideoutPower.Generator)
            {
                // The lamps have no place of their own to walk up to; they are wired from here.
                const int lamps = (int)EAreaType.Illumination;
                int lampLevel;
                HideoutStations.Levels.TryGetValue(lamps, out lampLevel);
                Space(left, 14f);
                Heading(left, "LIGHTING");
                string[] kinds = { "No lamps yet: only what the stations themselves give off.", "Candles.", "Light bulbs.", "Fluorescent lamps." };
                Text(left, kinds[Mathf.Clamp(lampLevel, 0, 3)] + (lampLevel >= 2 ? " They burn while the generator runs." : ""), 16f, Ink, false);
                if (HideoutStations.NextCosts.ContainsKey(lamps))
                {
                    var lampTally = HideoutStations.Tally(player, lamps);
                    bool lampsAffordable = true;
                    foreach (var pair in lampTally)
                    {
                        Row(left, pair.Key.Tpl, HideoutStations.FullName(pair.Key.Tpl), Stock(pair.Value, pair.Key.Count), 32f);
                        lampsAffordable &= pair.Value >= pair.Key.Count;
                    }
                    Button(left, lampLevel == 0 ? "PUT UP CANDLES" : lampLevel == 1 ? "FIT LIGHT BULBS" : "FIT FLUORESCENT LAMPS",
                        () => Do(() => HideoutStations.Build(lamps, "Illumination")), lampsAffordable && !_busy, 300f);
                }
            }
            if (_area == HideoutPower.WaterCollector && level >= 1)
            {
                Space(left, 14f);
                Heading(left, "CLEAN WATER");
                if (level < 3)
                {
                    Text(left, "At level 3 the collector takes a water filter and fills bottles of clean water by itself.", 16f, Dim, false);
                }
                else
                {
                    var filter = HideoutPower.WaterFilter;
                    if (filter != null)
                    {
                        var row = Node(left, "filter");
                        Horizontal(row, 12f);
                        Icon(row, filter.Tpl, 40f);
                        Flexible(Text(row, HideoutStations.FullName(filter.Tpl) + "  <color=" + Faint + ">" + filter.Left.ToString("0") + " / " + filter.Max.ToString("0") + "</color>", 17f, Ink, false).gameObject, 1f, 0f);
                        Button(row, "TAKE OUT", () => Do(HideoutPower.TakeFilter), !_busy, 120f);
                    }
                    else
                    {
                        int filters = HideoutStations.IsFree ? 1 : HideoutPower.FiltersAtHand(player).Count;
                        Text(left, "No filter in. You have " + (HideoutStations.IsFree ? "free filters" : filters + " at hand") + ".", 16f, Ink, false);
                        Button(left, "FIT A FILTER", () => Do(HideoutPower.AddFilter), filters > 0 && !_busy, 220f);
                    }
                    Row(left, HideoutPower.CleanWater, "Bottles ready", HideoutPower.Water + " / " + HideoutPower.WaterCap, 40f);
                    Text(left, HideoutPower.Water >= HideoutPower.WaterCap ? "Full. Collect to carry on."
                        : HideoutPower.NextWaterIn < 0 ? "Nothing is being collected."
                        : "Next one in " + WorldClock.Span(HideoutPower.NextWaterIn) + "  <color=" + Faint + ">(" + Played(HideoutPower.NextWaterIn) + ")</color>", 16f, Ink, false);
                    Button(left, "COLLECT", () => Do(HideoutPower.CollectWater), HideoutPower.Water > 0 && !_busy, 220f);
                    Text(left, "It needs no power, only a filter, which wears out as it works.", 15f, Dim, false);
                }
            }
            if (_area == HideoutPower.BitcoinFarm && level >= 1)
            {
                Space(left, 14f);
                Heading(left, "BITCOIN FARM");
                Row(left, HideoutPower.Card, "Graphics cards fitted", HideoutPower.Gpus + " / " + HideoutPower.GpuSlots, 40f);
                int cards = HideoutStations.IsFree ? 1 : HideoutPower.CardsAtHand(player);
                var cardRow = Node(left, "card buttons");
                Horizontal(cardRow, 10f);
                Button(cardRow, "ADD CARD", () => Do(HideoutPower.AddCard), cards > 0 && HideoutPower.Gpus < HideoutPower.GpuSlots && !_busy, 150f);
                Button(cardRow, "TAKE ONE OUT", () => Do(HideoutPower.RemoveCard), HideoutPower.Gpus > 0 && !_busy, 170f);
                Row(left, HideoutPower.Coin, "Bitcoins ready", HideoutPower.Coins + " / " + HideoutPower.CoinCap, 40f);
                string next = HideoutPower.Gpus == 0 ? "Fit a graphics card to start mining."
                    : HideoutPower.Coins >= HideoutPower.CoinCap ? "Full. Collect to carry on."
                    : !HideoutPower.Powered ? "<color=" + Bad + ">Stopped: the generator is not running.</color>"
                    : "Next one in " + WorldClock.Span(HideoutPower.NextCoinIn) + "  <color=" + Faint + ">(" + Played(HideoutPower.NextCoinIn) + ")</color>";
                Text(left, next, 16f, Ink, false);
                Button(left, "COLLECT", () => Do(HideoutPower.CollectCoins), HideoutPower.Coins > 0 && !_busy, 220f);
                Text(left, "More cards mine faster. It works only while the generator runs.", 15f, Dim, false);
            }

            if (_area == (int)EAreaType.Workbench && level >= 1)
            {
                Space(left, 14f);
                Heading(left, "WEAPONS");
                var guns = HideoutGunsmith.Weapons(player);
                if (guns.Count == 0)
                {
                    Text(left, "Carry a weapon, or keep one in the stash, to work on it here.", 16f, Dim, false);
                }
                foreach (var gun in guns.Take(8))
                {
                    var row = Node(left, "gun");
                    Horizontal(row, 12f);
                    Icon(row, gun.TemplateId, 40f);
                    Flexible(Text(row, HideoutStations.FullName(gun.TemplateId) + (HideoutGunsmith.InStash(gun) ? "  <color=" + Faint + ">in the stash</color>" : ""), 17f, Ink, false).gameObject, 1f, 0f);
                    var chosen = gun;
                    Button(row, "MODIFY", () =>
                    {
                        Close();
                        string failed = HideoutGunsmith.Open(chosen);
                        if (failed != null)
                        {
                            EFT.Communications.NotificationManager.DisplayWarningNotification(failed);
                        }
                    }, !_busy, 120f);
                }
                Text(left, "Parts come from what you carry and from the stash.", 15f, Dim, false);
            }
            if (_area == HideoutPower.Gym && level >= 1)
            {
                Space(left, 14f);
                Heading(left, "TRAINING");
                var strength = player.Skills.GetSkill(ESkillId.Strength);
                var endurance = player.Skills.GetSkill(ESkillId.Endurance);
                Text(left, "A set builds Strength (level " + strength.Level + ", +" + HideoutPower.WorkoutPoints(strength.Level).ToString("0") + " points) and Endurance (level " + endurance.Level + ", +"
                    + HideoutPower.WorkoutPoints(endurance.Level).ToString("0") + " points). It costs " + HideoutPower.WorkoutCost.ToString("0") + " energy and " + HideoutPower.WorkoutCost.ToString("0") + " water.", 16f, Ink, false);
                double ache = HideoutPower.WorkoutIn;
                Text(left, ache > 0 ? "<color=" + Bad + ">Your muscles ache.</color> You can train again in " + WorldClock.Span(ache) + "." : "<color=" + Good + ">You are rested.</color>", 16f, Ink, false);
                Button(left, "TRAIN", () => Do(HideoutPower.Workout), ache <= 0 && !_busy, 220f);
            }
            if (_area == HideoutPower.Circle && level >= 1 && HideoutShelves.Has(_area))
            {
                Space(left, 14f);
                Heading(left, "THE CIRCLE");
                double wait = HideoutPower.CircleDue - WorldClock.Now;
                int lying = HideoutShelves.Count(_area);
                if (wait > 0)
                {
                    Text(left, "The circle is at work. Come back in " + WorldClock.Span(wait) + "  <color=" + Faint + ">(" + Played(wait) + ")</color>", 16f, Ink, false);
                }
                else
                {
                    Text(left, lying == 0 ? "Nothing lies in the circle." : lying + " thing(s) lie in the circle.", 16f, Ink, false);
                    Button(left, "OPEN THE CIRCLE", () => { Close(); HideoutShelves.Open(HideoutPower.Circle); }, true, 260f);
                    Button(left, "SACRIFICE", () => Do(HideoutPower.Sacrifice), lying > 0 && !_busy, 220f);
                    Text(left, "Lay things in the circle and sacrifice them. The more they are worth, the more comes back, after half a day or so. What comes back lies in the circle.", 15f, Dim, false);
                }
            }
            else if (HideoutShelves.Has(_area) && level >= 1)
            {
                Space(left, 14f);
                Heading(left, _area == (int)EAreaType.PlaceOfFame ? "TROPHIES" : _area == (int)EAreaType.EquipmentPresetsStand ? "OUTFITS" : "ON THE STAND");
                int kept = HideoutShelves.Count(_area);
                Text(left, kept == 0 ? "Nothing is kept here yet." : kept + " thing(s) kept here.", 17f, Ink, false);
                int here = _area;
                Button(left, "STORE / TAKE", () => { Close(); HideoutShelves.Open(here); }, true, 220f);
                const int second = (int)EAreaType.WeaponStandSecondary;
                if (_area == (int)EAreaType.WeaponStand && HideoutShelves.Has(second))
                {
                    Text(left, "Second rack: " + HideoutShelves.Count(second) + " thing(s).", 17f, Ink, false);
                    Button(left, "SECOND RACK", () => { Close(); HideoutShelves.Open(second); }, true, 220f);
                }
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
            if (_area == HideoutPower.ScavCase && level >= 1)
            {
                ScavCase(right, player);
                _note = Text(column, "", 16f, Gold, false);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_window);
                return;
            }
            Heading(right, "CRAFTING");

            var craft = HideoutCrafting.Here.FirstOrDefault(c => c.Area == _area);
            _shownCraft = craft;
            _shownDue = craft != null && craft.Due <= WorldClock.Now;
            _shownPowered = HideoutPower.Powered;
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
                bool dead = HideoutPower.NeedsPower(_area) && !HideoutPower.Powered;
                if (dead)
                {
                    Text(right, "<color=" + Bad + ">No power. Start the generator and this station will work.</color>", 16f, Ink, false);
                }
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
                    Button(card, "CRAFT", () => Do(() => HideoutCrafting.Start(chosen)), HideoutCrafting.Missing(player, recipe) == null && !dead && !_busy, 180f);
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

        /// <summary>The scav case: pay, wait, and see what they bring back.</summary>
        private static void ScavCase(RectTransform right, Player player)
        {
            Heading(right, "SCAV CASE");
            _shownCraft = null;
            _status = null;
            if (!string.IsNullOrEmpty(HideoutPower.ScavRecipe))
            {
                double left = HideoutPower.ScavDue - WorldClock.Now;
                var card = Card(right);
                Text(card, "The scavs are out.", 22f, Ink, true);
                Text(card, left <= 0 ? "<color=" + Good + ">They are back.</color>" : "Back in " + WorldClock.Span(left) + "  <color=" + Faint + ">(" + Played(left) + ")</color>", 18f, Ink, false);
                Text(card, "What they bring goes into the stash.", 15f, Dim, false);
                Button(card, "SEE WHAT THEY BROUGHT", () => Do(HideoutPower.CollectScavs), left <= 0 && !_busy, 320f);
                return;
            }
            Text(right, "Pay the scavs and they go out looking. The more you pay, the better the finds.", 16f, Dim, false);
            var spendable = HideoutStations.Spendable(player);
            var list = Scroll(right);
            foreach (var offer in HideoutPower.ScavOffers.OrderBy(o => o.Seconds))
            {
                var card = Card(list);
                var top = Node(card, "top");
                Horizontal(top, 14f);
                bool affordable = true;
                foreach (var cost in offer.Items)
                {
                    int have = spendable.Where(item => item.TemplateId == cost.Tpl).Sum(item => item.StackObjectsCount);
                    have = HideoutStations.IsFree ? Mathf.Max(have, cost.Count) : have;
                    affordable &= have >= cost.Count;
                    Icon(top, cost.Tpl, 56f);
                    Flexible(Text(top, cost.Count.ToString("N0") + " × " + HideoutStations.FullName(cost.Tpl) + "   " + Stock(have, cost.Count), 19f, Ink, true).gameObject, 1f, 0f);
                }
                Text(top, WorldClock.Span(offer.Seconds) + "  <color=" + Faint + ">" + Played(offer.Seconds) + "</color>", 16f, Ink, false).alignment = TextAlignmentOptions.MidlineRight;
                Text(card, "Brings back: common " + offer.Common + ", rare " + offer.Rare + ", very rare " + offer.Superrare + ".", 16f, Dim, false);
                var chosen = offer;
                Button(card, "SEND THEM", () => Do(() => HideoutPower.SendScavs(chosen)), affordable && !_busy, 200f);
            }
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
