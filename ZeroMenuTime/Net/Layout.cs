using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>What the host built its rooms and doors from. Every machine builds from the same.</summary>
    public class LayoutData
    {
        public bool Rooms = true;
        public float Depth = 300f;
        public bool Shell;
        /// <summary>The host's own doors.json entries (the doors built into the mod are the same everywhere).</summary>
        public List<TraderSpot> Doors = new List<TraderSpot>();
        public bool CustomSpots;
        public List<TraderSpot> Traders = new List<TraderSpot>();
        public List<HideoutSpot> Hideouts = new List<HideoutSpot>();
        /// <summary>Where the host's rooms and doors ended up, to compare against.</summary>
        public List<string> Built = new List<string>();

        /// <summary>Where the host has each trader (see TraderPlaces). Null from a host of an older version.</summary>
        public List<TraderPlace> Places;

        /// <summary>The map the host is on. A client's own idea of it can be an older one after a transit.</summary>
        public string Map = "";
    }

    /// <summary>
    /// Trader rooms, their doors and the hideout doors must stand at the same places on every
    /// machine, or two players in "the same room" are in different ones. They are built by the same
    /// code everywhere; what can differ is each player's own settings and files. So in co-op the
    /// host's settings and files are the ones used: a client asks for them before it builds anything.
    /// </summary>
    public static class Layout
    {
        /// <summary>On a client: what the host sent. Null alone and on the host: one's own settings count.</summary>
        public static LayoutData Active;

        /// <summary>The map this raid is on, as the raid-start build settled it (the host's word on a client).</summary>
        public static string Map = "";

        private static GameWorld _waiting;
        private static float _askedAt;

        public static bool RoomsOn => Active != null ? Active.Rooms : TraderRoom.Enabled.Value;
        public static float Depth => Active != null ? Active.Depth : TraderRoom.Depth.Value;
        public static bool Shell => Active != null ? Active.Shell : RoomProps.Use != null && RoomProps.Use.Value;
        public static bool CustomSpots => Active != null ? Active.CustomSpots : Plugin.UseCustomSpots.Value;

        /// <summary>This machine's own settings and files, as the host sends them.</summary>
        private static LayoutData Mine()
        {
            var data = new LayoutData
            {
                Rooms = TraderRoom.Enabled.Value, Depth = TraderRoom.Depth.Value, Shell = RoomProps.Use != null && RoomProps.Use.Value,
                CustomSpots = Plugin.UseCustomSpots.Value, Hideouts = HideoutDoors.Load(), Built = Built(), Places = TraderPlaces.Mine(),
            };
            var world = Comfort.Common.Singleton<GameWorld>.Instance;
            var me = EFT.GamePlayerOwner.MyPlayer;
            data.Map = (me != null ? me.Location : world != null ? world.LocationId : "") ?? "";
            try
            {
                if (File.Exists(TraderConfig.DoorsPath))
                {
                    data.Doors = JsonConvert.DeserializeObject<List<TraderSpot>>(File.ReadAllText(TraderConfig.DoorsPath)) ?? new List<TraderSpot>();
                }
                if (data.CustomSpots)
                {
                    data.Traders = TraderConfig.Load();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Layout: own door files not read: " + e.Message);
            }
            return data;
        }

        /// <summary>Where this machine's rooms and doors stand, as short lines that can be compared.</summary>
        public static List<string> Built()
        {
            var lines = new List<string>();
            foreach (var room in TraderRoom.Rooms)
            {
                lines.Add("room " + room.TraderId + " " + (room.Root != null ? room.Root.transform.position.ToString("F1") : "?")
                          + " door " + (room.OutsideDoor != null ? room.OutsideDoor.transform.position.ToString("F1") : "none"));
            }
            foreach (var front in HideoutDoors.Fronts)
            {
                lines.Add("hideout " + front.Key + " " + front.Value.Key.ToString("F1") + " " + front.Value.Value.ToString("F0"));
            }
            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        /// <summary>On a client at raid start: nothing is built until the host has said how. True when the build must wait.</summary>
        public static bool WaitForHost(GameWorld world)
        {
            if (Net.Role != Role.Client || Active != null)
            {
                return false;
            }
            _waiting = world;
            _askedAt = Time.unscaledTime;
            Net.Current.Send("zmt.layout?", new byte[0], false);
            Plugin.Log.LogInfo("Layout: asked the host how its rooms and doors are set");
            if (Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(GiveUp(world));
            }
            return true;
        }

        private static IEnumerator GiveUp(GameWorld world)
        {
            // Asked again a few times: the host may still be loading. Then built from one's own settings.
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSecondsRealtime(5f);
                if (!ReferenceEquals(_waiting, world))
                {
                    yield break;
                }
                Net.Current.Send("zmt.layout?", new byte[0], false);
            }
            if (ReferenceEquals(_waiting, world))
            {
                Plugin.Log.LogWarning("Layout: the host did not answer; rooms and doors built from this game's own settings");
                _waiting = null;
                SpawnTradersPatch.Build(world, true);
            }
        }

        /// <summary>On the host, once it has built: says so again, so clients that asked early can compare.</summary>
        public static void TellBuilt()
        {
            if (Net.Coop && Net.IsHost)
            {
                Net.Current.Send("zmt.layout", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Mine())), true);
            }
        }

        /// <summary>On the host: a client asks how to build.</summary>
        public static void OnAsked(byte[] body, string from)
        {
            if (!Net.IsHost)
            {
                return;
            }
            Net.Current.Send("zmt.layout", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Mine())), true);
        }

        /// <summary>On a client: the host says how to build.</summary>
        public static void OnTold(byte[] body, string from)
        {
            if (Net.Role != Role.Client)
            {
                return;
            }
            if (Active != null)
            {
                // Told again once the host has built: nothing to build, only to compare.
                if (!_compared)
                {
                    try
                    {
                        Compare(JsonConvert.DeserializeObject<LayoutData>(Encoding.UTF8.GetString(body)).Built);
                    }
                    catch (Exception)
                    {
                    }
                }
                return;
            }
            try
            {
                Active = JsonConvert.DeserializeObject<LayoutData>(Encoding.UTF8.GetString(body));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Layout: the host's answer could not be read: " + e.Message);
                return;
            }
            var world = _waiting;
            _waiting = null;
            if (world == null || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(world, Singleton<GameWorld>.Instance))
            {
                return;
            }
            Plugin.Log.LogInfo("Layout: got the host's settings after " + (Time.unscaledTime - _askedAt).ToString("F1") + " s; building");
            SpawnTradersPatch.Build(world, true);
            Compare(Active.Built);
        }

        private static bool _compared;

        private static void Compare(List<string> theirs)
        {
            var mine = Built();
            theirs = theirs ?? new List<string>();
            if (theirs.Count == 0)
            {
                Plugin.Log.LogInfo("Layout: built " + mine.Count + " place(s); the host has not built yet, nothing to compare");
                return;
            }
            _compared = true;
            int same = 0;
            foreach (string line in mine)
            {
                if (theirs.Contains(line))
                {
                    same++;
                }
                else
                {
                    Plugin.Log.LogWarning("Layout: differs from the host: " + line);
                }
            }
            Plugin.Log.LogInfo((same == mine.Count && mine.Count == theirs.Count ? "PASS" : "FAILED") + " Layout: " + same + " of " + mine.Count
                               + " place(s) stand where the host's do (the host has " + theirs.Count + ")");
        }

        public static void Reset()
        {
            Active = null;
            _waiting = null;
            _compared = false;
        }
    }
}
