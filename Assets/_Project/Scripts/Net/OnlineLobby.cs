using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace GateRush.Net
{
    /// <summary>An open race in the list.</summary>
    public struct OpenRace
    {
        public string Id;
        public string Name;
        public string TrackName;
        public int Pilots;
        public int MaxPilots;
    }

    /// <summary>
    /// Finding and hosting online races (up to <see cref="OnlineRules.MaxPilots"/> pilots). The host's PC runs the race
    /// and Unity brokers the connection, so it works behind home routers. Races are listed while in the lobby and
    /// locked while racing. Joining also starts the race network (<see cref="NetRace"/>).
    /// </summary>
    public static class OnlineLobby
    {
        /// <summary>Game tag on every race; bump when the protocol changes so old builds don't meet new ones.</summary>
        public const string GameTag = "gaterush-1";
        private const string KeyGame = "game", KeyState = "state", KeyTrack = "track", KeyTrackName = "trackName";
        private const string StateLobby = "lobby", StateRacing = "racing";

        public static ISession Current { get; private set; }
        public static bool InSession => Current != null;
        public static bool IsHost => Current != null && Current.IsHost;
        public static string Code => Current?.Code ?? "";
        public static string Name => Current?.Name ?? "";
        public static int PlayerCount => Current?.PlayerCount ?? 0;
        public static int MaxPlayers => Current?.MaxPlayers ?? OnlineRules.MaxPilots;
        public static string LastError { get; private set; } = "";

        /// <summary>The session changed (players, track) — refresh the lobby page.</summary>
        public static event Action Changed;
        /// <summary>We are no longer in the session; the argument says why.</summary>
        public static event Action<string> Ended;

        public static string TrackId => Prop(KeyTrack);
        public static string TrackName => Prop(KeyTrackName);

        public static async Task<bool> CreateAsync(string trackId, string trackName, bool isPrivate)
        {
            return await Run(async () =>
            {
                var options = SessionOptions(trackId, trackName);
                options.IsPrivate = isPrivate;
                return await MultiplayerService.Instance.CreateSessionAsync(options);
            });
        }

        /// <summary>Joins any open race; hosts a public one on <paramref name="trackId"/> when there is none.</summary>
        public static async Task<bool> QuickRaceAsync(string trackId, string trackName)
        {
            return await Run(async () =>
            {
                var quick = new QuickJoinOptions { Filters = OpenFilters(), CreateSession = true };
                return await MultiplayerService.Instance.MatchmakeSessionAsync(quick, SessionOptions(trackId, trackName));
            });
        }

        public static async Task<bool> JoinByCodeAsync(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                LastError = "Type the race code first.";
                return false;
            }
            return await Run(async () => await MultiplayerService.Instance.JoinSessionByCodeAsync(code, JoinOptions()));
        }

        public static async Task<bool> JoinAsync(string sessionId)
        {
            return await Run(async () => await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, JoinOptions()));
        }

        /// <summary>Public races waiting in their lobby with a free slot, newest first.</summary>
        public static async Task<List<OpenRace>> ListAsync()
        {
            var list = new List<OpenRace>();
            if (!await OnlineServices.EnsureSignedInAsync())
            {
                LastError = OnlineServices.LastError;
                return list;
            }
            try
            {
                var query = new QuerySessionsOptions
                {
                    Count = 30,
                    FilterOptions = OpenFilters(),
                    SortOptions = new List<SortOption> { new SortOption(SortOrder.Descending, SortField.CreationTime) },
                };
                var results = await MultiplayerService.Instance.QuerySessionsAsync(query);
                foreach (var s in results.Sessions)
                {
                    s.Properties.TryGetValue(KeyTrackName, out var track);
                    list.Add(new OpenRace
                    {
                        Id = s.Id,
                        Name = s.Name,
                        TrackName = track?.Value ?? "",
                        Pilots = s.MaxPlayers - s.AvailableSlots,
                        MaxPilots = s.MaxPlayers,
                    });
                }
                LastError = "";
            }
            catch (Exception e)
            {
                LastError = OnlineServices.Describe(e);
            }
            return list;
        }

        /// <summary>Host: changes the track shown in the lobby and the list.</summary>
        public static async Task SetTrackAsync(string trackId, string trackName)
        {
            if (!IsHost) return;
            var host = Current.AsHost();
            host.SetProperties(new Dictionary<string, SessionProperty>
            {
                [KeyTrack] = new SessionProperty(trackId, VisibilityPropertyOptions.Public, PropertyIndex.String3),
                [KeyTrackName] = new SessionProperty(trackName ?? trackId),
            });
            await Save(host);
        }

        /// <summary>Host: hides the race from the list and stops new pilots joining while racing.</summary>
        public static async Task SetRacingAsync(bool racing)
        {
            if (!IsHost) return;
            var host = Current.AsHost();
            host.IsLocked = racing;
            host.SetProperties(new Dictionary<string, SessionProperty>
            {
                [KeyState] = new SessionProperty(racing ? StateRacing : StateLobby, VisibilityPropertyOptions.Public, PropertyIndex.String2),
            });
            await Save(host);
        }

        public static async Task LeaveAsync()
        {
            var s = Current;
            if (s == null) return;
            Detach(s);
            Current = null;
            try
            {
                await s.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GateRush] Leaving the race: " + e.Message);
            }
            if (NetRace.Instance != null) NetRace.Instance.ResetState();
        }

        /// <summary>Pilots in the session (names from the services, before the race network knows them).</summary>
        public static List<string> PlayerNames()
        {
            var names = new List<string>();
            if (Current == null) return names;
            foreach (var p in Current.Players)
            {
                string n = OnlineServices.DisplayName(p.GetPlayerName());
                names.Add(string.IsNullOrEmpty(n) ? "Pilot" : n);
            }
            return names;
        }

        // ---------- internals ----------

        private static async Task<bool> Run(Func<Task<ISession>> join)
        {
            LastError = "";
            if (Current != null) await LeaveAsync();
            if (!await OnlineServices.EnsureSignedInAsync())
            {
                LastError = OnlineServices.LastError;
                return false;
            }
            NetRace.Ensure();
            try
            {
                var s = await join();
                Attach(s);
                return true;
            }
            catch (Exception e)
            {
                LastError = e is SessionException se ? Friendly(se) : OnlineServices.Describe(e);
                Debug.LogWarning("[GateRush] Online race: " + e);
                return false;
            }
        }

        private static string Friendly(SessionException e)
        {
            string m = e.Error.ToString();
            if (m.Contains("NotFound")) return "No race with that code (it may have started or closed).";
            if (m.Contains("Full")) return "That race is full.";
            if (m.Contains("Locked")) return "That race has already started.";
            return "Could not join the race (" + m + ").";
        }

        private static SessionOptions SessionOptions(string trackId, string trackName)
        {
            string pilot = OnlineServices.DisplayName(OnlineServices.PlayerName(Core.SaveSystem.Settings.PilotName));
            return new SessionOptions
            {
                Name = pilot + "'s race",
                MaxPlayers = OnlineRules.MaxPilots,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    [KeyGame] = new SessionProperty(GameTag, VisibilityPropertyOptions.Public, PropertyIndex.String1),
                    [KeyState] = new SessionProperty(StateLobby, VisibilityPropertyOptions.Public, PropertyIndex.String2),
                    [KeyTrack] = new SessionProperty(trackId ?? "", VisibilityPropertyOptions.Public, PropertyIndex.String3),
                    [KeyTrackName] = new SessionProperty(trackName ?? trackId ?? ""),
                },
            }.WithPlayerName().WithRelayNetwork();
        }

        private static JoinSessionOptions JoinOptions() => new JoinSessionOptions().WithPlayerName();

        private static List<FilterOption> OpenFilters() => new List<FilterOption>
        {
            new FilterOption(FilterField.StringIndex1, GameTag, FilterOperation.Equal),
            new FilterOption(FilterField.StringIndex2, StateLobby, FilterOperation.Equal),
            new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater),
        };

        private static string Prop(string key) =>
            Current != null && Current.Properties.TryGetValue(key, out var p) ? p.Value : "";

        private static async Task Save(IHostSession host)
        {
            try
            {
                await host.SavePropertiesAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GateRush] Saving the race settings: " + e.Message);
            }
        }

        private static void Attach(ISession s)
        {
            Current = s;
            s.Changed += OnChanged;
            s.PlayerJoined += OnPlayer;
            s.PlayerHasLeft += OnPlayer;
            s.RemovedFromSession += OnRemoved;
            s.Deleted += OnDeleted;
        }

        private static void Detach(ISession s)
        {
            s.Changed -= OnChanged;
            s.PlayerJoined -= OnPlayer;
            s.PlayerHasLeft -= OnPlayer;
            s.RemovedFromSession -= OnRemoved;
            s.Deleted -= OnDeleted;
        }

        private static void OnChanged() => Changed?.Invoke();
        private static void OnPlayer(string _) => Changed?.Invoke();
        private static void OnRemoved() => End("You left the race.");
        private static void OnDeleted() => End("The host closed the race.");

        private static void End(string reason)
        {
            if (Current == null) return;
            Detach(Current);
            Current = null;
            if (NetRace.Instance != null) NetRace.Instance.ResetState();
            Ended?.Invoke(reason);
        }
    }
}
