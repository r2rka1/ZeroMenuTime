using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// Death in a co-op raid. The co-op layer has no way back from a real death, so a player who is
    /// killed while a teammate still lives does not really die: the kill is stopped, what they
    /// carried is dropped where they fell, and they wait out of the world as a ghost, watching the
    /// others. A teammate can bring them back at a trader (for money) or at the hideout's medical
    /// station; they then stand at the place they came into the map, with nothing. When the last
    /// living player falls, everyone dies for real and the raid is over.
    ///
    /// Alone, none of this happens: death is the game's death.
    /// </summary>
    public static class Afterlife
    {
        public const int ReviveCost = 100000;
        private const string Roubles = "5449016a4bdc2d6f028b456f";

        /// <summary>Profile ids of the players who are ghosts now. Kept the same on every machine.</summary>
        public static readonly HashSet<string> Ghosts = new HashSet<string>();

        /// <summary>The local player is a ghost.</summary>
        public static bool Dead;

        /// <summary>Everyone has fallen: kills are real again, so the raid can end.</summary>
        private static bool _wipe;

        private static Vector3 _cameInAt;
        private static bool _hasCameIn;
        private static GameObject _limbo;
        private static int _watching;
        private static float _nextCheck;
        private static string _line = "";

        /// <summary>The middle of the room ghosts wait in, far from anything, under the map.</summary>
        private static readonly Vector3 LimboAt = new Vector3(400f, -600f, 400f);

        /// <summary>Human players who are alive and not ghosts.</summary>
        public static List<Human> Living()
        {
            return Net.Current.Humans().Where(h => h.Alive && !Ghosts.Contains(h.ProfileId)).ToList();
        }

        /// <summary>Built with the trader rooms, on every machine, before the physics region is drawn.</summary>
        public static void BuildLimbo()
        {
            if (_limbo != null)
            {
                UnityEngine.Object.Destroy(_limbo);
            }
            _limbo = new GameObject("ZeroMenuTime limbo");
            _limbo.transform.position = LimboAt;
            int solid = LayerMask.NameToLayer("HighPolyCollider");
            // A shut box: floor, ceiling and four walls.
            Wall(new Vector3(0f, -0.25f, 0f), new Vector3(12f, 0.5f, 12f), solid);
            Wall(new Vector3(0f, 3.25f, 0f), new Vector3(12f, 0.5f, 12f), solid);
            Wall(new Vector3(-6f, 1.5f, 0f), new Vector3(0.5f, 3.5f, 12f), solid);
            Wall(new Vector3(6f, 1.5f, 0f), new Vector3(0.5f, 3.5f, 12f), solid);
            Wall(new Vector3(0f, 1.5f, -6f), new Vector3(12f, 3.5f, 0.5f), solid);
            Wall(new Vector3(0f, 1.5f, 6f), new Vector3(12f, 3.5f, 0.5f), solid);
        }

        private static void Wall(Vector3 at, Vector3 size, int layer)
        {
            var wall = new GameObject("wall", typeof(BoxCollider));
            wall.layer = layer;
            wall.transform.SetParent(_limbo.transform, false);
            wall.transform.localPosition = at;
            wall.GetComponent<BoxCollider>().size = size;
        }

        /// <summary>Dead when the group went through a transit: comes to life on the map just arrived at.</summary>
        private static bool _arriveDead;

        /// <summary>On a ghost's machine: the living are all in a transit; come and stand with them, unseen.</summary>
        public static void OnCome(byte[] body, string from)
        {
            var me = GamePlayerOwner.MyPlayer;
            string[] parts = Encoding.UTF8.GetString(body ?? new byte[0]).Split(' ');
            if (me == null || !Dead || parts.Length < 3)
            {
                return;
            }
            var at = new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture));
            _inLimbo = false;
            me.Teleport(at + Vector3.up * 0.1f, false);
            Plugin.Log.LogInfo("Afterlife: brought to the transit at " + at.ToString("F1") + " with the living");
        }

        public static void OnRaidStarted()
        {
            Ghosts.Clear();
            bool arrive = _arriveDead;
            _arriveDead = false;
            Dead = false;
            _inLimbo = false;
            _wipe = false;
            if (arrive && Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(Regroup.ArriveAfterDeath());
            }
            var me = GamePlayerOwner.MyPlayer;
            _hasCameIn = me != null;
            if (me != null)
            {
                _cameInAt = me.Transform.position;
            }
            if (Net.Role == Role.Client)
            {
                Net.Current.Send("zmt.ghosts?", new byte[0], false);
            }
        }

        public static void OnRaidEnded(ExitStatus status, string exit)
        {
            // A ghost carried through a transit is still dead when the next map begins.
            _arriveDead = Dead && status == ExitStatus.Transit;
            Ghosts.Clear();
            Dead = false;
            _wipe = false;
        }

        /// <summary>A kill is about to land on the local player. True: let it (alone, or the last one standing).</summary>
        public static bool AllowKill(Player me, EDamageType cause)
        {
            if (!Net.Coop || _wipe || Dead)
            {
                // A ghost cannot be killed again; nothing more to do, but the kill must not go through either.
                return !Dead || _wipe;
            }
            var others = Living().Where(h => !h.IsLocal).ToList();
            if (others.Count == 0)
            {
                // The last one standing: everyone has fallen. The ghosts go with them.
                Plugin.Log.LogInfo("Afterlife: the last living player was killed (" + cause + "); the raid is lost for everyone");
                _wipe = true;
                Net.Current.Send("zmt.wipe", new byte[0], true);
                Net.Current.EndRaidDead();
                return true;
            }
            try
            {
                Become(me, cause);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Afterlife: could not become a ghost; the death stands: " + e);
                return true;
            }
            return false;
        }

        private static void Become(Player me, EDamageType cause)
        {
            Dead = true;
            var fellAt = me.Transform.position;
            var health = me.ActiveHealthController;
            health.RestoreFullHealth();
            health.SetDamageCoeff(0f);
            Ghosts.Add(me.ProfileId);
            Net.Current.Send("zmt.ghost", Encoding.UTF8.GetBytes("on " + me.ProfileId + " " + Num(fellAt.x) + " " + Num(fellAt.y) + " " + Num(fellAt.z)), true);
            Plugin.Log.LogWarning("Afterlife: killed by " + cause + " at " + fellAt.ToString("F1") + "; now a ghost");
            NotificationManager.DisplayWarningNotification("You died. Your gear lies where you fell. A teammate can bring you back at a trader or at the hideout's medical station.",
                ENotificationDurationType.Long);
            if (Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(LeaveTheWorld(me, fellAt));
            }
        }

        /// <summary>
        /// The gear is dropped one thing at a time, each drop finished before the next, and only then
        /// does the ghost leave: a drop still under way when the player is moved lands where they went.
        /// </summary>
        private static System.Collections.IEnumerator LeaveTheWorld(Player me, Vector3 fellAt)
        {
            // Whatever is in the hands is put away first: a thing being held cannot be dropped as gear
            // on the other machines, and would stay in the ghost's hands there.
            bool emptied = false;
            try
            {
                me.SetEmptyHands(result => { emptied = true; });
            }
            catch (Exception e)
            {
                emptied = true;
                Plugin.Log.LogWarning("Afterlife: hands not emptied: " + e.Message);
            }
            float handsUntil = Time.unscaledTime + 4f;
            while (!emptied && Time.unscaledTime < handsUntil)
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.6f);
            int dropped = 0;
            foreach (var item in GearToDrop(me))
            {
                bool done = false;
                try
                {
                    me.InventoryController.ThrowItem(item, true, result => { done = true; });
                    dropped++;
                }
                catch (Exception e)
                {
                    done = true;
                    Plugin.Log.LogWarning("Afterlife: could not drop " + item.ShortName + ": " + e.Message);
                }
                float until = Time.unscaledTime + 2f;
                while (!done && Time.unscaledTime < until)
                {
                    yield return null;
                }
                // Held where they fell while the drops go on.
                if (Vector3.Distance(me.Transform.position, fellAt) > 1.5f)
                {
                    me.Teleport(fellAt, false);
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            if (Dead)
            {
                me.Teleport(LimboAt + Vector3.up * 0.1f, false);
                _inLimbo = true;
                Plugin.Log.LogInfo("Afterlife: " + dropped + " thing(s) dropped at " + fellAt.ToString("F1") + "; the ghost has left the world");
            }
        }

        private static string Num(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Drops what the player carries where they stand, as the game drops things: every machine
        /// sees the same. The secure container, the armband, the knife and the dog tag stay, as they
        /// do in every death setting.
        /// </summary>
        private static List<Item> GearToDrop(Player me)
        {
            var equipment = me.Profile.Inventory.Equipment;
            var keep = new HashSet<EquipmentSlot> { EquipmentSlot.SecuredContainer, EquipmentSlot.ArmBand, EquipmentSlot.Scabbard, EquipmentSlot.Dogtag, EquipmentSlot.Pockets };
            var toDrop = new List<Item>();
            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                if (keep.Contains(slot))
                {
                    continue;
                }
                var worn = equipment.GetSlot(slot).ContainedItem;
                if (worn != null)
                {
                    toDrop.Add(worn);
                }
            }
            // Pockets cannot be taken off; what is in them can.
            var pockets = equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem as CompoundItem;
            if (pockets != null)
            {
                foreach (var grid in pockets.Grids)
                {
                    toDrop.AddRange(grid.Items.ToList());
                }
            }
            return toDrop;
        }

        // ---- What every machine keeps: who is a ghost ----

        public static void OnGhost(byte[] body, string from)
        {
            string[] parts = Encoding.UTF8.GetString(body).Split(' ');
            if (parts.Length < 2)
            {
                return;
            }
            string name = "A player";
            foreach (var human in Net.Current.Humans())
            {
                if (human.ProfileId == parts[1])
                {
                    name = human.Name;
                }
            }
            if (parts[0] == "on")
            {
                if (Ghosts.Add(parts[1]) && !Net.Headless)
                {
                    NotificationManager.DisplayWarningNotification(name + " died. Revive them at a trader (" + ReviveCost.ToString("N0", CultureInfo.InvariantCulture)
                        + " roubles) or at the hideout's medical station.", ENotificationDurationType.Long);
                }
                Plugin.Log.LogInfo("Afterlife: " + name + " is a ghost" + (parts.Length > 4 ? "; fell at (" + parts[2] + ", " + parts[3] + ", " + parts[4] + ")" : ""));
            }
            else
            {
                if (Ghosts.Remove(parts[1]) && !Net.Headless)
                {
                    NotificationManager.DisplayMessageNotification(name + " is back.", ENotificationDurationType.Long);
                }
                Plugin.Log.LogInfo("Afterlife: " + name + " is back among the living");
            }
        }

        /// <summary>On the host: a machine that has just come into the raid asks who is a ghost.</summary>
        public static void OnAskedGhosts(byte[] body, string from)
        {
            if (!Net.IsHost)
            {
                return;
            }
            foreach (string ghost in Ghosts)
            {
                Net.Current.Send("zmt.ghost", Encoding.UTF8.GetBytes("on " + ghost), true);
            }
        }

        // ---- Bringing someone back ----

        /// <summary>The ghosts a living player could bring back now: profile id and name.</summary>
        public static List<KeyValuePair<string, string>> Revivable()
        {
            var list = new List<KeyValuePair<string, string>>();
            if (!Net.Coop || Dead)
            {
                return list;
            }
            foreach (var human in Net.Current.Humans())
            {
                if (Ghosts.Contains(human.ProfileId) && !human.IsLocal)
                {
                    list.Add(new KeyValuePair<string, string>(human.ProfileId, human.Name));
                }
            }
            return list;
        }

        public static int CarriedRoubles(Player player)
        {
            return LocalTrade.CarriedItems(player).Where(i => i.TemplateId == Roubles).Sum(i => i.StackObjectsCount);
        }

        /// <summary>At a trader: pays the price from carried roubles and brings the player back. Says how it went.</summary>
        public static async System.Threading.Tasks.Task<string> ReviveForMoney(string profile, string name)
        {
            var me = GamePlayerOwner.MyPlayer;
            if (me == null || Dead)
            {
                return "not possible right now";
            }
            if (!Ghosts.Contains(profile))
            {
                return name + " is not dead";
            }
            if (CarriedRoubles(me) < ReviveCost)
            {
                NotificationManager.DisplayWarningNotification("You need " + ReviveCost.ToString("N0", CultureInfo.InvariantCulture) + " roubles on you.", ENotificationDurationType.Long);
                return "not enough roubles";
            }
            int left = ReviveCost;
            foreach (var stack in LocalTrade.CarriedItems(me).Where(i => i.TemplateId == Roubles).OrderBy(i => i.StackObjectsCount).ToList())
            {
                if (left <= 0)
                {
                    break;
                }
                int take = Math.Min(left, stack.StackObjectsCount);
                if (!await LocalTrade.Take(me, stack, take))
                {
                    return "the payment failed";
                }
                left -= take;
            }
            Revive(profile);
            NotificationManager.DisplayMessageNotification("Paid. " + name + " is brought back at the place they came in.", ENotificationDurationType.Long);
            return "revived " + name + " for " + ReviveCost;
        }

        /// <summary>At the hideout's medical station: free.</summary>
        public static string ReviveFree(string profile, string name)
        {
            if (Dead || !Ghosts.Contains(profile))
            {
                return "not possible right now";
            }
            Revive(profile);
            NotificationManager.DisplayMessageNotification(name + " is brought back at the place they came in.", ENotificationDurationType.Long);
            return "revived " + name;
        }

        private static void Revive(string profile)
        {
            var body = Encoding.UTF8.GetBytes(profile);
            Net.Current.Send("zmt.revive", body, true);
        }

        /// <summary>On every machine: someone is brought back. The ghost's own machine does it.</summary>
        public static void OnRevive(byte[] body, string from)
        {
            var me = GamePlayerOwner.MyPlayer;
            string profile = Encoding.UTF8.GetString(body);
            if (me == null || me.ProfileId != profile || !Dead)
            {
                return;
            }
            ComeBack(me);
        }

        private static void ComeBack(Player me)
        {
            var to = _hasCameIn ? _cameInAt : me.Transform.position;
            me.Teleport(to + Vector3.up * 0.1f, false);
            var health = me.ActiveHealthController;
            health.RestoreFullHealth();
            health.SetDamageCoeff(1f);
            // Back, but not well: about a third of health everywhere, and hungry.
            foreach (EBodyPart part in new[] { EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg })
            {
                var now = health.GetBodyPartHealth(part);
                health.ChangeHealth(part, -now.Maximum * 0.65f, new EFT.Ballistics.DamageInfo { DamageType = EDamageType.Existence });
            }
            health.ChangeEnergy(30f - health.Energy.Current);
            health.ChangeHydration(30f - health.Hydration.Current);
            health.SetDamageCoeff(Plugin.Undying != null && Plugin.Undying.Value ? 0f : 1f);
            Dead = false;
            _inLimbo = false;
            Ghosts.Remove(me.ProfileId);
            _backAt = to;
            _backCheck = Time.unscaledTime + 1.5f;
            Net.Current.Send("zmt.ghost", Encoding.UTF8.GetBytes("off " + me.ProfileId), true);
            Plugin.Log.LogInfo("Afterlife: brought back at " + to.ToString("F1"));
            NotificationManager.DisplayMessageNotification("You are back, where you came into this map. Your gear lies where you fell.", ENotificationDurationType.Long);
        }

        private static bool _inLimbo;
        private static Vector3 _backAt;
        private static float _backCheck;

        /// <summary>On every machine: everyone has fallen. A ghost dies for real, so its raid ends.</summary>
        public static void OnWipe(byte[] body, string from)
        {
            _wipe = true;
            var me = GamePlayerOwner.MyPlayer;
            if (me == null || !Dead)
            {
                return;
            }
            Plugin.Log.LogInfo("Afterlife: the whole group has fallen; this ghost dies now");
            Dead = false;
            me.ActiveHealthController.SetDamageCoeff(1f);
            me.ActiveHealthController.Kill(EDamageType.Undefined);
            Net.Current.EndRaidDead();
        }

        /// <summary>Called every frame on every machine.</summary>
        public static void Tick()
        {
            var me = GamePlayerOwner.MyPlayer;
            if (_backCheck > 0f && Time.unscaledTime >= _backCheck && me != null)
            {
                // A move out of the waiting room does not always land; look, and do it again if not.
                _backCheck = 0f;
                if (Vector3.Distance(me.Transform.position, _backAt) > 5f)
                {
                    Plugin.Log.LogWarning("Afterlife: not where the revival put the player (" + me.Transform.position.ToString("F1") + "); moved again");
                    me.Teleport(_backAt + Vector3.up * 0.1f, false);
                    _backCheck = Time.unscaledTime + 1.5f;
                }
            }
            if (Dead && me != null)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    _watching++;
                }
                if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    _watching--;
                }
                // Should the ghost ever leave the waiting room (a fall, a push), it is put back.
                if (_inLimbo && Vector3.Distance(me.Transform.position, LimboAt) > 20f)
                {
                    me.Teleport(LimboAt + Vector3.up * 0.1f, false);
                }
            }
            if (Net.Coop && Net.IsHost && Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 3f;
                // The last living player may leave the raid or lose their connection instead of dying.
                if (!_wipe && Ghosts.Count > 0 && Living().Count == 0)
                {
                    Plugin.Log.LogInfo("Afterlife: nobody is left alive in the raid; the ghosts die");
                    _wipe = true;
                    Net.Current.Send("zmt.wipe", new byte[0], true);
                    OnWipe(null, "");
                }
            }
        }

        /// <summary>The teammate a ghost is watching, or null.</summary>
        private static Human Watched()
        {
            var living = Living().Where(h => !h.IsLocal && h.Player != null).ToList();
            if (living.Count == 0)
            {
                return null;
            }
            return living[((_watching % living.Count) + living.Count) % living.Count];
        }

        /// <summary>Just before the screen is drawn: a ghost's view is put behind a living teammate.</summary>
        public static void BeforeDraw(Camera camera)
        {
            if (!Dead || camera == null || camera != Camera.main)
            {
                return;
            }
            var seen = Watched();
            if (seen == null)
            {
                _line = "Nobody is left to watch.";
                return;
            }
            var body = seen.Player.Transform;
            var look = Quaternion.Euler(12f, seen.Player.Rotation.x, 0f);
            camera.transform.position = body.position + Vector3.up * 1.7f - (look * Vector3.forward) * 2.6f;
            camera.transform.rotation = look;
            _line = "You are dead. Watching " + seen.Name + "  (left / right arrow: someone else)";
        }

        /// <summary>The line across the top of a ghost's screen.</summary>
        public static void Draw()
        {
            if (!Dead)
            {
                return;
            }
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.Max(16, Screen.height / 45) };
            var before = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height * 0.06f), Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0.88f, 0.8f, 1f);
            GUI.Label(new Rect(0f, Screen.height * 0.012f, Screen.width, Screen.height * 0.05f), _line, style);
            GUI.color = before;
        }
    }

    /// <summary>A kill of the local player in a co-op raid goes by <see cref="Afterlife"/> first.</summary>
    public class AfterlifeKillPatch : ModulePatch
    {
        protected override System.Reflection.MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.HealthSystem.ActiveHealthController), "Kill");
        }

        [PatchPrefix]
        public static bool Prefix(EFT.HealthSystem.ActiveHealthController __instance, EDamageType damageType)
        {
            var me = GamePlayerOwner.MyPlayer;
            if (me == null || !ReferenceEquals(me.ActiveHealthController, __instance))
            {
                return true;
            }
            return Afterlife.AllowKill(me, damageType);
        }
    }
}
