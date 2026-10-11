using System;
using Spongykill.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Spongykill.UI
{
    /// <summary>
    /// Customise the touch controls: drag any control to move it, tap one to select it and change its
    /// size, set how see-through the controls are, then save. Works from the main menu or mid-match.
    /// </summary>
    public class TouchLayoutEditor : MonoBehaviour
    {
        TouchControls controls;
        TouchControlView selected;
        Slider sizeSlider;
        Slider opacitySlider;
        TextMeshProUGUI selectedLabel;
        CanvasGroup controlsGroup;
        Action closed;

        public static TouchLayoutEditor Open(Transform canvas, Action onClosed)
        {
            Image backdrop = UiKit.Image(canvas, "TouchLayoutEditor", new Color(0.06f, 0.24f, 0.33f, 0.98f), null, raycast: true);
            UiKit.Stretch(backdrop.rectTransform);
            backdrop.transform.SetAsLastSibling();
            var editor = backdrop.gameObject.AddComponent<TouchLayoutEditor>();
            editor.closed = onClosed;
            editor.Build(backdrop.rectTransform);
            MenuState.Push(editor);
            return editor;
        }

        void Build(RectTransform root)
        {
            TextMeshProUGUI hint = UiKit.Label(root, "Hint", "Drag the controls to where your thumbs rest.\nTap one to change its size.", 34f, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.35f), FontStyles.Bold);
            UiKit.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1100f, 140f));
            hint.textWrappingMode = TextWrappingModes.Normal;

            RectTransform area = UiKit.Stretch(UiKit.Rect(root, "Controls"));
            controls = TouchControls.CreateForEditing(area);
            controlsGroup = area.GetComponent<CanvasGroup>();
            foreach (TouchControlView view in controls.Views) view.Selected += Select;

            // Toolbar along the top centre, clear of the default control spots.
            Image bar = UiKit.Image(root, "Toolbar", UiKit.Panel, UiKit.Rounded);
            UiKit.Place(bar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(1060f, 190f));
            selectedLabel = UiKit.Label(bar.transform, "Selected", "Tap a control", 26f, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
            UiKit.Anchor(selectedLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(24f, -60f), new Vector2(0f, -14f));

            TextMeshProUGUI sizeLabel = UiKit.Label(bar.transform, "SizeLabel", "SIZE", 20f, TextAlignmentOptions.Left, UiKit.TextDim, FontStyles.Bold);
            UiKit.Anchor(sizeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -110f), new Vector2(120f, -70f));
            sizeSlider = UiKit.Slider(bar.transform, "Size", 0.5f, 1.8f, 1f, OnSizeChanged);
            UiKit.Anchor((RectTransform)sizeSlider.transform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(120f, -112f), new Vector2(-20f, -68f));
            sizeSlider.interactable = false;

            TextMeshProUGUI opacityLabel = UiKit.Label(bar.transform, "OpacityLabel", "OPACITY", 20f, TextAlignmentOptions.Left, UiKit.TextDim, FontStyles.Bold);
            UiKit.Anchor(opacityLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -170f), new Vector2(120f, -126f));
            opacitySlider = UiKit.Slider(bar.transform, "Opacity", 0.15f, 1f, GameSettings.Current.touchButtonOpacity, OnOpacityChanged);
            UiKit.Anchor((RectTransform)opacitySlider.transform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(120f, -172f), new Vector2(-20f, -124f));
            OnOpacityChanged(opacitySlider.value);

            HorizontalLayoutGroup buttons = UiKit.Row(bar.transform, "Buttons", 12f);
            UiKit.Anchor((RectTransform)buttons.transform, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(10f, 22f), new Vector2(-22f, -22f));
            buttons.childForceExpandWidth = true;
            UiKit.Button(buttons.transform, "Reset", "RESET", ResetLayout, UiKit.PanelRaised, 24f);
            UiKit.Button(buttons.transform, "Cancel", "CANCEL", Close, UiKit.PanelRaised, 24f);
            UiKit.Button(buttons.transform, "Save", "SAVE", Save, UiKit.Selected, 28f);
        }

        void Select(TouchControlView view)
        {
            if (selected) selected.SetSelected(false);
            selected = view;
            selected.SetSelected(true);
            selectedLabel.text = "Selected: " + Readable(view.Id);
            sizeSlider.interactable = true;
            sizeSlider.SetValueWithoutNotify(view.Scale);
        }

        void OnSizeChanged(float value)
        {
            if (selected) selected.SetPlacement(selected.Position, value);
        }

        void OnOpacityChanged(float value)
        {
            // Preview, but never so faint that the controls can't be found while editing.
            if (controlsGroup) controlsGroup.alpha = Mathf.Max(0.45f, value);
        }

        void ResetLayout()
        {
            controls.ResetPlacements();
            opacitySlider.value = new GameSettings().touchButtonOpacity;
            if (selected) sizeSlider.SetValueWithoutNotify(selected.Scale);
        }

        void Save()
        {
            GameSettings settings = GameSettings.Current;
            settings.touchLayout.Clear();
            foreach (TouchControlView view in controls.Views)
                settings.touchLayout.Add(new TouchControlLayout { id = view.Id, position = view.Position, scale = view.Scale });
            settings.touchButtonOpacity = opacitySlider.value;
            settings.Save();
            GameSettings.NotifyChanged();
            Close();
        }

        void Close()
        {
            MenuState.Pop(this);
            closed?.Invoke();
            Destroy(gameObject);
        }

        static string Readable(string id) => id switch
        {
            TouchControls.JoystickId => "Move stick",
            TouchControls.WeaponBarId => "Weapon bar",
            "fire_left" => "Left fire",
            _ => char.ToUpperInvariant(id[0]) + id.Substring(1),
        };
    }
}
