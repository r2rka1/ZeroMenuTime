using System;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using UnityEngine;

namespace TradersInRaid
{
    /// <summary>
    /// Testing aid: writes to the log why the thing the player is looking at does or does not offer
    /// an interaction menu. Walks the same steps the game takes each frame.
    /// </summary>
    public static class InteractionProbe
    {
        public static void Run()
        {
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                var world = Singleton<GameWorld>.Instance;
                if (player == null || world == null)
                {
                    return;
                }
                string report = "state=" + player.CurrentState?.GetType().Name
                                + " stateCanInteract=" + (player.CurrentState?.CanInteract.ToString() ?? "?")
                                + " hands=" + (player.HandsController != null ? player.HandsController.GetType().Name : "null")
                                + " handsCanInteract=" + (player.HandsController != null ? player.HandsController.CanInteract().ToString() : "?")
                                + " handsBusy=" + player.HandsController?.IsInInteractionStrictCheck();

                RaycastHit hit;
                GameObject found = GameWorld.FindInteractable(player.InteractionRay, out hit);
                report += " | rayHit=" + (found != null ? found.name + " dist=" + hit.distance.ToString("F2") + " layer=" + LayerMask.LayerToName(found.layer) : "nothing");

                InteractableObject interactable = found != null ? found.GetComponentInParent<InteractableObject>() : null;
                report += " | interactable=" + (interactable != null ? interactable.GetType().Name + " enabled=" + interactable.isActiveAndEnabled : "none");
                report += " | current=" + (player.InteractableObject != null ? player.InteractableObject.GetType().Name : "none");

                if (interactable != null)
                {
                    try
                    {
                        var owner = player.GetComponent<GamePlayerOwner>();
                        var actions = InteractionContextHelper.GetAvailableActions(owner, interactable);
                        report += " | actions=" + (actions == null ? "null" : actions.Actions.Count + (actions.Error != null ? " error=" + actions.Error : ""));
                    }
                    catch (Exception e)
                    {
                        report += " | actions THREW " + e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0].Trim();
                    }
                }

                // How much real loot exists, and how near the closest pieces are.
                try
                {
                    Vector3 eye = player.InteractionRay.origin;
                    var loose = UnityEngine.Object.FindObjectsOfType<LootItem>();
                    var containers = UnityEngine.Object.FindObjectsOfType<LootableContainer>();
                    float nearestLoose = float.MaxValue, nearestContainer = float.MaxValue;
                    string nearestLooseName = "", nearestLooseLayer = "";
                    bool nearestLooseCollider = false;
                    foreach (var item in loose)
                    {
                        float d = Vector3.Distance(eye, item.transform.position);
                        if (d < nearestLoose)
                        {
                            nearestLoose = d;
                            nearestLooseName = item.name;
                            nearestLooseLayer = LayerMask.LayerToName(item.gameObject.layer);
                            var collider = item.GetComponentInChildren<Collider>();
                            nearestLooseCollider = collider != null && collider.enabled;
                        }
                    }
                    foreach (var container in containers)
                    {
                        nearestContainer = Mathf.Min(nearestContainer, Vector3.Distance(eye, container.transform.position));
                    }
                    report += " | looseLoot=" + loose.Length + " nearest=" + (loose.Length > 0 ? nearestLoose.ToString("F1") + "m '" + nearestLooseName + "' layer=" + nearestLooseLayer + " collider=" + nearestLooseCollider : "-")
                              + " | containers=" + containers.Length + " nearest=" + (containers.Length > 0 ? nearestContainer.ToString("F1") + "m" : "-");
                }
                catch (Exception e)
                {
                    report += " | loot scan failed: " + e.Message;
                }

                report += " | screenOpen=" + EFT.UI.Screens.EftScreenManager.Instance.CurrentScreenController?.GetType().Name;
                Plugin.Log.LogInfo("Interaction probe: " + report);
                NotificationManager.DisplayMessageNotification("Interaction probe written to the log.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Interaction probe failed: " + e);
            }
        }
    }
}
