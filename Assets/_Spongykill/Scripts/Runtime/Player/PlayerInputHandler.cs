using Spongykill.Controls;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Spongykill.Player
{
    /// <summary>
    /// Keyboard and mouse bindings for the local player, defined in code so nothing needs wiring in
    /// the Inspector, merged with the on-screen touch controls (<see cref="TouchInput"/>). Other
    /// components poll the values below each frame. Mouse and touch look stay separate because each
    /// has its own sensitivity. Rebinding can later be persisted through <see cref="Actions"/> with
    /// InputActionMap.SaveBindingOverridesAsJson.
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        const int WeaponSlotCount = 4;

        InputActionMap map;
        InputAction move;
        InputAction look;
        InputAction jump;
        InputAction sprint;
        InputAction crouch;
        InputAction fire;
        InputAction aim;
        InputAction reload;
        InputAction interact;
        InputAction lastWeapon;
        InputAction inspect;
        InputAction weaponScroll;
        InputAction pause;
        InputAction scoreboard;
        InputAction[] weaponSlots;

        public InputActionMap Actions => map;

        public Vector2 Move => Vector2.ClampMagnitude(move.ReadValue<Vector2>() + TouchInput.Move, 1f);
        /// <summary>Mouse movement in counts.</summary>
        public Vector2 LookDelta => look.ReadValue<Vector2>();
        /// <summary>Touch-drag look in screen pixels.</summary>
        public Vector2 TouchLookPixels => TouchInput.LookPixels;
        public bool JumpPressed => jump.WasPressedThisFrame() || TouchInput.Pressed(TouchButton.Jump);
        public bool SprintHeld => sprint.IsPressed() || TouchInput.Sprint;
        public bool CrouchHeld => crouch.IsPressed() || TouchInput.Held(TouchButton.Crouch);
        public bool CrouchPressed => crouch.WasPressedThisFrame() || TouchInput.Pressed(TouchButton.Crouch);
        public bool FirePressed => fire.WasPressedThisFrame() || TouchInput.Pressed(TouchButton.Fire);
        public bool FireHeld => fire.IsPressed() || TouchInput.Held(TouchButton.Fire);
        /// <summary>Mouse fire only: a click on the game view that may need to recapture the cursor.</summary>
        public bool MouseFirePressed => fire.WasPressedThisFrame();
        public bool AimHeld => aim.IsPressed();
        public bool AimPressed => aim.WasPressedThisFrame() || TouchInput.Pressed(TouchButton.Scope);
        public bool ReloadPressed => reload.WasPressedThisFrame() || TouchInput.Pressed(TouchButton.Reload);
        public bool InteractHeld => interact.IsPressed() || TouchInput.Held(TouchButton.Interact);
        public bool InteractPressed => interact.WasPressedThisFrame() || TouchInput.Pressed(TouchButton.Interact);
        public bool LastWeaponPressed => lastWeapon.WasPressedThisFrame();
        public bool NextWeaponPressed => TouchInput.Pressed(TouchButton.SwitchWeapon);
        public bool InspectPressed => inspect.WasPressedThisFrame() || TouchInput.Pressed(TouchButton.Inspect);
        public bool PausePressed => pause.WasPressedThisFrame() || TouchInput.Pressed(TouchButton.Pause);
        public bool ScoreboardHeld => scoreboard.IsPressed() || TouchInput.Held(TouchButton.Scoreboard);
        public float WeaponScroll => weaponScroll.ReadValue<Vector2>().y;

        /// <summary>Zero-based index of the weapon slot key pressed this frame, or -1.</summary>
        public int WeaponSlotPressed
        {
            get
            {
                for (int i = 0; i < weaponSlots.Length; i++)
                    if (weaponSlots[i].WasPressedThisFrame()) return i;
                return TouchInput.SlotPressed;
            }
        }

        void Awake()
        {
            map = new InputActionMap("Player");

            move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            look = map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            jump = map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            sprint = map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            crouch = map.AddAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            crouch.AddBinding("<Keyboard>/c");

            fire = map.AddAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            aim = map.AddAction("Aim", InputActionType.Button, "<Mouse>/rightButton");
            reload = map.AddAction("Reload", InputActionType.Button, "<Keyboard>/r");
            interact = map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            lastWeapon = map.AddAction("LastWeapon", InputActionType.Button, "<Keyboard>/q");
            inspect = map.AddAction("Inspect", InputActionType.Button, "<Keyboard>/f");
            weaponScroll = map.AddAction("WeaponScroll", InputActionType.Value, "<Mouse>/scroll");
            pause = map.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            scoreboard = map.AddAction("Scoreboard", InputActionType.Button, "<Keyboard>/tab");

            weaponSlots = new InputAction[WeaponSlotCount];
            for (int i = 0; i < WeaponSlotCount; i++)
                weaponSlots[i] = map.AddAction($"WeaponSlot{i + 1}", InputActionType.Button, $"<Keyboard>/{i + 1}");
        }

        void OnEnable() => map.Enable();

        void OnDisable() => map.Disable();

        void OnDestroy() => map.Dispose();
    }
}
