using System;
using System.Collections.Generic;
using System.IO;
using GateRush.Track;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace GateRush.TrackEditor
{
    /// <summary>
    /// In-VR track editor. Right controller: point + trigger to select/drag (thumbstick X rotates, Y pushes/pulls,
    /// grip + thumbstick Y scales), A adds the current piece, B deletes. Left controller: thumbstick moves,
    /// trigger/grip fly down/up, X undo, Y next piece type. A wrist panel on the left hand has every action too.
    /// Desktop fallback: mouse pointer (LMB drag, wheel distance, Q/E rotate, Z/C scale), RMB look, WASD + Space/Shift
    /// move, N add, Del delete, Tab type, Ctrl+Z/Y undo/redo, Ctrl+S save.
    /// </summary>
    public sealed class TrackEditorController : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private TrackPrefabs prefabs;
        [SerializeField] private Material markerMaterial;
        [SerializeField] private Material panelMaterial;
        [SerializeField] private Transform xrOrigin;
        [SerializeField] private Camera head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private LineRenderer pointerLine;
        [SerializeField] private LayerMask pointerMask = ~0;
        [SerializeField] private int editorUiLayer = 11;
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float rotateSpeed = 90f;

        private TrackEditModel _model;
        private TrackEditorView _view;
        private GameObject _panel;
        private TMP_Text _status;
        private readonly Dictionary<string, EditorButton> _buttons = new Dictionary<string, EditorButton>();
        private readonly List<string> _pieces = new List<string>();
        private int _piece;
        private string _message = "";

        private EditTarget _selTarget = EditTarget.Gate;
        private int _selIndex = -1;
        private bool _dragging;
        private float _grabDistance, _dragYaw, _dragScale;
        private Vector3 _grabOffset;
        private bool _turnLatched;
        private float _lookYaw, _lookPitch;

        // XR controls, rebound on device change.
        private ButtonControl _rTrigger, _rGrip, _rA, _rB, _lTrigger, _lGrip, _lX, _lY;
        private Vector2Control _rStick, _lStick;
        private bool _controlsDirty = true;

        public event Action<TrackData> TestFlyRequested;
        public event Action ExitRequested;

        public TrackEditModel Model => _model;
        public EditTarget SelectedTarget => _selTarget;
        public int SelectedIndex => _selIndex;
        public string CurrentPiece => _pieces.Count > 0 ? _pieces[_piece] : "Square";

        // ---------- lifecycle ----------

        public void Begin(TrackData data)
        {
            End();
            gameObject.SetActive(true);
            _model = new TrackEditModel(data);
            _model.Changed += OnModelChanged;
            _view = new TrackEditorView(prefabs, transform, markerMaterial, LayerMaskToLayer(pointerMask));
            _pieces.Clear();
            _pieces.AddRange(prefabs.GateTypes());
            _pieces.AddRange(prefabs.PropTypes());
            _piece = 0;
            _selIndex = -1;
            BuildPanel();
            PlaceViewerBehindSpawn();
            OnModelChanged();
            _model.MarkSaved();
            _message = "Point at a gate and pull the trigger to move it.";
            RefreshPanel();
        }

        public void End()
        {
            _dragging = false;
            if (_model != null) _model.Changed -= OnModelChanged;
            _view?.Destroy();
            _view = null;
            if (_panel != null) Destroy(_panel);
            _panel = null;
            _buttons.Clear();
            if (pointerLine != null) pointerLine.enabled = false;
        }

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;
        private void OnDeviceChange(UnityEngine.InputSystem.InputDevice d, InputDeviceChange c) => _controlsDirty = true;

        private void OnModelChanged()
        {
            if (_view == null) return;
            if (_selTarget == EditTarget.Gate && _selIndex >= _model.Data.Gates.Count) _selIndex = _model.Data.Gates.Count - 1;
            if (_selTarget == EditTarget.Prop && _selIndex >= _model.Data.Props.Count) _selIndex = -1;
            _view.Sync(_model.Data, _selTarget, _selIndex);
            if (!_dragging) RefreshPanel();
        }

        // ---------- per frame ----------

        private void Update()
        {
            if (_model == null) return;
            // Cached controls can outlive their device (XR runtime restarts): rebind before touching them.
            if ((_rStick != null && !_rStick.device.added) || (_lStick != null && !_lStick.device.added)) _controlsDirty = true;
            if (_controlsDirty) BindControls();
            float dt = Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool xr = XRSettings.isDeviceActive && _rStick != null;

            Ray ray = PointerRay(xr, mouse);
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, 300f, pointerMask | (1 << editorUiLayer), QueryTriggerInteraction.Ignore);
            DrawPointer(ray, hasHit, hit, xr);

            bool lookDrag = !xr && mouse != null && mouse.rightButton.isPressed;
            bool press = (_rTrigger != null && _rTrigger.wasPressedThisFrame) || (!xr && mouse != null && mouse.leftButton.wasPressedThisFrame);
            bool held = (_rTrigger != null && _rTrigger.isPressed) || (!xr && mouse != null && mouse.leftButton.isPressed);

            if (press && hasHit) OnPress(hit);
            else if (press && !hasHit) Select(EditTarget.Gate, -1);

            if (_dragging)
            {
                if (held) Drag(ray, dt, kb, mouse);
                else
                {
                    _dragging = false;
                    RefreshPanel();
                }
            }

            // Buttons / shortcuts.
            bool ctrl = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            if (Pressed(_rA) || (kb != null && kb.nKey.wasPressedThisFrame)) AddPiece(hasHit ? hit : (RaycastHit?)null, ray);
            if (Pressed(_rB) || (kb != null && kb.deleteKey.wasPressedThisFrame)) DeleteSelected();
            if (Pressed(_lX) || (ctrl && kb.zKey.wasPressedThisFrame)) DoUndo();
            if (ctrl && kb.yKey.wasPressedThisFrame) DoRedo();
            if (Pressed(_lY) || (kb != null && kb.tabKey.wasPressedThisFrame)) CyclePiece(1);
            if (ctrl && kb.sKey.wasPressedThisFrame) Save();

            Move(xr, dt, kb, mouse, lookDrag, ctrl);
            _view?.Billboard(head.transform.position);
        }

        private static bool Pressed(ButtonControl b) => b != null && b.wasPressedThisFrame;

        private Ray PointerRay(bool xr, Mouse mouse)
        {
            if (xr && rightHand != null) return new Ray(rightHand.position, rightHand.forward);
            if (mouse != null && head != null) return head.ScreenPointToRay(mouse.position.ReadValue());
            return new Ray(head.transform.position, head.transform.forward);
        }

        private void DrawPointer(Ray ray, bool hasHit, RaycastHit hit, bool xr)
        {
            if (pointerLine == null) return;
            pointerLine.enabled = xr;
            if (!xr) return;
            pointerLine.positionCount = 2;
            pointerLine.SetPosition(0, ray.origin);
            pointerLine.SetPosition(1, hasHit ? hit.point : ray.origin + ray.direction * 20f);
        }

        // ---------- selection & dragging ----------

        private void OnPress(RaycastHit hit)
        {
            var button = hit.collider.GetComponentInParent<EditorButton>();
            if (button != null)
            {
                button.Clicked?.Invoke();
                return;
            }

            var handle = hit.collider.GetComponentInParent<EditorHandle>();
            if (handle == null)
            {
                Select(EditTarget.Gate, -1);
                return;
            }

            Select(handle.Target, handle.Index);
            var obj = _view.Get(handle.Target, handle.Index);
            _model.BeginEdit();
            _dragging = true;
            _grabDistance = hit.distance;
            _grabOffset = obj.transform.position - hit.point;
            ReadSelected(out _, out _dragYaw, out _dragScale);
        }

        /// <summary>Selects an object without dragging (also used by tests and the panel).</summary>
        public void Select(EditTarget target, int index)
        {
            _selTarget = target;
            _selIndex = index;
            if (_view != null) _view.Sync(_model.Data, _selTarget, _selIndex);
            RefreshPanel();
        }

        private bool HasSelection => _selIndex >= 0 || _selTarget == EditTarget.Spawn;

        private void ReadSelected(out Vector3 pos, out float yaw, out float scale)
        {
            switch (_selTarget)
            {
                case EditTarget.Gate:
                    var g = _model.Data.Gates[_selIndex];
                    pos = g.Position; yaw = g.Yaw; scale = g.Scale;
                    break;
                case EditTarget.Prop:
                    var p = _model.Data.Props[_selIndex];
                    pos = p.Position; yaw = p.Euler.y; scale = p.Scale.x;
                    break;
                default:
                    pos = _model.Data.Spawn.Position; yaw = _model.Data.Spawn.Yaw; scale = 1f;
                    break;
            }
        }

        private void Drag(Ray ray, float dt, Keyboard kb, Mouse mouse)
        {
            Vector2 stick = _rStick != null ? _rStick.ReadValue() : Vector2.zero;
            bool grip = _rGrip != null && _rGrip.isPressed;
            float rotate = stick.x + (kb != null ? (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f) : 0f);
            float scaleIn = (grip ? stick.y : 0f) + (kb != null ? (kb.cKey.isPressed ? 1f : 0f) - (kb.zKey.isPressed && !kb.leftCtrlKey.isPressed ? 1f : 0f) : 0f);
            float push = (grip ? 0f : stick.y) * 6f * dt + (mouse != null ? mouse.scroll.ReadValue().y * 0.01f : 0f);

            _dragYaw += rotate * rotateSpeed * dt;
            _dragScale *= 1f + scaleIn * dt;
            _dragScale = Mathf.Clamp(_dragScale, TrackEditModel.MinScale, TrackEditModel.MaxScale);
            _grabDistance = Mathf.Max(0.5f, _grabDistance + push);

            Vector3 pos = ray.origin + ray.direction * _grabDistance + _grabOffset;
            pos.y = Mathf.Max(0f, pos.y);
            switch (_selTarget)
            {
                case EditTarget.Gate: _model.SetGate(_selIndex, pos, _dragYaw, _dragScale); break;
                case EditTarget.Prop: _model.SetProp(_selIndex, pos, _dragYaw, _dragScale); break;
                default: _model.SetSpawn(new Vector3(pos.x, 0.15f, pos.z), _dragYaw); break;
            }
        }

        // ---------- actions ----------

        public void AddPiece(RaycastHit? hit, Ray ray)
        {
            Vector3 pos = hit.HasValue && hit.Value.collider.GetComponentInParent<EditorButton>() == null
                ? hit.Value.point
                : ray.origin + ray.direction * 6f;
            pos.y = 0f;
            float yaw = head.transform.eulerAngles.y;
            string type = CurrentPiece;
            if (prefabs.Gate(type) != null)
            {
                int insert = _selTarget == EditTarget.Gate && _selIndex >= 0 ? _selIndex + 1 : _model.Data.Gates.Count;
                Select(EditTarget.Gate, _model.AddGate(type, pos, yaw, insert));
                _message = $"Added {type} gate #{_selIndex}.";
            }
            else
            {
                Select(EditTarget.Prop, _model.AddProp(type, pos, yaw));
                _message = $"Added {type}.";
            }
            RefreshPanel();
        }

        public void DeleteSelected()
        {
            if (_selIndex < 0) return;
            if (_selTarget == EditTarget.Gate)
            {
                if (_model.Data.Gates.Count <= 1)
                {
                    _message = "A track needs at least one gate.";
                    RefreshPanel();
                    return;
                }
                _model.RemoveGate(_selIndex);
            }
            else if (_selTarget == EditTarget.Prop) _model.RemoveProp(_selIndex);
            else return;
            Select(EditTarget.Gate, -1);
            _message = "Deleted.";
            RefreshPanel();
        }

        private void MoveOrder(int delta)
        {
            if (_selTarget != EditTarget.Gate || _selIndex < 0) return;
            Select(EditTarget.Gate, _model.MoveOrder(_selIndex, _selIndex + delta));
            _message = _selIndex == 0 ? "This gate is now the start/finish." : $"Gate is now #{_selIndex} in the race order.";
            RefreshPanel();
        }

        private void DoUndo()
        {
            _message = _model.Undo() ? "Undone." : "Nothing to undo.";
            RefreshPanel();
        }

        private void DoRedo()
        {
            _message = _model.Redo() ? "Redone." : "Nothing to redo.";
            RefreshPanel();
        }

        private void CyclePiece(int delta)
        {
            if (_pieces.Count == 0) return;
            _piece = (_piece + delta + _pieces.Count) % _pieces.Count;
            RefreshPanel();
        }

        public string Save()
        {
            var errors = _model.Data.Validate(prefabs.GateTypes(), prefabs.PropTypes());
            errors.RemoveAll(e => e == "Missing id"); // the library assigns one
            try
            {
                string path = TrackLibrary.Default.Save(_model.Data);
                _model.MarkSaved();
                _message = errors.Count == 0 ? $"Saved {Path.GetFileName(path)}." : $"Saved with warnings: {errors[0]}";
                Debug.Log($"[GateRush] Track saved: {path}");
                RefreshPanel();
                return path;
            }
            catch (IOException e)
            {
                _message = "Save failed: " + e.Message;
                RefreshPanel();
                return null;
            }
        }

        private void TestFly()
        {
            if (_model.Dirty) Save();
            TestFlyRequested?.Invoke(TrackData.FromJson(_model.Data.ToJson(false)));
        }

        private void Exit()
        {
            if (_model.Dirty) Save();
            ExitRequested?.Invoke();
        }

        // ---------- locomotion ----------

        private void PlaceViewerBehindSpawn()
        {
            var s = _model.Data.Spawn;
            var rot = Quaternion.Euler(0f, s.Yaw, 0f);
            xrOrigin.SetPositionAndRotation(new Vector3(s.Position.x, 0f, s.Position.z) + rot * new Vector3(0f, 0f, -6f), rot);
            _lookYaw = 0f;
            _lookPitch = 10f;
            if (!XRSettings.isDeviceActive) head.transform.localRotation = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
        }

        private void Move(bool xr, float dt, Keyboard kb, Mouse mouse, bool lookDrag, bool ctrl)
        {
            Vector2 move = _lStick != null ? _lStick.ReadValue() : Vector2.zero;
            float up = (_lGrip != null && _lGrip.isPressed ? 1f : 0f) - (_lTrigger != null && _lTrigger.isPressed ? 1f : 0f);
            if (kb != null && !ctrl)
            {
                move += new Vector2((kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f), (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
                up += (kb.spaceKey.isPressed ? 1f : 0f) - (kb.leftShiftKey.isPressed ? 1f : 0f);
            }

            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 delta = (fwd * move.y + right * move.x) * moveSpeed + Vector3.up * up * moveSpeed * 0.5f;
            Vector3 p = xrOrigin.position + delta * dt;
            p.y = Mathf.Clamp(p.y, -1.5f, 60f);
            xrOrigin.position = p;

            if (xr && !_dragging && _rStick != null)
            {
                float x = _rStick.ReadValue().x;
                if (!_turnLatched && Mathf.Abs(x) > 0.7f)
                {
                    _turnLatched = true;
                    xrOrigin.RotateAround(head.transform.position, Vector3.up, Mathf.Sign(x) * 30f);
                }
                else if (Mathf.Abs(x) < 0.3f) _turnLatched = false;
            }

            if (lookDrag && mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue() * 0.15f;
                _lookYaw += d.x;
                _lookPitch = Mathf.Clamp(_lookPitch - d.y, -80f, 80f);
                head.transform.localRotation = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
            }
        }

        // ---------- wrist panel ----------

        private void BindControls()
        {
            _controlsDirty = false;
            var r = XRController.rightHand;
            var l = XRController.leftHand;
            _rTrigger = r?.TryGetChildControl<ButtonControl>("triggerPressed");
            _rGrip = r?.TryGetChildControl<ButtonControl>("gripPressed");
            _rA = r?.TryGetChildControl<ButtonControl>("primaryButton");
            _rB = r?.TryGetChildControl<ButtonControl>("secondaryButton");
            _rStick = r?.TryGetChildControl<Vector2Control>("thumbstick");
            _lTrigger = l?.TryGetChildControl<ButtonControl>("triggerPressed");
            _lGrip = l?.TryGetChildControl<ButtonControl>("gripPressed");
            _lX = l?.TryGetChildControl<ButtonControl>("primaryButton");
            _lY = l?.TryGetChildControl<ButtonControl>("secondaryButton");
            _lStick = l?.TryGetChildControl<Vector2Control>("thumbstick");
            if (_panel != null) AttachPanel();
        }

        private const float Bw = 0.095f, Bh = 0.036f, Gap = 0.006f;

        private void BuildPanel()
        {
            _panel = new GameObject("Editor Panel");
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Background";
            Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(_panel.transform, false);
            bg.transform.localScale = new Vector3(3f * Bw + 4f * Gap, 7.6f * (Bh + Gap), 1f);
            bg.transform.localPosition = new Vector3(0f, -0.3f * (Bh + Gap), 0.002f);
            var bgr = bg.GetComponent<Renderer>();
            bgr.sharedMaterial = panelMaterial;
            SetColor(bgr, new Color(0.06f, 0.07f, 0.09f, 1f));

            var statusGo = new GameObject("Status");
            statusGo.transform.SetParent(_panel.transform, false);
            statusGo.transform.localPosition = new Vector3(0f, 3f * (Bh + Gap) + 0.012f, 0f);
            var status = statusGo.AddComponent<TextMeshPro>();
            status.fontSize = 0.12f;
            status.alignment = TextAlignmentOptions.Center;
            status.rectTransform.sizeDelta = new Vector2(3f * Bw + 2f * Gap, 0.05f);
            status.textWrappingMode = TextWrappingModes.Normal;
            _status = status;

            string[,] grid =
            {
                { "<", "piece", ">" },
                { "Add", "Delete", "snap" },
                { "Earlier", "Later", "Reverse" },
                { "Laps -", "laps", "Laps +" },
                { "Undo", "Redo", "Save" },
                { "Test fly", "Spawn", "Exit" },
            };
            for (int row = 0; row < grid.GetLength(0); row++)
            for (int col = 0; col < 3; col++)
            {
                string id = grid[row, col];
                var pos = new Vector3((col - 1) * (Bw + Gap), (2 - row) * (Bh + Gap), 0f);
                _buttons[id] = MakeButton(id, pos);
            }

            _buttons["<"].Clicked = () => CyclePiece(-1);
            _buttons[">"].Clicked = () => CyclePiece(1);
            _buttons["piece"].Clicked = () => AddPiece(null, new Ray(head.transform.position, head.transform.forward));
            _buttons["Add"].Clicked = () => AddPiece(null, new Ray(head.transform.position, head.transform.forward));
            _buttons["Delete"].Clicked = DeleteSelected;
            _buttons["snap"].Clicked = () => { _model.Snap = !_model.Snap; RefreshPanel(); };
            _buttons["Earlier"].Clicked = () => MoveOrder(-1);
            _buttons["Later"].Clicked = () => MoveOrder(1);
            _buttons["Reverse"].Clicked = () => { _model.ReverseDirection(); _message = "Race direction reversed."; RefreshPanel(); };
            _buttons["Laps -"].Clicked = () => _model.SetLaps(_model.Data.Laps - 1);
            _buttons["laps"].Clicked = () => { };
            _buttons["Laps +"].Clicked = () => _model.SetLaps(_model.Data.Laps + 1);
            _buttons["Undo"].Clicked = DoUndo;
            _buttons["Redo"].Clicked = DoRedo;
            _buttons["Save"].Clicked = () => Save();
            _buttons["Test fly"].Clicked = TestFly;
            _buttons["Spawn"].Clicked = () => Select(EditTarget.Spawn, 0);
            _buttons["Exit"].Clicked = Exit;

            AttachPanel();
        }

        private EditorButton MakeButton(string id, Vector3 localPos)
        {
            var go = new GameObject("Button " + id) { layer = editorUiLayer };
            go.transform.SetParent(_panel.transform, false);
            go.transform.localPosition = localPos;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(Bw, Bh, 0.01f);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Face";
            Destroy(quad.GetComponent<Collider>());
            quad.layer = editorUiLayer;
            quad.transform.SetParent(go.transform, false);
            quad.transform.localScale = new Vector3(Bw, Bh, 1f);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = panelMaterial;
            SetColor(r, new Color(0.2f, 0.23f, 0.3f, 1f));

            var labelGo = new GameObject("Label") { layer = editorUiLayer };
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            var t = labelGo.AddComponent<TextMeshPro>();
            t.fontSize = 0.11f;
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = new Vector2(Bw, Bh);
            t.text = id;

            var b = go.AddComponent<EditorButton>();
            b.Label = t;
            b.Background = r;
            return b;
        }

        /// <summary>Panel on the left wrist in VR; bottom-left of the view on the desktop.</summary>
        private void AttachPanel()
        {
            bool xr = XRSettings.isDeviceActive && leftHand != null && XRController.leftHand != null;
            var t = _panel.transform;
            if (xr)
            {
                t.SetParent(leftHand, false);
                t.localPosition = new Vector3(0f, 0.06f, 0.08f);
                t.localRotation = Quaternion.Euler(50f, 0f, 0f);
                t.localScale = Vector3.one;
            }
            else
            {
                t.SetParent(head.transform, false);
                t.localPosition = new Vector3(-0.2f, -0.1f, 0.45f);
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one * 0.8f;
            }
        }

        private void RefreshPanel()
        {
            if (_panel == null || _model == null) return;
            var d = _model.Data;
            string type = CurrentPiece;
            bool isGate = prefabs.Gate(type) != null;
            _buttons["piece"].Label.text = (isGate ? "Gate: " : "Prop: ") + type;
            _buttons["snap"].Label.text = _model.Snap ? "Snap: on" : "Snap: off";
            _buttons["laps"].Label.text = $"{d.Laps} laps";
            SetColor(_buttons["Save"].Background, _model.Dirty ? new Color(0.55f, 0.35f, 0.1f, 1f) : new Color(0.2f, 0.23f, 0.3f, 1f));

            string sel = _selTarget == EditTarget.Spawn ? "spawn pad"
                : _selIndex < 0 ? "nothing"
                : _selTarget == EditTarget.Gate ? (_selIndex == 0 ? "start/finish gate" : $"gate #{_selIndex}") + $" ({d.Gates[_selIndex].Type})"
                : $"prop ({d.Props[_selIndex].Type})";
            _status.text = $"<b>{d.Name}</b>  {d.Gates.Count} gates, {d.Props.Count} props{(_model.Dirty ? "  <color=#ffb347>unsaved</color>" : "")}\n" +
                           $"Selected: {sel}\n<color=#9ab>{_message}</color>";
        }

        private static void SetColor(Renderer r, Color c)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColor, c);
            r.SetPropertyBlock(mpb);
        }

        private static int LayerMaskToLayer(LayerMask mask)
        {
            int m = mask.value;
            for (int i = 0; i < 32; i++) if ((m & (1 << i)) != 0) return i;
            return 0;
        }
    }
}
