using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// Reads Vagabond's trader extracts so our traders stand at the same places, and turns those
    /// extracts from "walk in and you leave" into a choice the player makes at the trader.
    /// </summary>
    public static class VagabondLink
    {
        public class TraderExtract
        {
            public string TraderId;
            public string ExfilIdentifier;
        }

        /// <summary>Extract points that belong to a trader in this raid (by Unity instance id).</summary>
        public static readonly HashSet<int> TraderExfils = new HashSet<int>();

        /// <summary>Trader extracts the player has chosen to leave through.</summary>
        public static readonly HashSet<int> LeaveRequested = new HashSet<int>();

        public static string ConfigPath =>
            Path.Combine(BepInEx.Paths.GameRootPath, "SPT_Runtime", "user", "mods", "Vagabond", "config", "trader_locations.json");

        public static BepInEx.Configuration.ConfigEntry<bool> RemoveHideoutExits;

        /// <summary>
        /// Vagabond lets the player set up exits that lead to its hideout in the menus. With this
        /// mod the hideout is behind a door on the map, so those exits lead nowhere useful; they are
        /// switched off. The trader exits stay: the traders' doors stand at them.
        /// </summary>
        public static int SwitchOffHideoutExits(GameWorld world)
        {
            int off = 0;
            try
            {
                var points = world?.ExfiltrationController?.ExfiltrationPoints;
                if (points == null || RemoveHideoutExits == null || !RemoveHideoutExits.Value)
                {
                    return 0;
                }
                foreach (var point in points)
                {
                    if (point != null && point.gameObject.name.StartsWith("VGB_HO_", StringComparison.OrdinalIgnoreCase) && point.gameObject.activeSelf)
                    {
                        point.Disable();
                        point.gameObject.SetActive(false);
                        off++;
                    }
                }
                if (off > 0)
                {
                    Plugin.Log.LogInfo("Vagabond's hideout exits switched off: " + off);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Switching off Vagabond's hideout exits failed: " + e.Message);
            }
            return off;
        }

        public static void Reset()
        {
            TraderExfils.Clear();
            LeaveRequested.Clear();
        }

        /// <summary>
        /// Vagabond's trader_locations.json. It lives in the server's mod folder, which is often not
        /// inside the game's folder, so the server is asked for it first; the file beside the game is
        /// only a fallback for when the server cannot answer.
        /// </summary>
        private static string ReadTraderLocations()
        {
            string why = "";
            try
            {
                var reply = JObject.Parse(SPT.Common.Http.RequestHandler.PostJson("/zeromenutime/vagabond/traders", "{}"));
                if ((bool?)reply["Ok"] ?? false)
                {
                    Plugin.Log.LogInfo("Vagabond's trader list read by the server from " + (string)reply["Path"]);
                    return (string)reply["Json"] ?? "";
                }
                why = (string)reply["Error"] ?? "no answer";
            }
            catch (Exception e)
            {
                why = e.GetType().Name + ": " + e.Message;
            }
            if (File.Exists(ConfigPath))
            {
                return File.ReadAllText(ConfigPath);
            }
            Plugin.Log.LogError("Vagabond's trader list could not be read, so no trader doors are placed. Server: " + why
                                + "; not beside the game either (" + ConfigPath + ")");
            return null;
        }

        public static List<TraderExtract> LoadTraderExtracts()
        {
            var result = new List<TraderExtract>();
            try
            {
                string json = ReadTraderLocations();
                if (string.IsNullOrEmpty(json))
                {
                    return result;
                }
                // Vagabond's config has // comments; JArray.Parse skips them.
                foreach (var entry in JArray.Parse(json))
                {
                    var traderId = (string)entry["traderId"];
                    var exfil = (string)entry["exfilIdentifier"];
                    if (!string.IsNullOrEmpty(traderId) && !string.IsNullOrEmpty(exfil))
                    {
                        result.Add(new TraderExtract { TraderId = traderId, ExfilIdentifier = exfil });
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read Vagabond trader_locations.json: " + e);
            }
            return result;
        }

        /// <summary>Find a Vagabond extract in the loaded map by its identifier.</summary>
        public static ExfiltrationPoint FindExfil(GameWorld world, string identifier)
        {
            var points = world?.ExfiltrationController?.ExfiltrationPoints;
            if (points == null)
            {
                return null;
            }
            foreach (var point in points)
            {
                if (point != null && string.Equals(point.gameObject.name, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return point;
                }
            }
            return null;
        }

        /// <summary>Start the normal extraction countdown at a trader's extract.</summary>
        public static void Leave(ExfiltrationPoint exfil)
        {
            var player = GamePlayerOwner.MyPlayer;
            if (exfil == null || player == null)
            {
                return;
            }
            // Leaving at a trader is the "save and quit to menu" choice: the next raid does not auto-start,
            // and the server keeps the character as it leaves as the save.
            if (Net.Coop)
            {
                // In co-op the game's extraction does not take the player out; the co-op layer does.
                string why;
                Seamless.SaveAndMenuRequested = true;
                _ = SaveGame.TraderSave(exfil.gameObject.name);
                if (!Net.Current.LeaveRaid(exfil.gameObject.name, out why))
                {
                    Seamless.SaveAndMenuRequested = false;
                    NotificationManager.DisplayWarningNotification("You cannot leave now: " + why + ".", ENotificationDurationType.Long);
                    Plugin.Log.LogInfo("Leave refused at " + exfil.gameObject.name + ": " + why);
                    return;
                }
                Plugin.Log.LogInfo("Leave requested at " + exfil.gameObject.name + " (co-op)");
                return;
            }
            Seamless.SaveAndMenuRequested = true;
            _ = SaveGame.TraderSave(exfil.gameObject.name);
            LeaveRequested.Add(exfil.GetInstanceID());
            if (!exfil.Entered.Contains(player))
            {
                exfil.Entered.Add(player);
                exfil.Proceed(player);
            }
            Plugin.Log.LogInfo("Leave requested at " + exfil.gameObject.name);
        }
    }

    /// <summary>Walking into a trader's extract no longer starts the countdown by itself.</summary>
    public class TraderExfilEnterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(ExfiltrationPoint).GetMethod("IPhysicsTrigger.OnTriggerEnter",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [PatchPrefix]
        public static bool Prefix(ExfiltrationPoint __instance, Collider col)
        {
            int id = __instance.GetInstanceID();
            if (!VagabondLink.TraderExfils.Contains(id) || VagabondLink.LeaveRequested.Contains(id))
            {
                return true;
            }
            var world = Singleton<GameWorld>.Instance;
            var player = world?.GetPlayerByCollider(col);
            // Only hold back the human player; leave everything else to the game.
            return !(player != null && ReferenceEquals(player, world.MainPlayer));
        }
    }

    /// <summary>Stepping out of the extract cancels a requested leave, so you can come back and trade.</summary>
    public class TraderExfilExitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(ExfiltrationPoint).GetMethod("IPhysicsTrigger.OnTriggerExit",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [PatchPostfix]
        public static void Postfix(ExfiltrationPoint __instance, Collider col)
        {
            var world = Singleton<GameWorld>.Instance;
            var player = world?.GetPlayerByCollider(col);
            if (player != null && ReferenceEquals(player, world.MainPlayer))
            {
                VagabondLink.LeaveRequested.Remove(__instance.GetInstanceID());
            }
        }
    }
}
