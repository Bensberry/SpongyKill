using UnityEngine;

namespace Spongykill.Weapons
{
    /// <summary>Per-frame inputs that drive the procedural viewmodel animation.</summary>
    public struct ViewmodelState
    {
        public Vector2 LookDelta;
        /// <summary>Horizontal speed relative to walking speed (0..1+).</summary>
        public float MoveFactor;
        public bool Grounded;
        /// <summary>Eased aim-down-sights blend, 0 = hip, 1 = aimed.</summary>
        public float AimBlend;
        /// <summary>0 when the weapon starts coming up, 1 once it is ready.</summary>
        public float DrawProgress;
        /// <summary>0..1 while reloading, negative otherwise.</summary>
        public float ReloadProgress;
        /// <summary>0..1 while inspecting, negative otherwise.</summary>
        public float InspectProgress;
        public bool Hidden;
    }

    /// <summary>Extra animation for moving parts of a specific weapon (balisong handles, bolts...).</summary>
    public interface IViewmodelAnimator
    {
        void Animate(in ViewmodelState state, float deltaTime);
    }

    public enum InspectStyle
    {
        Rifle,
        Pistol,
        Knife,
    }

    /// <summary>
    /// First-person weapon model with procedural animation: hip/aim positions, mouse sway, walk bob,
    /// firing kick, draw, reload and inspect motions, plus a muzzle flash. Components implementing
    /// <see cref="IViewmodelAnimator"/> on the same object animate extra parts. The same prefab is
    /// reused as the third-person weapon on bots and pickups, where only <see cref="Flash"/> is used.
    /// </summary>
    public class WeaponViewmodel : MonoBehaviour
    {
        [SerializeField] Transform muzzle;
        [SerializeField] GameObject muzzleFlash;
        [SerializeField] Light muzzleLight;

        [Header("Placement (camera space, metres)")]
        [SerializeField] Vector3 hipPosition = new Vector3(0.2f, -0.2f, 0.45f);
        [Tooltip("Height of the sight above the model origin; aiming centres it on screen.")]
        [SerializeField] float sightHeight = 0.08f;
        [SerializeField] float aimDistance = 0.32f;

        [Header("Motion")]
        [Tooltip("Translation applied per shot or swing.")]
        [SerializeField] Vector3 kickOffset = new Vector3(0f, 0.005f, -0.05f);
        [Tooltip("Rotation applied per shot or swing (degrees; negative X tips the muzzle up).")]
        [SerializeField] Vector3 kickRotation = new Vector3(-5f, 0f, 0f);
        [SerializeField] float kickRecovery = 14f;
        [SerializeField] float swayAmount = 0.0015f;
        [SerializeField] float swayLimit = 0.05f;
        [SerializeField] float bobAmount = 0.013f;
        [Tooltip("How far below the view the weapon starts when drawn (metres).")]
        [SerializeField] float drawDrop = 0.3f;
        [Tooltip("How far the weapon is tipped down when drawing starts (degrees).")]
        [SerializeField] float drawTilt = 40f;

        [Header("Inspect")]
        [SerializeField] InspectStyle inspectStyle = InspectStyle.Rifle;
        [SerializeField] float inspectDuration = 2.6f;

        const float FlashDuration = 0.05f;

        readonly struct InspectKey
        {
            public readonly float Time;
            public readonly Vector3 Position;
            public readonly Vector3 Euler;

            public InspectKey(float time, Vector3 position, Vector3 euler)
            {
                Time = time;
                Position = position;
                Euler = euler;
            }
        }

        // Poses are offsets from the hip pose. Euler angles are interpolated as numbers, so 360+ spins work.
        static readonly InspectKey[] RifleInspect =
        {
            new InspectKey(0f, Vector3.zero, Vector3.zero),
            new InspectKey(0.18f, new Vector3(-0.1f, 0.05f, -0.04f), new Vector3(0f, -38f, 28f)),
            new InspectKey(0.48f, new Vector3(-0.1f, 0.06f, -0.05f), new Vector3(-6f, -44f, 32f)),
            new InspectKey(0.62f, new Vector3(-0.04f, 0.06f, -0.02f), new Vector3(-12f, 22f, -12f)),
            new InspectKey(0.84f, new Vector3(-0.04f, 0.06f, -0.02f), new Vector3(-10f, 26f, -14f)),
            new InspectKey(1f, Vector3.zero, Vector3.zero),
        };

        static readonly InspectKey[] PistolInspect =
        {
            new InspectKey(0f, Vector3.zero, Vector3.zero),
            new InspectKey(0.16f, new Vector3(-0.08f, 0.05f, 0f), new Vector3(0f, -50f, 22f)),
            new InspectKey(0.42f, new Vector3(-0.08f, 0.05f, 0f), new Vector3(-4f, -55f, 26f)),
            new InspectKey(0.5f, new Vector3(-0.06f, 0.09f, 0.02f), new Vector3(-10f, -20f, 26f)),
            new InspectKey(0.72f, new Vector3(-0.06f, 0.09f, 0.02f), new Vector3(-10f, -20f, 386f)),
            new InspectKey(0.86f, new Vector3(-0.03f, 0.04f, 0f), new Vector3(-6f, -8f, 366f)),
            new InspectKey(1f, Vector3.zero, new Vector3(0f, 0f, 360f)),
        };

        static readonly InspectKey[] KnifeInspect =
        {
            new InspectKey(0f, Vector3.zero, Vector3.zero),
            new InspectKey(0.15f, new Vector3(-0.12f, 0.1f, 0.02f), new Vector3(-35f, -25f, 55f)),
            new InspectKey(0.45f, new Vector3(-0.12f, 0.11f, 0.02f), new Vector3(-30f, -30f, 60f)),
            new InspectKey(0.6f, new Vector3(-0.08f, 0.12f, 0.04f), new Vector3(-30f, 150f, -50f)),
            new InspectKey(0.85f, new Vector3(-0.08f, 0.12f, 0.04f), new Vector3(-28f, 155f, -55f)),
            new InspectKey(1f, Vector3.zero, new Vector3(0f, 360f, 0f)),
        };

        Vector3 currentKickOffset;
        Vector3 currentKickRotation;
        Vector3 sway;
        float bobPhase;
        float flashUntil;
        bool hidden;
        Renderer[] renderers;
        IViewmodelAnimator[] partAnimators;

        public Transform Muzzle => muzzle ? muzzle : transform;
        public float InspectDuration => inspectDuration;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            partAnimators = GetComponents<IViewmodelAnimator>();
            SetFlash(false);
        }

        void OnDisable() => SetFlash(false);

        // Runs for third-person copies too, which are never ticked.
        void LateUpdate()
        {
            if (flashUntil > 0f && Time.time >= flashUntil) SetFlash(false);
        }

        public void Kick()
        {
            currentKickOffset += kickOffset;
            currentKickRotation += kickRotation;
        }

        public void Flash()
        {
            if (muzzleFlash == null || !Core.GameSettings.Current.effects) return;
            SetFlash(true);
            muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flashUntil = Time.time + FlashDuration;
        }

        public void Tick(in ViewmodelState state, float deltaTime)
        {
            float recover = 1f - Mathf.Exp(-kickRecovery * deltaTime);
            currentKickOffset = Vector3.Lerp(currentKickOffset, Vector3.zero, recover);
            currentKickRotation = Vector3.Lerp(currentKickRotation, Vector3.zero, recover);

            float steadiness = 1f - 0.75f * state.AimBlend;
            Vector3 swayTarget = Vector3.ClampMagnitude(new Vector3(-state.LookDelta.x, -state.LookDelta.y, 0f) * swayAmount, swayLimit) * steadiness;
            sway = Vector3.Lerp(sway, swayTarget, 1f - Mathf.Exp(-10f * deltaTime));

            float move = Mathf.Clamp01(state.MoveFactor);
            if (state.Grounded) bobPhase += deltaTime * 9f * move;
            float bobWeight = bobAmount * move * (state.Grounded ? 1f : 0.3f) * (1f - 0.85f * state.AimBlend);
            var bob = new Vector3(Mathf.Sin(bobPhase), -Mathf.Abs(Mathf.Cos(bobPhase)) * 1.2f, 0f) * bobWeight;

            Vector3 aimPosition = new Vector3(0f, -sightHeight, aimDistance);
            Vector3 position = Vector3.Lerp(hipPosition, aimPosition, state.AimBlend);
            Quaternion rotation = Quaternion.identity;

            // Drawing: the weapon swings up from below the screen.
            float lowered = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(state.DrawProgress));
            position += new Vector3(0f, -drawDrop, -0.05f) * lowered;
            rotation *= Quaternion.Euler(drawTilt * lowered, 0f, 0f);

            // Reloading: dip and roll the weapon toward the player, then bring it back.
            if (state.ReloadProgress >= 0f)
            {
                float dip = Mathf.Sin(Mathf.Clamp01(state.ReloadProgress) * Mathf.PI);
                position += new Vector3(-0.04f, -0.1f, 0f) * dip;
                rotation *= Quaternion.Euler(12f * dip, -12f * dip, 35f * dip);
            }

            if (state.InspectProgress >= 0f)
            {
                SampleInspect(Mathf.Clamp01(state.InspectProgress), out Vector3 inspectOffset, out Vector3 inspectEuler);
                position += inspectOffset;
                rotation *= Quaternion.Euler(inspectEuler);
            }

            position += currentKickOffset + sway + bob;
            rotation *= Quaternion.Euler(currentKickRotation);
            transform.SetLocalPositionAndRotation(position, rotation);

            foreach (IViewmodelAnimator animator in partAnimators) animator.Animate(state, deltaTime);

            if (state.Hidden != hidden)
            {
                hidden = state.Hidden;
                foreach (Renderer meshRenderer in renderers) meshRenderer.enabled = !hidden;
            }
        }

        void SampleInspect(float progress, out Vector3 offset, out Vector3 euler)
        {
            InspectKey[] keys = inspectStyle == InspectStyle.Pistol ? PistolInspect : inspectStyle == InspectStyle.Knife ? KnifeInspect : RifleInspect;
            for (int i = 1; i < keys.Length; i++)
            {
                if (progress > keys[i].Time) continue;
                InspectKey from = keys[i - 1];
                InspectKey to = keys[i];
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from.Time, to.Time, progress));
                offset = Vector3.Lerp(from.Position, to.Position, t);
                euler = Vector3.Lerp(from.Euler, to.Euler, t);
                return;
            }
            offset = keys[keys.Length - 1].Position;
            euler = keys[keys.Length - 1].Euler;
        }

        void SetFlash(bool on)
        {
            if (!on) flashUntil = 0f;
            if (muzzleFlash) muzzleFlash.SetActive(on && !hidden);
            if (muzzleLight) muzzleLight.enabled = on;
        }
    }
}
