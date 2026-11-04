using GateRush.Game;
using GateRush.Input;
using GateRush.Race;
using GateRush.Track;
using GateRush.Vehicle;
using UnityEngine;

namespace GateRush.EditorTools.SphereDistrict
{
    /// <summary>
    /// E6 Play-mode flight test (driven from <c>unity command eval</c>): the autopilot flies the loaded track, following
    /// the flight line on air courses, and <see cref="Status"/> reports the phase, gate, lap times and rescues.
    /// </summary>
    public static class CourseFlightTest
    {
        public static string Attach(float cruiseSpeed)
        {
            var race = Object.FindAnyObjectByType<RaceSession>();
            var v = Object.FindAnyObjectByType<QuadVehicle>();
            if (race == null || v == null) return "no race";
            // Skip the track showdown: the autopilot flies at once.
            var intro = Object.FindAnyObjectByType<GateRush.Game.TrackIntro>();
            if (intro != null) intro.enabled = false;
            var src = v.gameObject.AddComponent<ScriptedInputSource>();
            v.InputSource = src;
            var ap = v.gameObject.AddComponent<RaceAutopilot>();
            ap.Configure(race, v, src, race.CourseGates);
            ap.CruiseSpeed = cruiseSpeed;
            var data = GameFlow.Resolve(GameFlow.Catalog, race.TrackId);
            if (data.Route.AirCourse)
            {
                var route = new TrackRoute(data.Route);
                ap.FollowLine(d => route.Sample(d).Position, route.Length);
            }
            return $"{race.TrackId}: {race.GateCount} gates, autopilot at {cruiseSpeed} m/s";
        }

        public static string Status()
        {
            var race = Object.FindAnyObjectByType<RaceSession>();
            if (race == null) return "no race";
            var ap = Object.FindAnyObjectByType<RaceAutopilot>();
            var t = race.Timer;
            var laps = "";
            for (int i = 0; i < t.CompletedLaps; i++) laps += t.LapTime(i).ToString("F1") + " ";
            return $"{race.Phase} lap {t.CurrentLap} next {t.NextCheckpoint}/{race.GateCount} laps [{laps.Trim()}] total {t.TotalTime(race.Now):F1} " +
                   $"rescues {(ap != null ? ap.Rescues : -1)} gaveup {(ap != null && ap.GaveUp)} crashes {race.Crashes} top {race.TopSpeed * 3.6f:F0} km/h {(ap != null ? ap.LastReport : "")}";
        }
    }
}
