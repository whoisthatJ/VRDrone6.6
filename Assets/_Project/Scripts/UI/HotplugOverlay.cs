using GateRush.Game;
using TMPro;
using UnityEngine;

namespace GateRush.UI
{
    /// <summary>OSD message while the race is paused for a controller change.</summary>
    public sealed class HotplugOverlay : MonoBehaviour
    {
        [SerializeField] private ControllerHotplugGuard guard;
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text text;

        private void OnEnable()
        {
            if (guard != null) guard.PausedChanged += OnPausedChanged;
            OnPausedChanged(guard != null && guard.Paused);
        }

        private void OnDisable()
        {
            if (guard != null) guard.PausedChanged -= OnPausedChanged;
        }

        private void OnPausedChanged(bool paused)
        {
            if (panel != null) panel.SetActive(paused);
            if (paused && text != null)
                text.SetText($"<b>PAUSED</b>\n{guard.Reason}\n<size=70%>Centre the sticks and arm to continue.\nRadio not working? Menu > Radio setup.</size>");
        }
    }
}
