using System;
using UnityEngine;

namespace Spongykill.Combat
{
    /// <summary>
    /// Hit points for anything that can be damaged and eliminated: players, bots and training dummies.
    /// Online, only the server changes health; clients mirror it with <see cref="ApplyRemote"/>.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField, Min(1f)] float maxHealth = 100f;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public bool IsDead { get; private set; }
        /// <summary>Damage is ignored until this time (spawn protection).</summary>
        public float InvulnerableUntil { get; set; } = float.NegativeInfinity;
        public bool IsInvulnerable => Time.time < InvulnerableUntil;

        /// <summary>Raised whenever the current value changes.</summary>
        public event Action<float> Changed;
        /// <summary>Raised for every hit that removes health, before <see cref="Died"/>.</summary>
        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Died;
        /// <summary>Raised when a dead owner is restored to full health.</summary>
        public event Action Revived;

        void Awake() => Current = maxHealth;

        /// <returns>The amount of health actually removed (0 when forwarded to the server).</returns>
        public float ApplyDamage(in DamageInfo damage)
        {
            if (!CombatAuthority.IsAuthoritative)
            {
                CombatAuthority.ForwardDamage?.Invoke(this, damage);
                return 0f;
            }
            if (IsDead || damage.Amount <= 0f || IsInvulnerable) return 0f;

            float applied = Mathf.Min(damage.Amount, Current);
            Current -= applied;
            if (Current <= 0.0001f)
            {
                Current = 0f;
                IsDead = true;
            }

            Changed?.Invoke(Current);
            Damaged?.Invoke(damage);
            if (IsDead) Died?.Invoke(damage);
            return applied;
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Current = Mathf.Min(maxHealth, Current + amount);
            Changed?.Invoke(Current);
        }

        public void ResetHealth()
        {
            bool wasDead = IsDead;
            IsDead = false;
            Current = maxHealth;
            Changed?.Invoke(Current);
            if (wasDead) Revived?.Invoke();
        }

        /// <summary>
        /// Mirrors the server's state on a client and raises the same events a local hit would, so HUD,
        /// hitboxes, death camera and animation react exactly as they do offline.
        /// </summary>
        public void ApplyRemote(float current, bool dead, DamageInfo? damage)
        {
            Current = Mathf.Clamp(current, 0f, maxHealth);
            Changed?.Invoke(Current);
            if (damage.HasValue && damage.Value.Amount > 0f) Damaged?.Invoke(damage.Value);
            if (dead && !IsDead)
            {
                IsDead = true;
                Died?.Invoke(damage ?? default);
            }
            else if (!dead && IsDead)
            {
                IsDead = false;
                Revived?.Invoke();
            }
        }
    }
}
