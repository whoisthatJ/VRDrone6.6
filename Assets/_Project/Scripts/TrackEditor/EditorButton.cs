using UnityEngine;

namespace GateRush.TrackEditor
{
    /// <summary>A pointer-clickable 3D button on the editor's wrist panel.</summary>
    public sealed class EditorButton : MonoBehaviour
    {
        public System.Action Clicked;
        public TMPro.TMP_Text Label;
        public Renderer Background;
    }
}
