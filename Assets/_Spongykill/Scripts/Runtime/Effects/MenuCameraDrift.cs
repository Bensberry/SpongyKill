using UnityEngine;

namespace Spongykill.Effects
{
    /// <summary>Slow back-and-forth sweep around a point, for the title screen's backdrop.</summary>
    public class MenuCameraDrift : MonoBehaviour
    {
        [SerializeField] Vector3 focus = new Vector3(0f, 3f, 8f);
        [SerializeField] float radius = 24f;
        [SerializeField] float height = 4f;
        [SerializeField] float baseAngle = -140f;
        [Tooltip("How far either side of the base angle the camera swings (degrees).")]
        [SerializeField] float sweep = 14f;
        [SerializeField] float period = 40f;

        void LateUpdate()
        {
            float phase = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / period);
            float angle = (baseAngle + phase * sweep) * Mathf.Deg2Rad;
            var offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            transform.position = focus + offset + Vector3.up * (height + Mathf.Sin(Time.unscaledTime * 0.4f) * 0.3f);
            transform.rotation = Quaternion.LookRotation(focus - transform.position);
        }

        public void Configure(Vector3 newFocus, float newRadius, float newHeight, float newBaseAngle)
        {
            focus = newFocus;
            radius = newRadius;
            height = newHeight;
            baseAngle = newBaseAngle;
        }
    }
}
