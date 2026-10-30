using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GateRush.UI
{
    /// <summary>
    /// Builds simple uGUI controls from code (no sprites needed): panels, labels, buttons, sliders and steppers.
    /// Positions are top-left anchored in canvas units (1 unit = 1 mm on the world-space menu).
    /// </summary>
    public static class UiKit
    {
        public static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.96f);
        public static readonly Color ButtonColor = new Color(0.18f, 0.2f, 0.26f, 1f);
        public static readonly Color AccentColor = new Color(1f, 0.55f, 0.15f, 1f);
        public static readonly Color SelectedColor = new Color(0.25f, 0.45f, 0.3f, 1f);
        public static readonly Color TextColor = new Color(0.92f, 0.93f, 0.95f, 1f);
        public static readonly Color DimTextColor = new Color(0.65f, 0.68f, 0.74f, 1f);

        public static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>A rect that fills its parent.</summary>
        public static RectTransform Fill(Transform parent, string name)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Image(RectTransform rt, Color color, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, Color? color = null)
        {
            var rt = Rect(parent, "Label", pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color ?? TextColor;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        public static Button Button(Transform parent, string text, Vector2 pos, Vector2 size, UnityAction onClick, float fontSize = 34f)
        {
            var rt = Rect(parent, "Button " + text, pos, size);
            var img = Image(rt, ButtonColor, true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.5f, 1f);
            colors.pressedColor = AccentColor;
            colors.selectedColor = new Color(1.2f, 1.2f, 1.3f, 1f);
            colors.colorMultiplier = 1.5f;
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(onClick);
            var label = Label(rt, text, Vector2.zero, size, fontSize, TextAlignmentOptions.Center);
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16f, 4f);
            lrt.offsetMax = new Vector2(-16f, -4f);
            return b;
        }

        public static void SetButtonColor(Button b, Color c) => ((Image)b.targetGraphic).color = c;

        public static void SetButtonText(Button b, string text) => b.GetComponentInChildren<TMP_Text>().text = text;

        /// <summary>Label on the left, slider in the middle, value text on the right.</summary>
        public static Slider SliderRow(Transform parent, string label, float y, float min, float max, float value,
            Func<float, string> format, Action<float> onChange, float width = 1400f, bool whole = false)
        {
            // Label | slider | value, proportional so half-width rows still get a usable slider.
            float labelW = Mathf.Min(420f, width * 0.34f);
            float valueW = Mathf.Min(240f, width * 0.16f);
            Label(parent, label, new Vector2(0f, y), new Vector2(labelW, 60f), 30f);
            var valueText = Label(parent, format(value), new Vector2(width - valueW, y), new Vector2(valueW, 60f), 30f, TextAlignmentOptions.MidlineRight);

            var root = Rect(parent, "Slider " + label, new Vector2(labelW + 20f, y - 14f), new Vector2(width - labelW - valueW - 40f, 32f));
            Image(Fill(root, "Background"), new Color(1f, 1f, 1f, 0.12f), true);

            var fillArea = Fill(root, "Fill Area");
            var fill = Fill(fillArea, "Fill");
            Image(fill, AccentColor * new Color(1f, 1f, 1f, 0.7f));

            var handleArea = Fill(root, "Handle Area");
            handleArea.offsetMin = new Vector2(12f, 0f);
            handleArea.offsetMax = new Vector2(-12f, 0f);
            var handle = Rect(handleArea, "Handle", Vector2.zero, new Vector2(24f, 0f));
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(24f, 16f);
            var handleImg = Image(handle, Color.white, true);

            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = handleImg;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = whole;
            s.SetValueWithoutNotify(value);
            s.onValueChanged.AddListener(v =>
            {
                valueText.text = format(v);
                onChange(v);
            });
            return s;
        }

        /// <summary>Label, then "&lt;  value  &gt;" buttons cycling through options.</summary>
        public static void StepperRow(Transform parent, string label, float y, string[] options, int index, Action<int> onChange, float width = 1400f)
        {
            Label(parent, label, new Vector2(0f, y), new Vector2(420f, 60f), 30f);
            int current = Mathf.Clamp(index, 0, options.Length - 1);
            var valueText = Label(parent, options[current], new Vector2(540f, y), new Vector2(width - 780f, 60f), 30f, TextAlignmentOptions.Center);
            Button(parent, "<", new Vector2(440f, y), new Vector2(90f, 60f), () =>
            {
                current = (current - 1 + options.Length) % options.Length;
                valueText.text = options[current];
                onChange(current);
            });
            Button(parent, ">", new Vector2(width - 230f, y), new Vector2(90f, 60f), () =>
            {
                current = (current + 1) % options.Length;
                valueText.text = options[current];
                onChange(current);
            });
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
