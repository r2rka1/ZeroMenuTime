using System;
using System.Collections;
using EFT;
using EFT.Communications;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// Sleeping at a hideout's Rest Space. The world clock jumps by the hours slept, so the time of
    /// day changes and crafts everywhere move on; the character heals, more per hour the better the
    /// Rest Space; and grows hungry and thirsty as if the time had passed, less so where the hideout
    /// feeds and waters them.
    /// </summary>
    public static class HideoutSleep
    {
        /// <summary>The Rest Space's number among the stations.</summary>
        public const int RestSpace = 9;

        /// <summary>What a sleeper uses up per hour, before anything the hideout gives back.</summary>
        private const float EnergyPerHour = 4f, WaterPerHour = 5f;

        private static bool _sleeping;

        /// <summary>Health regained per hour slept, by Rest Space level.</summary>
        public static float HealPerHour(int level)
        {
            return level >= 3 ? 70f : level == 2 ? 40f : level == 1 ? 20f : 0f;
        }

        /// <summary>Game hours from now until six in the morning.</summary>
        public static float HoursUntilMorning()
        {
            double day = 24 * 3600.0;
            // The clock reads zero at six in the morning.
            double intoDay = WorldClock.Now % day;
            double left = (day - intoDay) / 3600.0;
            return (float)(left < 1.0 ? left + 24.0 : left);
        }

        public static void Sleep(float hours)
        {
            if (_sleeping || HideoutDoors.Inside == null || Plugin.Instance == null || hours <= 0f)
            {
                return;
            }
            Plugin.Instance.StartCoroutine(Sleeping(hours));
        }

        private static IEnumerator Sleeping(float hours)
        {
            _sleeping = true;
            var player = GamePlayerOwner.MyPlayer;
            int level;
            HideoutStations.Levels.TryGetValue(RestSpace, out level);
            RoomDoor.FadeText = "Sleeping...";
            yield return RoomDoor.FadeTo(1f);
            yield return new WaitForSecondsRealtime(1.5f);

            string outcome = "";
            try
            {
                var health = player.ActiveHealthController;
                float before = health.GetBodyPartHealth(EBodyPart.Common).Current;
                WorldClock.Add(hours * 3600.0);

                // Never let sleep itself starve the sleeper: stop short of the last few points.
                float energyChange = (HideoutBoosts.Energy - EnergyPerHour) * hours;
                float waterChange = (HideoutBoosts.Hydration - WaterPerHour) * hours;
                energyChange = Mathf.Max(energyChange, 5f - health.Energy.Current);
                waterChange = Mathf.Max(waterChange, 5f - health.Hydration.Current);
                health.ChangeEnergy(energyChange);
                health.ChangeHydration(waterChange);
                HideoutBoosts.Give(player, hours, HealPerHour(level) + HideoutBoosts.Health, 0f, 0f);

                float after = health.GetBodyPartHealth(EBodyPart.Common).Current;
                outcome = "You slept " + hours.ToString("0.#") + " hours. It is " + WorldClock.Describe(WorldClock.Now)
                          + ". Health +" + Mathf.Max(0f, after - before).ToString("0") + ".";
                Plugin.Log.LogInfo("Slept " + hours.ToString("0.#") + " h in hideout '" + HideoutDoors.Inside + "' (Rest Space level " + level + "): health "
                                   + before.ToString("0") + " -> " + after.ToString("0") + ", energy " + health.Energy.Current.ToString("0") + ", water " + health.Hydration.Current.ToString("0"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Sleeping failed: " + e);
                outcome = "Something woke you early.";
            }

            HideoutCrafting.AnnounceDue();

            // Sleeping is also where the game is saved.
            SaveGame.Save("slept");

            yield return new WaitForSecondsRealtime(1.0f);
            RoomDoor.FadeText = null;
            yield return RoomDoor.FadeTo(0f);
            NotificationManager.DisplayMessageNotification(outcome, ENotificationDurationType.Long);
            _sleeping = false;
        }
    }

    /// <summary>Saving at a bed. Filled in with the save stage; until then it only says so in the log.</summary>
    public static class SaveGame
    {
        public static void Save(string reason)
        {
            Plugin.Log.LogInfo("Save requested (" + reason + "); saving is not built yet");
        }
    }
}
