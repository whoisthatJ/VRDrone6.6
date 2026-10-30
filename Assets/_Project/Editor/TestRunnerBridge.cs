using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace GateRush.EditorTools
{
    /// <summary>
    /// Runs EditMode tests inside the open Editor (the CLI's batchmode runner can't open a project that's already open)
    /// and writes a summary plus JUnit-style XML to TestResults/.
    /// </summary>
    public static class TestRunnerBridge
    {
        public const string SummaryPath = "TestResults/editmode-summary.txt";
        public const string JUnitPath = "TestResults/editmode.xml";

        [MenuItem("GateRush/Tests/Run EditMode Tests")]
        public static string RunEditMode() => RunEditMode("GateRush.Tests.EditMode");

        public static string RunEditMode(string assemblyName)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var collector = new Collector();
            api.RegisterCallbacks(collector);
            try
            {
                var filter = new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { assemblyName } };
                api.Execute(new ExecutionSettings(filter) { runSynchronously = true });
            }
            finally
            {
                api.UnregisterCallbacks(collector);
            }
            return collector.Summary;
        }

        private sealed class Collector : ICallbacks
        {
            public string Summary = "no results";

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                var xml = new StringBuilder();
                sb.AppendLine($"passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} duration={result.Duration:F2}s");
                xml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                xml.AppendLine($"<testsuite name=\"EditMode\" tests=\"{result.PassCount + result.FailCount + result.SkipCount}\" failures=\"{result.FailCount}\" skipped=\"{result.SkipCount}\" time=\"{result.Duration:F3}\">");
                Walk(result, sb, xml);
                xml.AppendLine("</testsuite>");

                Directory.CreateDirectory("TestResults");
                Summary = sb.ToString();
                File.WriteAllText(SummaryPath, Summary);
                File.WriteAllText(JUnitPath, xml.ToString());
            }

            private static void Walk(ITestResultAdaptor r, StringBuilder sb, StringBuilder xml)
            {
                if (!r.HasChildren)
                {
                    string cls = r.Test.Parent != null ? r.Test.Parent.FullName : "";
                    xml.Append($"  <testcase classname=\"{Esc(cls)}\" name=\"{Esc(r.Name)}\" time=\"{r.Duration:F3}\"");
                    if (r.TestStatus == TestStatus.Failed)
                    {
                        sb.AppendLine($"FAIL {r.FullName}: {r.Message?.Trim()}");
                        xml.AppendLine($"><failure message=\"{Esc(r.Message)}\">{Esc(r.StackTrace)}</failure></testcase>");
                    }
                    else if (r.TestStatus == TestStatus.Skipped)
                        xml.AppendLine("><skipped/></testcase>");
                    else
                        xml.AppendLine("/>");
                    return;
                }
                foreach (var c in r.Children) Walk(c, sb, xml);
            }

            private static string Esc(string s) => System.Security.SecurityElement.Escape(s ?? "");
        }
    }
}
