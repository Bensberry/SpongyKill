using Spongykill.Characters;
using Spongykill.Combat;
using Spongykill.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Spongykill.Net
{
    /// <summary>
    /// Shares a combatant's identity (team, name, skin), scoreboard stats and health with every machine.
    /// The server owns all of it: its health events are replayed on clients through
    /// <see cref="Health.ApplyRemote"/> and its eliminations feed every client's kill feed.
    /// </summary>
    [RequireComponent(typeof(TeamMember), typeof(Health))]
    public class NetworkCombatant : NetworkBehaviour
    {
        [SerializeField] CharacterAppearance appearance;

        readonly NetworkVariable<byte> team = new NetworkVariable<byte>();
        readonly NetworkVariable<FixedString32Bytes> displayName = new NetworkVariable<FixedString32Bytes>();
        readonly NetworkVariable<byte> skin = new NetworkVariable<byte>();
        readonly NetworkVariable<float> health = new NetworkVariable<float>(100f);
        readonly NetworkVariable<bool> dead = new NetworkVariable<bool>();
        readonly NetworkVariable<int> kills = new NetworkVariable<int>();
        readonly NetworkVariable<int> deaths = new NetworkVariable<int>();
        readonly NetworkVariable<int> headshots = new NetworkVariable<int>();

        TeamMember member;
        Health hp;

        public int Kills => kills.Value;
        public int Deaths => deaths.Value;
        public int Headshots => headshots.Value;
        public TeamMember Member => member;

        void Awake()
        {
            member = GetComponent<TeamMember>();
            hp = GetComponent<Health>();
        }

        /// <summary>Server, before or after spawning: who this combatant is.</summary>
        public void ServerConfigure(Team newTeam, string newName, int skinIndex)
        {
            team.Value = (byte)newTeam;
            displayName.Value = new FixedString32Bytes(DamagePacket.Truncate(newName, 28));
            skin.Value = (byte)Mathf.Clamp(skinIndex, 0, 255);
            ApplyIdentity();
        }

        public void ServerAddKill(bool headshot)
        {
            kills.Value++;
            if (headshot) headshots.Value++;
        }

        public void ServerAddDeath() => deaths.Value++;

        public void ServerResetStats()
        {
            kills.Value = 0;
            deaths.Value = 0;
            headshots.Value = 0;
        }

        public override void OnNetworkSpawn()
        {
            team.OnValueChanged += OnIdentityChanged;
            displayName.OnValueChanged += OnNameChanged;
            skin.OnValueChanged += OnIdentityChanged;
            ApplyIdentity();

            if (IsServer)
            {
                hp.Damaged += OnServerDamaged;
                hp.Died += OnServerDied;
                hp.Revived += OnServerRevived;
                health.Value = hp.Current;
                dead.Value = hp.IsDead;
            }
            else
            {
                // Late joiners see the current state, including people already lying down.
                hp.ApplyRemote(health.Value, dead.Value, null);
            }
        }

        public override void OnNetworkDespawn()
        {
            team.OnValueChanged -= OnIdentityChanged;
            displayName.OnValueChanged -= OnNameChanged;
            skin.OnValueChanged -= OnIdentityChanged;
            if (!IsServer) return;
            hp.Damaged -= OnServerDamaged;
            hp.Died -= OnServerDied;
            hp.Revived -= OnServerRevived;
        }

        void OnIdentityChanged(byte previous, byte current) => ApplyIdentity();

        void OnNameChanged(FixedString32Bytes previous, FixedString32Bytes current) => ApplyIdentity();

        void ApplyIdentity()
        {
            var memberTeam = (Team)team.Value;
            string memberName = displayName.Value.ToString();
            if (string.IsNullOrEmpty(memberName)) memberName = member.DisplayName;
            member.Configure(memberTeam, memberName);
            GameCatalog catalog = GameCatalog.Instance;
            if (appearance && catalog) appearance.Apply(catalog.Skin(memberTeam, skin.Value));
        }

        // ------------------------------------------------------------------ Health (server side)

        void OnServerDamaged(DamageInfo damage)
        {
            health.Value = hp.Current;
            // A killing blow is sent once, by the death handler, so clients get damage and death together.
            if (hp.IsDead) return;
            HealthEventRpc(DamagePacket.From(damage), hp.Current, false);
        }

        void OnServerDied(DamageInfo damage)
        {
            health.Value = 0f;
            dead.Value = true;
            HealthEventRpc(DamagePacket.From(damage), 0f, true);

            TeamMember killer = damage.Instigator ? damage.Instigator.GetComponent<TeamMember>() : null;
            EliminationRpc(NetLookup.IdOf(killer), new FixedString64Bytes(DamagePacket.Truncate(damage.Source, 60)), damage.IsHeadshot);
        }

        void OnServerRevived()
        {
            health.Value = hp.Current;
            dead.Value = false;
            HealthEventRpc(default, hp.Current, false);
        }

        // ------------------------------------------------------------------ Health (clients)

        [Rpc(SendTo.NotServer)]
        void HealthEventRpc(DamagePacket damage, float current, bool isDead)
        {
            hp.ApplyRemote(current, isDead, damage.Amount > 0f || isDead ? damage.ToInfo() : (DamageInfo?)null);
        }

        [Rpc(SendTo.NotServer)]
        void EliminationRpc(ulong killerId, FixedString64Bytes source, bool headshot)
        {
            TeamMember killer = NetLookup.Find<TeamMember>(killerId);
            CombatEvents.RaiseElimination(new EliminationRecord(killer, member, source.ToString(), headshot));
        }
    }
}
