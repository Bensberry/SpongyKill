using UnityEngine;

namespace Spongykill.Combat
{
    /// <summary>Marks a collider on the Hitbox layer as a damageable part of a character.</summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour
    {
        [SerializeField] Health owner;
        [SerializeField] HitZone zone = HitZone.Body;

        public Health Owner => owner;
        public HitZone Zone => zone;

        public void Configure(Health newOwner, HitZone newZone)
        {
            owner = newOwner;
            zone = newZone;
        }
    }
}
