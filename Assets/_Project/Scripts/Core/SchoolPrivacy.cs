#if USHQYR_SCHOOL
using System;
using System.Reflection;
using UnityEngine;

namespace Ushqyr.Core
{
    /// <summary>
    /// The school build tells the engine, before anything else runs, not to send its own statistics (EDU_PLAN F5:
    /// nothing leaves the classroom). These are the switches Unity documents for a running program: analytics off,
    /// device statistics off, performance reporting off. They are set by name, because the modules that hold them are
    /// not part of the project while the school build is made.
    /// </summary>
    public static class SchoolPrivacy
    {
        /// <summary>What was switched, for the log ("Analytics.enabled=False, …").</summary>
        public static string Report { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void SwitchOff()
        {
            var done = new System.Text.StringBuilder();
            Set(done, "UnityEngine.Analytics.Analytics, UnityEngine.UnityAnalyticsModule", "initializeOnStartup", false);
            Set(done, "UnityEngine.Analytics.Analytics, UnityEngine.UnityAnalyticsModule", "enabled", false);
            Set(done, "UnityEngine.Analytics.Analytics, UnityEngine.UnityAnalyticsModule", "deviceStatsEnabled", false);
            Set(done, "UnityEngine.Analytics.Analytics, UnityEngine.UnityAnalyticsModule", "limitUserTracking", true);
            Set(done, "UnityEngine.Analytics.PerformanceReporting, UnityEngine.PerformanceReportingModule", "enabled", false);
            Set(done, "UnityEngine.CrashReportHandler.CrashReportHandler, UnityEngine.CrashReportingModule", "enableCaptureExceptions", false);
            Report = done.ToString();
            Debug.Log("[Ushqyr] School build, engine reporting: " + (Report.Length > 0 ? Report : "no reporting module in this program"));
        }

        private static void Set(System.Text.StringBuilder done, string typeName, string property, bool value)
        {
            try
            {
                PropertyInfo p = Type.GetType(typeName)?.GetProperty(property, BindingFlags.Static | BindingFlags.Public);
                if (p == null || !p.CanWrite) return;
                p.SetValue(null, value);
                if (done.Length > 0) done.Append(", ");
                done.Append(p.DeclaringType.Name).Append('.').Append(property).Append('=').Append(p.GetValue(null));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ushqyr] " + typeName + "." + property + ": " + (e.InnerException ?? e).Message);
            }
        }
    }
}
#endif
