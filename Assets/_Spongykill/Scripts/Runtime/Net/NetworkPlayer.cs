using System.Collections.Generic;
using Spongykill.Combat;
using Spongykill.Core;
using Spongykill.Player;
using Spongykill.Weapons;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spongykill.Net
{
    /// <summary>
    /// The network side of a human player. The owner moves and shoots locally for instant response;
    /// its hits are sent to the server, which checks them (rate of fire, loadout, range, line of
    /// sight, target position) before dealing damage. The server owns health, death and respawns.
    /// Everyone else sees the third-person body driven by <see cref="NetworkAvatar"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(NetworkAvatar))]
    public class NetworkPlayer : NetworkBehaviour
    {
        [Tooltip("Input, movement, weapons and other components that only run for the owner.")]
        [SerializeField] Behaviour[] ownerOnlyComponents = new Behaviour[0];
        [Tooltip("Cameras, the HUD and other objects that only exist for the owner.")]
        [SerializeField] GameObject[] ownerOnlyObjects = new GameObject[0];
        [Tooltip("The third-person body: shadows only for the owner, fully visible to others.")]
        [SerializeField] Renderer[] thirdPersonRenderers = new Renderer[0];
        [Tooltip("The offline shadow stand-in, hidden online.")]
        [SerializeField] Renderer[] ownerShadowRenderers = new Renderer[0];
        [SerializeField] NetworkTransform networkTransform;
        [Tooltip("What blocks a claimed shot on the server: world geometry only.")]
        [SerializeField] LayerMask lineOfSightMask = 1;

        const float PositionTolerance = 3.5f;
        const float FireRateTolerance = 0.7f;
        static readonly string[] SelfDamageSources = { "Fall", "The abyss" };

        readonly NetworkVariable<double> respawnAt = new NetworkVariable<double>(-1d);
        readonly NetworkVariable<bool> holdRespawn = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly List<ShotClaim> pendingClaims = new List<ShotClaim>();
        readonly List<PelletResult> pendingPellets = new List<PelletResult>();
        readonly Dictionary<byte, float> lastShotTimes = new Dictionary<byte, float>();
        readonly byte[] serverLoadout = { GameCatalog.None, GameCatalog.None, GameCatalog.None };

        PlayerController controller;
        PlayerWeapons weapons;
        PlayerLook look;
        PlayerMotor motor;
        Health health;
        TeamMember member;
        NetworkAvatar avatar;

        /// <summary>This machine's player, once spawned.</summary>
        public static NetworkPlayer Local { get; private set; }

        /// <summary>Seconds until the server respawns this player; negative while alive.</summary>
        public float RespawnRemaining
        {
            get
            {
                if (respawnAt.Value < 0d || NetworkManager == null) return -1f;
                return Mathf.Max(0f, (float)(respawnAt.Value - NetworkManager.ServerTime.Time));
            }
        }

        public bool HoldsRespawn => holdRespawn.Value;
        public PlayerController Controller => controller;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Local = null;

        void Awake()
        {
            controller = GetComponent<PlayerController>();
            weapons = GetComponent<PlayerWeapons>();
            look = GetComponent<PlayerLook>();
            motor = GetComponent<PlayerMotor>();
            health = GetComponent<Health>();
            member = GetComponent<TeamMember>();
            avatar = GetComponent<NetworkAvatar>();
            // Until ownership is known, nothing local may run (no input, cameras or cursor capture).
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening) SetOwnerMode(false);
        }

        public override void OnNetworkSpawn()
        {
            SetOwnerMode(IsOwner);
            member.SetLocalPlayer(IsOwner);
            if (!IsOwner) return;

            Local = this;
            // Before PlayerWeapons.Start hands out the first loadout.
            if (Match.TdmMatch.Current != null) Match.TdmMatch.Current.ApplyLocalLoadout(this);
            WeaponFiring.HitClaimed += OnHitClaimed;
            weapons.PelletTraced += OnPelletTraced;
            weapons.Fired += OnFired;
            if (!IsServer) CombatAuthority.ForwardDamage = ForwardSelfDamage;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner) return;
            WeaponFiring.HitClaimed -= OnHitClaimed;
            weapons.PelletTraced -= OnPelletTraced;
            weapons.Fired -= OnFired;
            if (CombatAuthority.ForwardDamage == (System.Action<Health, DamageInfo>)ForwardSelfDamage) CombatAuthority.ForwardDamage = null;
            if (Local == this) Local = null;
            member.SetLocalPlayer(false);
        }

        void SetOwnerMode(bool owner)
        {
            foreach (Behaviour component in ownerOnlyComponents)
                if (component) component.enabled = owner;
            foreach (GameObject ownerObject in ownerOnlyObjects)
                if (ownerObject) ownerObject.SetActive(owner);
            foreach (Renderer part in thirdPersonRenderers)
                if (part) part.shadowCastingMode = owner ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            // Online the animated body casts the owner's shadow, so the offline stand-in capsule goes.
            foreach (Renderer part in ownerShadowRenderers)
                if (part) part.enabled = false;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            avatar.Publish(weapons.Current?.Definition, look.Pitch, motor.IsCrouching);
        }

        // ------------------------------------------------------------------ Owner: loadout and respawn

        /// <summary>Owner: the weapons for the next life (also tells the server, which checks shots against them).</summary>
        public void SetLoadout(WeaponDefinition primary, WeaponDefinition secondary, WeaponDefinition melee)
        {
            weapons.SetLoadout(primary, secondary, melee);
            GameCatalog catalog = GameCatalog.Instance;
            if (catalog != null && IsSpawned) LoadoutRpc(catalog.WeaponId(primary), catalog.WeaponId(secondary), catalog.WeaponId(melee));
        }

        /// <summary>Owner: keep waiting instead of respawning (the loadout screen is open).</summary>
        public void SetHoldRespawn(bool hold)
        {
            if (IsSpawned && IsOwner && holdRespawn.Value != hold) holdRespawn.Value = hold;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void LoadoutRpc(byte primary, byte secondary, byte melee) => ServerSetLoadout(primary, secondary, melee);

        public void ServerSetLoadout(byte primary, byte secondary, byte melee)
        {
            serverLoadout[0] = primary;
            serverLoadout[1] = secondary;
            serverLoadout[2] = melee;
        }

        public void ServerSetRespawnTime(double serverTime) => respawnAt.Value = serverTime;

        /// <summary>Server: revive at a spawn point with brief protection, and move the owner there.</summary>
        public void ServerRespawn(Vector3 position, Quaternion rotation, float protection)
        {
            respawnAt.Value = -1d;
            health.ResetHealth();
            health.InvulnerableUntil = Time.time + protection;
            RespawnRpc(position, rotation);
        }

        [Rpc(SendTo.Owner)]
        void RespawnRpc(Vector3 position, Quaternion rotation)
        {
            controller.SetSpawnPoint(position, rotation);
            controller.Respawn();
            if (networkTransform) networkTransform.Teleport(transform.position, transform.rotation, transform.localScale);
        }

        // ------------------------------------------------------------------ Owner: shots

        void OnHitClaimed(HitClaim claim)
        {
            if (claim.Shooter != gameObject) return;
            ulong victim = NetLookup.IdOf(claim.Victim);
            if (victim == DamagePacket.NoInstigator) return;
            pendingClaims.Add(new ShotClaim
            {
                VictimId = victim,
                Zone = (byte)claim.Zone,
                Distance = claim.Distance,
                Point = claim.Point,
                Direction = claim.Direction,
            });
        }

        void OnPelletTraced(PelletResult pellet) => pendingPellets.Add(pellet);

        void OnFired(WeaponRuntime weapon)
        {
            GameCatalog catalog = GameCatalog.Instance;
            if (catalog != null)
            {
                var packet = new ShotPacket
                {
                    Origin = look.CameraPivot ? look.CameraPivot.position : transform.position + Vector3.up * 1.6f,
                    Effects = NetworkAvatar.Pack(catalog.WeaponId(weapon.Definition), weapons.LastMuzzlePosition, pendingPellets),
                    Claims = pendingClaims.ToArray(),
                };
                ShotRpc(packet);
            }
            pendingClaims.Clear();
            pendingPellets.Clear();
        }

        // ------------------------------------------------------------------ Server: shot checks

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ShotRpc(ShotPacket shot)
        {
            GameCatalog catalog = GameCatalog.Instance;
            WeaponDefinition weapon = catalog ? catalog.Weapon(shot.Effects.WeaponId) : null;
            if (weapon == null || health.IsDead) return;
            // Firing ends spawn protection.
            health.InvulnerableUntil = float.NegativeInfinity;

            // The host's own hits were already applied locally; only remote claims need checking.
            bool claimsValid = IsAllowedWeapon(shot.Effects.WeaponId) && CheckFireRate(shot.Effects.WeaponId, weapon);
            if (claimsValid && shot.Claims != null)
            {
                int limit = weapon.IsMelee ? 1 : Mathf.Max(1, weapon.pellets);
                for (int i = 0; i < shot.Claims.Length && i < limit; i++) ApplyClaim(weapon, shot.Origin, shot.Claims[i]);
            }
            ShotEffectsRpc(shot.Effects);
        }

        bool IsAllowedWeapon(byte id)
        {
            // Before the client has reported a loadout (or for the host) any catalogued weapon is allowed.
            if (serverLoadout[0] == GameCatalog.None && serverLoadout[1] == GameCatalog.None && serverLoadout[2] == GameCatalog.None) return true;
            return id == serverLoadout[0] || id == serverLoadout[1] || id == serverLoadout[2];
        }

        bool CheckFireRate(byte id, WeaponDefinition weapon)
        {
            float now = Time.time;
            if (lastShotTimes.TryGetValue(id, out float last) && now - last < weapon.FireInterval * FireRateTolerance) return false;
            lastShotTimes[id] = now;
            return true;
        }

        void ApplyClaim(WeaponDefinition weapon, Vector3 origin, in ShotClaim claim)
        {
            Health victim = NetLookup.Find<Health>(claim.VictimId);
            if (victim == null || victim == health || victim.IsDead) return;
            if (TeamMember.AreAllies(gameObject, victim.gameObject)) return;
            if (claim.Distance > weapon.range + 2f) return;

            // The claimed origin must be near where the server thinks the shooter is...
            if ((origin - transform.position).sqrMagnitude > PositionTolerance * PositionTolerance * 4f) return;
            // ...the hit point near where the victim is (allowing for latency)...
            Vector3 offset = claim.Point - victim.transform.position;
            if (new Vector2(offset.x, offset.z).magnitude > PositionTolerance || offset.y < -1f || offset.y > 3f) return;
            // ...and no wall between them.
            Vector3 toPoint = claim.Point - origin;
            float distance = toPoint.magnitude;
            if (distance > 0.2f && Physics.Raycast(origin, toPoint / distance, distance - 0.15f, lineOfSightMask, QueryTriggerInteraction.Ignore)) return;

            var zone = (HitZone)claim.Zone;
            float amount = WeaponFiring.DamageFor(weapon, claim.Distance, zone);
            float applied = victim.ApplyDamage(new DamageInfo(amount, zone, claim.Point, claim.Direction, gameObject, weapon.displayName));
            if (applied > 0f && victim.IsDead && !IsOwnedByServer) KillConfirmedRpc(zone == HitZone.Head, claim.Distance, applied);
        }

        [Rpc(SendTo.Owner)]
        void KillConfirmedRpc(bool headshot, float distance, float damage)
        {
            weapons.ConfirmHit(new HitFeedback(damage, headshot, true, distance));
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        void ShotEffectsRpc(ShotEffects shot) => avatar.PlayShot(shot);

        // ------------------------------------------------------------------ Self damage (falls)

        void ForwardSelfDamage(Health target, DamageInfo damage)
        {
            if (target != health || damage.Instigator != null) return;
            SelfDamageRpc(damage.Amount, new FixedString32Bytes(DamagePacket.Truncate(damage.Source, 28)));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SelfDamageRpc(float amount, FixedString32Bytes source)
        {
            string name = source.ToString();
            if (amount <= 0f || amount > health.Max * 2f || System.Array.IndexOf(SelfDamageSources, name) < 0) return;
            health.ApplyDamage(new DamageInfo(amount, HitZone.Body, transform.position, Vector3.down, null, name));
        }
    }
}
