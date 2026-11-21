using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Code-built uGUI pieces for the Education Suite's flat screens (the replay viewer, later the training hub and the
    /// teacher console). Places are given from the top-left corner on a 1920 × 1080 canvas that scales with the window.
    /// </summary>
    public static class EduUi
    {
        public static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.86f);
        public static readonly Color ButtonColor = new Color(0.18f, 0.2f, 0.26f, 1f);
        public static readonly Color Accent = new Color(1f, 0.55f, 0.15f);
        public static readonly Color Dim = new Color(0.72f, 0.76f, 0.82f);

        /// <summary>A screen-space canvas above the game's own UI, with something to send clicks to it.</summary>
        public static Canvas Canvas(string name, int order)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<InputSystemUIInputModule>();
            }
            return canvas;
        }

        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Panel(Transform parent, string name, float x, float y, float w, float h, Color? color = null, bool raycast = true)
        {
            RectTransform rt = Rect(parent, name, x, y, w, h);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color ?? PanelColor;
            image.raycastTarget = raycast;
            return rt;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float x, float y, float w, float h, float size, Color? color = null,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            RectTransform rt = Rect(parent, "Text", x, y, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color ?? Color.white;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string text, float x, float y, float w, float h, Action onClick, float size = 22f)
        {
            RectTransform rt = Panel(parent, "Button " + text, x, y, w, h, ButtonColor);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = rt.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            // The keyboard drives the viewer; a clicked button must not keep the space bar for itself.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                onClick();
            });
            TextMeshProUGUI label = Text(rt, text, 6f, 0f, w - 12f, h, size, Color.white, TextAlignmentOptions.Center);
            label.name = "Label";
            return button;
        }

        public static void SetLabel(Button b, string text) => b.GetComponentInChildren<TextMeshProUGUI>().text = text;

        public static Slider Slider(Transform parent, float x, float y, float w, float h, Action<float> onChange)
        {
            RectTransform rt = Panel(parent, "Slider", x, y, w, h, new Color(0.14f, 0.15f, 0.19f, 1f));
            var slider = rt.gameObject.AddComponent<Slider>();
            RectTransform fillArea = Stretch(Rect(rt, "Fill Area", 0f, 0f, w, h));
            RectTransform fill = Stretch(Panel(fillArea, "Fill", 0f, 0f, 0f, 0f, new Color(1f, 0.55f, 0.15f, 0.45f), false));
            RectTransform handleArea = Stretch(Rect(rt, "Handle Area", 0f, 0f, w, h));
            RectTransform handle = Panel(handleArea, "Handle", 0f, 0f, 8f, 0f, Color.white);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(8f, 8f);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Minutes and seconds with hundredths: 1:07.25.</summary>
        public static string Clock(float seconds)
        {
            seconds = seconds < 0f ? 0f : Mathf.Round(seconds * 100f) / 100f; // rounded first, so 59.999 is 1:00.00, not 0:60.00
            int minutes = (int)(seconds / 60f);
            return minutes + ":" + (seconds - minutes * 60f).ToString("00.00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
