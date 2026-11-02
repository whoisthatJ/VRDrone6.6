using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GateRush.Core;
using GateRush.Game;
using GateRush.Garage;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace GateRush.UI
{
    /// <summary>
    /// The garage (T7/T8): the quad on the workbench and the build screen around it. Top bar: Back, part tabs
    /// (Frames · Motors · Props · LiPos · Extras · Style · Color), Save drone. Left: build name, preset/saved builds,
    /// stat rows with bars and live deltas while hovering a part (green better, red worse), T/W, hover throttle and
    /// hover time, then Drag model, MOI, Spin, Data, See and Fly. Right: size filter, a 3×3 grid of part cards with
    /// rendered thumbnails and paging, and the selected part's card. Bottom bar: build summary, input device, menu.
    /// Every stat comes from <see cref="BuildComposer"/>, i.e. the same physics the quad flies with.
    /// </summary>
    public sealed class GarageController : MonoBehaviour
    {
        [SerializeField] private RectTransform canvas;
        [SerializeField] private Transform turntable;
        [SerializeField] private GarageCamera orbit;
        [SerializeField] private Volume postVolume;
        [SerializeField] private QuadModelMaterials materials = new QuadModelMaterials();
        [SerializeField] private Material moiMaterial;
        [SerializeField] private TrackCatalog tracks;
        [Tooltip("Where part thumbnails are rendered (out of sight).")]
        [SerializeField] private Vector3 thumbnailStage = new Vector3(0f, -200f, 0f);
        [SerializeField] private float spinDegPerSec = 18f;

        private static readonly Color Bar = new Color(0.04f, 0.05f, 0.07f, 0.92f);
        private static readonly Color Panel = new Color(0.07f, 0.12f, 0.18f, 0.88f);
        private static readonly Color PanelLine = new Color(1f, 1f, 1f, 0.75f);
        private static readonly Color Good = new Color(0.35f, 0.95f, 0.35f);
        private static readonly Color Bad = new Color(1f, 0.25f, 0.2f);
        private static readonly Color Green = new Color(0.1f, 0.72f, 0.2f);
        private static readonly Color Yellow = new Color(1f, 0.85f, 0.05f);
        private static readonly Color Blue = new Color(0.12f, 0.45f, 0.95f);
        private static readonly Color Orange = new Color(1f, 0.62f, 0.05f);

        private static readonly (PartSlot slot, string label, Color line)[] Tabs =
        {
            (PartSlot.Frame, "FRAMES", new Color(1f, 0.6f, 0.05f)),
            (PartSlot.Motor, "MOTORS", new Color(0.2f, 0.5f, 1f)),
            (PartSlot.Prop, "PROPS", new Color(0.2f, 0.5f, 1f)),
            (PartSlot.Battery, "LIPOS", new Color(0.2f, 0.5f, 1f)),
            (PartSlot.Extra, "EXTRAS", new Color(0.8f, 0.2f, 0.9f)),
            (PartSlot.Style, "STYLE", new Color(0.9f, 0.15f, 0.15f)),
            (PartSlot.Color, "COLOR", new Color(0.9f, 0.15f, 0.15f)),
        };

        private enum Stat { Thrust, Weight, TopSpeed, Torque, Drag, Rpm, Temp }

        private sealed class StatRow
        {
            public TMP_Text Delta, Value;
            public Image Fill, DeltaBar;
        }

        private const int CardsPerPage = 9;

        private QuadBuild _build;
        private BuildStats _stats;
        private PartSlot _tab = PartSlot.Frame;
        private SizeClass _sizeFilter;
        private int _page;
        private bool _spin = true, _see, _moi;
        private QuadModelBuilder.Result _model;
        private GameObject _moiGo;
        private Dictionary<string, Vector3> _homePositions = new Dictionary<string, Vector3>();
        private float _explode;
        private bool _dirty;

        private readonly Dictionary<Stat, StatRow> _rows = new Dictionary<Stat, StatRow>();
        private TMP_InputField _name;
        private TMP_Text _preset, _extraStats, _sizeLabel, _pageLabel, _infoName, _infoBody, _infoBrand, _summary, _device, _toast;
        private Button _dragButton, _moiButton, _spinButton, _seeButton;
        private RectTransform _grid, _dataPanel, _tabBar;
        private readonly List<Button> _tabButtons = new List<Button>();
        private TMP_Text _dataText;
        private float _toastUntil;
        private List<QuadBuild> _choices = new List<QuadBuild>();
        private int _choice;

        private Camera _thumbCam;
        private readonly Dictionary<string, RenderTexture> _thumbs = new Dictionary<string, RenderTexture>();

        private DepthOfField _dof;

        // ---------- lifecycle ----------

        private void Start()
        {
            _build = InitialBuild();
            _sizeFilter = _build.Size;
            if (postVolume != null && postVolume.profile != null) postVolume.profile.TryGet(out _dof);
            BuildUi();
            Rebuild();
        }

        private void OnDestroy()
        {
            foreach (var rt in _thumbs.Values)
                if (rt != null) rt.Release();
        }

        private static QuadBuild InitialBuild()
        {
            string id = SaveSystem.Settings.QuadId;
            var b = BuildLibrary.Default.Resolve(id);
            if (b != null && BuildComposer.Validate(b).Count == 0) return b;
            return id switch
            {
                "freestyle5" => QuadPresets.Freestyle5,
                "whoop65" => QuadPresets.Whoop65,
                _ => QuadPresets.Race5,
            };
        }

        private void Update()
        {
            if (_spin && turntable != null && !_see) turntable.Rotate(0f, spinDegPerSec * Time.unscaledDeltaTime, 0f, Space.World);
            // Exploded view eases in and out.
            float target = _see ? 1f : 0f;
            if (!Mathf.Approximately(_explode, target))
            {
                _explode = Mathf.MoveTowards(_explode, target, Time.unscaledDeltaTime * 2.5f);
                ApplyExplode();
            }
            if (_dof != null && orbit != null) _dof.focusDistance.value = orbit.Distance;
            if (_toast != null && _toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toast.gameObject.SetActive(false);
            if (_device != null) _device.text = DeviceName();
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (_dataPanel != null && _dataPanel.gameObject.activeSelf) _dataPanel.gameObject.SetActive(false);
                else Back();
            }
        }

        // ---------- build changes ----------

        private void SetBuild(QuadBuild b, bool reframe = false)
        {
            var problems = BuildComposer.Validate(b);
            if (problems.Count > 0)
            {
                Toast(problems[0]);
                return;
            }
            bool sizeChanged = b.Size != _build.Size;
            _build = b;
            _dirty = true;
            if (sizeChanged)
            {
                _sizeFilter = b.Size;
                _page = 0;
            }
            Rebuild(reframe || sizeChanged);
        }

        private void Rebuild(bool reframe = true)
        {
            _stats = BuildComposer.Stats(_build);
            Quaternion spin = turntable != null ? turntable.rotation : Quaternion.identity;
            if (_model?.Root != null) Destroy(_model.Root);
            if (turntable != null)
            {
                turntable.rotation = Quaternion.identity;
                _model = QuadModelBuilder.Build(_build, materials, turntable, turntable.gameObject.layer);
                _homePositions = _model.Parts.ToDictionary(kv => kv.Key, kv => kv.Value.localPosition);
                ApplyExplode();
                turntable.rotation = spin;
                if (reframe && orbit != null) orbit.Frame(QuadModelBuilder.Bounds(_model.Root));
                UpdateMoi();
            }
            RefreshStats(null);
            RefreshCards();
            RefreshInfo(CurrentPart(_tab));
            if (_name != null && _name.text != _build.Name) _name.SetTextWithoutNotify(_build.Name);
            if (_summary != null) _summary.text = $"{_build.Name.ToUpperInvariant()}   <color=#9aa>{PartCatalog.SizeLabel(_build.Size).ToUpperInvariant()}  ·  {_build.Frame.Name.ToUpperInvariant()}</color>";
            if (_dragButton != null) UiKit.SetButtonText(_dragButton, _build.AdvancedDrag ? "ADVANCED\nDRAG MODEL" : "SIMPLE\nDRAG MODEL");
            if (_dataPanel != null && _dataPanel.gameObject.activeSelf) _dataText.text = SpecSheet();
        }

        private PartDef CurrentPart(PartSlot slot)
        {
            if (slot == PartSlot.Extra) return _build.Extras.FirstOrDefault();
            return PartCatalog.Find(_build.PartId(slot));
        }

        private void ApplyExplode()
        {
            if (_model == null) return;
            float size = Mathf.Max(0.05f, _build.Frame.WheelbaseMm * 0.001f);
            var lift = new Dictionary<string, float> { ["Frame"] = 0f, ["Motors"] = 0.18f, ["Props"] = 0.45f, ["Battery"] = 0.62f, ["Extras"] = 0.3f, ["LEDs"] = 0.12f };
            foreach (var kv in _model.Parts)
            {
                if (!_homePositions.TryGetValue(kv.Key, out var home)) continue;
                lift.TryGetValue(kv.Key, out float l);
                kv.Value.localPosition = home + Vector3.up * (l * size * Mathf.SmoothStep(0f, 1f, _explode));
            }
        }

        private void UpdateMoi()
        {
            if (_moiGo == null)
            {
                _moiGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _moiGo.name = "Inertia Ellipsoid";
                Destroy(_moiGo.GetComponent<Collider>());
                if (moiMaterial != null) _moiGo.GetComponent<Renderer>().sharedMaterial = moiMaterial;
                _moiGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            _moiGo.SetActive(_moi && _model != null);
            if (!_moi || _model == null) return;
            // Inertia ellipsoid: radius along an axis ∝ 1/√I, scaled so the largest radius matches the frame.
            var p = BuildComposer.Compose(_build);
            var r = new Vector3(1f / Mathf.Sqrt(p.Inertia.x), 1f / Mathf.Sqrt(p.Inertia.y), 1f / Mathf.Sqrt(p.Inertia.z));
            float scale = _build.Frame.WheelbaseMm * 0.001f * 0.9f / Mathf.Max(r.x, r.y, r.z);
            _moiGo.transform.SetParent(_model.Root.transform, false);
            _moiGo.transform.localPosition = Vector3.up * (_build.Frame.StackMm * 0.001f * 0.6f);
            _moiGo.transform.localScale = r * scale * 2f;
        }

        // ---------- stats ----------

        private static float Value(in BuildStats s, Stat st) => st switch
        {
            Stat.Thrust => s.ThrustG,
            Stat.Weight => s.WeightG,
            Stat.TopSpeed => s.TopSpeedKmh,
            Stat.Torque => s.TorqueNm,
            Stat.Drag => s.Drag,
            Stat.Rpm => s.Rpm,
            _ => s.PeakTempC,
        };

        /// <summary>Higher is better for these; lower for weight, drag and temperature.</summary>
        private static bool HigherIsBetter(Stat st) => st != Stat.Weight && st != Stat.Drag && st != Stat.Temp;

        /// <summary>Bar fill: value over a top-of-the-catalogue reference.</summary>
        private static float Fill(Stat st, float v) => Mathf.Clamp01(st switch
        {
            Stat.Thrust => v / 9000f,
            Stat.Weight => v / 900f,
            Stat.TopSpeed => v / 180f,
            Stat.Torque => v / 0.3f,
            Stat.Drag => v / 0.04f,
            Stat.Rpm => v / 70000f,
            _ => (v - 20f) / 100f,
        });

        private static string Format(Stat st, float v) => st switch
        {
            Stat.Thrust => Inv($"{v:0} g"),
            Stat.Weight => Inv($"{v:0} g"),
            Stat.TopSpeed => Inv($"{v:0} km/h"),
            Stat.Torque => Inv($"{v:0.000} N·m"),
            Stat.Drag => Inv($"{v:0.0000} kg/m"),
            Stat.Rpm => Inv($"{v:0}"),
            _ => Inv($"{v:0} °C"),
        };

        private static string FormatDelta(Stat st, float d) => st switch
        {
            Stat.Torque => Inv($"{d:+0.000;-0.000}"),
            Stat.Drag => Inv($"{d:+0.0000;-0.0000}"),
            _ => Inv($"{d:+0;-0}"),
        };

        private void RefreshStats(QuadBuild preview)
        {
            BuildStats? alt = null;
            if (preview != null && BuildComposer.Validate(preview).Count == 0) alt = BuildComposer.Stats(preview);
            foreach (Stat st in Enum.GetValues(typeof(Stat)))
            {
                if (!_rows.TryGetValue(st, out var row)) continue;
                float v = Value(_stats, st);
                row.Value.text = Format(st, v);
                SetFill(row.Fill, Fill(st, v));
                if (alt.HasValue)
                {
                    float nv = Value(alt.Value, st);
                    float d = nv - v;
                    bool significant = Mathf.Abs(d) > Mathf.Abs(v) * 0.001f + 1e-5f;
                    bool better = HigherIsBetter(st) ? d > 0f : d < 0f;
                    row.Delta.text = significant ? $"({FormatDelta(st, d)})" : "";
                    row.Delta.color = better ? Good : Bad;
                    row.DeltaBar.gameObject.SetActive(significant);
                    if (significant)
                    {
                        row.DeltaBar.color = better ? Good : Bad;
                        float a = Fill(st, v), b = Fill(st, nv);
                        var rt = row.DeltaBar.rectTransform;
                        rt.anchorMin = new Vector2(Mathf.Min(a, b), 0f);
                        rt.anchorMax = new Vector2(Mathf.Max(a, b), 1f);
                    }
                }
                else
                {
                    row.Delta.text = "";
                    row.DeltaBar.gameObject.SetActive(false);
                }
            }
            var s = alt ?? _stats;
            if (_extraStats != null)
                _extraStats.text = Inv($"T/W {s.ThrustToWeight:0.0}:1   HOVER {s.HoverThrottle * 100f:0}%   HOVER TIME {s.HoverMinutes:0} MIN");
        }

        private static void SetFill(Image fill, float f)
        {
            var rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(f, 1f);
        }

        // ---------- cards ----------

        private List<PartDef> PartsForTab()
        {
            var list = PartCatalog.InSlot(_tab).ToList();
            if (_tab == PartSlot.Frame) return list.Where(p => p.Fits(_sizeFilter)).ToList();
            if (_tab == PartSlot.Style || _tab == PartSlot.Color) return list;
            return list.Where(p => p.Fits(_build.Size)).ToList();
        }

        private void RefreshCards()
        {
            if (_grid == null) return;
            UiKit.Clear(_grid);
            var parts = PartsForTab();
            int pages = Mathf.Max(1, (parts.Count + CardsPerPage - 1) / CardsPerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.text = pages > 1 ? $"{_page + 1}/{pages}" : "";
            _sizeLabel.text = (_tab == PartSlot.Frame ? PartCatalog.SizeLabel(_sizeFilter) : PartCatalog.SizeLabel(_build.Size)).ToUpperInvariant() + (_tab == PartSlot.Frame ? "  (click: size)" : "");
            const float cell = 128f, gap = 9f;
            for (int i = 0; i < CardsPerPage; i++)
            {
                int idx = _page * CardsPerPage + i;
                if (idx >= parts.Count) break;
                var part = parts[idx];
                int r = i / 3, c = i % 3;
                Card(part, new Vector2(c * (cell + gap), -r * (cell + gap)), cell);
            }
        }

        private bool IsSelected(PartDef p) => p.Slot == PartSlot.Extra ? _build.ExtraIds.Contains(p.Id) : _build.PartId(p.Slot) == p.Id;

        private void Card(PartDef part, Vector2 pos, float cell)
        {
            bool selected = IsSelected(part);
            var rt = UiKit.Rect(_grid, part.Id, pos, new Vector2(cell, cell));
            var bg = UiKit.Image(rt, new Color(0.16f, 0.17f, 0.19f, 0.95f), true);
            if (part is ColorDef col)
            {
                var sw = UiKit.Rect(rt, "Swatch", new Vector2(20f, -20f), new Vector2(cell - 40f, cell - 52f));
                UiKit.Image(sw, col.Accent);
            }
            else
            {
                var img = UiKit.Rect(rt, "Thumb", new Vector2(2f, -2f), new Vector2(cell - 4f, cell - 4f));
                var raw = img.gameObject.AddComponent<RawImage>();
                raw.texture = Thumbnail(part);
                raw.raycastTarget = false;
            }
            var label = UiKit.Label(rt, part.Name.ToUpperInvariant(), new Vector2(4f, -cell + 30f), new Vector2(cell - 8f, 28f), 13f, TextAlignmentOptions.Bottom, Color.white);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.fontStyle = FontStyles.Bold;
            if (selected) Outline(rt, Green, 4f);

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() =>
            {
                SetBuild(_build.With(part));
                RefreshInfo(part);
            });
            var trigger = rt.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, () =>
            {
                RefreshInfo(part);
                if (!selected || part.Slot == PartSlot.Extra) RefreshStats(_build.With(part));
            });
            AddTrigger(trigger, EventTriggerType.PointerExit, () =>
            {
                RefreshStats(null);
                RefreshInfo(CurrentPart(_tab));
            });
        }

        private static void AddTrigger(EventTrigger t, EventTriggerType type, Action a)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(_ => a());
            t.triggers.Add(e);
        }

        private void RefreshInfo(PartDef part)
        {
            if (_infoName == null) return;
            if (part == null)
            {
                _infoName.text = _tab == PartSlot.Extra ? "NO EXTRAS" : "";
                _infoBody.text = _tab == PartSlot.Extra ? "Click a card to add or remove it." : "";
                _infoBrand.text = "";
                return;
            }
            _infoName.text = part.Name.ToUpperInvariant();
            _infoBrand.text = (part.Brand ?? "GateRush").ToUpperInvariant();
            var sb = new StringBuilder();
            switch (part)
            {
                case FrameDef f:
                    sb.AppendLine(Inv($"SIZE: {f.WheelbaseMm:0} mm   ({f.Geometry})"));
                    sb.AppendLine(Inv($"WEIGHT: {f.MassG:0} g   PROPS UP TO {f.MaxPropIn:0.#}\""));
                    break;
                case MotorDef m:
                    sb.AppendLine(Inv($"STATOR: {m.StatorCode}   {m.Kv:0} KV"));
                    sb.AppendLine(Inv($"WEIGHT: {m.MassG:0.#} g   Rm {m.Rm * 1000f:0} mΩ"));
                    break;
                case PropDef p:
                    sb.AppendLine(Inv($"SIZE: {p.DiameterIn:0.##}x{p.PitchIn:0.##}x{p.Blades}"));
                    sb.AppendLine(Inv($"WEIGHT: {p.MassG:0.#} g"));
                    break;
                case BatteryDef bt:
                    sb.AppendLine(Inv($"{bt.Cells}S  {bt.CapacityMah:0} mAh  {bt.CRating:0}C"));
                    sb.AppendLine(Inv($"WEIGHT: {bt.MassG:0} g"));
                    break;
                default:
                    if (part.MassG > 0f) sb.AppendLine(Inv($"WEIGHT: +{part.MassG:0} g"));
                    break;
            }
            sb.Append("<color=#ffd84a>").Append(part.Description).Append("</color>");
            _infoBody.text = sb.ToString();
        }

        // ---------- thumbnails ----------

        private Texture Thumbnail(PartDef part)
        {
            string key = part.Id + "|" + _build.ColorId + "|" + _build.StyleId + "|" + (_tab == PartSlot.Frame || _tab == PartSlot.Style ? "" : _build.FrameId);
            if (_thumbs.TryGetValue(key, out var cached) && cached != null) return cached;

            if (_thumbCam == null)
            {
                var go = new GameObject("Thumbnail Camera");
                _thumbCam = go.AddComponent<Camera>();
                _thumbCam.enabled = false;
                _thumbCam.clearFlags = CameraClearFlags.SolidColor;
                _thumbCam.backgroundColor = new Color(0.17f, 0.18f, 0.2f, 1f);
                _thumbCam.fieldOfView = 30f;
                _thumbCam.nearClipPlane = 0.01f;
                _thumbCam.farClipPlane = 10f;
                var data = _thumbCam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                var light = new GameObject("Thumbnail Light").AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 3f;
                light.intensity = 2.5f;
                light.transform.SetParent(go.transform, false);
                light.transform.localPosition = new Vector3(0.3f, 0.5f, 0f);
            }

            var b = part.Slot == PartSlot.Extra ? _build.Clone() : _build.With(part);
            if (part is ExtraDef ex && !b.ExtraIds.Contains(ex.Id)) b.ExtraIds.Add(ex.Id);
            if (BuildComposer.Validate(b).Count > 0) b.FitTo(b.Size);
            var stage = new GameObject("Thumbnail Stage").transform;
            stage.position = thumbnailStage;
            var model = QuadModelBuilder.Build(b, materials, stage);
            // Show only the part in question (whole quad for frames, styles and colours).
            string only = part.Slot switch
            {
                PartSlot.Motor => "Motors",
                PartSlot.Prop => "Props",
                PartSlot.Battery => "Battery",
                PartSlot.Extra => ex2(part),
                _ => null,
            };
            Bounds bounds;
            if (only != null && model.Parts.TryGetValue(only, out var keep))
            {
                foreach (var kv in model.Parts)
                    if (kv.Value != keep) kv.Value.gameObject.SetActive(false);
                bounds = QuadModelBuilder.Bounds(keep.gameObject);
                if (part.Slot == PartSlot.Motor || part.Slot == PartSlot.Prop)
                {
                    // One motor / one prop: frame the first one.
                    var p0 = model.Props[0].position;
                    float r = part.Slot == PartSlot.Prop ? b.Prop.DiameterIn * 0.0254f * 0.55f : b.Motor.StatorD * 0.0012f + 0.01f;
                    bounds = new Bounds(part.Slot == PartSlot.Prop ? p0 : new Vector3(p0.x, p0.y - 0.01f, p0.z), Vector3.one * r * 2f);
                }
            }
            else bounds = QuadModelBuilder.Bounds(model.Root);

            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float dist = size * 0.5f / Mathf.Tan(_thumbCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
            var dir = Quaternion.Euler(38f, -140f, 0f) * Vector3.forward;
            _thumbCam.transform.SetPositionAndRotation(bounds.center - dir * dist, Quaternion.LookRotation(dir));
            _thumbCam.nearClipPlane = dist * 0.05f;
            _thumbCam.farClipPlane = dist * 4f;

            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { name = "Thumb " + part.Id, antiAliasing = 4 };
            _thumbCam.targetTexture = rt;
            _thumbCam.Render();
            _thumbCam.targetTexture = null;
            Destroy(stage.gameObject);
            _thumbs[key] = rt;
            return rt;
        }

        private static string ex2(PartDef part) => part is ExtraDef e && e.Kind == ExtraKind.LedKit ? "LEDs" : "Extras";

        // ---------- actions ----------

        private void Back()
        {
            GameFlow.Menu();
        }

        private void Save()
        {
            var lib = BuildLibrary.Default;
            lib.Save(_build);
            SaveSystem.Settings.QuadId = BuildLibrary.IdPrefix + _build.Id;
            SaveSystem.Commit();
            _dirty = false;
            LoadChoices();
            Toast($"Saved \"{_build.Name}\" and selected it for flying.");
        }

        private void Fly()
        {
            if (_dirty || !BuildLibrary.IsBuildId(SaveSystem.Settings.QuadId)) Save();
            if (tracks == null || tracks.Tracks.Count == 0) return;
            string id = GameFlow.Resolve(tracks, SaveSystem.Settings.TrackId) != null ? SaveSystem.Settings.TrackId : tracks.Tracks[0].Id;
            GameFlow.Race(tracks, id);
        }

        private void LoadChoices()
        {
            _choices = QuadPresets.All.ToList();
            _choices.AddRange(BuildLibrary.Default.LoadAll());
            _choice = Mathf.Max(0, _choices.FindIndex(c => c.Id == _build.Id));
            if (_preset != null) _preset.text = PresetLabel();
        }

        private string PresetLabel()
        {
            if (_choices.Count == 0) return "";
            var c = _choices[_choice];
            bool preset = c.Id.StartsWith("preset-", StringComparison.Ordinal);
            return (preset ? "PRESET: " : "SAVED: ") + c.Name.ToUpperInvariant();
        }

        private void StepChoice(int dir)
        {
            if (_choices.Count == 0) return;
            _choice = (_choice + dir + _choices.Count) % _choices.Count;
            _preset.text = PresetLabel();
            _build = _choices[_choice].Clone();
            _sizeFilter = _build.Size;
            _page = 0;
            _dirty = false;
            Rebuild();
        }

        private void Toast(string message)
        {
            if (_toast == null) return;
            _toast.text = message;
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 3f;
        }

        private string SpecSheet()
        {
            var p = BuildComposer.Compose(_build);
            var s = _stats;
            var sb = new StringBuilder();
            sb.AppendLine($"<b><size=130%>{_build.Name.ToUpperInvariant()}</size></b>");
            sb.AppendLine(Inv($"{_build.Frame.Name} · {_build.Motor.Name} · {_build.Prop.Name} · {_build.Battery.Name}"));
            if (_build.Extras.Any()) sb.AppendLine("Extras: " + string.Join(", ", _build.Extras.Select(e => e.Name)));
            sb.AppendLine();
            sb.AppendLine(Inv($"Mass {p.Mass * 1000f:0} g     Inertia (pitch/yaw/roll) {p.Inertia.x * 1e6f:0} / {p.Inertia.y * 1e6f:0} / {p.Inertia.z * 1e6f:0} (x10^-6 kg m²)"));
            sb.AppendLine(Inv($"Arms {p.ArmX * 1000f:0} × {p.ArmZ * 1000f:0} mm     Drag (side/top/front) {p.Aero.QuadraticDrag.x:0.0000} / {p.Aero.QuadraticDrag.y:0.0000} / {p.Aero.QuadraticDrag.z:0.0000} kg/m"));
            sb.AppendLine(Inv($"Full throttle: {s.CurrentA:0.0} A per motor at {s.VoltageV:0.0} V,  {s.Rpm:0} rpm,  {s.ThrustG / 4f:0} g per motor"));
            sb.AppendLine(Inv($"Prop pitch speed {p.Motors.PropPitchSpeed:0} m/s     Motor spool {p.Motors.TimeConstantUp * 1000f:0} ms     Yaw torque/thrust {p.Motors.YawTorquePerThrust:0.0000} m"));
            sb.AppendLine(Inv($"Top speed {s.TopSpeedKmh:0} km/h     T/W {s.ThrustToWeight:0.0}:1     Hover {s.HoverThrottle * 100f:0} %, {s.HoverMinutes:0} min     Peak motor temp ~{s.PeakTempC:0} °C (estimate)"));
            sb.AppendLine(Inv($"PID pitch P {p.Pid.Pitch.P:0.000} I {p.Pid.Pitch.I:0.000} D {p.Pid.Pitch.D:0.00000}    yaw P {p.Pid.Yaw.P:0.000}"));
            sb.AppendLine();
            sb.AppendLine("<b>THROTTLE   THRUST (all)   CURRENT/motor   RPM</b>");
            foreach (var (t, g, a, rpm) in BuildComposer.Curve(_build))
                sb.AppendLine(Inv($"{t * 100f,6:0} %   {g,10:0} g   {a,12:0.0} A   {rpm,9:0}"));
            sb.AppendLine();
            sb.AppendLine("<color=#9aa>Stats are computed from the parts: motor and prop operating point on the battery under load, " +
                          "drag areas and part masses. The same numbers fly in the simulator.</color>");
            return sb.ToString();
        }

        private static string DeviceName()
        {
            if (Joystick.current != null) return "RADIO";
            if (Gamepad.current != null) return "GAMEPAD";
            return "KEYBOARD";
        }

        // ---------- layout ----------

        private void BuildUi()
        {
            var root = canvas;
            UiKit.Clear(root);

            // Top bar.
            var top = Anchored(root, "Top Bar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -58f), Vector2.zero);
            UiKit.Image(top, Bar);
            var back = FlatButton(top, "<   BACK", new Vector2(10f, -6f), new Vector2(190f, 46f), new Color(0f, 0f, 0f, 0f), Back, 24f);
            Outline((RectTransform)back.transform, PanelLine, 2f);
            _tabBar = UiKit.Rect(top, "Tabs", new Vector2(370f, -6f), new Vector2(1180f, 46f));
            float x = 0f;
            foreach (var (slot, label, line) in Tabs)
            {
                var s = slot;
                var tb = FlatButton(_tabBar, label, new Vector2(x, 0f), new Vector2(160f, 46f), new Color(0.3f, 0.32f, 0.36f, 0.95f), () => SelectTab(s), 22f);
                var ul = UiKit.Rect(tb.transform, "Underline", new Vector2(0f, -42f), new Vector2(160f, 4f));
                UiKit.Image(ul, line);
                _tabButtons.Add(tb);
                x += 168f;
            }
            var save = FlatButton(top, "SAVE DRONE", Vector2.zero, new Vector2(210f, 46f), Green, Save, 24f);
            var srt = (RectTransform)save.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 1f);
            srt.anchoredPosition = new Vector2(-10f, -6f);

            // Left panel.
            var left = UiKit.Rect(root, "Left", new Vector2(60f, -125f), new Vector2(360f, 860f));
            var nameRt = UiKit.Rect(left, "Name", Vector2.zero, new Vector2(360f, 48f));
            UiKit.Image(nameRt, Panel, true);
            Outline(nameRt, PanelLine, 2f);
            _name = NameField(nameRt);
            var presetRt = UiKit.Rect(left, "Preset", new Vector2(0f, -56f), new Vector2(360f, 48f));
            UiKit.Image(presetRt, Panel);
            Outline(presetRt, PanelLine, 2f);
            _preset = UiKit.Label(presetRt, "", new Vector2(14f, 0f), new Vector2(280f, 48f), 20f, TextAlignmentOptions.MidlineLeft, Color.white);
            FlatButton(presetRt, "^", new Vector2(300f, -2f), new Vector2(56f, 22f), new Color(0, 0, 0, 0), () => StepChoice(-1), 16f);
            FlatButton(presetRt, "v", new Vector2(300f, -24f), new Vector2(56f, 22f), new Color(0, 0, 0, 0), () => StepChoice(1), 16f);

            var names = new[] { (Stat.Thrust, "THRUST"), (Stat.Weight, "WEIGHT"), (Stat.TopSpeed, "TOP SPEED"), (Stat.Torque, "TORQUE"), (Stat.Drag, "DRAG"), (Stat.Rpm, "RPM"), (Stat.Temp, "PEAK TEMP") };
            float y = -118f;
            foreach (var (st, label) in names)
            {
                var row = UiKit.Rect(left, label, new Vector2(0f, y), new Vector2(360f, 52f));
                UiKit.Image(row, Panel);
                var track = UiKit.Rect(row, "Bar", new Vector2(0f, 0f), new Vector2(360f, 5f));
                UiKit.Image(track, new Color(1f, 1f, 1f, 0.15f));
                var fill = UiKit.Image(UiKit.Rect(track, "Fill", Vector2.zero, Vector2.zero), Color.white);
                SetFill(fill, 0.5f);
                var deltaBar = UiKit.Image(UiKit.Rect(track, "Delta", Vector2.zero, Vector2.zero), Good);
                deltaBar.rectTransform.offsetMin = deltaBar.rectTransform.offsetMax = Vector2.zero;
                deltaBar.gameObject.SetActive(false);
                UiKit.Label(row, label, new Vector2(12f, -6f), new Vector2(170f, 44f), 21f, TextAlignmentOptions.MidlineLeft, Color.white);
                var delta = UiKit.Label(row, "", new Vector2(140f, -6f), new Vector2(110f, 44f), 17f, TextAlignmentOptions.MidlineRight, Good);
                var value = UiKit.Label(row, "", new Vector2(250f, -6f), new Vector2(104f, 44f), 17f, TextAlignmentOptions.MidlineRight, Color.white);
                _rows[st] = new StatRow { Delta = delta, Value = value, Fill = fill, DeltaBar = deltaBar };
                y -= 58f;
            }
            _extraStats = UiKit.Label(left, "", new Vector2(0f, y - 4f), new Vector2(360f, 30f), 16f, TextAlignmentOptions.MidlineLeft, new Color(0.8f, 0.85f, 0.9f));

            float by = -620f;
            _dragButton = FlatButton(left, "", new Vector2(0f, by), new Vector2(360f, 78f), Yellow, () => { var b = _build.Clone(); b.AdvancedDrag = !b.AdvancedDrag; SetBuild(b); }, 22f);
            SetTextColor(_dragButton, Color.black, TextAlignmentOptions.MidlineLeft);
            _moiButton = FlatButton(left, "", new Vector2(0f, by - 88f), new Vector2(360f, 52f), Panel, () => { _moi = !_moi; UpdateMoi(); RefreshToggles(); }, 21f);
            SetTextColor(_moiButton, Color.white, TextAlignmentOptions.MidlineLeft);
            _spinButton = FlatButton(left, "", new Vector2(0f, by - 148f), new Vector2(176f, 52f), Panel, () => { _spin = !_spin; RefreshToggles(); }, 21f);
            SetTextColor(_spinButton, Color.white, TextAlignmentOptions.MidlineLeft);
            var data = FlatButton(left, "DATA  >", new Vector2(184f, by - 148f), new Vector2(176f, 52f), Panel, ToggleData, 21f);
            SetTextColor(data, Color.white, TextAlignmentOptions.MidlineLeft);
            _seeButton = FlatButton(left, "", new Vector2(0f, by - 208f), new Vector2(176f, 52f), Blue, () => { _see = !_see; RefreshToggles(); }, 21f);
            SetTextColor(_seeButton, Color.white, TextAlignmentOptions.MidlineLeft);
            var fly = FlatButton(left, "FLY  >", new Vector2(184f, by - 208f), new Vector2(176f, 52f), Orange, Fly, 21f);
            SetTextColor(fly, Color.white, TextAlignmentOptions.MidlineLeft);
            RefreshToggles();

            // Right panel.
            var right = Anchored(root, "Right", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-475f, -125f), new Vector2(-60f, -125f));
            right.sizeDelta = new Vector2(415f, 860f);
            right.pivot = new Vector2(0f, 1f);
            right.anchoredPosition = new Vector2(-475f, -125f);
            var sizeRt = UiKit.Rect(right, "Size", Vector2.zero, new Vector2(415f, 48f));
            UiKit.Image(sizeRt, Panel, true);
            _sizeLabel = UiKit.Label(sizeRt, "", new Vector2(0f, 0f), new Vector2(400f, 48f), 21f, TextAlignmentOptions.MidlineRight, Color.white);
            var sizeBtn = sizeRt.gameObject.AddComponent<Button>();
            sizeBtn.targetGraphic = sizeRt.GetComponent<Image>();
            sizeBtn.onClick.AddListener(() =>
            {
                if (_tab != PartSlot.Frame) return;
                _sizeFilter = (SizeClass)(((int)_sizeFilter + 1) % 5);
                _page = 0;
                RefreshCards();
            });
            _grid = UiKit.Rect(right, "Grid", new Vector2(0f, -60f), new Vector2(415f, 410f));
            var prev = FlatButton(right, "<   PREVIOUS", new Vector2(0f, -482f), new Vector2(200f, 50f), new Color(0, 0, 0, 0), () => { _page--; RefreshCards(); }, 20f);
            Outline((RectTransform)prev.transform, new Color(1f, 1f, 1f, 0.4f), 1f);
            var next = FlatButton(right, "NEXT   >", new Vector2(215f, -482f), new Vector2(200f, 50f), Panel, () => { _page++; RefreshCards(); }, 20f);
            Outline((RectTransform)next.transform, PanelLine, 2f);
            _pageLabel = UiKit.Label(right, "", new Vector2(0f, -536f), new Vector2(415f, 26f), 16f, TextAlignmentOptions.Center, UiKit.DimTextColor);

            var info = UiKit.Rect(right, "Info", new Vector2(0f, -640f), new Vector2(415f, 190f));
            UiKit.Image(info, Panel);
            Outline(info, new Color(1f, 1f, 1f, 0.25f), 1f);
            _infoBrand = UiKit.Label(info, "", new Vector2(10f, -40f), new Vector2(150f, 100f), 30f, TextAlignmentOptions.Center, new Color(1f, 0.5f, 0.1f));
            _infoBrand.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _infoBrand.textWrappingMode = TextWrappingModes.NoWrap;
            _infoBrand.enableAutoSizing = true;
            _infoBrand.fontSizeMin = 14f;
            _infoBrand.fontSizeMax = 30f;
            _infoName = UiKit.Label(info, "", new Vector2(165f, -14f), new Vector2(240f, 34f), 21f, TextAlignmentOptions.TopLeft, Color.white);
            _infoName.fontStyle = FontStyles.Bold;
            _infoBody = UiKit.Label(info, "", new Vector2(165f, -50f), new Vector2(240f, 136f), 15f, TextAlignmentOptions.TopLeft, new Color(0.88f, 0.9f, 0.93f));
            _infoBody.textWrappingMode = TextWrappingModes.Normal;

            // Bottom bar.
            var bottom = Anchored(root, "Bottom Bar", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 64f));
            UiKit.Image(bottom, Bar);
            var logo = UiKit.Rect(bottom, "Logo", new Vector2(0f, 0f), new Vector2(150f, 64f));
            logo.anchorMin = logo.anchorMax = logo.pivot = new Vector2(0f, 0f);
            UiKit.Image(logo, new Color(1f, 0.4f, 0.05f));
            var lt = UiKit.Label(logo, "GATE\nRUSH", Vector2.zero, new Vector2(150f, 64f), 22f, TextAlignmentOptions.Center, Color.white);
            lt.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _summary = UiKit.Label(bottom, "", new Vector2(175f, 0f), new Vector2(900f, 64f), 22f, TextAlignmentOptions.MidlineLeft, Color.white);
            ((RectTransform)_summary.transform).anchorMin = ((RectTransform)_summary.transform).anchorMax = ((RectTransform)_summary.transform).pivot = new Vector2(0f, 0f);
            var dev = UiKit.Rect(bottom, "Device", Vector2.zero, new Vector2(200f, 64f));
            dev.anchorMin = dev.anchorMax = dev.pivot = new Vector2(1f, 0f);
            dev.anchoredPosition = new Vector2(-170f, 0f);
            _device = UiKit.Label(dev, "", Vector2.zero, new Vector2(200f, 64f), 22f, TextAlignmentOptions.Center, Color.white);
            var menu = FlatButton(bottom, "MENU", Vector2.zero, new Vector2(150f, 64f), new Color(0.12f, 0.13f, 0.16f), Back, 22f);
            var mrt = (RectTransform)menu.transform;
            mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(1f, 0f);
            mrt.anchoredPosition = Vector2.zero;

            // Toast + data sheet.
            _toast = UiKit.Label(root, "", new Vector2(560f, -980f), new Vector2(800f, 40f), 22f, TextAlignmentOptions.Center, new Color(1f, 0.85f, 0.3f));
            _toast.gameObject.SetActive(false);
            _dataPanel = UiKit.Rect(root, "Data", new Vector2(450f, -120f), new Vector2(1020f, 820f));
            UiKit.Image(_dataPanel, new Color(0.03f, 0.05f, 0.08f, 0.94f), true);
            Outline(_dataPanel, PanelLine, 2f);
            _dataText = UiKit.Label(_dataPanel, "", new Vector2(30f, -24f), new Vector2(960f, 720f), 20f, TextAlignmentOptions.TopLeft, Color.white);
            _dataText.textWrappingMode = TextWrappingModes.Normal;
            FlatButton(_dataPanel, "CLOSE", new Vector2(860f, -760f), new Vector2(140f, 46f), Panel, ToggleData, 20f);
            _dataPanel.gameObject.SetActive(false);

            LoadChoices();
            SelectTab(_tab);
        }

        private void ToggleData()
        {
            bool show = !_dataPanel.gameObject.activeSelf;
            _dataPanel.gameObject.SetActive(show);
            if (show) _dataText.text = SpecSheet();
        }

        private void SelectTab(PartSlot slot)
        {
            _tab = slot;
            _page = 0;
            if (slot == PartSlot.Frame) _sizeFilter = _build.Size;
            for (int i = 0; i < _tabButtons.Count; i++)
                UiKit.SetButtonColor(_tabButtons[i], Tabs[i].slot == slot ? Tabs[i].line : new Color(0.3f, 0.32f, 0.36f, 0.95f));
            RefreshCards();
            RefreshInfo(CurrentPart(slot));
        }

        private void RefreshToggles()
        {
            UiKit.SetButtonText(_moiButton, "   MOI ELLIPSOID   " + (_moi ? "ON" : "OFF"));
            UiKit.SetButtonText(_spinButton, "   SPIN  " + (_spin ? "ON" : "OFF"));
            UiKit.SetButtonText(_seeButton, _see ? "   ASSEMBLE" : "   SEE");
        }

        private TMP_InputField NameField(RectTransform parent)
        {
            var text = UiKit.Label(parent, "", new Vector2(14f, 0f), new Vector2(330f, 48f), 21f, TextAlignmentOptions.MidlineRight, Color.white);
            var field = parent.gameObject.AddComponent<TMP_InputField>();
            field.textComponent = text;
            field.textViewport = parent;
            field.characterLimit = 24;
            field.targetGraphic = parent.GetComponent<Image>();
            field.onValueChanged.AddListener(v =>
            {
                _build.Name = string.IsNullOrWhiteSpace(v) ? "My quad" : v;
                _dirty = true;
            });
            field.onEndEdit.AddListener(_ => Rebuild(false));
            UiKit.Label(parent, "NAME", new Vector2(12f, 0f), new Vector2(80f, 48f), 16f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            return field;
        }

        private static RectTransform Anchored(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private static Button FlatButton(Transform parent, string text, Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick, float fontSize)
        {
            var b = UiKit.Button(parent, text, pos, size, onClick, fontSize);
            UiKit.SetButtonColor(b, color);
            var t = b.GetComponentInChildren<TMP_Text>();
            t.fontStyle = FontStyles.Bold;
            return b;
        }

        private static void SetTextColor(Button b, Color c, TextAlignmentOptions align)
        {
            var t = b.GetComponentInChildren<TMP_Text>();
            t.color = c;
            t.alignment = align;
        }

        /// <summary>Thin border inside a rect.</summary>
        private static void Outline(RectTransform rt, Color c, float w)
        {
            void Edge(string n, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
            {
                var e = new GameObject(n, typeof(RectTransform), typeof(Image));
                e.transform.SetParent(rt, false);
                var ert = (RectTransform)e.transform;
                ert.anchorMin = min;
                ert.anchorMax = max;
                ert.offsetMin = offMin;
                ert.offsetMax = offMax;
                var img = e.GetComponent<Image>();
                img.color = c;
                img.raycastTarget = false;
            }
            Edge("Top", new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -w), Vector2.zero);
            Edge("Bottom", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, w));
            Edge("Left", Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(w, 0f));
            Edge("Right", new Vector2(1f, 0f), Vector2.one, new Vector2(-w, 0f), Vector2.zero);
        }

        private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
    }
}
