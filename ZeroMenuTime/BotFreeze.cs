using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// While the player is in the hideout, every bot on the map is switched off: nothing moves,
    /// fights or loots up there, and the processor time they take is free. They are switched back
    /// on, where they stood, when the player steps out. Bots that spawn in the meantime are caught
    /// every few seconds.
    /// </summary>
    public static class BotFreeze
    {
        private static readonly List<GameObject> Frozen = new List<GameObject>();
        private static bool _on;
        private static float _nextSweep;

        public static int Count => Frozen.Count;

        public static void Set(bool on)
        {
            if (on == _on)
            {
                return;
            }
            _on = on;
            if (on)
            {
                Sweep();
                Plugin.Log.LogInfo("Bots switched off, the raid stands still: " + Frozen.Count);
            }
            else
            {
                int back = 0;
                foreach (var bot in Frozen)
                {
                    if (bot != null)
                    {
                        bot.SetActive(true);
                        back++;
                    }
                }
                Frozen.Clear();
                Plugin.Log.LogInfo("Bots switched back on: " + back);
            }
        }

        /// <summary>Called every frame.</summary>
        public static void Tick()
        {
            if (!_on || Time.unscaledTime < _nextSweep)
            {
                return;
            }
            if (!Singleton<GameWorld>.Instantiated)
            {
                _on = false;
                Frozen.Clear();
                return;
            }
            Sweep();
        }

        private static void Sweep()
        {
            _nextSweep = Time.unscaledTime + 0.1f;   // other mods switch bots back on; a second was long enough for one to shoot
            try
            {
                var world = Singleton<GameWorld>.Instance;
                if (world == null || world.AllAlivePlayersList == null)
                {
                    return;
                }
                foreach (var player in world.AllAlivePlayersList)
                {
                    if (player == null || player.IsYourPlayer || !player.IsAI)
                    {
                        continue;
                    }
                    var body = player.gameObject;
                    if (body.activeSelf)
                    {
                        body.SetActive(false);
                        Frozen.Add(body);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Switching bots off failed: " + e.Message);
            }
        }
    }
}
