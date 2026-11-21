using System.Text;
using EFT.Communications;

namespace ZeroMenuTime
{
    /// <summary>
    /// Everyone in a co-op raid must run the same version of this mod, or what one machine builds
    /// the other does not. A client says its version to the host when its raid starts; the host
    /// tells everyone its own; a machine that differs from the host switches the co-op features off.
    /// </summary>
    public static class Handshake
    {
        /// <summary>This machine's version differs from the host's.</summary>
        public static bool Mismatch;

        private static string Version => typeof(Plugin).Assembly.GetName().Version.ToString(3);

        public static void OnRaidStarted()
        {
            Mismatch = false;
            if (Net.Role == Role.Client)
            {
                Net.Current.Send("zmt.hello", Encoding.UTF8.GetBytes(Version), false);
            }
        }

        /// <summary>On the host: a client has said its version.</summary>
        public static void OnHello(byte[] body, string from)
        {
            string theirs = Encoding.UTF8.GetString(body);
            if (theirs != Version)
            {
                Plugin.Log.LogWarning("Co-op: player " + from + " runs Zero Menu Time " + theirs + ", this host runs " + Version);
                if (!Net.Headless)
                {
                    NotificationManager.DisplayWarningNotification("A player joined with a different Zero Menu Time version (" + theirs + ").", ENotificationDurationType.Long);
                }
            }
            else
            {
                Plugin.Log.LogInfo("Co-op: player " + from + " runs the same version (" + theirs + ")");
            }
            Net.Current.Send("zmt.version", Encoding.UTF8.GetBytes(Version), true);
        }

        /// <summary>On a client: the host has said its version.</summary>
        public static void OnVersion(byte[] body, string from)
        {
            string hosts = Encoding.UTF8.GetString(body);
            if (Net.Role != Role.Client || hosts == Version || Mismatch)
            {
                return;
            }
            Mismatch = true;
            Plugin.Log.LogWarning("Co-op: the host runs Zero Menu Time " + hosts + ", this game runs " + Version + "; co-op features are off");
            NotificationManager.DisplayWarningNotification("The host runs Zero Menu Time " + hosts + ", you run " + Version
                + ". Its doors and rooms are switched off for you in this raid.", ENotificationDurationType.Long);
        }
    }
}
