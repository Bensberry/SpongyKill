using Spongykill.Combat;
using UnityEngine;

namespace Spongykill.Player
{
    /// <summary>Damages the player for hard landings, scaling from harmless to lethal with impact speed.</summary>
    [RequireComponent(typeof(PlayerMotor), typeof(Health))]
    public class FallDamage : MonoBehaviour
    {
        [Tooltip("Landing slower than this is harmless (about a 3.8 m drop with default gravity).")]
        [SerializeField] float safeImpactSpeed = 13f;
        [Tooltip("Landing at this speed or faster is lethal (about a 15 m drop).")]
        [SerializeField] float lethalImpactSpeed = 26f;

        PlayerMotor motor;
        Health health;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            health = GetComponent<Health>();
        }

        void OnEnable() => motor.Landed += OnLanded;

        void OnDisable() => motor.Landed -= OnLanded;

        void OnLanded(float impactSpeed)
        {
            if (impactSpeed <= safeImpactSpeed) return;
            float severity = Mathf.InverseLerp(safeImpactSpeed, lethalImpactSpeed, impactSpeed);
            health.ApplyDamage(new DamageInfo(severity * health.Max, HitZone.Body, transform.position, Vector3.down, null, "Fall"));
        }
    }
}
