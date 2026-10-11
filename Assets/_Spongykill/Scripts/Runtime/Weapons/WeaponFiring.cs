using System;
using Spongykill.Combat;
using UnityEngine;

namespace Spongykill.Weapons
{
    /// <summary>Where one pellet (or melee swing) ended up.</summary>
    public struct PelletResult
    {
        public Vector3 EndPoint;
        public bool HitSomething;
        public bool HitCharacter;
        public Vector3 Normal;
    }

    /// <summary>A character hit seen by a client, sent to the server to be checked and turned into damage.</summary>
    public readonly struct HitClaim
    {
        public readonly GameObject Shooter;
        public readonly WeaponDefinition Weapon;
        public readonly Health Victim;
        public readonly HitZone Zone;
        public readonly float Distance;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;

        public HitClaim(GameObject shooter, WeaponDefinition weapon, Health victim, HitZone zone, float distance, Vector3 point, Vector3 direction)
        {
            Shooter = shooter;
            Weapon = weapon;
            Victim = victim;
            Zone = zone;
            Distance = distance;
            Point = point;
            Direction = direction;
        }
    }

    /// <summary>Summary of one trigger pull, used for hit markers and feedback.</summary>
    public struct ShotReport
    {
        public bool HitCharacter;
        public bool Headshot;
        public bool Killed;
        public float Damage;
        public float Distance;
        public Health Victim;
    }

    /// <summary>
    /// Resolves shots for any shooter: applies spread to each pellet, traces it, applies damage with
    /// head multipliers and distance falloff. Player and bot weapon code both call into this. On an
    /// online client hits are only predicted for feedback and raised through <see cref="HitClaimed"/>.
    /// </summary>
    public static class WeaponFiring
    {
        /// <summary>Raised on non-authoritative clients for every character hit, instead of applying damage.</summary>
        public static event Action<HitClaim> HitClaimed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => HitClaimed = null;

        /// <summary>Damage for a hit at a distance, with the head multiplier; the server uses the same rule.</summary>
        public static float DamageFor(WeaponDefinition weapon, float distance, HitZone zone) =>
            weapon.DamageAt(distance) * (zone == HitZone.Head ? weapon.headMultiplier : 1f);

        const float MeleeTraceRadius = 0.22f;
        const float GunshotHearingRadius = 40f;
        const float MeleeHearingRadius = 6f;

        public static ShotReport Fire(WeaponDefinition weapon, Vector3 origin, Vector3 aimDirection, float spreadDegrees,
            GameObject shooter, int layerMask, Action<PelletResult> onPellet = null)
        {
            var report = new ShotReport();
            CombatEvents.MakeNoise(origin, weapon.IsMelee ? MeleeHearingRadius : GunshotHearingRadius, shooter);
            if (!weapon.IsMelee) CombatEvents.RaiseShot(shooter);
            int pellets = weapon.IsMelee ? 1 : Mathf.Max(1, weapon.pellets);
            float traceRadius = weapon.IsMelee ? MeleeTraceRadius : 0f;

            for (int i = 0; i < pellets; i++)
            {
                Vector3 direction = weapon.IsMelee ? aimDirection : ApplySpread(aimDirection, spreadDegrees);
                var pellet = new PelletResult { EndPoint = origin + direction * weapon.range };

                if (Hitscan.Trace(origin, direction, weapon.range, traceRadius, layerMask, shooter.transform, out RaycastHit hit))
                {
                    pellet.EndPoint = hit.point;
                    pellet.Normal = hit.normal;
                    pellet.HitSomething = true;

                    if (hit.collider.TryGetComponent(out Hitbox hitbox) && hitbox.Owner != null)
                    {
                        pellet.HitCharacter = true;
                        ApplyHit(weapon, hitbox, hit, direction, shooter, ref report);
                    }
                }
                onPellet?.Invoke(pellet);
            }
            return report;
        }

        static void ApplyHit(WeaponDefinition weapon, Hitbox hitbox, RaycastHit hit, Vector3 direction, GameObject shooter, ref ShotReport report)
        {
            // No friendly fire: teammates absorb the shot without taking damage.
            if (TeamMember.AreAllies(shooter, hitbox.Owner.gameObject)) return;

            bool headshot = hitbox.Zone == HitZone.Head;
            float amount = DamageFor(weapon, hit.distance, hitbox.Zone);

            if (!CombatAuthority.IsAuthoritative)
            {
                // Predict the result for instant hit markers; the server decides what really happened.
                report.HitCharacter = true;
                report.Damage += amount;
                report.Headshot |= headshot;
                report.Killed |= !hitbox.Owner.IsDead && amount >= hitbox.Owner.Current;
                report.Distance = hit.distance;
                report.Victim = hitbox.Owner;
                HitClaimed?.Invoke(new HitClaim(shooter, weapon, hitbox.Owner, hitbox.Zone, hit.distance, hit.point, direction));
                return;
            }

            var damage = new DamageInfo(amount, hitbox.Zone, hit.point, direction, shooter, weapon.displayName);

            float applied = hitbox.Owner.ApplyDamage(damage);
            if (applied <= 0f) return;

            report.HitCharacter = true;
            report.Damage += applied;
            report.Headshot |= headshot;
            report.Killed |= hitbox.Owner.IsDead;
            report.Distance = hit.distance;
            report.Victim = hitbox.Owner;
        }

        /// <summary>Random direction inside a cone of the given half-angle around <paramref name="direction"/>.</summary>
        public static Vector3 ApplySpread(Vector3 direction, float coneDegrees)
        {
            if (coneDegrees <= 0f) return direction;
            Vector2 offset = UnityEngine.Random.insideUnitCircle * Mathf.Tan(coneDegrees * Mathf.Deg2Rad);
            return (Quaternion.LookRotation(direction) * new Vector3(offset.x, offset.y, 1f)).normalized;
        }
    }
}
