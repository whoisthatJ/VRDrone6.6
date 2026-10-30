using UnityEngine;

namespace GateRush.Cameras
{
    /// <summary>
    /// Comfort aid for Immersive view: a faint ring on the true world horizon around the viewer's head,
    /// a stable reference while the image rotates with the quad. One LineRenderer, positions built once.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class HorizonRing : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private float radius = 25f;
        [SerializeField] private int segments = 96;

        private void Awake()
        {
            var line = GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius));
            }
        }

        private void LateUpdate()
        {
            if (head == null) return;
            // Follows the eye position, never its rotation: the ring stays level with the real horizon.
            transform.SetPositionAndRotation(head.position, Quaternion.identity);
        }
    }
}
