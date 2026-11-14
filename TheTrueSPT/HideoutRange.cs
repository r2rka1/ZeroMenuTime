using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.Hideout.ShootingRange;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TheTrueSPT
{
    /// <summary>
    /// The shooting range's controls. The game offers their actions only to its own hideout
    /// player; here the same actions are offered to the player in the raid, and the range's
    /// targets get power with the rest of the hideout.
    /// </summary>
    public static class HideoutRange
    {
        /// <summary>Gives the range power or takes it away, and wakes its controls.</summary>
        public static void Power(bool on)
        {
            try
            {
                foreach (var root in HideoutBunker.Roots())
                {
                    foreach (var control in root.GetComponentsInChildren<InteractiveShootingRange>(true))
                    {
                        control.Enable();
                        var poppers = control as PopperTargetControl;
                        if (poppers != null)
                        {
                            if (on) poppers.TurnOnEnergy(); else poppers.TurnOffEnergy();
                        }
                        var rail = control as RailTargetControl;
                        if (rail != null)
                        {
                            if (on) rail.TurnOnEnergy(); else rail.TurnOffEnergy();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Shooting range: " + e.Message);
            }
        }

        /// <summary>The actions for a control of the range, or null if the thing looked at is not one.</summary>
        public static AvailableInteractionState ActionsFor(object interactive)
        {
            var control = interactive as InteractiveShootingRange;
            if (control == null || HideoutDoors.Inside == null)
            {
                return null;
            }
            var state = new AvailableInteractionState();
            if (!HideoutPower.Powered && !(control is PaperTargetControl) && !(control is Turnstile))
            {
                state.Actions.Add(new InteractionAction { Name = "No power", Action = () => { }, Disabled = true });
                return state;
            }
            var poppers = control as PopperTargetControl;
            var rail = control as RailTargetControl;
            var paper = control as PaperTargetControl;
            var gate = control as Turnstile;
            if (poppers != null)
            {
                Add(state, "Start training", poppers.TurnOnTraining);
                Add(state, "Stop training", poppers.TurnOffTraining);
                Add(state, "Raise the targets", poppers.TurnOnPoppers);
                Add(state, "Lower the targets", poppers.TurnOffPoppers);
            }
            else if (rail != null)
            {
                Add(state, "Run the target", rail.Shooting);
                Add(state, "Stop", rail.Stop);
                Add(state, "Bring the target back", rail.Inspect);
                Add(state, "Fold the target", rail.Fold);
                Add(state, "Faster", rail.IncrementShootingSpeed);
            }
            else if (paper != null)
            {
                Add(state, "Hang new paper targets", paper.ReplaceTargets);
            }
            else if (gate != null)
            {
                Add(state, "Open", gate.Open);
                Add(state, "Close", gate.Close);
            }
            return state.Actions.Count > 0 ? state : null;
        }

        private static void Add(AvailableInteractionState state, string name, Action what)
        {
            state.Actions.Add(new InteractionAction
            {
                Name = name,
                Action = () =>
                {
                    try
                    {
                        what();
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning("Shooting range, " + name + ": " + e.Message);
                    }
                },
                Disabled = false,
            });
        }
    }

    /// <summary>Recording a best score assumes the game's own hideout session; without it, skip the record.</summary>
    public class RangeBestScorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(PopperTargetControl), nameof(PopperTargetControl.UpdateBestScore));
        }

        [PatchPrefix]
        public static bool Prefix()
        {
            return Singleton<HideoutRepresentation>.Instantiated;
        }
    }
}
