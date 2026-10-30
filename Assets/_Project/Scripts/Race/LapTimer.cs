using System;

namespace GateRush.Race
{
    public enum PassResult
    {
        /// <summary>Race not running, or already finished.</summary>
        Ignored = 0,
        /// <summary>Expected checkpoint passed; lap continues.</summary>
        Checkpoint = 1,
        /// <summary>Start/finish gate passed after all checkpoints: a lap was completed.</summary>
        LapCompleted = 2,
        /// <summary>Final lap completed.</summary>
        RaceFinished = 3,
        /// <summary>A gate other than the expected one: doesn't count, the pilot must fly the missed gate.</summary>
        WrongGate = 4,
    }

    /// <summary>
    /// Lap timing and checkpoint order. Gate 0 is start/finish. After the start signal the pilot flies
    /// gate 0 (holeshot), 1, 2 … N-1, then gate 0 again to complete a lap. Lap 1 runs from the start
    /// signal, so it includes the launch from the pad. Times are in seconds (any monotonic clock).
    /// </summary>
    public sealed class LapTimer
    {
        private readonly double[] _laps;
        private bool _holeshotDone;

        public LapTimer(int checkpointCount, int totalLaps)
        {
            if (checkpointCount < 1) throw new ArgumentOutOfRangeException(nameof(checkpointCount));
            if (totalLaps < 1) throw new ArgumentOutOfRangeException(nameof(totalLaps));
            CheckpointCount = checkpointCount;
            TotalLaps = totalLaps;
            _laps = new double[totalLaps];
            Reset();
        }

        public int CheckpointCount { get; }
        public int TotalLaps { get; }
        public bool Started { get; private set; }
        public bool Finished { get; private set; }
        /// <summary>Index of the gate the pilot must fly next.</summary>
        public int NextCheckpoint { get; private set; }
        /// <summary>Last gate that counted, or -1 before the holeshot.</summary>
        public int LastCheckpoint { get; private set; }
        public int CompletedLaps { get; private set; }
        /// <summary>1-based lap being flown (stays at TotalLaps once finished).</summary>
        public int CurrentLap => Math.Min(CompletedLaps + 1, TotalLaps);
        public double StartTime { get; private set; }
        public double LapStartTime { get; private set; }
        public double FinishTime { get; private set; }
        public double BestLap { get; private set; }
        public int BestLapIndex { get; private set; }
        public bool HasBestLap => BestLapIndex >= 0;

        public double LapTime(int index) => _laps[index];

        public void Reset()
        {
            Started = Finished = false;
            NextCheckpoint = 0;
            LastCheckpoint = -1;
            CompletedLaps = 0;
            StartTime = LapStartTime = FinishTime = 0;
            BestLap = double.PositiveInfinity;
            BestLapIndex = -1;
            Array.Clear(_laps, 0, _laps.Length);
            _holeshotDone = false;
        }

        public void Start(double time)
        {
            Reset();
            Started = true;
            StartTime = LapStartTime = time;
        }

        public PassResult Pass(int checkpoint, double time)
        {
            if (!Started || Finished) return PassResult.Ignored;
            if (checkpoint != NextCheckpoint) return PassResult.WrongGate;

            LastCheckpoint = checkpoint;
            NextCheckpoint = (checkpoint + 1) % CheckpointCount;

            if (checkpoint != 0) return PassResult.Checkpoint;
            if (!_holeshotDone)
            {
                _holeshotDone = true;
                // With a single gate the holeshot is gate 0 and the next pass of it completes lap 1.
                return PassResult.Checkpoint;
            }

            double lap = time - LapStartTime;
            _laps[CompletedLaps] = lap;
            if (lap < BestLap)
            {
                BestLap = lap;
                BestLapIndex = CompletedLaps;
            }
            CompletedLaps++;
            LapStartTime = time;

            if (CompletedLaps < TotalLaps) return PassResult.LapCompleted;
            Finished = true;
            FinishTime = time;
            return PassResult.RaceFinished;
        }

        public double CurrentLapTime(double now) => !Started ? 0 : (Finished ? _laps[TotalLaps - 1] : now - LapStartTime);

        public double TotalTime(double now) => !Started ? 0 : (Finished ? FinishTime - StartTime : now - StartTime);
    }
}
