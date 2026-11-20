using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using JsonType;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// The group moving as one: the hosting player ending the raid for everybody, and dead
    /// teammates being carried through a transit to the next map and coming back to life there.
    /// </summary>
    public static class Regroup
    {
        // ---- The hosting player leaves: the raid ends for everyone ----

        /// <summary>
        /// A hosting player cannot go while others are in their raid. Asked to save and go to the
        /// menu, they are told what it means; on yes, everyone leaves by the same exit, the host last.
        /// </summary>
        public static void AskEndForAll(string exitName)
        {
            var ui = ItemUiContext.Instance;
            if (ui == null)
            {
                return;
            }
            ui.ShowMessageWindow("You host this raid. Leaving ends it for everyone in it.\n\nEach living player leaves with you and starts next time from here. "
                                 + "Anyone who is dead stays dead, and their own death setting applies.\n\nEnd the raid for everyone?",
                () => EndForAll(exitName), () => { }, "SAVE AND GO TO MENU");
        }

        public static void EndForAll(string exitName)
        {
            Plugin.Log.LogInfo("Regroup: the host ends the raid for everyone, by " + exitName);
            Net.Current.Send("zmt.endraid", Encoding.UTF8.GetBytes(exitName), true);
            if (Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(HostLeavesLast(exitName));
            }
        }

        private static IEnumerator HostLeavesLast(string exitName)
        {
            Seamless.SaveAndMenuRequested = true;
            _ = SaveGame.TraderSave(exitName);
            NotificationManager.DisplayMessageNotification("Ending the raid for everyone...", ENotificationDurationType.Long);
            for (int tries = 0; tries < 45; tries++)
            {
                yield return new WaitForSecondsRealtime(1f);
                string why;
                if (Net.Current.LeaveRaid(exitName, out why))
                {
                    yield break;
                }
            }
            Seamless.SaveAndMenuRequested = false;
            NotificationManager.DisplayWarningNotification("The others have not left; the raid goes on.", ENotificationDurationType.Long);
        }

        /// <summary>On every other machine: the host ends the raid.</summary>
        public static void OnEndRaid(byte[] body, string from)
        {
            var me = GamePlayerOwner.MyPlayer;
            if (me == null)
            {
                return;
            }
            string exit = Encoding.UTF8.GetString(body ?? new byte[0]);
            if (Afterlife.Dead)
            {
                // Dead when the raid ends: a death, with the player's own death setting.
                Afterlife.OnWipe(null, "");
                return;
            }
            NotificationManager.DisplayWarningNotification("The host has ended the raid. You leave with them.", ENotificationDurationType.Long);
            Seamless.SaveAndMenuRequested = true;
            string why;
            if (!Net.Current.LeaveRaid(exit, out why))
            {
                Plugin.Log.LogWarning("Regroup: could not leave with the host: " + why);
            }
        }

        // ---- Transits: the dead travel with the living ----

        private static float _nextLook;
        private static readonly List<TransitPoint> Transits = new List<TransitPoint>();
        private static float _transitsRead = -100f;
        private static float _lastCall = -100f;

        /// <summary>
        /// On the host, about once a second while anyone is a ghost: when every living player stands
        /// in the same transit, the ghosts are brought to it. To the co-op layer a ghost is a living
        /// player, and a transit waits for every living player to be in it.
        /// </summary>
        public static void Tick()
        {
            if (!Net.Coop || !Net.IsHost || Afterlife.Ghosts.Count == 0 || Time.unscaledTime < _nextLook)
            {
                return;
            }
            _nextLook = Time.unscaledTime + 1f;
            if (Time.unscaledTime - _transitsRead > 20f)
            {
                _transitsRead = Time.unscaledTime;
                Transits.Clear();
                Transits.AddRange(UnityEngine.Object.FindObjectsOfType<TransitPoint>());
            }
            var living = Afterlife.Living();
            if (living.Count == 0 || Time.unscaledTime - _lastCall < 4f)
            {
                return;
            }
            foreach (var transit in Transits)
            {
                if (transit == null)
                {
                    continue;
                }
                var zone = transit.GetComponentInChildren<Collider>();
                if (zone == null)
                {
                    continue;
                }
                var bounds = zone.bounds;
                bounds.Expand(1.5f);
                if (living.All(h => bounds.Contains(h.Position)))
                {
                    _lastCall = Time.unscaledTime;
                    var at = living[0].Position;
                    string told = at.x.ToString("F2", CultureInfo.InvariantCulture) + " " + at.y.ToString("F2", CultureInfo.InvariantCulture) + " " + at.z.ToString("F2", CultureInfo.InvariantCulture);
                    var bytes = Encoding.UTF8.GetBytes(told);
                    Net.Current.Send("zmt.ghost.come", bytes, true);
                    Afterlife.OnCome(bytes, "");
                    Plugin.Log.LogInfo("Regroup: everyone alive is in a transit; the dead are brought to it at " + at.ToString("F1"));
                    return;
                }
            }
        }

        // ---- Arriving on the next map after dying on the last ----

        /// <summary>
        /// A player who was dead when the group went through a transit comes to life on the new map,
        /// as if they had died there: in the state their own death setting gives.
        /// </summary>
        public static IEnumerator ArriveAfterDeath()
        {
            yield return new WaitForSecondsRealtime(4f);
            var me = GamePlayerOwner.MyPlayer;
            if (me == null)
            {
                yield break;
            }
            var mode = Death.OnDeath != null ? Death.OnDeath.Value : Death.Loss.KeepItems;
            Plugin.Log.LogInfo("Regroup: arrived on a new map after dying on the last; death setting " + mode);
            var health = me.ActiveHealthController;
            health.SetDamageCoeff(Plugin.Undying != null && Plugin.Undying.Value ? 0f : 1f);
            if (mode == Death.Loss.LoseEverything)
            {
                // The secure container's contents go too.
                var secure = me.Profile.Inventory.Equipment.GetSlot(EquipmentSlot.SecuredContainer).ContainedItem as CompoundItem;
                int lost = 0;
                foreach (var item in secure != null ? secure.Grids.SelectMany(g => g.Items).ToList() : new List<Item>())
                {
                    var taking = LocalTrade.Take(me, item, item.StackObjectsCount);
                    while (!taking.IsCompleted)
                    {
                        yield return null;
                    }
                    lost++;
                }
                Plugin.Log.LogInfo("Regroup: lose everything: " + lost + " thing(s) taken from the secure container");
            }
            else if (mode == Death.Loss.KeepItems)
            {
                // The gear of the last save is laid in front of the player, to be put on again.
                var asking = RequestHandler.PostJsonAsync("/zeromenutime/coop/savedgear", "{}");
                while (!asking.IsCompleted)
                {
                    yield return null;
                }
                int laid = 0;
                try
                {
                    var reply = JObject.Parse(asking.Result);
                    var flat = (reply["Items"] as JArray ?? new JArray()).ToString().ParseJsonTo<FlatItem[]>();
                    var tree = Singleton<ItemFactory>.Instance.FlatItemsToTree(flat, true);
                    foreach (var root in reply["Roots"] as JArray ?? new JArray())
                    {
                        Item saved;
                        if (tree.Items != null && tree.Items.TryGetValue((string)root, out saved))
                        {
                            // A copy with ids of its own: the thing itself may still be about, carried here by a teammate.
                            _ = RewardTable.LayNear(me, saved.CloneItem(me.InventoryController));
                            laid++;
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Regroup: the gear of the last save could not be given back: " + e);
                }
                Plugin.Log.LogInfo("Regroup: keep items: " + laid + " thing(s) from the last save laid in front of the player");
                if (laid > 0)
                {
                    NotificationManager.DisplayMessageNotification("You are back. The gear from your last save lies in front of you.", ENotificationDurationType.Long);
                }
            }
            if (mode != Death.Loss.KeepItems)
            {
                NotificationManager.DisplayMessageNotification("You are back, with what your death left you.", ENotificationDurationType.Long);
            }
        }
    }
}
