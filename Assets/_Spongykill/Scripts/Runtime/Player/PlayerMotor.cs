using System;
using UnityEngine;

namespace Spongykill.Player
{
    /// <summary>
    /// Kinematic first-person movement on top of CharacterController: acceleration-based walking and
    /// sprinting, crouching with a ceiling check, buffered jumps with coyote time, sliding off slopes
    /// steeper than the controller's slope limit, and ground snapping so stairs and ramps don't launch
    /// the player into the air.
    /// The motor is driven through Set*/RequestJump and an explicit Tick so it doesn't depend on the
    /// keyboard; the same commands could later come from replays or network input.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speed (m/s)")]
        [SerializeField] float walkSpeed = 4.8f;
        [SerializeField] float sprintSpeed = 6.8f;
        [SerializeField] float crouchSpeed = 2.4f;

        [Header("Acceleration (m/s²)")]
        [SerializeField] float groundAcceleration = 50f;
        [SerializeField] float groundDeceleration = 60f;
        [SerializeField] float airAcceleration = 12f;

        [Header("Jump & Gravity")]
        [SerializeField] float jumpHeight = 1.05f;
        [SerializeField] float gravity = 22f;
        [SerializeField] float maxFallSpeed = 45f;
        [Tooltip("Grace period after walking off a ledge during which a jump still works.")]
        [SerializeField] float coyoteTime = 0.1f;
        [Tooltip("A jump pressed up to this long before landing still fires on touchdown.")]
        [SerializeField] float jumpBufferTime = 0.12f;
        [Tooltip("How far down the motor looks for ground to stick to when stepping off stairs or slopes.")]
        [SerializeField] float groundSnapDistance = 0.45f;

        [Header("Crouch")]
        [SerializeField] float standingHeight = 1.8f;
        [SerializeField] float crouchingHeight = 1.2f;
        [SerializeField] float standingEyeHeight = 1.62f;
        [SerializeField] float crouchingEyeHeight = 1.05f;
        [Tooltip("Higher is snappier.")]
        [SerializeField] float crouchTransitionSpeed = 12f;

        [Header("Collision Queries")]
        [Tooltip("Layers that block standing up and count as ground. The player's own colliders are always ignored.")]
        [SerializeField] LayerMask obstructionMask = Physics.DefaultRaycastLayers;

        [Header("References")]
        [SerializeField] Transform cameraRoot;
        [Tooltip("Optional third-person body mesh, scaled with the crouch height.")]
        [SerializeField] Transform bodyVisual;

        const float GroundStickSpeed = 2f;

        public event Action Jumped;
        /// <summary>Raised on touchdown with the downward speed at impact (m/s).</summary>
        public event Action<float> Landed;
        /// <summary>Raised whenever the capsule height changes while crouching or standing.</summary>
        public event Action<float> HeightChanged;

        CharacterController controller;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        Vector2 moveInput;
        bool sprintRequested;
        bool crouchRequested;
        float lastGroundedTime = float.NegativeInfinity;
        float lastJumpRequestTime = float.NegativeInfinity;
        float currentHeight;
        Vector3 bodyVisualScale = Vector3.one;
        readonly Collider[] overlapBuffer = new Collider[16];
        readonly RaycastHit[] hitBuffer = new RaycastHit[16];

        public bool IsGrounded { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
        public float HorizontalSpeed => horizontalVelocity.magnitude;
        public float WalkSpeed => walkSpeed;
        public float CurrentHeight => currentHeight;
        /// <summary>Scales every movement speed, e.g. for heavy weapons or aiming down sights.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (bodyVisual) bodyVisualScale = bodyVisual.localScale;
            currentHeight = standingHeight;
            ApplyHeight(currentHeight);
        }

        public void SetMoveInput(Vector2 input) => moveInput = Vector2.ClampMagnitude(input, 1f);

        public void SetSprint(bool sprint) => sprintRequested = sprint;

        public void SetCrouch(bool crouch) => crouchRequested = crouch;

        public void RequestJump() => lastJumpRequestTime = Time.time;

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            // CharacterController caches its position; disable it so the transform write sticks.
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            IsGrounded = false;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || !controller.enabled) return;
            float now = Time.time;

            UpdateCrouch(deltaTime);
            if (IsGrounded) lastGroundedTime = now;

            IsSprinting = sprintRequested && !IsCrouching && moveInput.y > 0.1f;
            UpdateHorizontalVelocity(deltaTime);

            bool jumped = false;
            if (now - lastJumpRequestTime <= jumpBufferTime && now - lastGroundedTime <= coyoteTime)
            {
                verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
                lastJumpRequestTime = float.NegativeInfinity;
                lastGroundedTime = float.NegativeInfinity;
                jumped = true;
                Jumped?.Invoke();
            }
            else if (IsGrounded)
            {
                verticalVelocity = -GroundStickSpeed;
            }
            else
            {
                verticalVelocity = Mathf.Max(verticalVelocity - gravity * deltaTime, -maxFallSpeed);
            }

            bool wasGrounded = IsGrounded;
            float impactSpeed = -verticalVelocity;
            CollisionFlags flags = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);

            if ((flags & CollisionFlags.Sides) != 0)
            {
                // Drop the part of the velocity that went into the wall so sliding along it stays smooth.
                Vector3 actual = controller.velocity;
                actual.y = 0f;
                if (actual.sqrMagnitude < horizontalVelocity.sqrMagnitude) horizontalVelocity = actual;
            }
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;

            IsGrounded = (flags & CollisionFlags.Below) != 0 && !IsOnSteepSlope();
            if (!IsGrounded && wasGrounded && !jumped && verticalVelocity <= 0f) IsGrounded = TrySnapToGround();

            if (IsGrounded && !wasGrounded)
            {
                Landed?.Invoke(Mathf.Max(0f, impactSpeed));
                verticalVelocity = -GroundStickSpeed;
            }
        }

        void UpdateHorizontalVelocity(float deltaTime)
        {
            Vector3 wishDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
            float targetSpeed = (IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed) * SpeedMultiplier;
            bool hasInput = wishDirection.sqrMagnitude > 0.0001f;

            if (IsGrounded)
            {
                float rate = hasInput ? groundAcceleration : groundDeceleration;
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, wishDirection * targetSpeed, rate * deltaTime);
            }
            else if (hasInput)
            {
                // Air control steers without bleeding off the momentum carried from the ground.
                float speed = Mathf.Max(targetSpeed, horizontalVelocity.magnitude);
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, wishDirection * speed, airAcceleration * deltaTime);
            }
        }

        void UpdateCrouch(float deltaTime)
        {
            bool blockedOverhead = !crouchRequested && currentHeight < standingHeight - 0.001f && !HasHeadroomToStand();
            IsCrouching = crouchRequested || blockedOverhead;

            float targetHeight = IsCrouching ? crouchingHeight : standingHeight;
            if (Mathf.Approximately(currentHeight, targetHeight)) return;

            currentHeight = Mathf.Lerp(currentHeight, targetHeight, 1f - Mathf.Exp(-crouchTransitionSpeed * deltaTime));
            if (Mathf.Abs(currentHeight - targetHeight) < 0.005f) currentHeight = targetHeight;
            ApplyHeight(currentHeight);
        }

        /// <summary>Centre of the capsule's bottom hemisphere.</summary>
        Vector3 CapsuleBottomCenter => transform.position + Vector3.up * (controller.skinWidth + controller.radius);

        void ApplyHeight(float height)
        {
            // Feet stay planted at the transform pivot; the capsule grows and shrinks from the top.
            // The controller rests skinWidth above whatever it stands on, so lift the capsule by that
            // much to keep the pivot (and eye height) measured from the actual floor.
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f + controller.skinWidth, 0f);

            float standBlend = Mathf.InverseLerp(crouchingHeight, standingHeight, height);
            if (cameraRoot)
                cameraRoot.localPosition = new Vector3(0f, Mathf.Lerp(crouchingEyeHeight, standingEyeHeight, standBlend), 0f);

            if (bodyVisual)
            {
                bodyVisual.localScale = new Vector3(bodyVisualScale.x, bodyVisualScale.y * height / standingHeight, bodyVisualScale.z);
                bodyVisual.localPosition = new Vector3(0f, height * 0.5f, 0f);
            }
            HeightChanged?.Invoke(height);
        }

        bool HasHeadroomToStand()
        {
            float radius = controller.radius * 0.9f;
            Vector3 bottom = CapsuleBottomCenter + Vector3.up * 0.15f;
            Vector3 top = CapsuleBottomCenter + Vector3.up * (standingHeight - 2f * controller.radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlapBuffer, obstructionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!overlapBuffer[i].transform.IsChildOf(transform)) return false;
            return true;
        }

        bool IsOnSteepSlope()
        {
            // Edge contacts report angled normals even on flat ground, so only the surface straight
            // below the capsule decides whether it is too steep to stand on.
            float distance = controller.radius * 3f + controller.skinWidth;
            if (!CastDown(CapsuleBottomCenter, 0f, distance, out RaycastHit hit)) return false;
            return Vector3.Angle(hit.normal, Vector3.up) > controller.slopeLimit + 0.5f;
        }

        bool TrySnapToGround()
        {
            if (!CastDown(CapsuleBottomCenter, controller.radius * 0.95f, groundSnapDistance, out RaycastHit hit)) return false;

            CollisionFlags flags = controller.Move(Vector3.down * (hit.distance + controller.skinWidth));
            return (flags & CollisionFlags.Below) != 0 && !IsOnSteepSlope();
        }

        /// <summary>Closest downward ray (radius 0) or sphere hit that isn't part of this player.</summary>
        bool CastDown(Vector3 origin, float radius, float distance, out RaycastHit closest)
        {
            int count = radius > 0f
                ? Physics.SphereCastNonAlloc(origin, radius, Vector3.down, hitBuffer, distance, obstructionMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, distance, obstructionMask, QueryTriggerInteraction.Ignore);

            closest = default;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                // Zero distance means the shape started inside that collider; there is no usable surface.
                if (hit.distance <= 0f || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    closest = hit;
                }
            }
            return closestDistance < float.PositiveInfinity;
        }
    }
}
