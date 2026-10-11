using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Spongykill.UI
{
    /// <summary>
    /// Builds uGUI widgets from code for the menus (loadout, scoreboard, settings, touch layout), so
    /// they need no prefab wiring. Every canvas uses a 1920x1080 reference scaled by height, like the HUD.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Backdrop = new Color(0.01f, 0.05f, 0.09f, 0.82f);
        public static readonly Color Panel = new Color(0.03f, 0.13f, 0.2f, 0.95f);
        public static readonly Color PanelRaised = new Color(0.08f, 0.23f, 0.32f, 1f);
        public static readonly Color Selected = new Color(0.1f, 0.45f, 0.4f, 1f);
        public static readonly Color Accent = new Color(0.45f, 0.95f, 0.75f);
        public static readonly Color Warm = new Color(1f, 0.74f, 0.3f);
        public static readonly Color Danger = new Color(1f, 0.42f, 0.42f);
        public static readonly Color TextDim = new Color(1f, 1f, 1f, 0.62f);

        static Sprite rounded;
        static Sprite circle;
        static Sprite ring;

        /// <summary>A rounded rectangle, sliced so it stretches to any size.</summary>
        public static Sprite Rounded => rounded ? rounded : rounded = CreateSprite(64, 18f, 0f, sliced: true);
        public static Sprite Circle => circle ? circle : circle = CreateSprite(128, 64f, 0f, sliced: false);
        public static Sprite Ring => ring ? ring : ring = CreateSprite(128, 64f, 7f, sliced: false);

        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform));
            if (parent) canvasObject.transform.SetParent(parent, false);
            canvasObject.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>Menus need an EventSystem; scenes built before the menus existed don't have one.</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var systemObject = new GameObject("EventSystem");
            systemObject.AddComponent<EventSystem>();
            var module = systemObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        // ------------------------------------------------------------------ Layout

        public static RectTransform Rect(Transform parent, string name)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            rectObject.layer = parent ? parent.gameObject.layer : 5;
            return (RectTransform)rectObject.transform;
        }

        public static RectTransform Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        /// <summary>Anchors at one point; the pivot matches so the position reads as an offset from it.</summary>
        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        public static LayoutElement Size(Component component, float preferredHeight, float preferredWidth = -1f, float flexibleWidth = -1f)
        {
            if (!component.TryGetComponent(out LayoutElement element)) element = component.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.minHeight = preferredHeight;
            // Rows of buttons would otherwise soak up a column's spare height.
            element.flexibleHeight = 0f;
            if (preferredWidth >= 0f) element.preferredWidth = preferredWidth;
            if (flexibleWidth >= 0f) element.flexibleWidth = flexibleWidth;
            return element;
        }

        public static HorizontalLayoutGroup Row(Transform parent, string name, float spacing, float height = -1f)
        {
            RectTransform rect = Rect(parent, name);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            if (height > 0f) Size(rect, height);
            return layout;
        }

        public static VerticalLayoutGroup Column(Transform parent, string name, float spacing)
        {
            RectTransform rect = Rect(parent, name);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        /// <summary>A vertically scrolling list; add rows to <paramref name="content"/>.</summary>
        public static ScrollRect ScrollList(Transform parent, string name, out RectTransform content, float spacing = 10f)
        {
            RectTransform root = Rect(parent, name);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            RectTransform viewport = Stretch(Rect(root, "Viewport"));
            viewport.gameObject.AddComponent<RectMask2D>();
            // An invisible graphic so drags anywhere in the list scroll it.
            Image hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(4, 24, 4, 24);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return scroll;
        }

        // ------------------------------------------------------------------ Widgets

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null, bool raycast = false)
        {
            RectTransform rect = Rect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            if (sprite != null && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
            image.raycastTarget = raycast;
            return image;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string text, float size,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left, Color? color = null, FontStyles style = FontStyles.Normal)
        {
            RectTransform rect = Rect(parent, name);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset) label.font = TMP_Settings.defaultFontAsset;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color ?? Color.white;
            label.fontStyle = style;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            // The default font has no ellipsis glyph, so cut long text off cleanly instead.
            label.overflowMode = TextOverflowModes.Truncate;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(Transform parent, string name, string text, Action onClick, Color? color = null, float fontSize = 30f)
        {
            Image background = Image(parent, name, color ?? PanelRaised, Rounded, raycast: true);
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.85f, 0.95f, 1f);
            colors.pressedColor = new Color(0.65f, 0.7f, 0.75f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            if (!string.IsNullOrEmpty(text))
            {
                TextMeshProUGUI label = Label(background.transform, "Label", text, fontSize, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
                Stretch(label.rectTransform, 6f);
            }
            return button;
        }

        public static Slider Slider(Transform parent, string name, float min, float max, float value, Action<float> onChanged, bool wholeNumbers = false)
        {
            RectTransform root = Rect(parent, name);
            var slider = root.gameObject.AddComponent<Slider>();

            Image track = Image(root, "Track", new Color(0f, 0f, 0f, 0.45f), Rounded, raycast: true);
            Anchor(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -7f), new Vector2(0f, 7f));
            RectTransform fillArea = Anchor(Rect(root, "FillArea"), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -7f), new Vector2(0f, 7f));
            Image fill = Image(fillArea, "Fill", Accent, Rounded);
            fill.rectTransform.sizeDelta = Vector2.zero;
            RectTransform handleArea = Anchor(Rect(root, "HandleArea"), Vector2.zero, Vector2.one, new Vector2(18f, 0f), new Vector2(-18f, 0f));
            Image handle = Image(handleArea, "Handle", Color.white, Circle, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(38f, 0f);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.SetValueWithoutNotify(value);
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        public static TMP_InputField InputField(Transform parent, string name, string value, string placeholder, int characterLimit, Action<string> onChanged = null)
        {
            Image back = Image(parent, name, new Color(0f, 0f, 0f, 0.35f), Rounded, raycast: true);
            // Configure while inactive: the field expects its text parts to exist when it enables.
            back.gameObject.SetActive(false);
            RectTransform area = Anchor(Rect(back.transform, "TextArea"), Vector2.zero, Vector2.one, new Vector2(20f, 6f), new Vector2(-20f, -6f));
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI hint = Label(area, "Placeholder", placeholder, 28f, TextAlignmentOptions.Left, TextDim, FontStyles.Italic);
            Stretch(hint.rectTransform);
            TextMeshProUGUI text = Label(area, "Text", "", 28f, TextAlignmentOptions.Left, Color.white);
            Stretch(text.rectTransform);
            text.overflowMode = TextOverflowModes.Overflow;

            var field = back.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.targetGraphic = back;
            field.characterLimit = characterLimit;
            field.pointSize = 28f;
            field.caretColor = Accent;
            field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.text = value;
            if (onChanged != null) field.onEndEdit.AddListener(v => onChanged(v));
            back.gameObject.SetActive(true);
            return field;
        }

        public static Selector Selector(Transform parent, string name, string[] options, int index, Action<int> onChanged)
        {
            RectTransform root = Rect(parent, name);
            var selector = root.gameObject.AddComponent<Selector>();
            Image back = Image(root, "Back", new Color(0f, 0f, 0f, 0.3f), Rounded);
            Stretch(back.rectTransform);
            Button previous = Button(root, "Previous", "<", null, PanelRaised, 32f);
            Anchor((RectTransform)previous.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(64f, 0f));
            Button next = Button(root, "Next", ">", null, PanelRaised, 32f);
            Anchor((RectTransform)next.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-64f, 0f), Vector2.zero);
            TextMeshProUGUI value = Label(root, "Value", "", 28f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            Anchor(value.rectTransform, Vector2.zero, Vector2.one, new Vector2(70f, 0f), new Vector2(-70f, 0f));
            selector.Initialise(previous, next, value, options, index, onChanged);
            return selector;
        }

        // ------------------------------------------------------------------ Sprites

        /// <summary>A white disc, rounded rectangle or ring with a one-pixel soft edge.</summary>
        static Sprite CreateSprite(int size, float radius, float ringWidth, bool sliced)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "UiKitSprite",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance outside a rounded box whose corners have the given radius.
                    float px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                    float alpha = Mathf.Clamp01(0.5f - outside);
                    if (ringWidth > 0f) alpha *= Mathf.Clamp01(outside + ringWidth + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = sliced ? radius + 1f : 0f;
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }

    /// <summary>"&lt; value &gt;" picker for a fixed list of options.</summary>
    public class Selector : MonoBehaviour
    {
        TextMeshProUGUI valueLabel;
        string[] options;
        Action<int> changed;

        public int Index { get; private set; }

        public void Initialise(Button previous, Button next, TextMeshProUGUI value, string[] optionNames, int index, Action<int> onChanged)
        {
            valueLabel = value;
            options = optionNames;
            changed = onChanged;
            previous.onClick.AddListener(() => Step(-1));
            next.onClick.AddListener(() => Step(1));
            SetIndexWithoutNotify(index);
        }

        public void SetIndexWithoutNotify(int index)
        {
            Index = Mathf.Clamp(index, 0, options.Length - 1);
            valueLabel.text = options[Index];
        }

        void Step(int direction)
        {
            SetIndexWithoutNotify((Index + direction + options.Length) % options.Length);
            changed?.Invoke(Index);
        }
    }

    /// <summary>Keeps a centred panel no wider than a maximum, leaving a margin on narrow screens.</summary>
    [ExecuteAlways]
    public class MaxSizeFitter : MonoBehaviour
    {
        public Vector2 maxSize = new Vector2(1500f, 960f);
        public Vector2 margin = new Vector2(60f, 40f);

        RectTransform rect;

        void LateUpdate()
        {
            if (rect == null) rect = (RectTransform)transform;
            var parent = rect.parent as RectTransform;
            if (parent == null) return;
            Vector2 available = parent.rect.size - margin * 2f;
            var size = new Vector2(Mathf.Min(maxSize.x, available.x), Mathf.Min(maxSize.y, available.y));
            if (rect.sizeDelta != size) rect.sizeDelta = size;
        }
    }

    /// <summary>Fits a full-screen rectangle to the device's safe area (notches and rounded corners).</summary>
    public class SafeAreaFitter : MonoBehaviour
    {
        Rect applied;
        Vector2Int screen;

        void LateUpdate()
        {
            Rect safe = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (safe == applied && size == screen) return;
            applied = safe;
            screen = size;
            if (size.x <= 0 || size.y <= 0) return;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / size.x, safe.yMin / size.y);
            rect.anchorMax = new Vector2(safe.xMax / size.x, safe.yMax / size.y);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
