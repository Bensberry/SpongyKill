using System.Collections.Generic;
using Spongykill.Combat;
using Spongykill.Controls;
using Spongykill.Core;
using Spongykill.Player;
using Spongykill.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Spongykill.UI
{
    /// <summary>One on-screen control and where it sits by default (fractions of the safe area).</summary>
    public readonly struct TouchControlSpec
    {
        public readonly string Id;
        public readonly string Label;
        public readonly TouchButton Button;
        public readonly Vector2 Position;
        public readonly float Size;
        /// <summary>Held down (fire, crouch...) rather than tapped.</summary>
        public readonly bool Hold;
        /// <summary>Dragging the button also turns the view (fire on the aiming thumb).</summary>
        public readonly bool Looks;

        public TouchControlSpec(string id, string label, TouchButton button, Vector2 position, float size, bool hold = false, bool looks = false)
        {
            Id = id;
            Label = label;
            Button = button;
            Position = position;
            Size = size;
            Hold = hold;
            Looks = looks;
        }
    }

    /// <summary>
    /// Phone and tablet controls: a floating joystick on the left (push it all the way up to sprint),
    /// drag anywhere else to look, and buttons for firing, jumping, crouching, reloading, scoping,
    /// using pickups, inspecting, the scoreboard and the menu, plus a weapon bar. Positions and sizes
    /// come from the saved layout (see <see cref="TouchLayoutEditor"/>). In edit mode nothing is sent
    /// to the game; dragging moves controls instead.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public const string JoystickId = "move";
        public const string WeaponBarId = "weapons";

        public static readonly TouchControlSpec[] Buttons =
        {
            new TouchControlSpec("fire", "FIRE", TouchButton.Fire, new Vector2(0.85f, 0.32f), 190f, hold: true, looks: true),
            new TouchControlSpec("fire_left", "FIRE", TouchButton.Fire, new Vector2(0.07f, 0.55f), 120f, hold: true),
            new TouchControlSpec("jump", "JUMP", TouchButton.Jump, new Vector2(0.94f, 0.50f), 120f),
            new TouchControlSpec("crouch", "CROUCH", TouchButton.Crouch, new Vector2(0.94f, 0.17f), 115f),
            new TouchControlSpec("reload", "RELOAD", TouchButton.Reload, new Vector2(0.72f, 0.14f), 105f),
            new TouchControlSpec("scope", "SCOPE", TouchButton.Scope, new Vector2(0.71f, 0.44f), 115f),
            new TouchControlSpec("interact", "USE", TouchButton.Interact, new Vector2(0.60f, 0.40f), 100f),
            new TouchControlSpec("inspect", "INSPECT", TouchButton.Inspect, new Vector2(0.80f, 0.70f), 85f),
            new TouchControlSpec("scores", "SCORES", TouchButton.Scoreboard, new Vector2(0.84f, 0.93f), 80f),
            new TouchControlSpec("pause", "MENU", TouchButton.Pause, new Vector2(0.95f, 0.93f), 80f),
        };

        public static readonly Vector2 JoystickPosition = new Vector2(0.15f, 0.28f);
        public const float JoystickSize = 250f;
        public static readonly Vector2 WeaponBarPosition = new Vector2(0.5f, 0.07f);

        RectTransform safeArea;
        readonly List<TouchControlView> views = new List<TouchControlView>();
        readonly Dictionary<string, TouchControlView> byId = new Dictionary<string, TouchControlView>();
        CanvasGroup group;
        Button[] slotButtons;
        TextMeshProUGUI[] slotLabels;
        bool editing;

        public bool Editing => editing;
        public IReadOnlyList<TouchControlView> Views => views;
        public RectTransform SafeArea => safeArea;

        /// <summary>The in-game controls on their own canvas (between the HUD and the menus).</summary>
        public static TouchControls CreateForGame(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas("TouchControls", 20, parent);
            var controls = canvas.gameObject.AddComponent<TouchControls>();
            controls.Build(canvas.transform, editMode: false);
            return controls;
        }

        /// <summary>A copy for the layout editor, inside an existing panel.</summary>
        public static TouchControls CreateForEditing(RectTransform parent)
        {
            var controls = parent.gameObject.AddComponent<TouchControls>();
            controls.Build(parent, editMode: true);
            return controls;
        }

        void Build(Transform root, bool editMode)
        {
            editing = editMode;
            if (!root.TryGetComponent(out group)) group = root.gameObject.AddComponent<CanvasGroup>();
            safeArea = UiKit.Stretch(UiKit.Rect(root, "SafeArea"));
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            if (!editMode)
            {
                // Behind everything: drag here to look around.
                Image lookArea = UiKit.Image(safeArea, "LookArea", Color.clear, null, raycast: true);
                UiKit.Stretch(lookArea.rectTransform);
                lookArea.gameObject.AddComponent<TouchLookArea>();
            }

            var joystick = TouchControlView.CreateJoystick(safeArea, this, JoystickSize);
            Register(JoystickId, joystick);
            foreach (TouchControlSpec spec in Buttons) Register(spec.Id, TouchControlView.CreateButton(safeArea, this, spec));
            Register(WeaponBarId, BuildWeaponBar());
            ApplyLayout();
            GameSettings.Changed += ApplyLayout;
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ApplyLayout;
            if (!editing) TouchInput.ReleaseAll();
        }

        void Register(string id, TouchControlView view)
        {
            view.Id = id;
            views.Add(view);
            byId[id] = view;
        }

        TouchControlView BuildWeaponBar()
        {
            RectTransform bar = UiKit.Rect(safeArea, "WeaponBar");
            bar.sizeDelta = new Vector2(600f, 92f);
            HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            slotButtons = new Button[3];
            slotLabels = new TextMeshProUGUI[3];
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                slotButtons[i] = UiKit.Button(bar, $"Slot{i + 1}", "", () =>
                {
                    if (!editing) TouchInput.TapSlot(slot);
                }, new Color(0.02f, 0.12f, 0.2f, 0.8f), 22f);
                slotLabels[i] = UiKit.Label(slotButtons[i].transform, "Label", "", 22f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
                UiKit.Stretch(slotLabels[i].rectTransform, 6f);
                // Long names shrink rather than being cut off (cutting would also drop the ammo line).
                slotLabels[i].overflowMode = TextOverflowModes.Overflow;
                slotLabels[i].enableAutoSizing = true;
                slotLabels[i].fontSizeMin = 12f;
                slotLabels[i].fontSizeMax = 22f;
            }
            if (editing)
            {
                // While editing, the whole bar is one draggable piece.
                foreach (Button slotButton in slotButtons)
                {
                    slotButton.interactable = false;
                    slotButton.GetComponent<Image>().raycastTarget = false;
                    slotLabels[System.Array.IndexOf(slotButtons, slotButton)].text = $"Weapon {System.Array.IndexOf(slotButtons, slotButton) + 1}";
                }
                bar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            }
            var view = bar.gameObject.AddComponent<TouchControlView>();
            view.InitialiseStatic(this, WeaponBarPosition, 1f);
            return view;
        }

        /// <summary>Puts every control where the saved layout says (or its default spot).</summary>
        public void ApplyLayout()
        {
            GameSettings settings = GameSettings.Current;
            foreach (TouchControlView view in views)
            {
                if (settings.TryGetTouchLayout(view.Id, out TouchControlLayout saved)) view.SetPlacement(saved.position, saved.scale);
                else view.SetPlacement(view.DefaultPosition, 1f);
            }
            group.alpha = editing ? 1f : settings.touchButtonOpacity;
        }

        /// <summary>Back to the factory layout (not saved until the editor saves).</summary>
        public void ResetPlacements()
        {
            foreach (TouchControlView view in views) view.SetPlacement(view.DefaultPosition, 1f);
        }

        public Vector2 ScreenToNormalized(Vector2 screenPosition, Camera eventCamera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safeArea, screenPosition, eventCamera, out Vector2 local);
            Rect rect = safeArea.rect;
            return new Vector2(Mathf.Clamp01((local.x - rect.xMin) / rect.width), Mathf.Clamp01((local.y - rect.yMin) / rect.height));
        }

        void Update()
        {
            if (editing) return;
            bool show = ControlScheme.UseTouch && !MenuState.AnyOpen && TeamMember.LocalPlayer != null;
            if (safeArea.gameObject.activeSelf != show)
            {
                safeArea.gameObject.SetActive(show);
                if (!show) TouchInput.ReleaseAll();
            }
            if (!show) return;

            TeamMember local = TeamMember.LocalPlayer;
            bool alive = local.IsAlive;
            PlayerWeapons weapons = local.GetComponent<PlayerWeapons>();
            PlayerInteractor interactor = local.GetComponent<PlayerInteractor>();
            foreach (TouchControlView view in views)
            {
                bool visible = view.Id switch
                {
                    "scope" => alive && weapons != null && weapons.CurrentCanAim,
                    "interact" => alive && interactor != null && interactor.Focused != null,
                    "scores" or "pause" => true,
                    _ => alive,
                };
                view.SetVisible(visible);
            }
            UpdateWeaponBar(weapons);
        }

        void UpdateWeaponBar(PlayerWeapons weapons)
        {
            if (weapons == null) return;
            for (int i = 0; i < slotButtons.Length; i++)
            {
                WeaponRuntime slot = i < weapons.SlotCount ? weapons.GetSlot(i) : null;
                string text = slot == null ? "" : slot.Definition.displayName;
                // The weapon in hand also shows its ammo (the HUD's ammo panel is hidden on touch screens).
                if (slot != null && i == weapons.CurrentSlotIndex && !slot.IsMelee)
                    text += weapons.IsReloading ? "\n<size=80%>RELOADING</size>" : $"\n<size=90%><b>{slot.Magazine}</b> / {slot.Reserve}</size>";
                if (slotLabels[i].text != text) slotLabels[i].text = text;
                slotButtons[i].GetComponent<Image>().color = i == weapons.CurrentSlotIndex ? new Color(0.1f, 0.45f, 0.4f, 0.9f) : new Color(0.02f, 0.12f, 0.2f, 0.8f);
            }
        }
    }

    /// <summary>A movable on-screen control: a button, the joystick or the weapon bar.</summary>
    public class TouchControlView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        static readonly Color Idle = new Color(0.02f, 0.12f, 0.2f, 0.75f);
        static readonly Color Pressed = new Color(0.25f, 0.75f, 0.6f, 0.9f);

        TouchControls owner;
        TouchControlSpec spec;
        bool isButton;
        bool isJoystick;
        float baseSize = 1f;
        Image background;
        Image ring;
        RectTransform knob;
        int pointer = int.MinValue;
        Vector2 joystickCentre;
        bool selected;

        public string Id { get; set; }
        public Vector2 DefaultPosition { get; private set; }
        public Vector2 Position { get; private set; }
        public float Scale { get; private set; } = 1f;
        public event System.Action<TouchControlView> Selected;

        public static TouchControlView CreateButton(RectTransform parent, TouchControls owner, TouchControlSpec spec)
        {
            Image background = UiKit.Image(parent, spec.Id, Idle, UiKit.Circle, raycast: true);
            Image ring = UiKit.Image(background.transform, "Ring", new Color(1f, 1f, 1f, 0.7f), UiKit.Ring);
            UiKit.Stretch(ring.rectTransform);
            TextMeshProUGUI label = UiKit.Label(background.transform, "Label", spec.Label, spec.Size * 0.2f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            UiKit.Stretch(label.rectTransform, spec.Size * 0.12f);
            // Long words shrink to fit the circle instead of being cut off.
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = spec.Size * 0.2f;
            var view = background.gameObject.AddComponent<TouchControlView>();
            view.owner = owner;
            view.spec = spec;
            view.isButton = true;
            view.background = background;
            view.ring = ring;
            view.baseSize = spec.Size;
            view.DefaultPosition = spec.Position;
            return view;
        }

        public static TouchControlView CreateJoystick(RectTransform parent, TouchControls owner, float size)
        {
            Image background = UiKit.Image(parent, "Joystick", new Color(0.02f, 0.12f, 0.2f, 0.45f), UiKit.Circle, raycast: true);
            Image ring = UiKit.Image(background.transform, "Ring", new Color(1f, 1f, 1f, 0.6f), UiKit.Ring);
            UiKit.Stretch(ring.rectTransform);
            Image knob = UiKit.Image(background.transform, "Knob", new Color(1f, 1f, 1f, 0.8f), UiKit.Circle);
            knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            knob.rectTransform.sizeDelta = Vector2.one * size * 0.42f;
            TextMeshProUGUI hint = UiKit.Label(background.transform, "Hint", "MOVE", 22f, TextAlignmentOptions.Bottom, new Color(1f, 1f, 1f, 0.5f), FontStyles.Bold);
            UiKit.Anchor(hint.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, -40f), new Vector2(0f, -6f));
            var view = background.gameObject.AddComponent<TouchControlView>();
            view.owner = owner;
            view.isJoystick = true;
            view.background = background;
            view.ring = ring;
            view.knob = knob.rectTransform;
            view.baseSize = size;
            view.DefaultPosition = TouchControls.JoystickPosition;
            return view;
        }

        public void InitialiseStatic(TouchControls controls, Vector2 defaultPosition, float size)
        {
            owner = controls;
            DefaultPosition = defaultPosition;
            baseSize = size;
        }

        public void SetPlacement(Vector2 position, float scale)
        {
            Position = new Vector2(Mathf.Clamp01(position.x), Mathf.Clamp01(position.y));
            Scale = Mathf.Clamp(scale, 0.5f, 1.8f);
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = Position;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            if (isButton || isJoystick) rect.sizeDelta = Vector2.one * baseSize * Scale;
            else rect.localScale = Vector3.one * Scale;
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf == visible) return;
            if (!visible) Release();
            gameObject.SetActive(visible);
        }

        public void SetSelected(bool value)
        {
            selected = value;
            if (ring) ring.color = value ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(1f, 1f, 1f, 0.7f);
        }

        void OnDisable() => Release();

        // ------------------------------------------------------------------ Pointer handling

        public void OnPointerDown(PointerEventData eventData)
        {
            if (owner.Editing)
            {
                Selected?.Invoke(this);
                return;
            }
            if (pointer != int.MinValue) return;
            pointer = eventData.pointerId;
            if (isJoystick)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, eventData.position, eventData.pressEventCamera, out joystickCentre);
                joystickCentre = Vector2.ClampMagnitude(joystickCentre, ((RectTransform)transform).rect.width * 0.3f);
                UpdateJoystick(eventData);
                return;
            }
            if (!isButton) return;
            background.color = Pressed;
            if (spec.Hold) TouchInput.SetHeld(spec.Button, true);
            else TouchInput.Tap(spec.Button);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == pointer) Release();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (owner.Editing)
            {
                // Move the control under the finger.
                SetPlacement(owner.ScreenToNormalized(eventData.position, eventData.pressEventCamera), Scale);
                return;
            }
            if (eventData.pointerId != pointer) return;
            if (isJoystick) UpdateJoystick(eventData);
            else if (isButton && spec.Looks) TouchInput.AddLook(eventData.delta);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
        }

        void UpdateJoystick(PointerEventData eventData)
        {
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local);
            float radius = rect.rect.width * 0.5f;
            Vector2 offset = local - joystickCentre;
            Vector2 stick = Vector2.ClampMagnitude(offset / radius, 1f);
            knob.anchoredPosition = joystickCentre + stick * radius;
            TouchInput.Move = stick;
            // Pushing hard forward, past the rim, sprints.
            TouchInput.Sprint = offset.y > radius * 1.05f && Mathf.Abs(stick.x) < 0.5f;
        }

        void Release()
        {
            if (pointer == int.MinValue) return;
            pointer = int.MinValue;
            if (isJoystick)
            {
                if (knob) knob.anchoredPosition = Vector2.zero;
                TouchInput.Move = Vector2.zero;
                TouchInput.Sprint = false;
            }
            else if (isButton)
            {
                background.color = Idle;
                if (spec.Hold) TouchInput.SetHeld(spec.Button, false);
            }
        }
    }

    /// <summary>The empty part of the screen: dragging it turns the view (one finger at a time).</summary>
    public class TouchLookArea : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        int pointer = int.MinValue;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (pointer == int.MinValue) pointer = eventData.pointerId;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == pointer) pointer = int.MinValue;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == pointer) TouchInput.AddLook(eventData.delta);
        }

        void OnDisable() => pointer = int.MinValue;
    }
}
