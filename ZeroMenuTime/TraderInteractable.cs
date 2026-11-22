using EFT.Interactive;
using EFT.UI;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>A spot in the world the player can look at and use to open a trader's screen.</summary>
    public class TraderInteractable : InteractableObject
    {
        public string TraderId;
        public string TraderName;

        /// <summary>The Vagabond extract this trader stands at, if any. Enables the "Leave raid" option.</summary>
        public ExfiltrationPoint Exfil;

        /// <summary>For a trader with no extract of their own: the way into the map nearest their door, where a save made here continues.</summary>
        public string SaveExit;

        /// <summary>Where that trader's door is on the map, to look for the way in again if none was there when the raid began.</summary>
        public Vector3? Door;

        public AvailableInteractionState GetActions()
        {
            var state = new AvailableInteractionState();
            state.Actions.Add(new InteractionAction
            {
                Name = "Trade with " + TraderName,
                Action = () => { if (Coop.Allowed(Coop.Feature.Trade)) { TradeOpener.Open(TraderId, TraderName); } },
                Disabled = false,
            });
            state.Actions.Add(new InteractionAction
            {
                Name = "Collect deliveries",
                Action = () => { if (Coop.Allowed(Coop.Feature.Deliveries)) { Mail.Collect(TraderId, TraderName); } },
                Disabled = false,
            });
            foreach (var ghost in Afterlife.Revivable())
            {
                string who = ghost.Key, name = ghost.Value;
                state.Actions.Add(new InteractionAction
                {
                    Name = "Revive " + name + " (" + Afterlife.ReviveCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " roubles)",
                    Action = () => { _ = Afterlife.ReviveForMoney(who, name); },
                    Disabled = false,
                });
            }
            if (TraderId == TraderConfig.Therapist)
            {
                int cost = Treatment.Cost();
                if (cost > 0)
                {
                    state.Actions.Add(new InteractionAction
                    {
                        Name = "Get treated (" + cost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " roubles)",
                        Action = () => { _ = Treatment.Buy(); },
                        Disabled = false,
                    });
                }
            }
            float untilMorning = HideoutSleep.HoursUntilMorning();
            state.Actions.Add(new InteractionAction
            {
                Name = "Wait until morning (" + untilMorning.ToString("0.#") + " hours)",
                Action = () => { if (Coop.Allowed(Coop.Feature.Wait)) { HideoutSleep.Wait(untilMorning); } },
                Disabled = false,
            });
            if (Exfil != null)
            {
                var exfil = Exfil;
                state.Actions.Add(new InteractionAction
                {
                    Name = "Save",
                    Action = () => { if (Coop.Allowed(Coop.Feature.Save)) { _ = SaveGame.SaveNow("at a trader", exfil.gameObject.name); } },
                    Disabled = false,
                });
                state.Actions.Add(new InteractionAction
                {
                    Name = "Save and go to menu",
                    Action = () => { if (Coop.Allowed(Coop.Feature.SaveAndLeave)) { VagabondLink.Leave(exfil); } },
                    Disabled = false,
                });
            }
            else if (Door.HasValue && string.IsNullOrEmpty(SaveExit))
            {
                SaveExit = VagabondLink.NearestWayIn(Comfort.Common.Singleton<EFT.GameWorld>.Instance, Door.Value);
            }
            if (Exfil == null && !string.IsNullOrEmpty(SaveExit))
            {
                string exit = SaveExit;
                state.Actions.Add(new InteractionAction
                {
                    Name = "Save",
                    Action = () => { if (Coop.Allowed(Coop.Feature.Save)) { _ = SaveGame.SaveNow("at a trader", exit); } },
                    Disabled = false,
                });
            }
            return state;
        }

        public static TraderInteractable Spawn(string name, string traderId, Vector3 position, float rotationY, bool visible)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ZeroMenuTime -- " + name;
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
            // Sized to a standing person, so looking anywhere at the figure (head to feet) offers the menu.
            go.transform.localScale = new Vector3(0.7f, 1.8f, 0.7f);
            go.layer = LayerMask.NameToLayer("Interactive");

            var interactable = go.AddComponent<TraderInteractable>();
            interactable.TraderId = traderId;
            interactable.TraderName = name;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = visible;
                if (renderer.material != null)
                {
                    renderer.material.color = new Color(0.20f, 0.35f, 0.20f);
                }
            }

            var box = go.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.enabled = true;
            }
            go.SetActive(true);
            Spawned.Add(go);
            return interactable;
        }

        private static readonly System.Collections.Generic.List<GameObject> Spawned = new System.Collections.Generic.List<GameObject>();

        /// <summary>Remove every trader box this build placed (boxes from an ended raid are already gone).</summary>
        public static void DestroyAll()
        {
            foreach (var go in Spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            Spawned.Clear();
            TraderFigure.DestroyAll();
            TraderRoom.DestroyAll();
        }
    }
}
