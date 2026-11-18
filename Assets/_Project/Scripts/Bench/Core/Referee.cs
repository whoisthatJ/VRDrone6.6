using System;
using System.Collections.Generic;
using Ushqyr.Flight;

namespace Ushqyr.Bench
{
    /// <summary>The standard gate of the task: 250 x 250 cm outside, a centred 195 x 195 cm opening.</summary>
    public static class GateGeometry
    {
        public const float Outer = 2.5f;
        public const float Opening = 1.95f;
        /// <summary>Width of the frame bar on every side: (2.5 - 1.95) / 2.</summary>
        public const float Bar = (Outer - Opening) * 0.5f;
        /// <summary>Depth of the frame along the flight direction (plan default, to be confirmed).</summary>
        public const float Depth = 0.275f;
        /// <summary>Height of the opening's centre above the floor when the gate stands on it.</summary>
        public const float CentreHeight = Outer * 0.5f;
    }

    /// <summary>One gate as the referee sees it: the centre of the opening and the gate's axes.</summary>
    public struct RefereeGate
    {
        public string Id;
        public Vec3 Centre;
        /// <summary>Unit normal of the opening plane ("front to back").</summary>
        public Vec3 Normal;
        public Vec3 Right;
        public Vec3 Up;
        /// <summary>True: only a pass from the front (along <see cref="Normal"/>) counts.</summary>
        public bool FrontOnly;

        public static RefereeGate At(string id, Vec3 centre, Quat rotation, bool frontOnly = false) => new RefereeGate
        {
            Id = id, Centre = centre, Normal = rotation * Vec3.Forward, Right = rotation * Vec3.Right, Up = rotation * Vec3.Up,
            FrontOnly = frontOnly,
        };
    }

    public enum GateEventKind
    {
        /// <summary>A pass that gave a point.</summary>
        Scored,
        /// <summary>A complete pass of a gate that is still among the last scored ones.</summary>
        RepeatIgnored,
        /// <summary>A complete pass against the allowed direction.</summary>
        WrongDirection,
    }

    public struct GateEvent
    {
        public GateEventKind Kind;
        public int GateIndex;
        public string GateId;
        /// <summary>Simulator time of the moment the drone's centre crossed the opening plane.</summary>
        public double Time;
        /// <summary>Points after this event.</summary>
        public int Points;
        /// <summary>For a repeat: how many other gates have scored since this gate last scored.</summary>
        public int OthersSince;
    }

    /// <summary>
    /// Counts gate passes and points. Fed with the drone's centre before and after every physics step.
    ///
    /// A pass: the centre crosses the opening plane inside the 1.95 x 1.95 m opening and carries on until it is
    /// <see cref="CompleteDistance"/> beyond the plane. Poking in and backing out is not a pass, flying around the
    /// gate is not a pass. The time of a pass is the moment of the crossing.
    ///
    /// A point: +1 when the gate is not among the last <see cref="RepeatWindow"/> scored gates.
    /// </summary>
    public sealed class Referee
    {
        private struct GateState
        {
            /// <summary>The side the drone was last clearly on: +1 behind the gate, -1 in front, 0 not known yet.</summary>
            public int Side;
            public bool LastCrossInside;
            public double LastCrossTime;
        }

        private readonly RefereeGate[] _gates;
        private readonly GateState[] _state;
        private readonly List<int> _recent = new List<int>();
        private readonly List<GateEvent> _events = new List<GateEvent>();

        public int RepeatWindow { get; }
        public float HalfOpening { get; }
        public float CompleteDistance { get; }

        public int Points { get; private set; }
        /// <summary>Simulator time of the last scored gate (the tie-break time); 0 before the first point.</summary>
        public double LastScoreTime { get; private set; }
        public IReadOnlyList<GateEvent> Events => _events;
        public IReadOnlyList<RefereeGate> Gates => _gates;

        public Referee(IReadOnlyList<RefereeGate> gates, int repeatWindow = 4, float opening = GateGeometry.Opening, float completeDistance = 0.3f)
        {
            _gates = new RefereeGate[gates.Count];
            for (int i = 0; i < gates.Count; i++) _gates[i] = gates[i];
            _state = new GateState[_gates.Length];
            RepeatWindow = Math.Max(0, repeatWindow);
            HalfOpening = opening * 0.5f;
            CompleteDistance = completeDistance;
        }

        /// <summary>Forgets everything and takes <paramref name="start"/> as the drone's position.</summary>
        public void Reset(Vec3 start)
        {
            Points = 0;
            LastScoreTime = 0;
            _recent.Clear();
            _events.Clear();
            for (int i = 0; i < _gates.Length; i++)
            {
                float d = Vec3.Dot(start - _gates[i].Centre, _gates[i].Normal);
                _state[i] = new GateState { Side = d >= CompleteDistance ? 1 : d <= -CompleteDistance ? -1 : 0 };
            }
        }

        /// <summary>One physics step: the drone's centre moved from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public void Step(Vec3 from, Vec3 to, double timeFrom, double timeTo)
        {
            for (int i = 0; i < _gates.Length; i++)
            {
                ref readonly RefereeGate g = ref _gates[i];
                float d0 = Vec3.Dot(from - g.Centre, g.Normal);
                float d1 = Vec3.Dot(to - g.Centre, g.Normal);
                ref GateState st = ref _state[i];

                // The segment leaves the plane's first side (ending exactly on the plane is not a crossing yet).
                if ((d0 <= 0f && d1 > 0f) || (d0 >= 0f && d1 < 0f))
                {
                    float t = d0 == d1 ? 0f : d0 / (d0 - d1);
                    Vec3 hit = from + (to - from) * t - g.Centre;
                    float x = Vec3.Dot(hit, g.Right), y = Vec3.Dot(hit, g.Up);
                    st.LastCrossInside = MathF.Abs(x) <= HalfOpening && MathF.Abs(y) <= HalfOpening;
                    st.LastCrossTime = timeFrom + (timeTo - timeFrom) * t;
                }

                int side = d1 >= CompleteDistance ? 1 : d1 <= -CompleteDistance ? -1 : 0;
                if (side == 0 || side == st.Side) continue;
                bool known = st.Side != 0;
                st.Side = side;
                // Clearly on the other side now: a pass if the way there led through the opening.
                if (known && st.LastCrossInside) Pass(i, side, st.LastCrossTime);
                st.LastCrossInside = false;
            }
        }

        private void Pass(int gate, int side, double time)
        {
            var e = new GateEvent { GateIndex = gate, GateId = _gates[gate].Id, Time = time };
            int at = _recent.LastIndexOf(gate);
            if (_gates[gate].FrontOnly && side < 0)
            {
                e.Kind = GateEventKind.WrongDirection;
            }
            else if (at >= 0)
            {
                e.Kind = GateEventKind.RepeatIgnored;
                e.OthersSince = _recent.Count - 1 - at;
            }
            else
            {
                e.Kind = GateEventKind.Scored;
                Points++;
                LastScoreTime = time;
                _recent.Add(gate);
                while (_recent.Count > RepeatWindow) _recent.RemoveAt(0);
            }
            e.Points = Points;
            _events.Add(e);
        }
    }
}
