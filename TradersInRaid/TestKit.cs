using System;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;

namespace TradersInRaid
{
    /// <summary>Testing aid: hands the player a full set of gear and money in a raid.</summary>
    public static class TestKit
    {
        // (template id, stack count, use the trader-style preset so weapons and armour come assembled)
        private static readonly (string Tpl, int Count, bool Preset)[] Items =
        {
            ("5ca20d5986f774331e7c9602", 1, false), // Berkut backpack
            ("5648a69d4bdc2ded0b8b457b", 1, false), // BlackRock chest rig
            ("5c0e51be86f774598e797894", 1, true),  // 6B13 armour
            ("5a7c4850e899ef00150be885", 1, true),  // 6B47 helmet
            ("5644bd2b4bdc2d3b4c8b4572", 1, true),  // AK-74N
            ("55d480c04bdc2d1d4e8b456a", 1, false), // AK magazine
            ("55d480c04bdc2d1d4e8b456a", 1, false),
            ("55d480c04bdc2d1d4e8b456a", 1, false),
            ("56dfef82d2720bbd668b4567", 60, false), // 5.45 BP
            ("56dfef82d2720bbd668b4567", 60, false),
            ("56dfef82d2720bbd668b4567", 60, false),
            ("590c678286f77426c9660122", 1, false), // IFAK
            ("544fb45d4bdc2dee738b4568", 1, false), // Salewa
            ("5e831507ea0a7c419c2f9bd9", 1, false), // tourniquet
            ("544fb3364bdc2d34748b456a", 1, false), // splint
            ("544fb37f4bdc2dee738b4567", 1, false), // painkillers
            ("5c0fa877d174af02a012e1cf", 1, false), // water
            ("544fb6cc4bdc2d34748b456e", 2, false), // chocolate (two bars as separate items below)
            ("5449016a4bdc2d6f028b456f", 500000, false), // roubles
        };

        public static async void Give()
        {
            var player = GamePlayerOwner.MyPlayer;
            if (player == null)
            {
                return;
            }
            var factory = Singleton<ItemFactory>.Instance;
            int given = 0, failed = 0;
            foreach (var entry in Items)
            {
                try
                {
                    int remaining = entry.Count;
                    while (remaining > 0)
                    {
                        Item item = entry.Preset
                            ? factory.GetPresetItem(entry.Tpl).CloneItem(player.InventoryController)
                            : factory.CreateItem(MongoID.Generate(), entry.Tpl, null);
                        int amount = Math.Min(Math.Max(1, item.StackMaxSize), remaining);
                        item.StackObjectsCount = amount;
                        remaining -= amount;
                        if (await LocalTrade.Give(player, item))
                        {
                            given++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                }
                catch (Exception e)
                {
                    failed++;
                    Plugin.Log.LogError("Test kit item " + entry.Tpl + " failed: " + e);
                }
            }
            Plugin.Log.LogInfo("Test kit: " + given + " given, " + failed + " did not fit or failed");
            NotificationManager.DisplayMessageNotification("Test kit: " + given + " items given, " + failed + " did not fit.", ENotificationDurationType.Long);
        }
    }
}
