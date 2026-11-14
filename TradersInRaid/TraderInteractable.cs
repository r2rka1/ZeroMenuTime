using EFT.Interactive;
using EFT.UI;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>A spot in the world the player can look at and use to open a trader's screen.</summary>
    public class TraderInteractable : InteractableObject
    {
        public string TraderId;
        public string TraderName;

        /// <summary>The Vagabond extract this trader stands at, if any. Enables the "Leave raid" option.</summary>
        public ExfiltrationPoint Exfil;

        public AvailableInteractionState GetActions()
        {
            var state = new AvailableInteractionState();
            state.Actions.Add(new InteractionAction
            {
                Name = "Trade with " + TraderName,
                Action = () => TradeOpener.Open(TraderId, TraderName),
                Disabled = false,
            });
            state.Actions.Add(new InteractionAction
            {
                Name = "Collect deliveries",
                Action = () => Mail.Collect(TraderId, TraderName),
                Disabled = false,
            });
            if (Exfil != null)
            {
                var exfil = Exfil;
                state.Actions.Add(new InteractionAction
                {
                    Name = "Save and go to menu",
                    Action = () => VagabondLink.Leave(exfil),
                    Disabled = false,
                });
            }
            return state;
        }

        public static TraderInteractable Spawn(string name, string traderId, Vector3 position, float rotationY, bool visible)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TradersInRaid -- " + name;
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
