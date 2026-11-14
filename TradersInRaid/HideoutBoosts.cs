using System;
using System.Collections.Generic;
using EFT;
using EFT.Communications;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// What a hideout's built stations do for the character while they are inside it: health,
    /// energy and water coming back by themselves. The amounts are the game's own station bonuses,
    /// read as points per game hour. Outside the hideout nothing is given.
    /// </summary>
    public static class HideoutBoosts
    {
        public static float Health, Energy, Hydration, MaxEnergy;

        private static double _lastClock;
        private static float _nextApply;
        private static readonly EBodyPart[] Parts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg,
        };

        public static void Read(JObject reply)
        {
            Health = Energy = Hydration = MaxEnergy = 0f;
            var boosts = reply["Boosts"] as JObject;
            if (boosts == null)
            {
                return;
            }
            Health = Mathf.Max(0f, (float?)boosts["HealthRegeneration"] ?? 0f);
            Energy = Mathf.Max(0f, (float?)boosts["EnergyRegeneration"] ?? 0f);
            Hydration = Mathf.Max(0f, (float?)boosts["HydrationRegeneration"] ?? 0f);
            MaxEnergy = Mathf.Max(0f, (float?)boosts["MaximumEnergyReserve"] ?? 0f);
            _lastClock = WorldClock.Now;
        }

        public static string Describe()
        {
            var parts = new List<string>();
            if (Health > 0f) parts.Add("+" + Health.ToString("0") + " health");
            if (Energy > 0f) parts.Add("+" + Energy.ToString("0") + " energy");
            if (Hydration > 0f) parts.Add("+" + Hydration.ToString("0") + " water");
            return parts.Count == 0 ? "nothing yet" : string.Join(", ", parts.ToArray()) + " per hour";
        }

        public static void Announce()
        {
            NotificationManager.DisplayMessageNotification("This hideout gives you: " + Describe() + ".", ENotificationDurationType.Long);
        }

        /// <summary>Hands out a span of game time's worth of regeneration. Used every few seconds, and by sleeping.</summary>
        public static void Give(Player player, double gameHours, float healthPerHour, float energyPerHour, float hydrationPerHour)
        {
            var health = player.ActiveHealthController;
            if (health == null || !health.IsAlive || gameHours <= 0)
            {
                return;
            }
            if (energyPerHour != 0f)
            {
                health.ChangeEnergy((float)(energyPerHour * gameHours));
            }
            if (hydrationPerHour != 0f)
            {
                health.ChangeHydration((float)(hydrationPerHour * gameHours));
            }
            float pool = (float)(healthPerHour * gameHours);
            // Health goes to whatever is hurt, the worst first; a limb that is destroyed stays so.
            for (int round = 0; round < Parts.Length && pool > 0.01f; round++)
            {
                EBodyPart worst = EBodyPart.Common;
                float worstShare = 1f;
                foreach (var part in Parts)
                {
                    var value = health.GetBodyPartHealth(part);
                    if (value.Current <= 0f || value.Current >= value.Maximum)
                    {
                        continue;
                    }
                    float share = value.Current / value.Maximum;
                    if (share < worstShare)
                    {
                        worstShare = share;
                        worst = part;
                    }
                }
                if (worst == EBodyPart.Common)
                {
                    break;
                }
                var hurt = health.GetBodyPartHealth(worst);
                float given = Mathf.Min(pool, hurt.Maximum - hurt.Current);
                health.ChangeHealth(worst, given, new EFT.Ballistics.DamageInfo { DamageType = EDamageType.Existence });
                pool -= given;
            }
        }

        /// <summary>Called every frame.</summary>
        public static void Tick()
        {
            if (HideoutDoors.Inside == null || Time.unscaledTime < _nextApply)
            {
                return;
            }
            _nextApply = Time.unscaledTime + 3f;
            var player = GamePlayerOwner.MyPlayer;
            if (player == null)
            {
                return;
            }
            try
            {
                double now = WorldClock.Now;
                // A jump of the clock (sleeping) is not paid out here; sleeping has its own rules.
                double hours = Math.Min(Math.Max(0, now - _lastClock), 300) / 3600.0;
                _lastClock = now;
                Give(player, hours, Health, Energy, Hydration);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hideout boosts failed: " + e.Message);
            }
        }
    }
}
