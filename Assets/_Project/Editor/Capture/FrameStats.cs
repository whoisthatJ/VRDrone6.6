using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Ushqyr.EditorTools.Capture
{
    /// <summary>
    /// Play-mode frame counters for performance work: run <see cref="Sample"/> as a coroutine; it averages main /
    /// render thread time, draw calls, batches, SetPass calls, triangles, shadow casters and the main script and
    /// render markers over a number of frames, then writes a report.
    /// </summary>
    public static class FrameStats
    {
        private static readonly (ProfilerCategory cat, string name)[] Stats =
        {
            (ProfilerCategory.Internal, "Main Thread"), (ProfilerCategory.Render, "Render Thread"),
            (ProfilerCategory.Render, "Draw Calls Count"), (ProfilerCategory.Render, "Batches Count"),
            (ProfilerCategory.Render, "SetPass Calls Count"), (ProfilerCategory.Render, "Triangles Count"),
            (ProfilerCategory.Render, "Shadow Casters Count"),
            (ProfilerCategory.Scripts, "Update.ScriptRunBehaviourUpdate"), (ProfilerCategory.Scripts, "PreLateUpdate.ScriptRunBehaviourLateUpdate"),
            (ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate"), (ProfilerCategory.Render, "Inl_UniversalRenderPipeline.RenderSingleCameraInternal"),
            (ProfilerCategory.Render, "CullScriptable"), (ProfilerCategory.Render, "Inl_ScriptableRenderer.Setup"),
            (ProfilerCategory.Render, "Inl_MainLightShadowCasterPass.Configure"), (ProfilerCategory.Render, "Shadows.ShadowCasterBounds"),
            (ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"), (ProfilerCategory.Render, "Gfx.WaitForRenderThread"),
            (ProfilerCategory.Gui, "UGUI.Rendering.UpdateBatches"), (ProfilerCategory.Scripts, "PostLateUpdate.UpdateAllRenderers"),
            (ProfilerCategory.Render, "Inl_RenderGraph.Execute"), (ProfilerCategory.Render, "Inl_RenderGraph.Compile"),
            (ProfilerCategory.Scripts, "FpvCamera.LateUpdate() [Invoke]"),
            (ProfilerCategory.Scripts, "InstancedProps.LateUpdate() [Invoke]"),
            (ProfilerCategory.Scripts, "NextGateIndicator.LateUpdate() [Invoke]"),
            (ProfilerCategory.Scripts, "GhostSystem.LateUpdate() [Invoke]"),
            (ProfilerCategory.Scripts, "RaceHud.LateUpdate() [Invoke]"),
            (ProfilerCategory.Scripts, "OsdHud.LateUpdate() [Invoke]"),
            (ProfilerCategory.Scripts, "PropVisuals.LateUpdate() [Invoke]"),
            (ProfilerCategory.Scripts, "CinemachineBrain.LateUpdate() [Invoke]"),
            (ProfilerCategory.Scripts, "NextGateBeacon.LateUpdate() [Invoke]"),
        };

        public static IEnumerator Sample(int frames, string file)
        {
            var recs = new List<(string name, ProfilerRecorder rec)>();
            foreach (var (cat, name) in Stats) recs.Add((name, ProfilerRecorder.StartNew(cat, name)));
            var sums = new double[recs.Count];
            float dt = 0f;
            yield return null;
            for (int f = 0; f < frames; f++)
            {
                yield return null;
                dt += Time.unscaledDeltaTime;
                for (int i = 0; i < recs.Count; i++)
                    if (recs[i].rec.Valid) sums[i] += recs[i].rec.LastValue;
            }
            var sb = new StringBuilder($"frames {frames}, avg frame {dt / frames * 1000f:F2} ms ({frames / dt:F1} fps)\n");
            for (int i = 0; i < recs.Count; i++)
            {
                var r = recs[i].rec;
                if (!r.Valid) sb.AppendLine($"{recs[i].name}: n/a");
                else if (r.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds) sb.AppendLine($"{recs[i].name}: {sums[i] / frames / 1e6:F3} ms");
                else sb.AppendLine($"{recs[i].name}: {sums[i] / frames:F0}");
                r.Dispose();
            }
            File.WriteAllText(file, sb.ToString());
        }
    }
}
