using System;
using System.Collections.Generic;
using System.Threading;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using JsonType;
using UnityEngine;

namespace TheTrueSPT
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

        // Clothing and heads picked from the game's own customization data, chosen to come as close
        // to each trader as the catalogue allows. Only Savage-side pieces are used, because that is
        // the side the figure is built on. The game's real trader models are not used: they belong
        // to the game's makers and cannot be shipped with a mod.
        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            // An army warrant officer, in the uniform the Gluhar character wears.
            { TraderConfig.Prapor, new Look { Body = "5d5e7dd786f7744a7a274322", Feet = "5d5e7f2a86f77427997cfb80", Head = "5d5e805d86f77439eb4c2d0e" } },
            // The doctor's whites from the Sanitar character; the catalogue has no women.
            { TraderConfig.Therapist, new Look { Body = "5e9975c486f774382b6320cb", Feet = "5e99762586f77466320ff9a9", Head = "5e99767c86f7741ac7399393" } },
            // A gangster's leathers for the smuggler.
            { TraderConfig.Skier, new Look { Body = "65749b8fe32c4e3f7907bf42", Feet = "65749c05e0423b9ebe0c79c7", Head = "65749be133fdc9c0cf06d3c8" } },
            // Western kit for the foreign serviceman.
            { TraderConfig.Peacekeeper, new Look { Body = "628796f2af0a053a6672c29a", Feet = "628771f70828252c7a28b961", Head = "62875ad50828252c7a28b95c" } },
            // A worker's bomber jacket and a hard, lined face.
            { TraderConfig.Mechanic, new Look { Body = "619238266c614e6d15792bca", Feet = "6193be546e5968395b260157", Head = "5f68c4a7c174a17c0f4c8945" } },
            // The yellow coat suits the man who sells clothes.
            { TraderConfig.Ragman, new Look { Body = "618d1af10a5a59657e5f56f3", Feet = "61922589bb2cea6f7a22d964", Head = "65749bcce32c4e3f7907bf44" } },
            // A woodsman, from the Partizan character.
            { TraderConfig.Jaeger, new Look { Body = "66aa5d36f641b13ad238f8c3", Feet = "66aa5bba10d2d4006a3c4a84", Head = "66aa5ca23d22c9416c010be0" } },
            // The scav the game itself calls the dealmaker.
            { TraderConfig.Fence, new Look { Body = "5d28adcb86f77429242fc893", Feet = "5d28b3a186f7747f7e69ab8c", Head = "5d28b01486f77429242fc898" } },
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

                // The trader's own model, when it has been built from the player's copy of the game.
                if (TraderModelBundle.TryPlace(traderId, traderName, ground, rotationY, markerBox))
                {
                    return;
                }

                var anchor = new GameObject("TheTrueSPT figure -- " + traderName);
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
            TraderModelBundle.DestroyAll();
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
