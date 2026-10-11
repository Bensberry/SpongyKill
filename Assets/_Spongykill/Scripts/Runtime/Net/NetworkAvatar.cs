using Spongykill.Audio;
using Spongykill.Characters;
using Spongykill.Combat;
using Spongykill.Effects;
using Spongykill.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace Spongykill.Net
{
    /// <summary>
    /// How a character looks to everyone who doesn't control it: the weapon in its hands, aim pitch,
    /// crouching, walk animation from its synced movement, death topple, and other people's shots
    /// (tracers, muzzle flash, 3D sound). The controlling machine writes the state; remote copies
    /// play it back.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class NetworkAvatar : NetworkBehaviour
    {
        [SerializeField] CharacterAnimator characterAnimator;
        [SerializeField] Transform weaponMount;
        [SerializeField] HitboxRig hitboxRig;
        [Tooltip("3D source for sounds others hear from this character.")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] float eyeHeight = 1.6f;
        [Tooltip("Also animate the body on the controlling machine (players: it casts their shadow). Bots animate themselves on the server.")]
        [SerializeField] bool animateWhenOwner;

        readonly NetworkVariable<byte> weaponId = new NetworkVariable<byte>(GameCatalog.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<float> aimPitch = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> crouching = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        Health health;
        GameObject weaponModel;
        WeaponViewmodel weaponView;
        Vector3 lastPosition;
        Vector3 velocity;

        /// <summary>True on copies of the character this machine doesn't control: they animate from network state.</summary>
        public bool IsRemoteCopy => IsSpawned && !IsOwner;

        bool AnimatesBody => IsSpawned && (!IsOwner || animateWhenOwner);

        void Awake() => health = GetComponent<Health>();

        public override void OnNetworkSpawn()
        {
            weaponId.OnValueChanged += OnWeaponChanged;
            crouching.OnValueChanged += OnCrouchChanged;
            health.Died += OnDied;
            health.Revived += OnRevived;
            lastPosition = transform.position;
            if (!IsRemoteCopy) return;
            OnWeaponChanged(GameCatalog.None, weaponId.Value);
            OnCrouchChanged(false, crouching.Value);
        }

        public override void OnNetworkDespawn()
        {
            weaponId.OnValueChanged -= OnWeaponChanged;
            crouching.OnValueChanged -= OnCrouchChanged;
            health.Died -= OnDied;
            health.Revived -= OnRevived;
        }

        /// <summary>Called by the controlling machine whenever its state may have changed.</summary>
        public void Publish(WeaponDefinition weapon, float pitch, bool crouch)
        {
            if (!IsSpawned || !IsOwner) return;
            GameCatalog catalog = GameCatalog.Instance;
            byte id = catalog ? catalog.WeaponId(weapon) : GameCatalog.None;
            if (weaponId.Value != id) weaponId.Value = id;
            if (Mathf.Abs(Mathf.DeltaAngle(aimPitch.Value, pitch)) > 1f) aimPitch.Value = pitch;
            if (crouching.Value != crouch) crouching.Value = crouch;
        }

        void Update()
        {
            if (!AnimatesBody) return;
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;
            Vector3 position = transform.position;
            Vector3 frameVelocity = (position - lastPosition) / deltaTime;
            lastPosition = position;
            // Teleports (respawns) would read as a sprint across the map for a frame.
            if (frameVelocity.sqrMagnitude > 400f) frameVelocity = Vector3.zero;
            velocity = Vector3.Lerp(velocity, frameVelocity, 1f - Mathf.Exp(-12f * deltaTime));
            if (characterAnimator && !health.IsDead) characterAnimator.Tick(velocity, aimPitch.Value, crouching.Value, deltaTime);
        }

        void OnWeaponChanged(byte previous, byte current)
        {
            if (!IsRemoteCopy || Application.isBatchMode) return;
            GameCatalog catalog = GameCatalog.Instance;
            weaponView = ThirdPersonWeapon.Attach(weaponMount, catalog ? catalog.Weapon(current) : null, ref weaponModel);
        }

        void OnCrouchChanged(bool previous, bool current)
        {
            if (IsRemoteCopy && hitboxRig) hitboxRig.SetHeight(current ? 1.2f : 1.8f);
        }

        void OnDied(DamageInfo damage)
        {
            if (AnimatesBody && characterAnimator) characterAnimator.PlayDeath(damage.Direction);
        }

        void OnRevived()
        {
            if (AnimatesBody && characterAnimator) characterAnimator.ResetPose();
        }

        /// <summary>Draws a shot someone else fired: tracers from this character's gun, impacts, flash and sound.</summary>
        public void PlayShot(in ShotEffects shot)
        {
            if (Application.isBatchMode) return;
            GameCatalog catalog = GameCatalog.Instance;
            WeaponDefinition weapon = catalog ? catalog.Weapon(shot.WeaponId) : null;
            if (weapon == null) return;
            if (!weapon.IsMelee) CombatEvents.RaiseShot(gameObject);

            Vector3 muzzle = weaponView ? weaponView.Muzzle.position : transform.position + Vector3.up * eyeHeight;
            CombatEffects effects = CombatEffects.Instance;
            for (int i = 0; i < shot.Count; i++)
            {
                PelletResult pellet = shot.Pellet(i);
                if (effects == null) break;
                if (!weapon.IsMelee) effects.Tracer(muzzle, pellet.EndPoint, weapon.tracerColor);
                if (pellet.HitSomething) effects.Impact(pellet.EndPoint, pellet.Normal, pellet.HitCharacter);
            }
            if (weaponView && !weapon.IsMelee) weaponView.Flash();
            if (audioSource) audioSource.PlayOneShot(ProceduralSfx.Shot(weapon.shotSoundPitch, weapon.shotSoundLength, weapon.shotSoundNoise), 1f);
        }

        /// <summary>Packs locally traced pellets into the network form.</summary>
        public static ShotEffects Pack(byte weapon, Vector3 muzzle, System.Collections.Generic.List<PelletResult> pellets)
        {
            var shot = new ShotEffects
            {
                WeaponId = weapon,
                Muzzle = muzzle,
                Ends = new Vector3[pellets.Count],
                Normals = new Vector3[pellets.Count],
                Flags = new byte[pellets.Count],
            };
            for (int i = 0; i < pellets.Count; i++)
            {
                shot.Ends[i] = pellets[i].EndPoint;
                shot.Normals[i] = pellets[i].Normal;
                shot.Flags[i] = (byte)((pellets[i].HitSomething ? 1 : 0) | (pellets[i].HitCharacter ? 2 : 0));
            }
            return shot;
        }
    }
}
