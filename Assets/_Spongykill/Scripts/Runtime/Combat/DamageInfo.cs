using UnityEngine;

namespace Spongykill.Combat
{
    public enum HitZone
    {
        Body,
        Head,
    }

    /// <summary>Everything a receiver needs to know about one instance of damage.</summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly HitZone Zone;
        public readonly Vector3 Point;
        /// <summary>Direction the damage travelled, from the attacker toward the victim.</summary>
        public readonly Vector3 Direction;
        /// <summary>Root object of whoever caused the damage; null for environmental damage.</summary>
        public readonly GameObject Instigator;
        /// <summary>Weapon or hazard name, shown on the HUD and later in the kill feed.</summary>
        public readonly string Source;

        public DamageInfo(float amount, HitZone zone, Vector3 point, Vector3 direction, GameObject instigator, string source)
        {
            Amount = amount;
            Zone = zone;
            Point = point;
            Direction = direction;
            Instigator = instigator;
            Source = source;
        }

        public bool IsHeadshot => Zone == HitZone.Head;
    }
}
