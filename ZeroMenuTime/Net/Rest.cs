using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EFT;
using EFT.Communications;
using EFT.UI;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// Sleeping and waiting move the clock, and in co-op the clock is everyone's. So nobody sleeps
    /// alone: one player asks, every other living player is asked to agree, and only when all have
    /// does the host move the time on and everyone rests. Sleeping also needs everyone in the hideout.
    /// </summary>
    public static class Rest
    {
        private const float VoteSeconds = 30f;

        // On the host: the vote under way.
        private static int _vote;
        private static string _kind = "";
        private static float _hours;
        private static string _asker = "";
        private static readonly HashSet<string> Waiting = new HashSet<string>();
        private static float _until;

        /// <summary>The local player asks to sleep ("sleep") or to wait until morning ("wait").</summary>
        public static void Ask(string kind, float hours)
        {
            NotificationManager.DisplayMessageNotification("Asking the others...", ENotificationDurationType.Default);
            Net.Current.Send("zmt.rest.ask", Encoding.UTF8.GetBytes(kind + " " + hours.ToString(CultureInfo.InvariantCulture)), false);
        }

        private static string NameOf(string profile)
        {
            foreach (var human in Net.Current.Humans())
            {
                if (human.ProfileId == profile)
                {
                    return human.Name;
                }
            }
            return "A player";
        }

        /// <summary>On the host: someone asks.</summary>
        public static void OnAsk(byte[] body, string from)
        {
            if (!Net.IsHost)
            {
                return;
            }
            string[] parts = Encoding.UTF8.GetString(body).Split(' ');
            if (parts.Length < 2)
            {
                return;
            }
            if (Waiting.Count > 0 && Time.unscaledTime < _until)
            {
                Say("zmt.rest.off", "Someone has already asked; answer that first.");
                return;
            }
            _kind = parts[0];
            _hours = float.Parse(parts[1], CultureInfo.InvariantCulture);
            _asker = from;
            Waiting.Clear();
            foreach (var human in Afterlife.Living())
            {
                if (_kind == "sleep" && Presence.WhereIs(human.ProfileId) != Presence.Hideout)
                {
                    Say("zmt.rest.off", human.Name + " is not in the hideout. Everyone has to be inside to sleep.");
                    return;
                }
                if (human.ProfileId != from)
                {
                    Waiting.Add(human.ProfileId);
                }
            }
            if (Waiting.Count == 0)
            {
                Do();
                return;
            }
            _vote++;
            _until = Time.unscaledTime + VoteSeconds;
            Say("zmt.rest.vote", _vote + " " + _kind + " " + _hours.ToString(CultureInfo.InvariantCulture) + " " + from);
            Plugin.Log.LogInfo("Rest: " + NameOf(from) + " asks to " + _kind + " " + _hours.ToString("0.#") + " h; waiting for " + Waiting.Count + " answer(s)");
        }

        /// <summary>From the host to everyone, itself included when it has a player.</summary>
        private static void Say(string kind, string text)
        {
            var body = Encoding.UTF8.GetBytes(text);
            Net.Current.Send(kind, body, true);
            if (!Net.Headless)
            {
                Net.Deliver(kind, body, "");
            }
        }

        /// <summary>On every player's machine: someone asks; answer yes or no.</summary>
        public static void OnVote(byte[] body, string from)
        {
            string[] parts = Encoding.UTF8.GetString(body).Split(' ');
            var me = GamePlayerOwner.MyPlayer;
            if (parts.Length < 4 || me == null || me.ProfileId == parts[3] || !me.HealthController.IsAlive)
            {
                return;
            }
            string id = parts[0];
            _lastAsked = id;
            string what = parts[1] == "sleep" ? "sleep for " : "wait for ";
            float hours = float.Parse(parts[2], CultureInfo.InvariantCulture);
            string text = NameOf(parts[3]) + " wants to " + what + hours.ToString("0.#") + " hours.\n\nTime moves on for everyone. Agree?";
            var ui = ItemUiContext.Instance;
            if (ui == null)
            {
                Answer(id, false);
                return;
            }
            ui.ShowMessageWindow(text, () => Answer(id, true), () => Answer(id, false), "REST", VoteSeconds - 2f);
        }

        private static string _lastAsked;

        /// <summary>For tests: answers the question now on screen without a click.</summary>
        public static string AnswerLast(bool yes)
        {
            if (_lastAsked == null)
            {
                return "nothing was asked";
            }
            Answer(_lastAsked, yes);
            string was = _lastAsked;
            _lastAsked = null;
            return "answered " + (yes ? "yes" : "no") + " to question " + was;
        }

        private static void Answer(string id, bool yes)
        {
            Net.Current.Send("zmt.rest.answer", Encoding.UTF8.GetBytes(id + " " + (yes ? "yes" : "no")), false);
        }

        /// <summary>On the host: an answer.</summary>
        public static void OnAnswer(byte[] body, string from)
        {
            string[] parts = Encoding.UTF8.GetString(body).Split(' ');
            if (!Net.IsHost || parts.Length < 2 || parts[0] != _vote.ToString() || !Waiting.Contains(from))
            {
                return;
            }
            if (parts[1] != "yes")
            {
                Waiting.Clear();
                Say("zmt.rest.off", NameOf(from) + " does not want to " + _kind + " now.");
                return;
            }
            Waiting.Remove(from);
            if (Waiting.Count == 0)
            {
                Do();
            }
        }

        /// <summary>On the host, every frame: a vote nobody finished runs out.</summary>
        public static void Tick()
        {
            if (Net.IsHost && Waiting.Count > 0 && Time.unscaledTime >= _until)
            {
                Waiting.Clear();
                Say("zmt.rest.off", "Not everyone answered in time.");
            }
        }

        private static void Do()
        {
            Waiting.Clear();
            WorldClock.Add(_hours * 3600.0);
            Plugin.Log.LogInfo("Rest: all agreed; the clock moved on " + _hours.ToString("0.#") + " h for everyone");
            Say("zmt.rest.do", _kind + " " + _hours.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>On every player's machine: it is agreed; rest.</summary>
        public static void OnDo(byte[] body, string from)
        {
            string[] parts = Encoding.UTF8.GetString(body).Split(' ');
            var me = GamePlayerOwner.MyPlayer;
            if (parts.Length < 2 || me == null || !me.HealthController.IsAlive)
            {
                return;
            }
            HideoutSleep.Agreed(parts[0], float.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        public static void OnOff(byte[] body, string from)
        {
            if (GamePlayerOwner.MyPlayer != null)
            {
                NotificationManager.DisplayWarningNotification(Encoding.UTF8.GetString(body), ENotificationDurationType.Long);
            }
        }
    }
}
