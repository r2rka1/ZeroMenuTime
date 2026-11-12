using System;
using System.Collections.Generic;
using System.Threading;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using JsonType;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// A standing character at a trader's spot. It is built with the same loader the game uses for the
    /// character preview in the inventory screen, so it is a posed model, not a live bot: it does not
    /// move, cannot be shot and nobody reacts to it.
    /// </summary>
    public static class TraderFigure
    {
        private class Look
        {
            public string Body, Feet, Head;
        }

        // Scav clothing and heads from the game's own customization data.
        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            { TraderConfig.Prapor, new Look { Body = "5d28adfd86f774292364a6e5", Feet = "5d5e7f8986f7742798716582", Head = "5cc2e4d014c02e000d0115f8" } },
            { TraderConfig.Therapist, new Look { Body = "5e9975c486f774382b6320cb", Feet = "5e99762586f77466320ff9a9", Head = "5cde9ff17d6c8b0474535daa" } },
            { TraderConfig.Skier, new Look { Body = "5df8a10486f77412672a1e3a", Feet = "5f5e410c6bdad616ad46d60b", Head = "5d28afe786f774292668618d" } },
            { TraderConfig.Peacekeeper, new Look { Body = "66bf6769f08c35734d4940c4", Feet = "66bf6885952b42739a5f2298", Head = "5f68c4c217d579077152a252" } },
            { TraderConfig.Mechanic, new Look { Body = "5cc2e59214c02e000f16684e", Feet = "5cc2e5d014c02e15d53d9c03", Head = "5f68c4a7c174a17c0f4c8945" } },
            { TraderConfig.Ragman, new Look { Body = "618d1af10a5a59657e5f56f3", Feet = "61922589bb2cea6f7a22d964", Head = "5cc2e4d014c02e000d0115f8" } },
            { TraderConfig.Jaeger, new Look { Body = "5d5e7e7586f77427997cfb7f", Feet = "5d5e7f4986f7746956659f8a", Head = "5cde9ff17d6c8b0474535daa" } },
            { TraderConfig.Fence, new Look { Body = "637df28ac22da5bb8d046935", Feet = "637df25a1e688345e1097bd4", Head = "5d28afe786f774292668618d" } },
        };

        private const string DefaultInventoryTpl = "55d7217a4bdc2d86028b456d";

        private static readonly List<PlayerModelLoader> Loaders = new List<PlayerModelLoader>();
        private static readonly List<GameObject> Anchors = new List<GameObject>();

        /// <summary>Stand a figure at a spot on the ground. When it is ready, the marker box is hidden.</summary>
        public static async void Spawn(string traderId, string traderName, Vector3 ground, float rotationY, GameObject markerBox)
        {
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                if (player == null)
                {
                    return;
                }

                var anchor = new GameObject("TradersInRaid figure -- " + traderName);
                anchor.transform.position = ground;
                anchor.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
                Anchors.Add(anchor);

                // Start from the player's own look so every body part has a valid entry, then change clothes and head.
                var customization = new BodyCustomization(player.Profile.Customization);
                Look look;
                if (Looks.TryGetValue(traderId, out look))
                {
                    customization[EBodyModelPart.Body] = look.Body;
                    customization[EBodyModelPart.Feet] = look.Feet;
                    customization[EBodyModelPart.Head] = look.Head;
                }

                var equipment = (InventoryEquipment)Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), DefaultInventoryTpl, null);
                var info = new PlayerInfo { Nickname = traderName, Side = EPlayerSide.Savage, Level = 1 };
                var visual = new PlayerVisualRepresentation(info, customization, equipment);

                var loader = new PlayerModelLoader();
                Loaders.Add(loader);
                await loader.Load(visual, null, 0f, ThirdPersonCustomizationFilter.Default, null, CancellationToken.None,
                    Vector3.zero, anchor.transform, LayerMask.NameToLayer("Player"), false, null, false);

                if (loader.PlayerBody == null)
                {
                    Plugin.Log.LogWarning("Trader figure for " + traderName + " did not load; keeping the marker box");
                    return;
                }

                // The loader turns the model to face the camera of the preview screen; face the way the spot faces.
                loader.ModelPlayerPoser.transform.localRotation = Quaternion.identity;
                if (markerBox != null)
                {
                    var renderer = markerBox.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                }
                Plugin.Log.LogInfo("Trader figure for " + traderName + " placed at " + ground.ToString("F1"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Trader figure for " + traderName + " failed: " + e);
            }
        }

        public static void DestroyAll()
        {
            foreach (var loader in Loaders)
            {
                try
                {
                    loader.Clear();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Clearing a trader figure failed: " + e.Message);
                }
            }
            Loaders.Clear();
            foreach (var anchor in Anchors)
            {
                if (anchor != null)
                {
                    UnityEngine.Object.Destroy(anchor);
                }
            }
            Anchors.Clear();
        }
    }
}
