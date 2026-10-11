using Spongykill.Core;
using UnityEngine;

namespace Spongykill.Effects
{
    /// <summary>Gentle up-and-down drift and slow spin for floating decorations such as jellyfish.</summary>
    public class FloatingBob : MonoBehaviour
    {
        [SerializeField] float amplitude = 0.4f;
        [SerializeField] float speed = 0.8f;
        [SerializeField] float spinSpeed = 10f;

        Vector3 origin;
        float phase;

        void Awake()
        {
            origin = transform.localPosition;
            // Spread phases by position so a group of decorations doesn't move in lockstep.
            phase = (origin.x * 0.37f + origin.z * 0.61f) % (Mathf.PI * 2f);
        }

        void Update()
        {
            // Purely decorative; the performance options can switch it off.
            if (!GameSettings.Current.ambientMotion) return;
            transform.localPosition = origin + Vector3.up * (Mathf.Sin(Time.time * speed + phase) * amplitude);
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        }
    }
}
