using System.Collections.Generic;
using System.Text;
using EFT;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// Where each player is, as far as the raid's standstill goes: out on the map, in a trader's
    /// room, or in the hideout. Every machine says where its own player is; the host decides.
    ///
    /// Alone, the raid stands still while the player is in the hideout, as it always did. In co-op
    /// it stands still only while every living player is inside somewhere (a room or the hideout):
    /// one player indoors does not stop the world for the others.
    /// </summary>
    public static class Presence
    {
        public const string Out = "out", Room = "room", Hideout = "hideout";

        private static string _mine = Out;
        private static float _nextLook, _nextDecide;
        private static readonly Dictionary<string, string> Where = new Dictionary<string, string>();

        /// <summary>Where the local player is right now.</summary>
        private static string Look()
        {
            var me = GamePlayerOwner.MyPlayer;
            if (me == null)
            {
                return Out;
            }
            if (HideoutDoors.Inside != null)
            {
                return Hideout;
            }
            var at = me.Transform.position;
            foreach (var room in TraderRoom.Rooms)
            {
                if (room.Indoor != null && room.Indoor.Check(at) != null)
                {
                    return Room;
                }
            }
            return Out;
        }

        /// <summary>Looks at once, without waiting for the next tick: on stepping through a door.</summary>
        public static void Refresh()
        {
            _nextLook = 0f;
            Tick();
        }

        /// <summary>Called every frame, on every machine.</summary>
        public static void Tick()
        {
            if (!Net.Headless && Time.unscaledTime >= _nextLook)
            {
                _nextLook = Time.unscaledTime + 1f;
                string now = Look();
                if (now != _mine)
                {
                    _mine = now;
                    Net.Current.Send("zmt.where", Encoding.UTF8.GetBytes(now), false);
                }
            }
            if (Net.IsHost && Time.unscaledTime >= _nextDecide)
            {
                // Looked at again every so often: players die, leave and join without saying where they are.
                Decide();
            }
        }

        /// <summary>On the host: a machine says where its player is.</summary>
        public static void OnWhere(byte[] body, string from)
        {
            string where = Encoding.UTF8.GetString(body ?? new byte[0]);
            Where[from ?? ""] = where;
            Plugin.Log.LogInfo("Presence: " + (from ?? "?") + " is " + (where == Out ? "out on the map" : where == Room ? "in a trader's room" : "in the hideout"));
            Decide();
        }

        /// <summary>On the host: where a player last said they were.</summary>
        public static string WhereIs(string profile)
        {
            string where;
            return Where.TryGetValue(profile ?? "", out where) ? where : Out;
        }

        public static void Reset()
        {
            Where.Clear();
            _mine = Out;
            _nextLook = 0f;
        }

        private static void Decide()
        {
            _nextDecide = Time.unscaledTime + 2f;
            if (!Net.IsHost)
            {
                return;
            }
            bool still;
            if (!Net.Coop)
            {
                still = _mine == Hideout;
            }
            else
            {
                int living = 0, inside = 0;
                foreach (var human in Net.Current.Humans())
                {
                    if (!human.Alive)
                    {
                        continue;
                    }
                    living++;
                    string where;
                    if (Where.TryGetValue(human.ProfileId, out where) && where != Out)
                    {
                        inside++;
                    }
                }
                still = living > 0 && inside == living;
            }
            BotFreeze.Set(still);
        }
    }
}
