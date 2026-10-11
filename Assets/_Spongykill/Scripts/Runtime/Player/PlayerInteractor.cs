using Spongykill.Weapons;
using UnityEngine;

namespace Spongykill.Player
{
    /// <summary>Finds the weapon pickup the player is looking at and collects it on the interact key.</summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] Transform viewPoint;
        [SerializeField] float reach = 2.6f;
        [Tooltip("How directly the player must look at a pickup (cosine of the allowed angle).")]
        [SerializeField, Range(0.5f, 1f)] float minLookAlignment = 0.9f;

        readonly Collider[] overlapBuffer = new Collider[16];
        PlayerWeapons weapons;

        /// <summary>The pickup currently in reach and in view, if any.</summary>
        public WeaponPickup Focused { get; private set; }

        void Awake() => weapons = GetComponent<PlayerWeapons>();

        public void Tick(bool canInteract, bool interactPressed)
        {
            Focused = canInteract && viewPoint ? FindFocusedPickup() : null;
            if (Focused == null || !interactPressed || weapons == null) return;

            weapons.Give(Focused.Weapon);
            Focused.Collect();
        }

        WeaponPickup FindFocusedPickup()
        {
            Vector3 eye = viewPoint.position;
            Vector3 forward = viewPoint.forward;
            int count = Physics.OverlapSphereNonAlloc(eye, reach, overlapBuffer, Physics.AllLayers, QueryTriggerInteraction.Collide);

            WeaponPickup best = null;
            float bestAlignment = minLookAlignment;
            for (int i = 0; i < count; i++)
            {
                WeaponPickup pickup = overlapBuffer[i].GetComponentInParent<WeaponPickup>();
                if (pickup == null || pickup.Weapon == null) continue;

                float alignment = Vector3.Dot(forward, (overlapBuffer[i].bounds.center - eye).normalized);
                if (alignment <= bestAlignment) continue;
                bestAlignment = alignment;
                best = pickup;
            }
            return best;
        }
    }
}
