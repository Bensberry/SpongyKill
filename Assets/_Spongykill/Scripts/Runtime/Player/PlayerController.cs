using System.Collections;
using Spongykill.Audio;
using Spongykill.Combat;
using Spongykill.Controls;
using Spongykill.Core;
using Spongykill.Weapons;
using UnityEngine;

namespace Spongykill.Player
{
    /// <summary>
    /// Routes local input (keyboard and mouse, or touch and gyro) into look, movement, weapons and
    /// interaction once per frame (in that order so each step sees the previous one's result), owns
    /// cursor capture, and handles death and respawn. Menus and the match block input through
    /// <see cref="InputGate"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerInputHandler), typeof(PlayerMotor), typeof(PlayerLook))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] bool lockCursorOnStart = true;
        [Tooltip("Off: hold the crouch key. On: press it to toggle.")]
        [SerializeField] bool toggleCrouch;
        [Tooltip("Falling below this height eliminates the player.")]
        [SerializeField] float killHeight = -25f;
        [Tooltip("Degrees turned per pixel of touch drag on a 1080-pixel-tall screen at sensitivity 1.")]
        [SerializeField] float touchDegreesPerPixel = 0.11f;

        [Header("Death")]
        [Tooltip("Respawn automatically after being eliminated. The round system will switch this off.")]
        [SerializeField] bool autoRespawn = true;
        [SerializeField] float respawnDelay = 3f;
        [SerializeField] AudioSource audioSource;

        PlayerInputHandler input;
        PlayerMotor motor;
        PlayerLook look;
        Health health;
        PlayerWeapons weapons;
        PlayerInteractor interactor;
        HitboxRig hitboxRig;
        bool crouchToggled;
        bool suppressFireUntilRelease;
        float respawnTime = -1f;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        Coroutine deathCamera;

        public bool IsDead => health != null && health.IsDead;
        public PlayerWeapons Weapons => weapons;

        /// <summary>Respawn by itself after a delay. The match turns this off and respawns the player itself.</summary>
        public bool AutoRespawn
        {
            get => autoRespawn;
            set
            {
                autoRespawn = value;
                if (!value) respawnTime = -1f;
            }
        }
        /// <summary>Seconds until the automatic respawn, or negative when none is pending.</summary>
        public float RespawnTimeRemaining => respawnTime < 0f ? -1f : Mathf.Max(0f, respawnTime - Time.time);

        public bool ToggleCrouch
        {
            get => toggleCrouch;
            set
            {
                toggleCrouch = value;
                crouchToggled = false;
            }
        }

        void Awake()
        {
            input = GetComponent<PlayerInputHandler>();
            motor = GetComponent<PlayerMotor>();
            look = GetComponent<PlayerLook>();
            TryGetComponent(out health);
            TryGetComponent(out weapons);
            TryGetComponent(out interactor);
            hitboxRig = GetComponentInChildren<HitboxRig>();
        }

        void OnEnable()
        {
            GameSettings.Changed += ApplySettings;
            motor.HeightChanged += OnHeightChanged;
            if (health == null) return;
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        void OnDisable()
        {
            GameSettings.Changed -= ApplySettings;
            motor.HeightChanged -= OnHeightChanged;
            if (health == null) return;
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        void Start()
        {
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            ApplySettings();
            if (lockCursorOnStart && !ControlScheme.UseTouch && !InputGate.MenuOpen) CursorLock.Lock();
        }

        void ApplySettings()
        {
            GameSettings settings = GameSettings.Current;
            look.InvertY = settings.invertY;
            if (toggleCrouch != settings.toggleCrouch) ToggleCrouch = settings.toggleCrouch;
        }

        void Update()
        {
            bool touch = ControlScheme.UseTouch;
            // The click that recaptures the cursor must not also fire a shot.
            if (!touch && UpdateCursorCapture()) suppressFireUntilRelease = true;
            else if (suppressFireUntilRelease && !input.FireHeld) suppressFireUntilRelease = false;

            bool alive = !IsDead;
            bool canLook = alive && (touch || CursorLock.IsLocked) && !InputGate.MenuOpen;
            bool hasControl = canLook && !InputGate.MatchFrozen;
            bool fireHeld = hasControl && !suppressFireUntilRelease && input.FireHeld;
            bool scoped = weapons != null && weapons.IsAiming;
            Vector2 lookDegrees = canLook ? ReadLookDegrees(touch) : Vector2.zero;

            if (alive) look.Tick(lookDegrees);

            // Touch crouch is always a toggle: holding a button while aiming with the other thumb is awkward.
            bool crouchIsToggle = toggleCrouch || touch;
            if (crouchIsToggle && hasControl && input.CrouchPressed) crouchToggled = !crouchToggled;
            bool crouch = alive && (crouchIsToggle ? crouchToggled : hasControl && input.CrouchHeld);
            bool sprintBlocked = weapons != null && weapons.BlocksSprint(fireHeld, scoped);

            motor.SetMoveInput(hasControl ? input.Move : Vector2.zero);
            motor.SetSprint(hasControl && input.SprintHeld && !sprintBlocked);
            motor.SetCrouch(crouch);
            if (hasControl && input.JumpPressed) motor.RequestJump();
            motor.Tick(Time.deltaTime);

            // The viewmodel sway was tuned in mouse counts at the default 0.08 degrees per count.
            if (weapons != null) weapons.Tick(hasControl ? ReadWeaponInput(fireHeld, lookDegrees / 0.08f) : WeaponInput.None, Time.deltaTime);
            if (interactor != null) interactor.Tick(hasControl, hasControl && input.InteractPressed);

            if (alive && transform.position.y < killHeight) FellOutOfWorld();
            if (respawnTime >= 0f && Time.time >= respawnTime) Respawn();
        }

        /// <returns>x = degrees to turn right, y = degrees to look up.</returns>
        Vector2 ReadLookDegrees(bool touch)
        {
            GameSettings settings = GameSettings.Current;
            bool scoped = weapons != null && weapons.IsScoped;
            Vector2 degrees = input.LookDelta * (look.Sensitivity * settings.mouseSensitivity);

            if (touch)
            {
                // Normalised to screen height so the same swipe turns the same amount on any phone.
                float perPixel = touchDegreesPerPixel * settings.touchSensitivity * 1080f / Mathf.Max(1, Screen.height);
                degrees += input.TouchLookPixels * perPixel;
                bool gyro = settings.gyro == GyroMode.On || (settings.gyro == GyroMode.ScopedOnly && scoped);
                if (gyro)
                {
                    Vector2 turn = GyroInput.ReadDegrees(Time.deltaTime) * settings.gyroSensitivity;
                    if (settings.gyroInvertX) turn.x = -turn.x;
                    if (settings.gyroInvertY) turn.y = -turn.y;
                    degrees += turn;
                }
            }

            if (scoped) degrees *= settings.scopeSensitivity;
            return degrees;
        }

        WeaponInput ReadWeaponInput(bool fireHeld, Vector2 swayDelta)
        {
            return new WeaponInput
            {
                FireHeld = fireHeld,
                FirePressed = fireHeld && input.FirePressed,
                AimPressed = input.AimPressed,
                ReloadPressed = input.ReloadPressed,
                LastWeaponPressed = input.LastWeaponPressed,
                NextWeaponPressed = input.NextWeaponPressed,
                InspectPressed = input.InspectPressed,
                SlotPressed = input.WeaponSlotPressed,
                Scroll = input.WeaponScroll,
                LookDelta = swayDelta,
            };
        }

        public void SetSpawnPoint(Vector3 position, Quaternion rotation)
        {
            spawnPosition = position;
            spawnRotation = rotation;
        }

        public void Respawn()
        {
            if (deathCamera != null)
            {
                StopCoroutine(deathCamera);
                deathCamera = null;
            }
            if (look.CameraPivot) look.CameraPivot.localPosition = Vector3.zero;

            respawnTime = -1f;
            crouchToggled = false;
            motor.Teleport(spawnPosition, spawnRotation);
            look.ResetRecoil();
            look.SetYawPitch(spawnRotation.eulerAngles.y, 0f);
            if (health != null) health.ResetHealth();
            if (weapons != null)
            {
                weapons.SetDead(false);
                weapons.ResetLoadout();
            }
            if (!ControlScheme.UseTouch && !InputGate.MenuOpen) CursorLock.Lock();
        }

        void FellOutOfWorld()
        {
            if (health == null)
            {
                Respawn();
                return;
            }
            health.ApplyDamage(new DamageInfo(health.Current + 1f, HitZone.Body, transform.position, Vector3.down, null, "The abyss"));
        }

        /// <returns>True when this frame's click recaptured the cursor.</returns>
        bool UpdateCursorCapture()
        {
            // With a menu system in the scene, Escape opens the pause menu, which frees the cursor itself.
            if (input.PausePressed && !InputGate.MenusPresent)
            {
                CursorLock.Unlock();
                return false;
            }
            if (InputGate.MenuOpen || IsDead || CursorLock.IsLocked || !input.MouseFirePressed) return false;
            CursorLock.Lock();
            return true;
        }

        void OnHeightChanged(float height)
        {
            if (hitboxRig) hitboxRig.SetHeight(height);
        }

        void OnDamaged(DamageInfo damage)
        {
            if (audioSource) audioSource.PlayOneShot(ProceduralSfx.Hurt, 0.8f);
        }

        void OnDied(DamageInfo damage)
        {
            if (weapons != null) weapons.SetDead(true);
            if (autoRespawn) respawnTime = Time.time + respawnDelay;
            deathCamera = StartCoroutine(PlayDeathCamera());
        }

        IEnumerator PlayDeathCamera()
        {
            Transform pivot = look.CameraPivot;
            if (pivot == null) yield break;

            // Sink toward the floor and tip over sideways.
            float eyeHeight = pivot.parent ? pivot.parent.localPosition.y : 1.6f;
            Vector3 startPosition = pivot.localPosition;
            Quaternion startRotation = pivot.localRotation;
            var endPosition = new Vector3(0f, 0.45f - eyeHeight, 0f);
            Quaternion endRotation = startRotation * Quaternion.Euler(-10f, 0f, 35f);

            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
            {
                float eased = 1f - (1f - t) * (1f - t);
                pivot.SetLocalPositionAndRotation(Vector3.Lerp(startPosition, endPosition, eased), Quaternion.Slerp(startRotation, endRotation, eased));
                yield return null;
            }
            pivot.SetLocalPositionAndRotation(endPosition, endRotation);
        }
    }
}
