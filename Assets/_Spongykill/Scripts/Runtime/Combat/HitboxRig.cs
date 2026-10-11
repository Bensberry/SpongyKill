using UnityEngine;

namespace Spongykill.Combat
{
    /// <summary>
    /// A head sphere and a body capsule laid out for a character of a given height, with the feet at
    /// the transform pivot. The colliders switch off while the owner is dead so shots pass through.
    /// </summary>
    public class HitboxRig : MonoBehaviour
    {
        [SerializeField] Health health;
        [SerializeField] SphereCollider head;
        [SerializeField] CapsuleCollider body;
        [SerializeField] float height = 1.8f;
        [SerializeField] float headRadius = 0.17f;
        [SerializeField] float bodyRadius = 0.3f;

        public float Height => height;
        public Vector3 HeadCenter => head ? head.transform.TransformPoint(head.center) : transform.position + Vector3.up * (height - headRadius);
        /// <summary>A point on the upper chest, where most body shots are aimed.</summary>
        public Vector3 ChestCenter => transform.position + Vector3.up * ((height - 2f * headRadius) * 0.72f);

        void Awake() => SetHeight(height);

        void OnEnable()
        {
            if (health == null) return;
            health.Died += OnDied;
            health.Revived += OnRevived;
        }

        void OnDisable()
        {
            if (health == null) return;
            health.Died -= OnDied;
            health.Revived -= OnRevived;
        }

        public void SetHeight(float newHeight)
        {
            height = newHeight;
            float headCenter = height - headRadius - 0.02f;
            float neck = headCenter - headRadius;

            if (head)
            {
                head.radius = headRadius;
                head.center = new Vector3(0f, headCenter, 0f);
            }
            if (body)
            {
                body.direction = 1;
                body.radius = bodyRadius;
                body.height = Mathf.Max(neck, bodyRadius * 2f);
                body.center = new Vector3(0f, body.height * 0.5f, 0f);
            }
        }

        void OnDied(DamageInfo _) => SetCollidersEnabled(false);

        void OnRevived() => SetCollidersEnabled(true);

        void SetCollidersEnabled(bool enabled)
        {
            if (head) head.enabled = enabled;
            if (body) body.enabled = enabled;
        }
    }
}
