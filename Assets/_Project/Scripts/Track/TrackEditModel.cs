using System;
using System.Collections.Generic;
using UnityEngine;

namespace GateRush.Track
{
    /// <summary>What an editor handle refers to.</summary>
    public enum EditTarget
    {
        Gate = 0,
        Prop = 1,
        Spawn = 2,
    }

    /// <summary>
    /// Editing operations on a <see cref="TrackData"/> with snapping and undo. Pure data: the editor view
    /// mirrors it with GameObjects and calls these methods; tests drive it directly.
    /// </summary>
    public sealed class TrackEditModel
    {
        public const int MaxUndo = 50;
        public const float MinScale = 0.5f;
        public const float MaxScale = 4f;

        private readonly LinkedList<string> _undo = new LinkedList<string>();
        private readonly LinkedList<string> _redo = new LinkedList<string>();

        public TrackEditModel(TrackData data) => Data = data ?? throw new ArgumentNullException(nameof(data));

        public TrackData Data { get; private set; }
        public bool Snap { get; set; } = true;
        public float GridSize { get; set; } = 0.5f;
        public float AngleStep { get; set; } = 15f;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        /// <summary>True when there are changes since the last <see cref="MarkSaved"/>.</summary>
        public bool Dirty { get; private set; }

        /// <summary>Raised after any change (structure or transform).</summary>
        public event Action Changed;

        // ---------- snapping ----------

        public static Vector3 SnapPosition(Vector3 p, float grid)
        {
            if (grid <= 0f) return p;
            return new Vector3(Mathf.Round(p.x / grid) * grid, Mathf.Max(0f, Mathf.Round(p.y / grid) * grid), Mathf.Round(p.z / grid) * grid);
        }

        public static float SnapAngle(float degrees, float step)
        {
            float a = Mathf.Repeat(degrees, 360f);
            if (step > 0f) a = Mathf.Repeat(Mathf.Round(a / step) * step, 360f);
            return a;
        }

        private Vector3 Pos(Vector3 p) => Snap ? SnapPosition(p, GridSize) : new Vector3(p.x, Mathf.Max(0f, p.y), p.z);
        private float Ang(float a) => Snap ? SnapAngle(a, AngleStep) : Mathf.Repeat(a, 360f);
        private static float ClampScale(float s) => Mathf.Clamp(s, MinScale, MaxScale);

        // ---------- gates ----------

        public int AddGate(string type, Vector3 position, float yaw, int index = -1)
        {
            Record();
            var g = new GateData { Type = type, Position = Pos(position), Yaw = Ang(yaw), Scale = 1f };
            if (index < 0 || index > Data.Gates.Count) index = Data.Gates.Count;
            Data.Gates.Insert(index, g);
            Touch();
            return index;
        }

        public void RemoveGate(int index)
        {
            if (index < 0 || index >= Data.Gates.Count) return;
            Record();
            Data.Gates.RemoveAt(index);
            Touch();
        }

        /// <summary>Moves a gate. Call <see cref="BeginEdit"/> once when a drag starts so the whole drag is one undo step.</summary>
        public void SetGate(int index, Vector3 position, float yaw, float scale, float pitch = float.NaN)
        {
            if (index < 0 || index >= Data.Gates.Count) return;
            var g = Data.Gates[index];
            g.Position = Pos(position);
            g.Yaw = Ang(yaw);
            g.Scale = ClampScale(scale);
            if (!float.IsNaN(pitch)) g.Pitch = Mathf.Clamp(pitch, -60f, 60f);
            Touch();
        }

        public void SetGateType(int index, string type)
        {
            if (index < 0 || index >= Data.Gates.Count) return;
            Record();
            Data.Gates[index].Type = type;
            Touch();
        }

        /// <summary>Moves a gate to another position in the race order. Returns its new index.</summary>
        public int MoveOrder(int from, int to)
        {
            int n = Data.Gates.Count;
            if (from < 0 || from >= n) return from;
            to = Mathf.Clamp(to, 0, n - 1);
            if (to == from) return from;
            Record();
            var g = Data.Gates[from];
            Data.Gates.RemoveAt(from);
            Data.Gates.Insert(to, g);
            Touch();
            return to;
        }

        /// <summary>Reverses the race direction (gate 0 stays the start/finish) and turns every gate around.</summary>
        public void ReverseDirection()
        {
            if (Data.Gates.Count < 2) return;
            Record();
            var start = Data.Gates[0];
            var rest = Data.Gates.GetRange(1, Data.Gates.Count - 1);
            rest.Reverse();
            Data.Gates.Clear();
            Data.Gates.Add(start);
            Data.Gates.AddRange(rest);
            foreach (var g in Data.Gates) g.Yaw = Mathf.Repeat(g.Yaw + 180f, 360f);
            Touch();
        }

        // ---------- props ----------

        public int AddProp(string type, Vector3 position, float yaw)
        {
            Record();
            Data.Props.Add(new PropData { Type = type, Position = Pos(position), Euler = new Vector3(0f, Ang(yaw), 0f), Scale = Vector3.one });
            Touch();
            return Data.Props.Count - 1;
        }

        public void RemoveProp(int index)
        {
            if (index < 0 || index >= Data.Props.Count) return;
            Record();
            Data.Props.RemoveAt(index);
            Touch();
        }

        public void SetProp(int index, Vector3 position, float yaw, float uniformScale)
        {
            if (index < 0 || index >= Data.Props.Count) return;
            var p = Data.Props[index];
            p.Position = Pos(position);
            p.Euler = new Vector3(p.Euler.x, Ang(yaw), p.Euler.z);
            float s = ClampScale(uniformScale);
            p.Scale = Vector3.one * s;
            Touch();
        }

        // ---------- spawn / meta ----------

        public void SetSpawn(Vector3 position, float yaw)
        {
            Data.Spawn.Position = new Vector3(Pos(position).x, Mathf.Max(0.15f, position.y), Pos(position).z);
            Data.Spawn.Yaw = Ang(yaw);
            Touch();
        }

        public void SetLaps(int laps)
        {
            Record();
            Data.Laps = Mathf.Clamp(laps, 1, 10);
            Touch();
        }

        public void SetName(string name)
        {
            Record();
            Data.Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();
            Touch();
        }

        // ---------- undo ----------

        /// <summary>Records an undo point before a continuous edit (drag) begins.</summary>
        public void BeginEdit() => Record();

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            _redo.AddLast(Data.ToJson(false));
            Restore(_undo.Last.Value);
            _undo.RemoveLast();
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            _undo.AddLast(Data.ToJson(false));
            Restore(_redo.Last.Value);
            _redo.RemoveLast();
            return true;
        }

        public void MarkSaved() => Dirty = false;

        private void Record()
        {
            _undo.AddLast(Data.ToJson(false));
            if (_undo.Count > MaxUndo) _undo.RemoveFirst();
            _redo.Clear();
        }

        private void Restore(string json)
        {
            Data = TrackData.FromJson(json);
            Touch();
        }

        private void Touch()
        {
            Dirty = true;
            Changed?.Invoke();
        }

        /// <summary>A blank course for an environment: spawn at the origin and one start/finish gate ahead.</summary>
        public static TrackData NewTrack(string environment, string name)
        {
            var t = new TrackData { Name = name, Environment = environment, Author = "Pilot", Laps = 3 };
            t.Spawn.Position = new Vector3(0f, 0.15f, 0f);
            t.Gates.Add(new GateData { Type = "Square", Position = new Vector3(0f, 0f, 10f), Yaw = 0f });
            return t;
        }
    }
}
