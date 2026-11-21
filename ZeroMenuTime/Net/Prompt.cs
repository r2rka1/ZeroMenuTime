using System;
using EFT;
using EFT.UI;
using UnityEngine;

namespace ZeroMenuTime
{
    /// <summary>
    /// A yes/no question. In the menu it is the game's own message window. In a raid that window
    /// cannot be used (with no screen open it answers yes by itself, and there is no mouse to click
    /// with), so the question is drawn across the screen and answered with the Y and N keys.
    /// It can also be answered without a key (by the test harness).
    /// </summary>
    public static class Prompt
    {
        public static string Text;
        private static string _caption = "";
        private static Action _yes, _no;
        private static object _window;
        private static bool _drawn;
        private static float _until;

        public static bool Ask(string text, Action yes, Action no, string caption, float seconds = 0f)
        {
            if (_yes != null && _drawn)
            {
                // One question at a time: the one on screen is answered no.
                Answer(false);
            }
            Text = text;
            _caption = caption ?? "";
            _yes = yes;
            _no = no;
            if (GamePlayerOwner.MyPlayer != null)
            {
                _drawn = true;
                _until = seconds > 0f ? Time.unscaledTime + seconds : float.MaxValue;
                _window = null;
                return true;
            }
            var ui = ItemUiContext.Instance;
            if (ui == null)
            {
                Forget();
                return false;
            }
            _drawn = false;
            Action accept = () => { Forget(); yes(); };
            Action decline = () => { Forget(); no(); };
            _window = seconds > 0f ? ui.ShowMessageWindow(text, accept, decline, caption, seconds) : ui.ShowMessageWindow(text, accept, decline, caption);
            return true;
        }

        private static void Forget()
        {
            Text = null;
            _yes = null;
            _no = null;
            _window = null;
            _drawn = false;
        }

        /// <summary>Answers the question on screen, as the key or a click would.</summary>
        public static string Answer(bool yes)
        {
            if (_yes == null)
            {
                return "nothing is asked";
            }
            string asked = Text;
            var act = yes ? _yes : _no;
            var window = _window;
            Forget();
            try
            {
                if (window != null)
                {
                    foreach (string name in new[] { "Close", "CloseSilent", "Hide" })
                    {
                        var method = window.GetType().GetMethod(name, Type.EmptyTypes);
                        if (method != null)
                        {
                            method.Invoke(window, null);
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            act();
            return "answered " + (yes ? "yes" : "no") + " to: " + (asked ?? "").Replace("\n", " ");
        }

        /// <summary>Called every frame: the keys, and a question nobody answered in time.</summary>
        public static void Tick()
        {
            if (!_drawn || _yes == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Y))
            {
                Answer(true);
            }
            else if (Input.GetKeyDown(KeyCode.N) || Time.unscaledTime >= _until)
            {
                Answer(false);
            }
        }

        /// <summary>Called from OnGUI: the question, drawn in a raid.</summary>
        public static void Draw()
        {
            if (!_drawn || _yes == null || Text == null)
            {
                return;
            }
            float width = Mathf.Min(Screen.width * 0.6f, 900f), height = Screen.height * 0.26f;
            var box = new Rect((Screen.width - width) / 2f, Screen.height * 0.14f, width, height);
            var before = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.07f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.77f, 0.63f, 0.28f, 1f);
            var head = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.Max(16, Screen.height / 42), fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(box.x, box.y + 10f, box.width, 40f), _caption, head);
            GUI.color = new Color(0.9f, 0.89f, 0.83f, 1f);
            var body = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.Max(14, Screen.height / 52), wordWrap = true };
            GUI.Label(new Rect(box.x + 24f, box.y + 52f, box.width - 48f, box.height - 100f), Text, body);
            string left = _until < float.MaxValue ? "   (" + Mathf.CeilToInt(Mathf.Max(0f, _until - Time.unscaledTime)) + " s)" : "";
            GUI.Label(new Rect(box.x, box.yMax - 44f, box.width, 36f), "Y  yes          N  no" + left, head);
            GUI.color = before;
        }
    }
}
