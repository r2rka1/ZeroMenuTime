using System;
using EFT;
using EFT.Communications;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// A safety net. The hideout and the traders' rooms lie far under the map; should the floor not
    /// be there when the player arrives (the interior still loading, or let go), the character
    /// falls without end. Someone who has fallen a long way with nothing under them is put back
    /// where they last stood. What is known about the fall is written to the log.
    /// </summary>
    public static class FallGuard
    {
        private const float Drop = 40f;

        private static Player _for;
        private static Vector3 _last, _start;
        private static float _fallen, _nextCheck, _lastRescue;
        private static int _solid = -1;

        private static float _nothingSince;
        private static Vector3 _safe;
        private static bool _hasSafe;
        private static int _notMe = 0;

        /// <summary>How far below the feet the nearest thing is, whatever it is made of; -1 for nothing within 150 m.</summary>
        public static float Below(Vector3 at)
        {
            if (_notMe == 0)
            {
                _notMe = ~LayerMask.GetMask("Player", "PlayerCollisionTest", "PlayerSpiritAura", "HitCollider", "Triggers", "Ignore Raycast", "Interactive", "Loot", "Shells", "Grass", "Foliage");
            }
            RaycastHit hit;
            return Physics.Raycast(at + Vector3.up * 1.0f, Vector3.down, out hit, 151f, _notMe, QueryTriggerInteraction.Ignore) ? hit.distance - 1.0f : -1f;
        }

        /// <summary>Called every frame.</summary>
        public static void Tick()
        {
            if (Time.unscaledTime < _nextCheck)
            {
                return;
            }
            _nextCheck = Time.unscaledTime + 0.25f;
            var player = GamePlayerOwner.MyPlayer;
            if (player == null || !player.HealthController.IsAlive)
            {
                _for = null;
                return;
            }
            var at = player.Transform.position;
            if (!ReferenceEquals(_for, player))
            {
                _for = player;
                _last = at;
                _fallen = 0f;
                _lastRescue = -100f;
                _nothingSince = Time.unscaledTime;
                _hasSafe = false;
            }
            if (_solid < 0)
            {
                _solid = LayerMask.GetMask("HighPolyCollider", "LowPolyCollider", "Terrain");
            }
            // Falling: lower at every look, by more than any stairs and less than a step through a door.
            float down = _last.y - at.y;
            if (down > 0.75f && down < 40f)
            {
                if (_fallen <= 0f)
                {
                    _start = _last;
                }
                _fallen += down;
            }
            else
            {
                _fallen = 0f;
            }
            _last = at;
            // Out of the physics engine's reach nobody drops at all: they hang in the fall for good.
            // They stand on nothing: no floor, ground or anything else for a long way down.
            // A raid begins with nothing solid loaded yet, so that wait is a longer one.
            if (Below(at) >= 0f)
            {
                _nothingSince = Time.unscaledTime;
                _safe = at;
                _hasSafe = true;
            }
            if (_fallen < Drop && Time.unscaledTime - _nothingSince < (_hasSafe ? 5f : 30f))
            {
                return;
            }
            _nothingSince = Time.unscaledTime;
            if (_fallen < Drop && _hasSafe && Vector3.Distance(at, _safe) < 1f)
            {
                // Still where the ground was a moment ago: the map is only loading or settling.
                return;
            }
            if (_hasSafe)
            {
                _start = _safe;
            }
            else if (_fallen < Drop)
            {
                _start = at;
            }
            _fallen = 0f;
            try
            {
                Rescue(player, at, _start);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Fall guard: " + e);
            }
            _last = player.Transform.position;
        }

        private static void Rescue(Player player, Vector3 at, Vector3 from)
        {
            bool again = Time.unscaledTime - _lastRescue < 20f;
            _lastRescue = Time.unscaledTime;
            Plugin.Log.LogWarning("Fall guard: fell from " + from.ToString("F1") + " to " + at.ToString("F1") + " with nothing underfoot; inside hideout: "
                                  + (HideoutDoors.Inside ?? "no") + ", interior loaded " + HideoutBunker.Loaded + ", loading " + HideoutBunker.Loading
                                  + ", parked " + HideoutBunker.Parked + ", at " + HideoutBunker.Moved.ToString("F1") + (again ? "; second time in a row" : ""));
            if (HideoutDoors.Inside != null && (again || !HideoutBunker.Loaded || HideoutBunker.Parked))
            {
                // The hideout's floor is not there: back out through its door, onto the map.
                HideoutDoors.Leave();
                NotificationManager.DisplayWarningNotification("The hideout was not ready. You are back at its door; try again.", ENotificationDurationType.Long);
                return;
            }
            Vector3 to = from + Vector3.up * 0.3f;
            RaycastHit ground;
            if ((again || !_hasSafe)
                && Physics.Raycast(new Vector3(from.x, 1500f, from.z), Vector3.down, out ground, 3000f, _solid, QueryTriggerInteraction.Ignore))
            {
                // Nowhere known to be solid: the highest ground straight above or below.
                to = ground.point + Vector3.up * 0.3f;
            }
            player.Teleport(to, false);
            NotificationManager.DisplayWarningNotification("You fell out of the world and were put back.", ENotificationDurationType.Long);
            Plugin.Log.LogWarning("Fall guard: put back at " + to.ToString("F1"));
        }
    }
}
