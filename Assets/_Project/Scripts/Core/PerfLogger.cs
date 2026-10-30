using UnityEngine;

namespace GateRush.Core
{
    /// <summary>Logs average/min FPS to logcat every few seconds (adb logcat -s Unity).</summary>
    public sealed class PerfLogger : MonoBehaviour
    {
        [SerializeField] private float intervalSeconds = 5f;

        private float _elapsed;
        private int _frames;
        private float _worstDt;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;
            _frames++;
            if (dt > _worstDt) _worstDt = dt;

            if (_elapsed < intervalSeconds) return;

            Debug.Log($"[GateRush] FPS avg {_frames / _elapsed:F1}, worst frame {_worstDt * 1000f:F1} ms");
            _elapsed = 0f;
            _frames = 0;
            _worstDt = 0f;
        }
    }
}
