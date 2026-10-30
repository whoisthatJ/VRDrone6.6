using System.Collections.Generic;
using GateRush.Race;
using GateRush.Track;
using TMPro;
using UnityEngine;

namespace GateRush.TrackEditor
{
    /// <summary>
    /// Mirrors a <see cref="TrackEditModel"/> with GameObjects: gates and props from <see cref="TrackPrefabs"/>,
    /// a spawn marker, race-order labels and a course line. Structural changes rebuild; moves just update transforms.
    /// </summary>
    public sealed class TrackEditorView
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private readonly TrackPrefabs _prefabs;
        private readonly Transform _root;
        private readonly Material _markerMaterial;
        private readonly int _layer;
        private readonly List<GameObject> _gates = new List<GameObject>();
        private readonly List<GameObject> _props = new List<GameObject>();
        private readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
        private readonly List<string> _builtTypes = new List<string>();
        private GameObject _spawn;
        private LineRenderer _line;
        private MaterialPropertyBlock _mpb;

        public TrackEditorView(TrackPrefabs prefabs, Transform parent, Material markerMaterial, int layer)
        {
            _prefabs = prefabs;
            _markerMaterial = markerMaterial;
            _layer = layer;
            _root = new GameObject("Editor View").transform;
            _root.SetParent(parent, false);
        }

        public Transform Root => _root;

        public GameObject Get(EditTarget target, int index)
        {
            switch (target)
            {
                case EditTarget.Gate: return index >= 0 && index < _gates.Count ? _gates[index] : null;
                case EditTarget.Prop: return index >= 0 && index < _props.Count ? _props[index] : null;
                default: return _spawn;
            }
        }

        public void Sync(TrackData data, EditTarget selectedTarget, int selectedIndex)
        {
            if (NeedsRebuild(data)) Rebuild(data);

            for (int i = 0; i < data.Gates.Count; i++)
            {
                var g = data.Gates[i];
                var t = _gates[i].transform;
                t.SetPositionAndRotation(g.Position, TrackData.GateRotation(g));
                t.localScale = Vector3.one * g.Scale;
                var gate = _gates[i].GetComponent<Gate>();
                bool selected = selectedTarget == EditTarget.Gate && selectedIndex == i;
                if (gate != null) gate.SetVisualState(selected ? GateVisualState.Next : i == 0 ? GateVisualState.Upcoming : GateVisualState.Idle);

                var label = _labels[i];
                label.text = i == 0 ? "START" : i.ToString();
                label.color = selected ? new Color(0.3f, 1f, 0.4f) : Color.white;
                Vector3 top = gate != null ? gate.OpeningCenterWorld + Vector3.up * (gate.OpeningSize.y * 0.5f + 0.6f) : g.Position + Vector3.up * 3f;
                label.transform.position = top;
            }

            for (int i = 0; i < data.Props.Count; i++)
            {
                var p = data.Props[i];
                var t = _props[i].transform;
                t.SetPositionAndRotation(p.Position, Quaternion.Euler(p.Euler));
                t.localScale = p.Scale;
                Tint(_props[i], selectedTarget == EditTarget.Prop && selectedIndex == i ? new Color(0.5f, 1f, 0.6f) : (Color?)null);
            }

            _spawn.transform.SetPositionAndRotation(new Vector3(data.Spawn.Position.x, 0f, data.Spawn.Position.z), Quaternion.Euler(0f, data.Spawn.Yaw, 0f));
            Tint(_spawn, selectedTarget == EditTarget.Spawn ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.55f, 0.15f));

            UpdateLine(data);
        }

        /// <summary>Faces every label towards the viewer (call each frame).</summary>
        public void Billboard(Vector3 viewer)
        {
            foreach (var l in _labels)
            {
                Vector3 d = l.transform.position - viewer;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) l.transform.rotation = Quaternion.LookRotation(d);
            }
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }

        private bool NeedsRebuild(TrackData data)
        {
            if (_spawn == null || _gates.Count != data.Gates.Count || _props.Count != data.Props.Count) return true;
            for (int i = 0; i < data.Gates.Count; i++) if (_builtTypes[i] != data.Gates[i].Type) return true;
            for (int i = 0; i < data.Props.Count; i++) if (_props[i].name != "Prop " + data.Props[i].Type) return true;
            return false;
        }

        private void Rebuild(TrackData data)
        {
            for (int i = _root.childCount - 1; i >= 0; i--) Object.Destroy(_root.GetChild(i).gameObject);
            _gates.Clear();
            _props.Clear();
            _labels.Clear();
            _builtTypes.Clear();

            for (int i = 0; i < data.Gates.Count; i++)
            {
                var prefab = _prefabs.Gate(data.Gates[i].Type) ?? _prefabs.Gate("Square");
                var go = Object.Instantiate(prefab, _root);
                go.name = "Gate " + data.Gates[i].Type;
                AddHandle(go, EditTarget.Gate, i);
                _gates.Add(go);
                _builtTypes.Add(data.Gates[i].Type);
                _labels.Add(Label(go.name + " label"));
            }

            for (int i = 0; i < data.Props.Count; i++)
            {
                var prefab = _prefabs.Prop(data.Props[i].Type);
                var go = prefab != null ? Object.Instantiate(prefab, _root) : GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(_root, false);
                go.name = "Prop " + data.Props[i].Type;
                AddHandle(go, EditTarget.Prop, i);
                _props.Add(go);
            }

            _spawn = BuildSpawnMarker();
            AddHandle(_spawn, EditTarget.Spawn, 0);

            var lineGo = new GameObject("Course Line");
            lineGo.transform.SetParent(_root, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.sharedMaterial = _markerMaterial;
            _line.widthMultiplier = 0.06f;
            _line.loop = true;
            _line.startColor = _line.endColor = new Color(0.3f, 0.9f, 1f, 1f);
        }

        private void AddHandle(GameObject go, EditTarget target, int index)
        {
            var h = go.AddComponent<EditorHandle>();
            h.Target = target;
            h.Index = index;
            // Editor objects must be hittable by the pointer even if a prefab has no collider on the root.
            if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>();
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.gameObject.layer = _layer;
        }

        private TextMeshPro Label(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var t = go.AddComponent<TextMeshPro>();
            t.fontSize = 6f;
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = new Vector2(4f, 1.2f);
            t.outlineWidth = 0.2f;
            return t;
        }

        private GameObject BuildSpawnMarker()
        {
            var root = new GameObject("Spawn Marker");
            root.transform.SetParent(_root, false);
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Pad";
            pad.transform.SetParent(root.transform, false);
            pad.transform.localScale = new Vector3(1.2f, 0.06f, 1.2f);
            pad.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrow.name = "Arrow";
            Object.Destroy(arrow.GetComponent<Collider>());
            arrow.transform.SetParent(root.transform, false);
            arrow.transform.localScale = new Vector3(0.15f, 0.04f, 1.6f);
            arrow.transform.localPosition = new Vector3(0f, 0.08f, 1.2f);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.sharedMaterial = _markerMaterial;
            return root;
        }

        /// <summary>Overrides the base colour, or restores the material colour when <paramref name="c"/> is null.</summary>
        private void Tint(GameObject go, Color? c)
        {
            _mpb ??= new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (c == null)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColor, c.Value);
                r.SetPropertyBlock(_mpb);
            }
        }

        private void UpdateLine(TrackData data)
        {
            int n = data.Gates.Count;
            _line.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                var gate = _gates[i].GetComponent<Gate>();
                _line.SetPosition(i, gate != null ? gate.OpeningCenterWorld : data.Gates[i].Position + Vector3.up);
            }
        }
    }
}
