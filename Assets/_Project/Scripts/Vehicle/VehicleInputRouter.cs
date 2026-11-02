using GateRush.Flight;
using GateRush.Input;
using UnityEngine;

namespace GateRush.Vehicle
{
    /// <summary>
    /// Connects the highest-priority connected input source to the vehicle, and mixes every other connected source on
    /// top (so e.g. the keyboard still works while a radio is plugged in; see <see cref="PilotInput.Mix"/>).
    /// Runs after the sources (-100) and before the vehicle (-50). Switching sources disarms.
    /// </summary>
    [DefaultExecutionOrder(-75)]
    public sealed class VehicleInputRouter : MonoBehaviour
    {
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private PilotInputSource[] sources = new PilotInputSource[0];
        [Tooltip("Mix the other connected controllers on top of the main one, so several can fly at the same time.")]
        [SerializeField] private bool mixSources = true;

        public bool MixSources
        {
            get => mixSources;
            set => mixSources = value;
        }

        private void OnEnable()
        {
            if (vehicle != null) vehicle.Respawned += DisarmAll;
        }

        private void OnDisable()
        {
            if (vehicle != null) vehicle.Respawned -= DisarmAll;
        }

        // The vehicle only disarms its main source on respawn; the mixed-in ones must not keep the quad armed.
        private void DisarmAll()
        {
            foreach (var s in sources)
                if (s != null) s.Disarm();
        }

        public PilotInputSource Active => vehicle != null ? vehicle.InputSource : null;

        /// <summary>(previous, next) whenever the active source changes: hot-plug, or a higher-priority device appearing.</summary>
        public event System.Action<PilotInputSource, PilotInputSource> SourceChanged;

        private void Update()
        {
            if (vehicle == null) return;

            PilotInputSource best = null;
            int bestPriority = int.MinValue;
            var all = sources;
            for (int i = 0; i < all.Length; i++)
            {
                var s = all[i];
                if (s == null || !s.isActiveAndEnabled || !s.IsConnected) continue;
                int p = s.Priority;
                if (p > bestPriority)
                {
                    best = s;
                    bestPriority = p;
                }
            }

            // A source added at runtime (e.g. ScriptedInputSource on the quad) also competes.
            var extra = vehicle.InputSource;
            if (extra != null && extra.isActiveAndEnabled && extra.IsConnected && extra.Priority > bestPriority) best = extra;

            PilotInput assist = default;
            if (mixSources && !(best is ScriptedInputSource))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var s = all[i];
                    if (s == null || s == best || s is ScriptedInputSource || !s.isActiveAndEnabled || !s.IsConnected) continue;
                    assist = PilotInput.Mix(assist, s.Current);
                }
            }
            vehicle.AssistInput = assist;

            if (best == vehicle.InputSource) return;
            var previous = vehicle.InputSource;
            if (previous != null) previous.Disarm();
            vehicle.InputSource = best;
            Debug.Log(best != null ? $"[GateRush] Input source: {best.GetType().Name}" : "[GateRush] Input source: none");
            SourceChanged?.Invoke(previous, best);
        }
    }
}
