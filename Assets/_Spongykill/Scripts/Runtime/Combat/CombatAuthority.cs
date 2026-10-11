using System;
using UnityEngine;

namespace Spongykill.Combat
{
    /// <summary>
    /// Who decides damage. Offline, on the host and on a dedicated server, health changes happen
    /// directly. On an online client they don't: weapon hits are reported to the server (see
    /// <see cref="Weapons.WeaponFiring.HitClaimed"/>) and self-inflicted damage such as falls is
    /// forwarded through <see cref="ForwardDamage"/>; the server's result comes back over the network.
    /// </summary>
    public static class CombatAuthority
    {
        /// <summary>False only on a client connected to someone else's server.</summary>
        public static bool IsAuthoritative { get; set; } = true;

        /// <summary>Receives damage requested on a non-authoritative machine (falls, the abyss).</summary>
        public static Action<Health, DamageInfo> ForwardDamage { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsAuthoritative = true;
            ForwardDamage = null;
        }
    }
}
