using System.Collections.Generic;
using UnityEngine;

namespace Spongykill.Combat
{
    /// <summary>Hurts every character standing inside a box, in periodic ticks (sea urchins, jellyfish beds).</summary>
    public class DamageZone : MonoBehaviour
    {
        [SerializeField] Vector3 size = new Vector3(4f, 1f, 4f);
        [SerializeField] float damagePerSecond = 20f;
        [SerializeField] float tickInterval = 0.5f;
        [SerializeField] LayerMask targetLayers = Physics.DefaultRaycastLayers;
        [SerializeField] string sourceName = "Sea urchins";

        readonly Collider[] overlapBuffer = new Collider[16];
        readonly HashSet<Health> damagedThisTick = new HashSet<Health>();
        float nextTickTime;

        void Update()
        {
            if (Time.time < nextTickTime) return;
            nextTickTime = Time.time + tickInterval;

            Vector3 center = transform.TransformPoint(new Vector3(0f, size.y * 0.5f, 0f));
            int count = Physics.OverlapBoxNonAlloc(center, size * 0.5f, overlapBuffer, transform.rotation, targetLayers, QueryTriggerInteraction.Ignore);

            damagedThisTick.Clear();
            for (int i = 0; i < count; i++)
            {
                Health health = overlapBuffer[i].GetComponentInParent<Health>();
                if (health == null || !damagedThisTick.Add(health)) continue;
                var damage = new DamageInfo(damagePerSecond * tickInterval, HitZone.Body, health.transform.position, Vector3.down, null, sourceName);
                health.ApplyDamage(damage);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 0.8f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(new Vector3(0f, size.y * 0.5f, 0f), size);
        }
    }
}
