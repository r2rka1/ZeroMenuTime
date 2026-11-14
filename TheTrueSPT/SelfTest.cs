using System;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;

namespace TheTrueSPT
{
    /// <summary>
    /// Testing aid: runs the four inventory steps every trade is made of (take part of a money stack,
    /// add an item, remove an item, add money) and reports which ones worked. It does not click
    /// through the trade screen itself.
    /// </summary>
    public static class SelfTest
    {
        private const string Roubles = "5449016a4bdc2d6f028b456f";
        private const string Bandage = "544fb25a4bdc2dfb738b4567";

        public static async void Run()
        {
            var player = GamePlayerOwner.MyPlayer;
            if (player == null)
            {
                return;
            }
            int passed = 0, total = 4;
            string report = "";
            try
            {
                // 1. Pay: take 100 roubles out of a larger stack.
                var stack = LocalTrade.CarriedItems(player)
                    .Where(x => x.TemplateId == Roubles && x.StackObjectsCount > 100)
                    .OrderByDescending(x => x.StackObjectsCount).FirstOrDefault();
                if (stack == null)
                {
                    report += " [1 pay: SKIPPED, no rouble stack over 100 carried]";
                }
                else
                {
                    int before = stack.StackObjectsCount;
                    bool ok = await LocalTrade.Take(player, stack, 100) && stack.StackObjectsCount == before - 100;
                    report += " [1 pay: " + (ok ? "ok" : "FAILED " + before + "->" + stack.StackObjectsCount) + "]";
                    if (ok) passed++;
                }

                // 2. Receive goods: add a bandage.
                var bandage = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), Bandage, null);
                bool added = await LocalTrade.Give(player, bandage) && LocalTrade.CarriedItems(player).Contains(bandage);
                report += " [2 receive item: " + (added ? "ok" : "FAILED") + "]";
                if (added) passed++;

                // 3. Hand over goods: remove that bandage again.
                bool removed = added && await LocalTrade.Take(player, bandage, 1) && !LocalTrade.CarriedItems(player).Contains(bandage);
                report += " [3 hand over item: " + (removed ? "ok" : "FAILED") + "]";
                if (removed) passed++;

                // 4. Get paid: add 100 roubles.
                var money = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), Roubles, null);
                money.StackObjectsCount = 100;
                bool paid = await LocalTrade.Give(player, money) && LocalTrade.CarriedItems(player).Contains(money);
                report += " [4 receive money: " + (paid ? "ok" : "FAILED") + "]";
                if (paid) passed++;
            }
            catch (Exception e)
            {
                report += " EXCEPTION: " + e;
            }
            Plugin.Log.LogInfo("Self-test " + passed + "/" + total + ":" + report);
            NotificationManager.DisplayMessageNotification("Self-test: " + passed + " of " + total + " steps passed", ENotificationDurationType.Long);
        }
    }
}
