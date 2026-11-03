using System;
using System.Text;
using System.Threading.Tasks;
using GateRush.Core;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace GateRush.Net
{
    /// <summary>
    /// Unity Gaming Services start-up and anonymous sign-in. Only runs when the player opens an online feature (the
    /// game never connects by itself). <c>-online-profile NAME</c> signs in as a separate player, so two copies of the
    /// game on one PC can race each other.
    /// </summary>
    public static class OnlineServices
    {
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
                Debug.LogWarning("[GateRush] Online sign-in failed: " + e.Message);
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
                Debug.LogWarning("[GateRush] Could not set the online name: " + e.Message);
            }
            return true;
        }

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
            if (e is AuthenticationException || e is RequestFailedException)
                return "Online services unavailable (" + m + ")";
            return m;
        }
    }
}
