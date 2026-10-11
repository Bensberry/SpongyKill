using System.Collections.Generic;
using Spongykill.Core;
using UnityEngine;

namespace Spongykill.Combat
{
    /// <summary>
    /// A combatant's team and name, plus a registry of every combatant in the scene. Raises the
    /// elimination event when its <see cref="Health"/> runs out, crediting whoever dealt the blow
    /// (on the machine that decides damage; see <see cref="CombatAuthority"/>).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TeamMember : MonoBehaviour
    {
        [SerializeField] Team team;
        [SerializeField] string displayName = "Player";
        [Tooltip("True for the human player on this machine (HUD, name tags and kill feed highlight).")]
        [SerializeField] bool isLocalPlayer;
        [SerializeField] HitboxRig hitboxRig;

        static readonly List<TeamMember> Members = new List<TeamMember>();

        public static IReadOnlyList<TeamMember> All => Members;
        /// <summary>The human player on this machine, if any.</summary>
        public static TeamMember LocalPlayer { get; private set; }

        public Team Team => team;
        public string DisplayName => displayName;
        public bool IsLocalPlayer => isLocalPlayer;
        public Health Health { get; private set; }
        public bool IsAlive => Health != null && !Health.IsDead;

        /// <summary>Centre of the head hitbox (follows crouching).</summary>
        public Vector3 HeadPosition => hitboxRig ? hitboxRig.HeadCenter : transform.position + Vector3.up * 1.6f;
        /// <summary>Upper-chest point on the body hitbox (follows crouching).</summary>
        public Vector3 ChestPosition => hitboxRig ? hitboxRig.ChestCenter : transform.position + Vector3.up * 1.2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Members.Clear();
            LocalPlayer = null;
        }

        public void Configure(Team newTeam, string newName)
        {
            team = newTeam;
            displayName = newName;
        }

        /// <summary>Online, each machine marks only its own player as local once ownership is known.</summary>
        public void SetLocalPlayer(bool local)
        {
            isLocalPlayer = local;
            if (local && isActiveAndEnabled) LocalPlayer = this;
            else if (!local && LocalPlayer == this) LocalPlayer = null;
        }

        void Awake()
        {
            Health = GetComponent<Health>();
            if (hitboxRig == null) hitboxRig = GetComponentInChildren<HitboxRig>();
        }

        void OnEnable()
        {
            Members.Add(this);
            if (isLocalPlayer) LocalPlayer = this;
            Health.Died += OnDied;
        }

        void OnDisable()
        {
            Members.Remove(this);
            if (LocalPlayer == this) LocalPlayer = null;
            Health.Died -= OnDied;
        }

        void OnDied(DamageInfo damage)
        {
            // Online clients learn about eliminations from the server instead.
            if (!CombatAuthority.IsAuthoritative) return;
            TeamMember killer = damage.Instigator ? damage.Instigator.GetComponent<TeamMember>() : null;
            CombatEvents.RaiseElimination(new EliminationRecord(killer, this, damage.Source, damage.IsHeadshot));
        }

        /// <summary>True when both objects belong to (different) members of the same team.</summary>
        public static bool AreAllies(GameObject a, GameObject b)
        {
            if (a == null || b == null) return false;
            TeamMember first = a.GetComponentInParent<TeamMember>();
            TeamMember second = b.GetComponentInParent<TeamMember>();
            return first != null && second != null && first != second && first.team == second.team;
        }
    }
}
