using System.IO;
using UnityEngine;

namespace GateRush.Race
{
    /// <summary>
    /// Records the current lap and replays the best lap as a ghost quad. The best ghost per track + quad
    /// is saved to persistentDataPath/ghosts so it is there from the first lap of the next session.
    /// </summary>
    public sealed class GhostSystem : MonoBehaviour
    {
        [SerializeField] private Transform ghostVisual;
        [SerializeField] private float sampleRate = 30f;
        [Tooltip("Longest lap that can be recorded (s).")]
        [SerializeField] private float maxLapSeconds = 600f;

        private GhostTrack _recording, _best;
        private float _recordClock, _nextSampleAt;
        private bool _recordingLap, _playing;
        private double _playStart;
        private string _savePath;

        public bool HasBest => _best != null && _best.Count > 1;
        public float BestLapTime => HasBest ? _best.LapTime : 0f;

        private void Awake()
        {
            int capacity = Mathf.CeilToInt(sampleRate * maxLapSeconds) + 1;
            _recording = new GhostTrack(capacity, 1f / sampleRate);
            _best = new GhostTrack(capacity, 1f / sampleRate);
            if (ghostVisual != null) ghostVisual.gameObject.SetActive(false);
        }

        /// <summary>Loads the saved best ghost for this track + quad, if any.</summary>
        public void Load(string trackId, string quadName)
        {
            _savePath = Path.Combine(Application.persistentDataPath, "ghosts", Sanitize(trackId) + "__" + Sanitize(quadName) + ".ghost");
            _best.Clear();
            if (!File.Exists(_savePath)) return;
            try
            {
                using var r = new BinaryReader(File.OpenRead(_savePath));
                if (!_best.Read(r)) _best.Clear();
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[GateRush] Ghost unreadable: {e.Message}");
                _best.Clear();
            }
        }

        /// <summary>Starts recording a new lap and replays the best ghost from the same moment.</summary>
        public void BeginLap(double now)
        {
            _recording.Clear();
            _recordClock = 0f;
            _nextSampleAt = 0f;
            _recordingLap = true;
            _playStart = now;
            _playing = HasBest;
            if (ghostVisual != null) ghostVisual.gameObject.SetActive(_playing);
        }

        /// <summary>Call every frame while racing with the quad's pose.</summary>
        public void Record(float dt, Vector3 position, Quaternion rotation)
        {
            if (!_recordingLap) return;
            _recordClock += dt;
            while (_recordClock >= _nextSampleAt && !_recording.IsFull)
            {
                _recording.Add(position, rotation);
                _nextSampleAt += _recording.SampleInterval;
            }
        }

        /// <summary>Ends the current lap. Returns true if it became the new best ghost (and was saved).</summary>
        public bool EndLap(float lapTime)
        {
            _recordingLap = false;
            if (_recording.Count < 2 || _recording.IsFull) return false;
            if (HasBest && lapTime >= _best.LapTime) return false;

            _recording.LapTime = lapTime;
            _best.CopyFrom(_recording);
            Save();
            return true;
        }

        public void Stop()
        {
            _recordingLap = false;
            _playing = false;
            if (ghostVisual != null) ghostVisual.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!_playing || ghostVisual == null) return;
            float t = (float)(Time.timeAsDouble - _playStart);
            if (t > _best.Duration + 1f)
            {
                ghostVisual.gameObject.SetActive(false);
                _playing = false;
                return;
            }
            _best.Sample(t, out Vector3 p, out Quaternion q);
            ghostVisual.SetPositionAndRotation(p, q);
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(_savePath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_savePath));
                using var w = new BinaryWriter(File.Create(_savePath));
                _best.Write(w);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[GateRush] Could not save ghost: {e.Message}");
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "default";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars);
        }
    }
}
