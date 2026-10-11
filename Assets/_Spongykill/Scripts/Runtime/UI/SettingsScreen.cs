using System;
using Spongykill.Controls;
using Spongykill.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Spongykill.UI
{
    /// <summary>
    /// The options screen, shared by the main menu and the in-match menu: controls (sensitivity, gyro,
    /// touch layout), graphics (presets, render scale, shadows, effects, frame rate), crosshair with a
    /// live preview, HUD (enemy outline, minimap, button opacity) and audio. Every change applies
    /// immediately; leaving the screen saves.
    /// </summary>
    public class SettingsScreen : MonoBehaviour
    {
        static readonly string[] Tabs = { "CONTROLS", "GRAPHICS", "CROSSHAIR", "HUD", "AUDIO" };
        static readonly string[] OffOn = { "Off", "On" };

        RectTransform content;
        Button[] tabButtons;
        int tab;
        Action closed;

        /// <summary>Opens the settings over everything on the given canvas.</summary>
        public static SettingsScreen Open(Transform canvas, Action onClosed = null)
        {
            Image backdrop = UiKit.Image(canvas, "Settings", UiKit.Backdrop, null, raycast: true);
            UiKit.Stretch(backdrop.rectTransform);
            backdrop.transform.SetAsLastSibling();
            var screen = backdrop.gameObject.AddComponent<SettingsScreen>();
            screen.closed = onClosed;
            screen.Build(backdrop.rectTransform);
            MenuState.Push(screen);
            return screen;
        }

        void Build(RectTransform root)
        {
            RectTransform panel = UiKit.Image(root, "Panel", UiKit.Panel, UiKit.Rounded).rectTransform;
            UiKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 940f));
            panel.gameObject.AddComponent<MaxSizeFitter>().maxSize = new Vector2(1500f, 940f);

            TextMeshProUGUI title = UiKit.Label(panel, "Title", "SETTINGS", 52f, TextAlignmentOptions.TopLeft, Color.white, FontStyles.Bold);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -96f), new Vector2(-44f, -26f));

            HorizontalLayoutGroup tabs = UiKit.Row(panel, "Tabs", 10f);
            UiKit.Anchor((RectTransform)tabs.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -170f), new Vector2(-44f, -106f));
            tabs.childForceExpandWidth = true;
            tabButtons = new Button[Tabs.Length];
            for (int i = 0; i < Tabs.Length; i++)
            {
                int index = i;
                tabButtons[i] = UiKit.Button(tabs.transform, Tabs[i], Tabs[i], () => ShowTab(index), UiKit.PanelRaised, 24f);
            }

            ScrollRect list = UiKit.ScrollList(panel, "List", out content, 10f);
            UiKit.Anchor((RectTransform)list.transform, Vector2.zero, Vector2.one, new Vector2(44f, 130f), new Vector2(-44f, -186f));

            Button reset = UiKit.Button(panel, "Reset", "RESET TO DEFAULTS", ResetDefaults, UiKit.PanelRaised, 26f);
            UiKit.Place((RectTransform)reset.transform, new Vector2(0f, 0f), new Vector2(44f, 30f), new Vector2(380f, 80f));
            Button back = UiKit.Button(panel, "Back", "BACK", Close, UiKit.Selected, 32f);
            UiKit.Place((RectTransform)back.transform, new Vector2(1f, 0f), new Vector2(-44f, 30f), new Vector2(320f, 80f));

            ShowTab(0);
        }

        public void Close()
        {
            GameSettings.Current.Save();
            MenuState.Pop(this);
            closed?.Invoke();
            Destroy(gameObject);
        }

        void ResetDefaults()
        {
            GameSettings.ResetToDefaults();
            ShowTab(tab);
        }

        void ShowTab(int index)
        {
            tab = index;
            for (int i = 0; i < tabButtons.Length; i++) tabButtons[i].GetComponent<Image>().color = i == index ? UiKit.Selected : UiKit.PanelRaised;
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

            GameSettings s = GameSettings.Current;
            switch (index)
            {
                case 0: BuildControls(s); break;
                case 1: BuildGraphics(s); break;
                case 2: BuildCrosshair(s); break;
                case 3: BuildHud(s); break;
                default: BuildAudio(s); break;
            }
        }

        // ------------------------------------------------------------------ Tabs

        void BuildControls(GameSettings s)
        {
            Heading("Look");
            SliderRow("Mouse sensitivity", 0.1f, 5f, s.mouseSensitivity, v => s.mouseSensitivity = v, v => v.ToString("0.00"));
            SliderRow("Touch sensitivity", 0.1f, 5f, s.touchSensitivity, v => s.touchSensitivity = v, v => v.ToString("0.00"));
            SliderRow("Scope sensitivity", 0.2f, 2f, s.scopeSensitivity, v => s.scopeSensitivity = v, v => $"{v * 100f:0}%");
            ChoiceRow("Invert look up/down", OffOn, s.invertY ? 1 : 0, i => s.invertY = i == 1);
            Heading("Gyroscope (phones and tablets)");
            ChoiceRow("Gyro aiming", new[] { "Off", "On", "Only when scoped" }, (int)s.gyro, i => s.gyro = (GyroMode)i);
            SliderRow("Gyro sensitivity", 0.1f, 4f, s.gyroSensitivity, v => s.gyroSensitivity = v, v => v.ToString("0.00"));
            ChoiceRow("Invert gyro left/right", OffOn, s.gyroInvertX ? 1 : 0, i => s.gyroInvertX = i == 1);
            ChoiceRow("Invert gyro up/down", OffOn, s.gyroInvertY ? 1 : 0, i => s.gyroInvertY = i == 1);
            Heading("Controls");
            ChoiceRow("Control scheme", new[] { "Automatic", "Touch", "Keyboard & mouse" }, (int)s.controls, i => s.controls = (ControlsMode)i);
            ChoiceRow("Crouch (keyboard)", new[] { "Hold", "Toggle" }, s.toggleCrouch ? 1 : 0, i => s.toggleCrouch = i == 1);
            ButtonRow("Touch button layout", "CUSTOMISE", () => TouchLayoutEditor.Open(transform.parent, null));
            Note("Keyboard: WASD move, Shift sprint, Ctrl/C crouch, Space jump, R reload, F inspect, 1-3 or Q weapons, right-click scope (sniper), B loadout, Tab scores, Esc menu.");
        }

        void BuildGraphics(GameSettings s)
        {
            ChoiceRow("Quality preset", new[] { "Low", "Medium", "High", "Custom" }, (int)s.graphicsPreset, i =>
            {
                if (i == (int)GraphicsPreset.Custom) s.graphicsPreset = GraphicsPreset.Custom;
                else s.ApplyPreset((GraphicsPreset)i);
                GameSettings.NotifyChanged();
                ShowTab(1);
            }, notify: false);
            SliderRow("Render scale", 0.5f, 1f, s.renderScale, v => Custom(s, () => s.renderScale = v), v => $"{v * 100f:0}%");
            ChoiceRow("Shadows", new[] { "Off", "Low", "High" }, (int)s.shadows, i => Custom(s, () => s.shadows = (ShadowSetting)i));
            ChoiceRow("Anti-aliasing", OffOn, s.antiAliasing ? 1 : 0, i => Custom(s, () => s.antiAliasing = i == 1));
            ChoiceRow("Post-processing", OffOn, s.postProcessing ? 1 : 0, i => Custom(s, () => s.postProcessing = i == 1));
            ChoiceRow("Effects and particles", OffOn, s.effects ? 1 : 0, i => Custom(s, () => s.effects = i == 1));
            ChoiceRow("Ambient motion (jellyfish...)", OffOn, s.ambientMotion ? 1 : 0, i => Custom(s, () => s.ambientMotion = i == 1));
            Heading("Performance");
            int frameIndex = Mathf.Max(0, Array.IndexOf(GameSettings.FrameRateOptions, s.targetFrameRate));
            ChoiceRow("Frame rate limit", new[] { "30", "60", "90", "120", "Unlimited" }, frameIndex, i => s.targetFrameRate = GameSettings.FrameRateOptions[i]);
            ChoiceRow("V-sync", OffOn, s.vSync ? 1 : 0, i => s.vSync = i == 1);
            ChoiceRow("Show FPS", OffOn, s.showFps ? 1 : 0, i => s.showFps = i == 1);
        }

        static void Custom(GameSettings s, Action change)
        {
            change();
            s.graphicsPreset = GraphicsPreset.Custom;
        }

        void BuildCrosshair(GameSettings s)
        {
            RectTransform previewRow = UiKit.Image(content, "Preview", new Color(0.35f, 0.55f, 0.62f, 1f), UiKit.Rounded).rectTransform;
            UiKit.Size(previewRow, 180f);
            previewRow.gameObject.AddComponent<CrosshairPreview>();

            ChoiceRow("Style", new[] { "Static", "Dynamic (shows spread)" }, s.crosshairDynamic ? 1 : 0, i => s.crosshairDynamic = i == 1);
            ChoiceRow("Colour", GameSettings.CrosshairColorNames, s.crosshairColor, i => s.crosshairColor = i);
            SliderRow("Length", 2f, 30f, s.crosshairLength, v => s.crosshairLength = Mathf.Round(v), v => v.ToString("0"));
            SliderRow("Thickness", 1f, 8f, s.crosshairThickness, v => s.crosshairThickness = Mathf.Round(v), v => v.ToString("0"));
            SliderRow("Gap", 0f, 30f, s.crosshairGap, v => s.crosshairGap = Mathf.Round(v), v => v.ToString("0"));
            ChoiceRow("Centre dot", OffOn, s.crosshairDot ? 1 : 0, i => s.crosshairDot = i == 1);
            ChoiceRow("Outline", OffOn, s.crosshairOutline ? 1 : 0, i => s.crosshairOutline = i == 1);
            SliderRow("Opacity", 0.1f, 1f, s.crosshairOpacity, v => s.crosshairOpacity = v, v => $"{v * 100f:0}%");
        }

        void BuildHud(GameSettings s)
        {
            ChoiceRow("Enemy outline", OffOn, s.enemyOutline ? 1 : 0, i => s.enemyOutline = i == 1);
            ChoiceRow("Outline colour", GameSettings.OutlineColorNames, s.enemyOutlineColor, i => s.enemyOutlineColor = i);
            SliderRow("Outline thickness", 1f, 6f, s.enemyOutlineWidth, v => s.enemyOutlineWidth = v, v => v.ToString("0.0"));
            ChoiceRow("Minimap", new[] { "Fixed (north up)", "Rotates with you" }, s.minimapRotate ? 1 : 0, i => s.minimapRotate = i == 1);
            SliderRow("Minimap zoom", 0.5f, 2f, s.minimapZoom, v => s.minimapZoom = v, v => $"{v:0.0}x");
            SliderRow("Touch button opacity", 0.15f, 1f, s.touchButtonOpacity, v => s.touchButtonOpacity = v, v => $"{v * 100f:0}%");
        }

        void BuildAudio(GameSettings s)
        {
            SliderRow("Master volume", 0f, 1f, s.masterVolume, v => s.masterVolume = v, v => $"{v * 100f:0}%");
        }

        // ------------------------------------------------------------------ Rows

        void Heading(string text)
        {
            TextMeshProUGUI label = UiKit.Label(content, text, text.ToUpperInvariant(), 22f, TextAlignmentOptions.BottomLeft, UiKit.Accent, FontStyles.Bold);
            UiKit.Size(label, 46f);
        }

        void Note(string text)
        {
            TextMeshProUGUI label = UiKit.Label(content, "Note", text, 22f, TextAlignmentOptions.TopLeft, UiKit.TextDim);
            label.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Size(label, 90f);
        }

        RectTransform Row(string label)
        {
            HorizontalLayoutGroup row = UiKit.Row(content, label, 20f, 72f);
            row.padding = new RectOffset(20, 12, 4, 4);
            Image back = row.gameObject.AddComponent<Image>();
            back.sprite = UiKit.Rounded;
            back.type = Image.Type.Sliced;
            back.color = new Color(1f, 1f, 1f, 0.04f);
            TextMeshProUGUI name = UiKit.Label(row.transform, "Label", label, 28f, TextAlignmentOptions.Left, Color.white);
            UiKit.Size(name, 64f, 360f, 1f);
            return (RectTransform)row.transform;
        }

        void SliderRow(string label, float min, float max, float value, Action<float> set, Func<float, string> format)
        {
            RectTransform row = Row(label);
            TextMeshProUGUI shown = null;
            Slider slider = UiKit.Slider(row, "Slider", min, max, value, v =>
            {
                set(v);
                shown.text = format(v);
                GameSettings.NotifyChanged();
            });
            UiKit.Size(slider, 64f, 520f, 0f);
            shown = UiKit.Label(row, "Value", format(value), 26f, TextAlignmentOptions.Right, Color.white, FontStyles.Bold);
            UiKit.Size(shown, 64f, 110f, 0f);
        }

        void ChoiceRow(string label, string[] options, int index, Action<int> set, bool notify = true)
        {
            RectTransform row = Row(label);
            Selector selector = UiKit.Selector(row, "Choice", options, index, i =>
            {
                set(i);
                if (notify) GameSettings.NotifyChanged();
            });
            UiKit.Size(selector, 64f, 640f, 0f);
        }

        void ButtonRow(string label, string text, Action action)
        {
            RectTransform row = Row(label);
            Button button = UiKit.Button(row, "Button", text, action, UiKit.PanelRaised, 26f);
            UiKit.Size(button, 64f, 640f, 0f);
        }
    }

    /// <summary>Draws the crosshair as currently configured, at actual HUD size.</summary>
    public class CrosshairPreview : MonoBehaviour
    {
        RectTransform[] lines;
        Graphic[] graphics;
        Outline[] outlines;
        RectTransform dot;

        void Awake()
        {
            var center = UiKit.Rect(transform, "Center");
            UiKit.Place(center, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            lines = new RectTransform[4];
            graphics = new Graphic[5];
            outlines = new Outline[5];
            Vector2[] pivots = { new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f) };
            for (int i = 0; i < 5; i++)
            {
                Image image = UiKit.Image(center, i < 4 ? $"Line{i}" : "Dot", Color.white);
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                image.rectTransform.pivot = i < 4 ? pivots[i] : new Vector2(0.5f, 0.5f);
                outlines[i] = image.gameObject.AddComponent<Outline>();
                outlines[i].effectColor = new Color(0f, 0f, 0f, 0.6f);
                outlines[i].effectDistance = new Vector2(1f, -1f);
                graphics[i] = image;
                if (i < 4) lines[i] = image.rectTransform;
                else dot = image.rectTransform;
            }
            // Drawn at twice size so the details are easy to judge.
            center.localScale = Vector3.one * 2f;
        }

        void LateUpdate()
        {
            GameSettings s = GameSettings.Current;
            Color color = s.CrosshairColor;
            color.a = s.crosshairOpacity;
            for (int i = 0; i < 5; i++)
            {
                graphics[i].color = color;
                outlines[i].enabled = s.crosshairOutline;
            }
            var vertical = new Vector2(s.crosshairThickness, s.crosshairLength);
            var horizontal = new Vector2(s.crosshairLength, s.crosshairThickness);
            lines[0].sizeDelta = vertical;
            lines[1].sizeDelta = vertical;
            lines[2].sizeDelta = horizontal;
            lines[3].sizeDelta = horizontal;
            lines[0].anchoredPosition = new Vector2(0f, s.crosshairGap);
            lines[1].anchoredPosition = new Vector2(0f, -s.crosshairGap);
            lines[2].anchoredPosition = new Vector2(-s.crosshairGap, 0f);
            lines[3].anchoredPosition = new Vector2(s.crosshairGap, 0f);
            dot.sizeDelta = Vector2.one * (s.crosshairThickness + 1f);
            dot.gameObject.SetActive(s.crosshairDot);
        }
    }
}
