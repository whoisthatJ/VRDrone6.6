using UnityEngine;

namespace GateRush.Vehicle
{
    /// <summary>
    /// Procedural motor sound (no audio assets): per motor a band-limited saw at the prop blade-pass frequency
    /// (RPM / 60 × blades) through a one-pole low-pass, plus airflow noise, plus one-shot crash thump and lap beep.
    /// Synthesised on the audio thread from values the main thread copies each frame; no allocations.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class QuadAudio : MonoBehaviour
    {
        [SerializeField] private QuadVehicle vehicle;
        [Tooltip("Motor RPM at full speed (5\" ≈ 28k, whoop ≈ 45k).")]
        [SerializeField] private float maxRpm = 28000f;
        [SerializeField] private int blades = 3;
        [SerializeField, Range(0f, 1f)] private float volume = 0.6f;

        // Main thread → audio thread (single writer each way; torn floats are harmless for audio).
        private volatile float _s0, _s1, _s2, _s3, _volume, _rpm;
        private volatile int _crashCount, _beepCount;

        // Audio thread state.
        private readonly float[] _phase = new float[4];
        private readonly float[] _lp = new float[4];
        private float _sampleRate = 48000f;
        private uint _noise = 0x12345678u;
        private int _crashSeen, _beepSeen;
        private float _crashEnv, _beepEnv, _beepPhase, _noiseLp;

        public float Volume
        {
            get => volume;
            set => volume = Mathf.Clamp01(value);
        }

        public float MaxRpm
        {
            get => maxRpm;
            set => maxRpm = Mathf.Max(1000f, value);
        }

        /// <summary>0 = in your ears (FPV views), 1 = positioned at the quad (line of sight).</summary>
        public void SetSpatial(bool spatial)
        {
            var src = GetComponent<AudioSource>();
            src.spatialBlend = spatial ? 1f : 0f;
        }

        public void Crash() => _crashCount++;
        public void Beep() => _beepCount++;

        private void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.playOnAwake = true;
            src.loop = true;
            src.dopplerLevel = 0.3f;
            src.minDistance = 2f;
            src.maxDistance = 120f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            if (!src.isPlaying) src.Play();
            if (vehicle != null) vehicle.Crashed += Crash;
        }

        private void OnDestroy()
        {
            if (vehicle != null) vehicle.Crashed -= Crash;
        }

        private void Update()
        {
            if (vehicle == null) return;
            ref readonly var s = ref vehicle.State;
            _s0 = s.MotorSpeed.M0;
            _s1 = s.MotorSpeed.M1;
            _s2 = s.MotorSpeed.M2;
            _s3 = s.MotorSpeed.M3;
            _volume = volume;
            _rpm = maxRpm * blades / 60f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float vol = _volume;
            float bladeHz = _rpm;
            float s0 = _s0, s1 = _s1, s2 = _s2, s3 = _s3;
            float inv = 1f / _sampleRate;
            if (_crashCount != _crashSeen) { _crashSeen = _crashCount; _crashEnv = 1f; }
            if (_beepCount != _beepSeen) { _beepSeen = _beepCount; _beepEnv = 1f; }

            float load = (s0 * s0 + s1 * s1 + s2 * s2 + s3 * s3) * 0.25f;
            for (int i = 0; i < data.Length; i += channels)
            {
                float sum = Motor(0, s0, bladeHz, inv) + Motor(1, s1, bladeHz, inv) + Motor(2, s2, bladeHz, inv) + Motor(3, s3, bladeHz, inv);

                // White noise (xorshift), low-passed: prop wash / airflow.
                _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5;
                float white = (_noise & 0xFFFF) / 32768f - 1f;
                _noiseLp += (white - _noiseLp) * 0.15f;
                float sample = sum * 0.18f + _noiseLp * load * 0.35f;

                if (_crashEnv > 0.0005f)
                {
                    sample += white * _crashEnv * 0.9f;
                    _crashEnv *= 0.9993f;
                }
                if (_beepEnv > 0.0005f)
                {
                    _beepPhase += 1600f * inv;
                    if (_beepPhase > 1f) _beepPhase -= 1f;
                    sample += (_beepPhase < 0.5f ? 0.25f : -0.25f) * _beepEnv;
                    _beepEnv *= 0.9996f;
                }

                sample *= vol;
                for (int c = 0; c < channels; c++) data[i + c] = sample;
            }
        }

        private float Motor(int m, float speed, float bladeHz, float inv)
        {
            if (speed < 0.01f) return 0f;
            _phase[m] += speed * bladeHz * (1f + m * 0.013f) * inv; // slight detune per motor = natural beating
            if (_phase[m] > 1f) _phase[m] -= 1f;
            float saw = _phase[m] * 2f - 1f;
            _lp[m] += (saw - _lp[m]) * 0.25f;
            return _lp[m] * speed;
        }
    }
}
