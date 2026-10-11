using UnityEngine;

namespace Spongykill.Player
{
    /// <summary>
    /// First-person look: yaw turns the whole player body, pitch tilts only the camera pivot. Input
    /// arrives in degrees (mouse, touch drag and gyro are converted by the controller).
    /// Angles are stored as floats rather than read back from transforms to avoid drift.
    /// Weapon recoil is kept as a separate offset on top of the mouse-driven angles and settles back
    /// to zero after firing stops, so the player can pull down against it.
    /// </summary>
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] Transform cameraPivot;
        [Tooltip("Degrees of rotation per mouse count at a sensitivity setting of 1.")]
        [SerializeField, Range(0.005f, 0.5f)] float sensitivity = 0.08f;
        [SerializeField] bool invertY;
        [SerializeField, Range(60f, 89.9f)] float pitchLimit = 89f;
        [SerializeField] float maxRecoilPitch = 14f;
        [SerializeField] float maxRecoilYaw = 6f;

        float yaw;
        float pitch;
        // x = pitch offset (negative looks up), y = yaw offset.
        Vector2 recoil;
        float recoilRecoverySpeed = 10f;
        float recoilRecoverAfter;

        public float Sensitivity
        {
            get => sensitivity;
            set => sensitivity = Mathf.Max(0.001f, value);
        }

        public bool InvertY
        {
            get => invertY;
            set => invertY = value;
        }

        /// <summary>Scales sensitivity on top of the user setting, e.g. while zoomed in.</summary>
        public float SensitivityMultiplier { get; set; } = 1f;

        public float Yaw => yaw;
        public float Pitch => pitch;
        public Vector2 RecoilOffset => recoil;
        public Transform CameraPivot => cameraPivot;

        void Awake()
        {
            yaw = transform.eulerAngles.y;
            if (cameraPivot) pitch = Mathf.DeltaAngle(0f, cameraPivot.localEulerAngles.x);
            Apply();
        }

        /// <param name="degrees">x turns right, y looks up; already scaled by the input's own sensitivity.</param>
        public void Tick(Vector2 degrees)
        {
            float zoom = SensitivityMultiplier;
            float verticalSign = invertY ? -1f : 1f;
            yaw = Mathf.Repeat(yaw + degrees.x * zoom, 360f);
            pitch = Mathf.Clamp(pitch - degrees.y * zoom * verticalSign, -pitchLimit, pitchLimit);

            if (Time.time >= recoilRecoverAfter)
                recoil = Vector2.MoveTowards(recoil, Vector2.zero, recoilRecoverySpeed * Time.deltaTime);
            Apply();
        }

        /// <param name="pitchUp">Degrees to kick the view upward.</param>
        /// <param name="yawRight">Degrees to kick the view sideways.</param>
        /// <param name="recoverySpeed">Degrees per second the kick settles back once firing stops.</param>
        /// <param name="recoveryDelay">Seconds after this kick before settling starts.</param>
        public void AddRecoil(float pitchUp, float yawRight, float recoverySpeed, float recoveryDelay)
        {
            recoil.x = Mathf.Max(recoil.x - pitchUp, -maxRecoilPitch);
            recoil.y = Mathf.Clamp(recoil.y + yawRight, -maxRecoilYaw, maxRecoilYaw);
            recoilRecoverySpeed = recoverySpeed;
            recoilRecoverAfter = Time.time + recoveryDelay;
            Apply();
        }

        public void ResetRecoil()
        {
            recoil = Vector2.zero;
            Apply();
        }

        public void SetYawPitch(float newYaw, float newPitch)
        {
            yaw = Mathf.Repeat(newYaw, 360f);
            pitch = Mathf.Clamp(newPitch, -pitchLimit, pitchLimit);
            Apply();
        }

        void Apply()
        {
            transform.rotation = Quaternion.Euler(0f, yaw + recoil.y, 0f);
            if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(Mathf.Clamp(pitch + recoil.x, -89.9f, 89.9f), 0f, 0f);
        }
    }
}
