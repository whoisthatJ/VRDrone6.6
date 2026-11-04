using Ushqyr.Track;
using UnityEngine;

namespace Ushqyr.TrackEditor
{
    /// <summary>Marks an editor object (gate, prop or spawn) so pointer hits map back to the model.</summary>
    public sealed class EditorHandle : MonoBehaviour
    {
        public EditTarget Target;
        public int Index;
    }
}
