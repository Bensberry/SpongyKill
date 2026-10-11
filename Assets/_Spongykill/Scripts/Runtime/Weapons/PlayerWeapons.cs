using System;
using Spongykill.Audio;
using Spongykill.Effects;
using Spongykill.Player;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Spongykill.Weapons
{
    /// <summary>Weapon commands for one frame, already filtered by whether the player has control.</summary>
    public struct WeaponInput
    {
        public bool FireHeld;
        public bool FirePressed;
        /// <summary>Toggles the scope on weapons that have one (CS-style: other weapons fire from the hip only).</summary>
        public bool AimPressed;
        public bool ReloadPressed;
        public bool LastWeaponPressed;
        public bool NextWeaponPressed;
        public bool InspectPressed;
        /// <summary>Zero-based slot key pressed this frame, or -1.</summary>
        public int SlotPressed;
        public float Scroll;
        public Vector2 LookDelta;

        public static WeaponInput None => new WeaponInput { SlotPressed = -1 };
    }

    /// <summary>Confirmation that a shot damaged someone, for hit markers.</summary>
    public readonly struct HitFeedback
    {
        public readonly float Damage;
        public readonly bool Headshot;
        public readonly bool Killed;
        public readonly float Distance;

        public HitFeedback(float damage, bool headshot, bool killed, float distance)
        {
            Damage = damage;
            Headshot = headshot;
            Killed = killed;
            Distance = distance;
        }
    }

    /// <summary>
    /// The local player's weapons: primary / secondary / melee slots, switching with draw times,
    /// firing with spread and recoil, reloading, scoping (snipers only, toggled like CS) and the
    /// first-person viewmodel. Driven once per frame by <see cref="PlayerController"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerLook), typeof(PlayerMotor))]
    public class PlayerWeapons : MonoBehaviour
    {
        const int MaxSlots = 3;

        [SerializeField] WeaponDefinition[] startingLoadout = new WeaponDefinition[MaxSlots];
        [SerializeField] Camera worldCamera;
        [SerializeField] Transform viewmodelRoot;
        [SerializeField] AudioSource audioSource;
        [Tooltip("What shots can hit. Exclude the player, bot movement capsules and viewmodels.")]
        [SerializeField] LayerMask hitMask = Physics.DefaultRaycastLayers;
        [Tooltip("Delay between leaving a sprint and being able to fire.")]
        [SerializeField] float sprintToFireTime = 0.12f;
        [Tooltip("A semi-automatic trigger press this early still fires as soon as the weapon is ready.")]
        [SerializeField] float triggerBufferTime = 0.1f;

        public event Action EquippedChanged;
        public event Action<WeaponRuntime> Fired;
        public event Action<HitFeedback> HitConfirmed;
        /// <summary>Each traced pellet of the latest shot, raised before <see cref="Fired"/> (for network effects).</summary>
        public event Action<PelletResult> PelletTraced;

        readonly WeaponRuntime[] slots = new WeaponRuntime[MaxSlots];
        PlayerLook look;
        PlayerMotor motor;
        Action<PelletResult> pelletHandler;
        int currentSlot = -1;
        int previousSlot = -1;
        float drawStartTime;
        float drawCompleteTime;
        float reloadStartTime;
        float reloadCompleteTime = -1f;
        float inspectStartTime = -1f;
        float inspectDuration;
        float aimBlend;
        bool scopeToggled;
        float baseFieldOfView = 60f;
        float lastSprintTime = float.NegativeInfinity;
        float triggerBufferedUntil = float.NegativeInfinity;
        float lastShotTime = float.NegativeInfinity;
        int consecutiveShots;
        float recoilSeed;
        bool isDead;
        Vector3 shotMuzzlePosition;
        Color shotTracerColor;

        public WeaponRuntime Current => currentSlot >= 0 ? slots[currentSlot] : null;
        public int CurrentSlotIndex => currentSlot;
        public int SlotCount => slots.Length;
        public bool IsReloading => reloadCompleteTime >= 0f;
        public float ReloadProgress => IsReloading ? Mathf.InverseLerp(reloadStartTime, reloadCompleteTime, Time.time) : 0f;
        public bool IsDrawing => Time.time < drawCompleteTime;
        public bool IsInspecting => inspectStartTime >= 0f && Time.time < inspectStartTime + inspectDuration;
        public float AimBlend => EasedAim;
        /// <summary>Scoping in or scoped.</summary>
        public bool IsAiming => scopeToggled;
        public bool IsScoped => Current != null && Current.Definition.aimUsesScope && aimBlend >= 0.95f;
        /// <summary>The weapon in hand can be scoped (the touch scope button only shows for these).</summary>
        public bool CurrentCanAim => Current != null && Current.Definition.canAim;
        /// <summary>Current cone half-angle in degrees, used by the crosshair.</summary>
        public float CurrentSpread { get; private set; }
        public float WorldFieldOfView => worldCamera ? worldCamera.fieldOfView : baseFieldOfView;

        float EasedAim => Mathf.SmoothStep(0f, 1f, aimBlend);

        public WeaponRuntime GetSlot(int index) => slots[index];

        /// <summary>Where the latest shot's tracers start (the viewmodel muzzle).</summary>
        public Vector3 LastMuzzlePosition => shotMuzzlePosition;

        /// <summary>The server confirmed a hit the client only predicted (kills arrive this way online).</summary>
        public void ConfirmHit(in HitFeedback hit)
        {
            if (hit.Killed) Play(ProceduralSfx.Kill, 0.9f);
            HitConfirmed?.Invoke(hit);
        }

        void Awake()
        {
            look = GetComponent<PlayerLook>();
            motor = GetComponent<PlayerMotor>();
            pelletHandler = OnPellet;
            if (worldCamera) baseFieldOfView = worldCamera.fieldOfView;
        }

        void Start() => ResetLoadout();

        /// <summary>The weapons given on every (re)spawn, one per slot; applied by the next <see cref="ResetLoadout"/>.</summary>
        public void SetLoadout(WeaponDefinition primary, WeaponDefinition secondary, WeaponDefinition melee)
        {
            startingLoadout = new[] { primary, secondary, melee };
        }

        public WeaponDefinition LoadoutWeapon(WeaponSlot slot)
        {
            int index = (int)slot;
            return index < startingLoadout.Length ? startingLoadout[index] : null;
        }

        /// <summary>Restores the starting loadout with full ammunition.</summary>
        public void ResetLoadout()
        {
            if (Current?.Viewmodel) Current.Viewmodel.gameObject.SetActive(false);
            for (int i = 0; i < MaxSlots; i++)
                SetSlot(i, i < startingLoadout.Length ? startingLoadout[i] : null);
            currentSlot = -1;
            previousSlot = -1;
            Equip(FindOccupiedSlot(-1, 1));
        }

        /// <summary>Puts a weapon in its slot (refilling it if already owned) and switches to it.</summary>
        public void Give(WeaponDefinition weapon)
        {
            if (weapon == null) return;
            int slot = (int)weapon.slot;
            SetSlot(slot, weapon);
            Equip(slot, force: true);
        }

        public bool Owns(WeaponDefinition weapon)
        {
            foreach (WeaponRuntime owned in slots)
                if (owned != null && owned.Definition == weapon) return true;
            return false;
        }

        public void Equip(int slot, bool force = false)
        {
            if (slot < 0 || slot >= MaxSlots || slots[slot] == null) return;
            if (slot == currentSlot && !force) return;

            CancelReload();
            CancelInspect();
            if (Current?.Viewmodel) Current.Viewmodel.gameObject.SetActive(false);
            if (slot != currentSlot && currentSlot >= 0) previousSlot = currentSlot;
            currentSlot = slot;

            WeaponRuntime weapon = Current;
            drawStartTime = Time.time;
            drawCompleteTime = Time.time + weapon.Definition.drawTime;
            aimBlend = 0f;
            scopeToggled = false;
            consecutiveShots = 0;
            triggerBufferedUntil = float.NegativeInfinity;
            if (weapon.Viewmodel) weapon.Viewmodel.gameObject.SetActive(!isDead);
            EquippedChanged?.Invoke();
        }

        /// <summary>Firing and aiming both stop the player from sprinting.</summary>
        public bool BlocksSprint(bool fireHeld, bool aimHeld)
        {
            WeaponRuntime weapon = Current;
            if (weapon == null || isDead) return false;
            return fireHeld || (aimHeld && weapon.Definition.canAim);
        }

        public void SetDead(bool dead)
        {
            isDead = dead;
            CancelReload();
            CancelInspect();
            aimBlend = 0f;
            scopeToggled = false;
            ApplyFieldOfView(baseFieldOfView);
            motor.SpeedMultiplier = 1f;
            if (Current?.Viewmodel) Current.Viewmodel.gameObject.SetActive(!dead);
        }

        public void Tick(in WeaponInput input, float deltaTime)
        {
            if (isDead || Current == null) return;
            HandleSwitching(input);

            WeaponRuntime weapon = Current;
            WeaponDefinition definition = weapon.Definition;

            if (motor.IsSprinting) lastSprintTime = Time.time;
            UpdateReload();
            UpdateAim(input.AimPressed, deltaTime);
            weapon.RecoverBloom(definition.bloomRecovery * deltaTime);
            if (Time.time - lastShotTime > definition.FireInterval + 0.15f) consecutiveShots = 0;

            if (input.ReloadPressed) TryStartReload();
            if (input.InspectPressed) TryInspect(weapon);
            if (input.FirePressed) triggerBufferedUntil = Time.time + triggerBufferTime;
            // Automatic weapons also fire on the press edge so a click shorter than a frame isn't lost.
            bool wantsToFire = definition.fireMode == FireMode.Automatic
                ? input.FireHeld || input.FirePressed
                : Time.time <= triggerBufferedUntil;
            if (wantsToFire) TryFire(weapon);
            if (!weapon.HasAmmo && Time.time >= weapon.NextFireTime) TryStartReload();

            CurrentSpread = ComputeSpread(weapon);
            motor.SpeedMultiplier = definition.moveSpeedFactor * Mathf.Lerp(1f, definition.aimMoveSpeedFactor, EasedAim);
            TickViewmodel(weapon, input.LookDelta, deltaTime);
        }

        void HandleSwitching(in WeaponInput input)
        {
            if (input.SlotPressed >= 0) Equip(input.SlotPressed);
            else if (input.LastWeaponPressed && previousSlot >= 0) Equip(previousSlot);
            else if (input.NextWeaponPressed) Equip(FindOccupiedSlot(currentSlot, 1));
            else if (input.Scroll < 0f) Equip(FindOccupiedSlot(currentSlot, 1));
            else if (input.Scroll > 0f) Equip(FindOccupiedSlot(currentSlot, -1));
        }

        int FindOccupiedSlot(int from, int step)
        {
            for (int i = 1; i <= MaxSlots; i++)
            {
                int slot = ((from + step * i) % MaxSlots + MaxSlots) % MaxSlots;
                if (slots[slot] != null) return slot;
            }
            return from;
        }

        void SetSlot(int slot, WeaponDefinition weapon)
        {
            WeaponRuntime existing = slots[slot];
            if (existing != null && existing.Definition == weapon)
            {
                existing.Refill();
                return;
            }

            if (existing?.Viewmodel) Destroy(existing.Viewmodel.gameObject);
            slots[slot] = null;
            if (weapon == null) return;

            var runtime = new WeaponRuntime(weapon);
            if (weapon.viewmodelPrefab && viewmodelRoot)
            {
                GameObject model = Instantiate(weapon.viewmodelPrefab, viewmodelRoot, false);
                model.SetActive(false);
                runtime.Viewmodel = model.GetComponent<WeaponViewmodel>();
            }
            slots[slot] = runtime;
        }

        void TryFire(WeaponRuntime weapon)
        {
            WeaponDefinition definition = weapon.Definition;
            if (IsDrawing || IsReloading || Time.time < weapon.NextFireTime) return;
            if (motor.IsSprinting || Time.time - lastSprintTime < sprintToFireTime) return;

            triggerBufferedUntil = float.NegativeInfinity;
            CancelInspect();
            if (!weapon.TryConsumeRound())
            {
                weapon.NextFireTime = Time.time + 0.25f;
                Play(ProceduralSfx.DryFire, 0.8f);
                TryStartReload();
                return;
            }

            // Keep the cadence exact while the trigger is held instead of losing a frame per shot.
            float interval = definition.FireInterval;
            weapon.NextFireTime = Time.time - weapon.NextFireTime < interval ? weapon.NextFireTime + interval : Time.time + interval;

            Transform view = worldCamera.transform;
            float spread = ComputeSpread(weapon);
            shotMuzzlePosition = weapon.Viewmodel ? weapon.Viewmodel.Muzzle.position : view.position;
            shotTracerColor = definition.tracerColor;
            ShotReport report = WeaponFiring.Fire(definition, view.position, view.forward, spread, gameObject, hitMask, pelletHandler);

            weapon.AddBloom(definition.spreadPerShot);
            consecutiveShots++;
            lastShotTime = Time.time;
            if (consecutiveShots == 1) recoilSeed = Random.Range(0f, 100f);
            ApplyRecoil(definition);

            if (weapon.Viewmodel)
            {
                weapon.Viewmodel.Kick();
                if (!weapon.IsMelee) weapon.Viewmodel.Flash();
            }
            Play(ProceduralSfx.Shot(definition.shotSoundPitch, definition.shotSoundLength, definition.shotSoundNoise), 0.9f);
            Fired?.Invoke(weapon);

            if (report.HitCharacter)
            {
                Play(report.Killed ? ProceduralSfx.Kill : report.Headshot ? ProceduralSfx.Headshot : ProceduralSfx.Hit, 0.9f);
                HitConfirmed?.Invoke(new HitFeedback(report.Damage, report.Headshot, report.Killed, report.Distance));
            }
        }

        void ApplyRecoil(WeaponDefinition definition)
        {
            if (definition.recoilVertical <= 0f && definition.recoilHorizontal <= 0f) return;

            float scale = Mathf.Lerp(1f, definition.aimRecoilFactor, EasedAim) * (motor.IsCrouching ? 0.85f : 1f);
            // The first shots climb hardest, then a sustained spray settles into a sideways wander.
            float climb = consecutiveShots <= 3 ? 1.25f : consecutiveShots <= 10 ? 1f : 0.45f;
            float wander = (Mathf.PerlinNoise(recoilSeed, consecutiveShots * 0.35f) * 2f - 1f) * (consecutiveShots <= 2 ? 0.3f : 1.5f);
            look.AddRecoil(definition.recoilVertical * climb * scale, definition.recoilHorizontal * wander * scale,
                definition.recoilRecoverySpeed, definition.FireInterval + 0.05f);
        }

        float ComputeSpread(WeaponRuntime weapon) =>
            WeaponSpread.Compute(weapon.Definition, EasedAim, motor.HorizontalSpeed, motor.WalkSpeed, motor.IsGrounded, motor.IsCrouching, weapon.Bloom);

        void TryStartReload()
        {
            WeaponRuntime weapon = Current;
            if (weapon == null || !weapon.CanReload || IsReloading || IsDrawing) return;
            reloadStartTime = Time.time;
            reloadCompleteTime = Time.time + weapon.Definition.reloadTime;
            consecutiveShots = 0;
            CancelInspect();
            Play(ProceduralSfx.ReloadStart, 0.7f);
        }

        void UpdateReload()
        {
            if (!IsReloading || Time.time < reloadCompleteTime) return;
            reloadCompleteTime = -1f;
            Current.FinishReload();
            Play(ProceduralSfx.ReloadEnd, 0.8f);
        }

        void CancelReload() => reloadCompleteTime = -1f;

        void TryInspect(WeaponRuntime weapon)
        {
            if (weapon.Viewmodel == null || IsDrawing || IsReloading || aimBlend > 0.05f) return;
            inspectStartTime = Time.time;
            inspectDuration = Mathf.Max(0.1f, weapon.Viewmodel.InspectDuration);
        }

        void CancelInspect() => inspectStartTime = -1f;

        void UpdateAim(bool aimPressed, float deltaTime)
        {
            WeaponDefinition definition = Current.Definition;
            bool canAim = definition.canAim && !IsReloading && !IsDrawing && !motor.IsSprinting;
            if (!definition.canAim || IsReloading) scopeToggled = false;
            else if (aimPressed && !IsDrawing) scopeToggled = !scopeToggled;
            float target = scopeToggled && canAim ? 1f : 0f;
            if (target > 0f) CancelInspect();
            aimBlend = Mathf.MoveTowards(aimBlend, target, deltaTime / Mathf.Max(0.01f, definition.aimTime));
            ApplyFieldOfView(Mathf.Lerp(baseFieldOfView, definition.aimFieldOfView, EasedAim));
        }

        void ApplyFieldOfView(float fieldOfView)
        {
            if (worldCamera == null) return;
            worldCamera.fieldOfView = fieldOfView;
            // Scale mouse speed with the zoom so the crosshair moves across targets at the same rate.
            look.SensitivityMultiplier = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(baseFieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        void TickViewmodel(WeaponRuntime weapon, Vector2 lookDelta, float deltaTime)
        {
            if (!weapon.Viewmodel) return;
            var state = new ViewmodelState
            {
                LookDelta = lookDelta,
                MoveFactor = motor.HorizontalSpeed / Mathf.Max(0.01f, motor.WalkSpeed),
                Grounded = motor.IsGrounded,
                AimBlend = EasedAim,
                DrawProgress = drawCompleteTime > drawStartTime ? Mathf.InverseLerp(drawStartTime, drawCompleteTime, Time.time) : 1f,
                ReloadProgress = IsReloading ? ReloadProgress : -1f,
                InspectProgress = IsInspecting ? (Time.time - inspectStartTime) / inspectDuration : -1f,
                Hidden = IsScoped,
            };
            weapon.Viewmodel.Tick(state, deltaTime);
        }

        void OnPellet(PelletResult pellet)
        {
            PelletTraced?.Invoke(pellet);
            CombatEffects effects = CombatEffects.Instance;
            if (effects == null) return;
            if (!Current.IsMelee) effects.Tracer(shotMuzzlePosition, pellet.EndPoint, shotTracerColor);
            if (pellet.HitSomething) effects.Impact(pellet.EndPoint, pellet.Normal, pellet.HitCharacter);
        }

        void Play(AudioClip clip, float volume)
        {
            if (audioSource && clip) audioSource.PlayOneShot(clip, volume);
        }
    }
}
