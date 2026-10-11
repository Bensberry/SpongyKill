using UnityEngine;

namespace Spongykill.Combat
{
    /// <summary>Instant-hit traces shared by every shooter, player or bot.</summary>
    public static class Hitscan
    {
        static readonly RaycastHit[] Buffer = new RaycastHit[32];

        /// <summary>
        /// Closest hit along a ray, or a thick ray when <paramref name="radius"/> is above zero,
        /// skipping colliders that belong to <paramref name="ignoreRoot"/> (usually the shooter).
        /// </summary>
        public static bool Trace(Vector3 origin, Vector3 direction, float range, float radius, int layerMask, Transform ignoreRoot, out RaycastHit closest)
        {
            int count = radius > 0f
                ? Physics.SphereCastNonAlloc(origin, radius, direction, Buffer, range, layerMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, direction, Buffer, range, layerMask, QueryTriggerInteraction.Ignore);

            closest = default;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Buffer[i];
                if (ignoreRoot != null && hit.collider.transform.IsChildOf(ignoreRoot)) continue;
                if (hit.distance >= closestDistance) continue;

                // A sphere cast that starts inside a collider reports distance 0 and no point.
                if (hit.distance <= 0f) hit.point = origin;
                closestDistance = hit.distance;
                closest = hit;
            }
            return closestDistance < float.PositiveInfinity;
        }
    }
}
