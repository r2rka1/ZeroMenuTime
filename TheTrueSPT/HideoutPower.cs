using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace TheTrueSPT
{
    /// <summary>
    /// The generator of the hideout the player is in, and what runs off it. Fuel cans are put in
    /// and taken out by hand; while the generator is on, the fuel drains on the world clock, so it
    /// keeps burning while the player is away or asleep. The bitcoin farm mines only while there
    /// is power. The server keeps the count; items change hands here, as with building.
    /// </summary>
    public static class HideoutPower
    {
        public class Can
        {
            public string Tpl;
            public double Left, Max;
        }

        public const int Generator = 4, BoozeGenerator = 19, BitcoinFarm = 20;
        public static readonly string[] FuelKinds = { "5d1b36a186f7742523398433", "5d1b371186f774253763a656" };
        public const string Card = "57347ca924597744596b4e71", Coin = "59faff1d86f7746c51718c9c";

        public static bool On, Powered;
        public static readonly List<Can> Fuel = new List<Can>();
        public static int FuelSlots, Gpus, GpuSlots, Coins, CoinCap = 3;
        public static double SlowFactor = 1;
        private static double _fuelLasts, _nextCoinIn = -1, _readAt;

        /// <summary>Game seconds of fuel left, counted down from the server's last word.</summary>
        public static double FuelLasts => Math.Max(0, _fuelLasts - (Powered ? WorldClock.Now - _readAt : 0));
        /// <summary>Game seconds until the next bitcoin, or below zero when the farm is not working.</summary>
        public static double NextCoinIn => _nextCoinIn < 0 ? -1 : Math.Max(0, _nextCoinIn - (WorldClock.Now - _readAt));

        public static void Read(JObject reply)
        {
            var power = reply != null ? reply["Power"] as JObject : null;
            if (power == null)
            {
                return;
            }
            On = (bool?)power["On"] ?? false;
            Powered = (bool?)power["Powered"] ?? false;
            FuelSlots = (int?)power["FuelSlots"] ?? 0;
            Gpus = (int?)power["Gpus"] ?? 0;
            GpuSlots = (int?)power["GpuSlots"] ?? 0;
            Coins = (int?)power["Coins"] ?? 0;
            CoinCap = (int?)power["CoinCap"] ?? 3;
            SlowFactor = (double?)power["SlowFactor"] ?? 1;
            _fuelLasts = (double?)power["FuelLasts"] ?? 0;
            _nextCoinIn = (double?)power["NextCoinIn"] ?? -1;
            _readAt = WorldClock.Now;
            Fuel.Clear();
            foreach (var can in power["Fuel"] as JArray ?? new JArray())
            {
                Fuel.Add(new Can { Tpl = (string)can["Tpl"], Left = (double?)can["Left"] ?? 0, Max = (double?)can["Max"] ?? 0 });
            }
            if (HideoutDoors.Inside != null && _lookKnown && _lookPowered != Powered)
            {
                ApplyLook();
            }
        }

        /// <summary>Everything is electric but the Lavatory, where things are made by hand.</summary>
        public static bool NeedsPower(int area)
        {
            return area != 2;
        }

        private static EFT.Hideout.HideoutController _shown;
        private static bool _lookKnown, _lookPowered;

        /// <summary>
        /// Makes the hideout look the way its power is: the game's own switch for lamps, props and
        /// hum, and the lamps the Illumination station has earned. Without power only candles burn.
        /// </summary>
        public static void ApplyLook()
        {
            var controller = HideoutBunker.Loaded ? HideoutStations.Controller() : null;
            if (controller == null)
            {
                return;
            }
            try
            {
                if (_shown != controller)
                {
                    _shown = controller;
                    _lookKnown = false;
                    // The game's switch stays deaf until it has been shown once; an empty list will do.
                    controller._ambianceController.Show(new List<EFT.Hideout.AreaData>(), Powered);
                }
                if (!_lookKnown || _lookPowered != Powered)
                {
                    controller.EnergySupplyChanged(Powered);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hideout power: the game's own switch failed: " + e.Message);
            }
            try
            {
                int illumination;
                HideoutStations.Levels.TryGetValue((int)EAreaType.Illumination, out illumination);
                var have = new List<EFT.Hideout.ELightingLevel> { EFT.Hideout.ELightingLevel.Off };
                if (illumination >= 1) have.Add(EFT.Hideout.ELightingLevel.Candles);
                if (illumination >= 2) have.Add(EFT.Hideout.ELightingLevel.LightBulbs);
                if (illumination >= 3) have.Add(EFT.Hideout.ELightingLevel.FluorescentLamps);
                var use = Powered ? have[have.Count - 1] : (illumination >= 1 ? EFT.Hideout.ELightingLevel.Candles : EFT.Hideout.ELightingLevel.Off);
                foreach (var pair in controller.Areas)
                {
                    var area = pair.Value;
                    if (area == null || area.AreaLevels == null)
                    {
                        continue;
                    }
                    int level;
                    HideoutStations.Levels.TryGetValue((int)pair.Key, out level);
                    level = Mathf.Clamp(level, 0, area.AreaLevels.Length - 1);
                    var shown = area.AreaLevels[level];
                    if (shown != null)
                    {
                        try
                        {
                            shown.SetAvailableLightingLevels(have);
                            shown.SetLightLevel(use);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                foreach (var pair in controller._globalLighting)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.SetActive(pair.Key == use);
                    }
                }
                if (!_lookKnown || _lookPowered != Powered)
                {
                    Plugin.Log.LogInfo("Hideout power: " + (Powered ? "on" : "off") + ", lamps " + use);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hideout power: setting the lamps failed: " + e.Message);
            }
            _lookKnown = true;
            _lookPowered = Powered;
        }

        private static List<Item> Carried(Player player, Func<Item, bool> wanted)
        {
            return HideoutStations.Spendable(player).Where(wanted).ToList();
        }

        public static List<Item> FuelAtHand(Player player)
        {
            return Carried(player, item => FuelKinds.Contains((string)item.TemplateId) && Left(item) > 0.01f);
        }

        public static int CardsAtHand(Player player)
        {
            return Carried(player, item => item.TemplateId == Card).Count;
        }

        private static float Left(Item item)
        {
            var resource = item.GetItemComponent<ResourceComponent>();
            return resource != null ? resource.Value : 0f;
        }

        private static async Task<JObject> Ask(object body)
        {
            var reply = JObject.Parse(await RequestHandler.PostJsonAsync("/thetruespt/hideout/power", JsonConvert.SerializeObject(body)));
            if ((bool?)reply["Ok"] ?? false)
            {
                HideoutStations.Read(reply);
                HideoutCrafting.Read(reply);
            }
            return reply;
        }

        private static string Refused(JObject reply)
        {
            return ((bool?)reply["Ok"] ?? false) ? null : "Not done: " + reply["Error"];
        }

        public static async Task<string> Switch(bool on)
        {
            var reply = await Ask(new { Id = HideoutDoors.Inside, Action = on ? "on" : "off", Seconds = WorldClock.Now });
            return Refused(reply) ?? (on ? "The generator is running." : "The generator is off.");
        }

        /// <summary>Puts in the fullest can at hand.</summary>
        public static async Task<string> AddFuel()
        {
            var player = GamePlayerOwner.MyPlayer;
            if (player == null || HideoutDoors.Inside == null)
            {
                return "not possible right now";
            }
            string tpl = FuelKinds[0];
            double left = 100, max = 100;
            Item can = null;
            if (!HideoutStations.IsFree)
            {
                can = FuelAtHand(player).OrderByDescending(Left).FirstOrDefault();
                if (can == null)
                {
                    return "You have no fuel at hand.";
                }
                var resource = can.GetItemComponent<ResourceComponent>();
                tpl = can.TemplateId;
                left = resource.Value;
                max = resource.MaxResource;
            }
            if (Fuel.Count >= FuelSlots)
            {
                return "There is no room for another can.";
            }
            if (can != null && !await LocalTrade.Take(player, can, 1))
            {
                return "Could not hand over the can.";
            }
            var reply = await Ask(new { Id = HideoutDoors.Inside, Action = "fuel", Tpl = tpl, Left = left, Max = max, Seconds = WorldClock.Now });
            HideoutStash.Send("after fuelling");
            return Refused(reply) ?? "Fuel put in.";
        }

        public static async Task<string> TakeFuel(int index)
        {
            var player = GamePlayerOwner.MyPlayer;
            var reply = await Ask(new { Id = HideoutDoors.Inside, Action = "unfuel", Index = index, Seconds = WorldClock.Now });
            string refused = Refused(reply);
            if (refused != null || player == null)
            {
                return refused;
            }
            var taken = reply["Power"]["Taken"] as JObject;
            if (taken != null && !HideoutStations.IsFree)
            {
                var item = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), (string)taken["Tpl"], null);
                var resource = item.GetItemComponent<ResourceComponent>();
                if (resource != null)
                {
                    resource.Value = (float)((double?)taken["Left"] ?? 0);
                }
                await HideoutCrafting.PutItem(player, item);
                HideoutStash.Send("after taking fuel out");
            }
            return "Can taken out; it is in the stash.";
        }

        public static async Task<string> AddCard()
        {
            var player = GamePlayerOwner.MyPlayer;
            if (player == null || HideoutDoors.Inside == null)
            {
                return "not possible right now";
            }
            if (Gpus >= GpuSlots)
            {
                return "There is no room for another card.";
            }
            if (!HideoutStations.IsFree)
            {
                var card = Carried(player, item => item.TemplateId == Card).FirstOrDefault();
                if (card == null)
                {
                    return "You have no graphics card at hand.";
                }
                if (!await LocalTrade.Take(player, card, 1))
                {
                    return "Could not hand over the card.";
                }
            }
            var reply = await Ask(new { Id = HideoutDoors.Inside, Action = "gpu", Seconds = WorldClock.Now });
            HideoutStash.Send("after adding a card");
            return Refused(reply) ?? "Card fitted.";
        }

        public static async Task<string> RemoveCard()
        {
            var player = GamePlayerOwner.MyPlayer;
            var reply = await Ask(new { Id = HideoutDoors.Inside, Action = "ungpu", Seconds = WorldClock.Now });
            string refused = Refused(reply);
            if (refused != null || player == null)
            {
                return refused;
            }
            if (!HideoutStations.IsFree)
            {
                await HideoutCrafting.PutInStash(player, Card, 1);
                HideoutStash.Send("after removing a card");
            }
            return "Card taken out; it is in the stash.";
        }

        public static async Task<string> CollectCoins()
        {
            var player = GamePlayerOwner.MyPlayer;
            var reply = await Ask(new { Id = HideoutDoors.Inside, Action = "coins", Seconds = WorldClock.Now });
            string refused = Refused(reply);
            if (refused != null || player == null)
            {
                return refused;
            }
            int coins = (int?)reply["Power"]["CoinsTaken"] ?? 0;
            if (coins > 0)
            {
                await HideoutCrafting.PutInStash(player, Coin, coins);
                HideoutStash.Send("after collecting bitcoins");
            }
            return coins > 0 ? coins + " bitcoin(s) put in the stash." : "Nothing to collect yet.";
        }
    }
}
