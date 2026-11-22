using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// Treatment at Therapist: for a price she puts the player right again, lost limbs included.
    /// It is what her treatment after a raid was in the menus, done in her room.
    /// </summary>
    public static class Treatment
    {
        private static readonly EBodyPart[] Parts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg,
        };

        private static bool _busy;

        /// <summary>What it costs now, in roubles; 0 when there is nothing to treat.</summary>
        public static int Cost()
        {
            try
            {
                var health = GamePlayerOwner.MyPlayer?.ActiveHealthController;
                if (health == null)
                {
                    return 0;
                }
                float missing = 0f;
                int lost = 0;
                foreach (var part in Parts)
                {
                    var now = health.GetBodyPartHealth(part);
                    missing += Mathf.Max(0f, now.Maximum - now.Current);
                    if (health.IsBodyPartDestroyed(part))
                    {
                        lost++;
                    }
                }
                if (missing < 1f && lost == 0)
                {
                    return 0;
                }
                // A visit, the health given back, and each lost limb restored.
                int cost = 5000 + (int)(missing * 100f) + lost * 15000;
                return (cost + 999) / 1000 * 1000;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static async Task<string> Buy()
        {
            var me = GamePlayerOwner.MyPlayer;
            int cost = Cost();
            if (me == null || cost <= 0 || _busy || Afterlife.Dead)
            {
                return "not possible right now";
            }
            _busy = true;
            try
            {
                if (Afterlife.CarriedRoubles(me) < cost)
                {
                    NotificationManager.DisplayWarningNotification("You need " + cost.ToString("N0", CultureInfo.InvariantCulture) + " roubles on you.", ENotificationDurationType.Long);
                    return "not enough roubles";
                }
                if (!await Afterlife.Pay(me, cost))
                {
                    return "the payment failed";
                }
                me.ActiveHealthController.RestoreFullHealth();
                NotificationManager.DisplayMessageNotification("Paid " + cost.ToString("N0", CultureInfo.InvariantCulture) + " roubles. You have been treated.", ENotificationDurationType.Long);
                Plugin.Log.LogInfo("Treated at Therapist for " + cost + " roubles");
                return "treated for " + cost;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Treatment failed: " + e);
                return "failed: " + e.Message;
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
