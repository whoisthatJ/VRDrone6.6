using System;
using System.Text;
using System.Threading.Tasks;
using Ushqyr.Core;
#if USHQYR_CLOUD
using Unity.Services.Authentication;
using Unity.Services.Core;
#endif
using UnityEngine;

namespace Ushqyr.Net
{
    /// <summary>
    /// Unity Gaming Services start-up and anonymous sign-in. Only runs when the player opens an online feature (the
    /// game never connects by itself). <c>-online-profile NAME</c> signs in as a separate player, so two copies of the
    /// game on one PC can race each other.
    /// <para>
    /// The school build is made without the Unity services packages (EDU_PLAN F5: nothing leaves the classroom).
    /// There <see cref="Available"/> is false, nothing here can connect anywhere, and only LAN races are offered.
    /// </para>
    /// </summary>
    public static class OnlineServices
    {
#if USHQYR_CLOUD
        /// <summary>True when this build holds the internet features (races through Unity's servers, online boards).</summary>
        public static readonly bool Available = true;

        private static Task<bool> _signIn;

        public static bool SignedIn => UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;
        public static string PlayerId => SignedIn ? AuthenticationService.Instance.PlayerId : "";
        /// <summary>Last problem, for the menu ("" when fine).</summary>
        public static string LastError { get; private set; } = "";

        public static Task<bool> EnsureSignedInAsync()
        {
            if (SignedIn) return SyncNameAsync();
            if (_signIn == null || _signIn.IsCompleted) _signIn = SignInAsync();
            return _signIn;
        }

        private static async Task<bool> SignInAsync()
        {
            try
            {
                LastError = "";
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var options = new InitializationOptions();
                    string profile = Profile(Environment.GetCommandLineArgs());
                    if (!string.IsNullOrEmpty(profile)) options.SetProfile(profile);
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return await SyncNameAsync();
            }
            catch (Exception e)
            {
                LastError = Describe(e);
                Debug.LogWarning("[Ushqyr] Online sign-in failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Keeps the online player name equal to the callsign (shown in lobbies and leaderboards).</summary>
        private static async Task<bool> SyncNameAsync()
        {
            string wanted = PlayerName(SaveSystem.Settings.PilotName);
            string current = StripNumber(AuthenticationService.Instance.PlayerName);
            if (current == wanted) return true;
            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(wanted);
            }
            catch (Exception e)
            {
                // A name problem never blocks playing online.
                Debug.LogWarning("[Ushqyr] Could not set the online name: " + e.Message);
            }
            return true;
        }

#else
        public static readonly bool Available = false;
        public static bool SignedIn => false;
        public static string PlayerId => "";
        public static string LastError { get; private set; } = "";

        public static Task<bool> EnsureSignedInAsync()
        {
            LastError = L.T("This build has no internet features; LAN races work.");
            return Task.FromResult(false);
        }
#endif

        /// <summary><c>-online-profile NAME</c> from the command line, cleaned to what the services accept.</summary>
        public static string Profile(string[] args)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, "-online-profile", StringComparison.OrdinalIgnoreCase));
            if (i < 0 || i + 1 >= args.Length) return "";
            var sb = new StringBuilder();
            foreach (char c in args[i + 1])
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            return sb.Length > 30 ? sb.ToString(0, 30) : sb.ToString();
        }

        /// <summary>Online player names cannot contain spaces: "Red Fox" becomes "Red_Fox".</summary>
        public static string PlayerName(string callsign)
        {
            var sb = new StringBuilder();
            foreach (char c in callsign ?? "")
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            string s = sb.ToString().Trim('_');
            if (s.Length == 0) s = "Pilot";
            return s.Length > 30 ? s.Substring(0, 30) : s;
        }

        /// <summary>"Red_Fox#1234" → "Red Fox" (the services add a number to make names unique).</summary>
        public static string DisplayName(string playerName) => StripNumber(playerName).Replace('_', ' ');

        /// <summary>"Red_Fox#1234" → "Red_Fox".</summary>
        public static string StripNumber(string playerName)
        {
            if (string.IsNullOrEmpty(playerName)) return "";
            int hash = playerName.LastIndexOf('#');
            return hash > 0 ? playerName.Substring(0, hash) : playerName;
        }

        public static string Describe(Exception e)
        {
            string m = e.Message;
#if USHQYR_CLOUD
            if (e is AuthenticationException || e is RequestFailedException)
                return L.F("Online services unavailable ({0})", m);
#endif
            return m;
        }
    }
}
