using GateRush.Garage;
using UnityEngine;

namespace GateRush.Net
{
    /// <summary>
    /// Another pilot's quad: their own build's model, posed from their snapshots (interpolated slightly in the past)
    /// with the props spinning at their motor speeds. Visual only; quads don't collide with each other.
    /// </summary>
    public sealed class RemotePilotView : MonoBehaviour
    {
        private const float MaxVisualDegPerSec = 2200f;
        private static readonly float[] Direction = { -1f, 1f, 1f, -1f };

        private NetPilot _pilot;
        private Transform[] _props = new Transform[0];
        private GameObject _model;
        private readonly float[] _motors = new float[4];

        public NetPilot Pilot => _pilot;
        /// <summary>Has had at least one snapshot (shown).</summary>
        public bool Visible => _model != null && _model.activeSelf;

        public static RemotePilotView Create(NetPilot pilot, QuadModelMaterials materials, Transform parent, int layer)
        {
            var go = new GameObject("Remote " + pilot.Name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RemotePilotView>();
            view._pilot = pilot;
            var build = Parse(pilot.BuildJson);
            if (materials != null && materials.Carbon != null)
            {
                var model = QuadModelBuilder.Build(build, materials, go.transform, layer);
                model.Root.transform.localPosition = Vector3.down * (build.Frame.PlateMm * 0.001f + build.Frame.StackMm * 0.0005f);
                foreach (var t in model.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                view._model = model.Root;
                view._props = model.Props;
                view._model.SetActive(false);
            }
            return view;
        }

        /// <summary>The build a pilot flies, or the stock racer if it doesn't parse or validate.</summary>
        public static QuadBuild Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return QuadPresets.Race5;
            try
            {
                var b = QuadBuild.FromJson(json);
                return b != null && BuildComposer.Validate(b).Count == 0 ? b : QuadPresets.Race5;
            }
            catch (System.Exception)
            {
                return QuadPresets.Race5;
            }
        }

        private void LateUpdate()
        {
            var net = NetRace.Instance;
            if (_pilot == null || net == null || !_pilot.Buffer.HasData) return;
            if (!_pilot.Buffer.Sample(net.ServerTime - SnapshotBuffer.Delay, out var pos, out var rot, _motors)) return;
            transform.SetPositionAndRotation(pos, rot);
            if (_model != null && !_model.activeSelf) _model.SetActive(true);
            float dt = Time.deltaTime;
            for (int i = 0; i < _props.Length && i < 4; i++)
                if (_props[i] != null) _props[i].Rotate(0f, Direction[i] * _motors[i] * MaxVisualDegPerSec * dt, 0f, Space.Self);
        }
    }
}
