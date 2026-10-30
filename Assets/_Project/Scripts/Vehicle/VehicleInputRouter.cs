using GateRush.Input;
using UnityEngine;

namespace GateRush.Vehicle
{
    /// <summary>
    /// Connects the highest-priority connected input source to the vehicle.
    /// Runs after the sources (-100) and before the vehicle (-50). Switching sources disarms.
    /// </summary>
    [DefaultExecutionOrder(-75)]
    public sealed class VehicleInputRouter : MonoBehaviour
    {
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private PilotInputSource[] sources = new PilotInputSource[0];

        public PilotInputSource Active => vehicle != null ? vehicle.InputSource : null;

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

            if (best == vehicle.InputSource) return;
            if (vehicle.InputSource != null) vehicle.InputSource.Disarm();
            vehicle.InputSource = best;
            Debug.Log(best != null ? $"[GateRush] Input source: {best.GetType().Name}" : "[GateRush] Input source: none");
        }
    }
}
