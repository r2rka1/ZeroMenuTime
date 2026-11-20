using System;
using System.Linq;
using BepInEx;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.InventoryLogic;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using UnityEngine;

namespace ZeroMenuTime.Fika
{
    /// <summary>
    /// The part of Zero Menu Time that talks to Fika. Loaded only where Fika is installed.
    /// SPIKE state (stage 0 of plans/fika): one packet, an item laid in the world for everyone.
    /// </summary>
    [BepInPlugin("com.r2rka1.zeromenutime.fika", "r2rka1-ZeroMenuTime-Fika", "0.0.1")]
    [BepInDependency("com.fika.core")]
    [BepInDependency("com.r2rka1.zeromenutime")]
    public class FikaAddon : BaseUnityPlugin
    {
        private void Awake()
        {
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnManager);
            FikaSpike.PlaceForAll = PlaceForAll;
            // SPIKE S2: let a new player into a raid that has started.
            var harmony = new HarmonyLib.Harmony("com.r2rka1.zeromenutime.fika");
            foreach (string name in new[] { "OnConnectionRequest", "OnNetworkReceiveUnconnected" })
            {
                var target = HarmonyLib.AccessTools.Method(typeof(FikaServer), name);
                if (target == null)
                {
                    Logger.LogWarning("Late join: FikaServer." + name + " not found");
                    continue;
                }
                harmony.Patch(target, new HarmonyLib.HarmonyMethod(typeof(LateJoin), nameof(LateJoin.Before)), new HarmonyLib.HarmonyMethod(typeof(LateJoin), nameof(LateJoin.After)));
            }
            Logger.LogInfo("Zero Menu Time: Fika add-on loaded");
        }

        private void OnManager(FikaNetworkManagerCreatedEvent created)
        {
            var manager = created.Manager;
            if (manager is FikaServer)
            {
                // From a client: do it here, and pass it on to the other clients.
                manager.RegisterPacket<WorldItemPacket, NetPeer>((packet, peer) =>
                {
                    manager.SendData(ref packet, DeliveryMethod.ReliableOrdered, peer);
                    Place(packet);
                });
            }
            else
            {
                manager.RegisterPacket<WorldItemPacket>(Place);
            }
            Logger.LogInfo("Zero Menu Time: packets registered on " + manager.GetType().Name);
        }

        /// <summary>Lays an item in the world on this machine and tells every other machine to do the same.</summary>
        private static void PlaceForAll(Item item, Vector3 at)
        {
            var packet = new WorldItemPacket { Item = item, Position = at };
            Place(packet);
            Singleton<IFikaNetworkManager>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
        }

        private static async void Place(WorldItemPacket packet)
        {
            try
            {
                var world = Singleton<GameWorld>.Instance;
                var models = packet.Item.GetAllItems().SelectMany(part => part.Template.AllResources).ToArray();
                await Singleton<EFT.ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                    EFT.ObjectsFactory.PoolsCategory.Raid, EFT.ObjectsFactory.AssemblyType.Online, models, JobYieldPriority.Immediate);
                world.SetupItem(packet.Item, world.MainPlayer, packet.Position, Quaternion.identity);
                Plugin.Log.LogInfo("Fika add-on: item " + packet.Item.Id + " (" + packet.Item.TemplateId + ") laid at " + packet.Position.ToString("F1"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Fika add-on: laying an item failed: " + e);
            }
        }
    }

    public struct WorldItemPacket : INetSerializable
    {
        public Item Item;
        public Vector3 Position;

        public void Deserialize(NetDataReader reader)
        {
            Item = reader.GetItem();
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }

        public readonly void Serialize(NetDataWriter writer)
        {
            writer.PutItem(Item);
            writer.Put(Position.x);
            writer.Put(Position.y);
            writer.Put(Position.z);
        }
    }

    /// <summary>
    /// SPIKE S2. Fika's host refuses new players once the raid has started, in two places, both by
    /// reading the "raid started" flag. While either of them runs, the flag reads false.
    /// </summary>
    public static class LateJoin
    {
        private static System.Reflection.PropertyInfo _flag;
        private static object _controller;

        public static void Before(out bool __state)
        {
            __state = false;
            if (!FikaSpike.LateJoin)
            {
                return;
            }
            var game = Singleton<global::Fika.Core.Main.GameMode.IFikaGame>.Instance;
            if (game == null || game.GameController == null || !game.GameController.RaidStarted)
            {
                return;
            }
            _controller = game.GameController;
            _flag = HarmonyLib.AccessTools.Property(_controller.GetType(), "RaidStarted");
            _flag.SetValue(_controller, false);
            __state = true;
        }

        public static void After(bool __state)
        {
            if (__state)
            {
                _flag.SetValue(_controller, true);
            }
        }
    }
}
