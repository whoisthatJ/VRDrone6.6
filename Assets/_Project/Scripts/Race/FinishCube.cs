using UnityEngine;

namespace Ushqyr.Race
{
    /// <summary>
    /// The finish cube (T6): a truss cube entered through the glowing hexagon on its front face. Marks the obstacle so
    /// the finish moment can find the cinematic camera spot (inside the cube, looking out through the hexagon) and the
    /// auto-hover spot in front of it. Both anchors are children of the obstacle, built by the obstacle kit.
    /// </summary>
    public sealed class FinishCube : MonoBehaviour
    {
        [Tooltip("Inside the cube, looking out through the hexagon (local −Z of the obstacle).")]
        public Transform CameraAnchor;
        [Tooltip("In front of the hexagon, facing the camera: where the quad hovers after the finish.")]
        public Transform HoverAnchor;
    }
}
