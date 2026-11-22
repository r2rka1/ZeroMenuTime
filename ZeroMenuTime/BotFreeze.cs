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

        /// <summary>The raid stands still now.</summary>
        public static bool On => _on;

        public static void Set(bool on)
        {
            if (on == _on)
            {
                return;
            }
            _on = on;
            if (on)
            {
                OrbitLink.Hook();
                Sweep();
                Plugin.Log.LogInfo("Bots switched off, the raid stands still: " + Frozen.Count);
            }
            else
            {
                int back = 0, restarted = 0;
                foreach (var bot in Frozen)
                {
                    if (bot != null)
                    {
                        bot.SetActive(true);
                        back++;
                        // Switching a bot off leaves it marked as not active; switched on again it
                        // would stand where it is and do nothing until told to start up again.
                        try
                        {
                            var owner = bot.GetComponent<BotOwner>();
                            if (owner != null && owner.BotState == EBotState.NonActive)
                            {
                                owner.PostActivate();
                                restarted++;
                            }
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning("Starting a bot up again failed: " + e.Message);
                        }
                    }
                }
                Frozen.Clear();
                Plugin.Log.LogInfo("Bots switched back on: " + back + (restarted > 0 ? " (" + restarted + " started up again)" : ""));
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

    /// <summary>
    /// ORBIT (a bot mod) keeps its far-away bots switched off itself and goes on thinking for them
    /// outside their bodies: they travel, fight one another with shots that are heard, and are woken
    /// when it sees fit. Switching bodies off, as <see cref="BotFreeze"/> does, stops none of that, and
    /// from the hideout every bot is far away. So while the raid stands still, ORBIT's own update is
    /// held back as well. Nothing here is needed, or done, without ORBIT installed.
    /// </summary>
    public static class OrbitLink
    {
        private static bool _looked;
        public static bool Hooked;
        /// <summary>For the test harness: lets ORBIT run during the pause, to compare.</summary>
        public static bool LetRun;

        public static void Hook()
        {
            if (_looked)
            {
                return;
            }
            _looked = true;
            try
            {
                Type manager = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    manager = assembly.GetType("Orbit.Core.OrbitManager", false);
                    if (manager != null)
                    {
                        break;
                    }
                }
                var update = manager != null ? HarmonyLib.AccessTools.Method(manager, "Update", Type.EmptyTypes) : null;
                if (update == null)
                {
                    return;
                }
                new HarmonyLib.Harmony("com.r2rka1.zeromenutime.orbit").Patch(update,
                    prefix: new HarmonyLib.HarmonyMethod(typeof(OrbitLink), nameof(Hold)));
                Hooked = true;
                Plugin.Log.LogInfo("ORBIT found: its bots stand still too while the raid stands still");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("ORBIT could not be held back while the raid stands still: " + e.Message);
            }
        }

        /// <summary>False keeps ORBIT's update from running.</summary>
        public static bool Hold()
        {
            return !BotFreeze.On || LetRun;
        }
    }
}
