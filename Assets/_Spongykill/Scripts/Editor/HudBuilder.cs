using Spongykill.Combat;
using Spongykill.Player;
using Spongykill.UI;
using Spongykill.Weapons;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// Builds the combat HUD canvas (reference resolution 1920x1080, scaled by height) under a parent
    /// and wires it to the player's components.
    /// </summary>
    public static class HudBuilder
    {
        static readonly Color PanelColor = new Color(0.02f, 0.12f, 0.2f, 0.55f);
        static readonly Color BarBackColor = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color Accent = new Color(0.45f, 0.95f, 0.75f);

        static TMP_FontAsset font;

        public static PlayerHUD Build(Transform parent, Health health, PlayerWeapons weapons, PlayerMotor motor,
            PlayerInteractor interactor, PlayerController controller)
        {
            font = LoadFont();
            Sprite scopeSprite = GetOrCreateScopeSprite();
            Sprite vignetteSprite = GetOrCreateVignetteSprite();

            var hudObject = new GameObject("HUD", typeof(RectTransform));
            hudObject.transform.SetParent(parent, false);
            hudObject.layer = LayerMask.NameToLayer("UI");
            var canvas = hudObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = hudObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Matching height keeps the canvas exactly 1080 units tall, which the scope overlay relies on.
            scaler.matchWidthOrHeight = 1f;
            Transform root = hudObject.transform;

            // Drawn back to front.
            Image vignette = Stretch(NewImage("DamageVignette", root, new Color(1f, 0.15f, 0.1f, 0f), vignetteSprite));

            GameObject scopeOverlay = Stretch(NewRect("ScopeOverlay", root)).gameObject;
            Image scope = NewImage("Scope", scopeOverlay.transform, Color.black, scopeSprite);
            Place(scope.rectTransform, Center, Center, Vector2.zero, new Vector2(1080f, 1080f));
            Image leftBar = NewImage("LeftBar", scopeOverlay.transform, Color.black);
            SetAnchors(leftBar.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-540f, 0f));
            Image rightBar = NewImage("RightBar", scopeOverlay.transform, Color.black);
            SetAnchors(rightBar.rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(540f, 0f), Vector2.zero);

            // Crosshair: four lines pushed apart by the weapon's current spread, plus a centre dot.
            RectTransform crosshair = NewRect("Crosshair", root);
            Place(crosshair, Center, Center, Vector2.zero, Vector2.zero);
            RectTransform lines = NewRect("Lines", crosshair);
            Place(lines, Center, Center, Vector2.zero, Vector2.zero);
            CanvasGroup linesGroup = lines.gameObject.AddComponent<CanvasGroup>();
            RectTransform top = CrosshairLine("Top", lines, new Vector2(0.5f, 0f), new Vector2(2f, 9f));
            RectTransform bottom = CrosshairLine("Bottom", lines, new Vector2(0.5f, 1f), new Vector2(2f, 9f));
            RectTransform left = CrosshairLine("Left", lines, new Vector2(1f, 0.5f), new Vector2(9f, 2f));
            RectTransform right = CrosshairLine("Right", lines, new Vector2(0f, 0.5f), new Vector2(9f, 2f));
            RectTransform dot = NewRect("Dot", crosshair);
            Place(dot, Center, Center, Vector2.zero, Vector2.zero);
            CanvasGroup dotGroup = dot.gameObject.AddComponent<CanvasGroup>();
            Image dotImage = NewImage("DotImage", dot, Color.white);
            Place(dotImage.rectTransform, Center, Center, Vector2.zero, new Vector2(3f, 3f));
            AddOutline(dotImage);

            // Hit marker: four short diagonals around the centre.
            RectTransform hitMarker = NewRect("HitMarker", root);
            Place(hitMarker, Center, Center, Vector2.zero, Vector2.zero);
            CanvasGroup hitMarkerGroup = hitMarker.gameObject.AddComponent<CanvasGroup>();
            var hitLines = new Graphic[4];
            Vector2[] corners = { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f) };
            for (int i = 0; i < corners.Length; i++)
            {
                Image line = NewImage($"Line{i}", hitMarker, Color.white);
                Place(line.rectTransform, Center, Center, corners[i] * 9f, new Vector2(2.5f, 10f));
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, corners[i].x * corners[i].y > 0f ? -45f : 45f);
                AddOutline(line);
                hitLines[i] = line;
            }

            // Damage direction: a bar orbiting the crosshair, pointing toward the attacker.
            RectTransform indicator = NewRect("DamageIndicator", root);
            Place(indicator, Center, Center, Vector2.zero, Vector2.zero);
            CanvasGroup indicatorGroup = indicator.gameObject.AddComponent<CanvasGroup>();
            RectTransform indicatorPivot = NewRect("Pivot", indicator);
            Place(indicatorPivot, Center, Center, Vector2.zero, Vector2.zero);
            Image indicatorBar = NewImage("Bar", indicatorPivot, new Color(1f, 0.25f, 0.2f, 0.9f));
            Place(indicatorBar.rectTransform, Center, Center, new Vector2(0f, 170f), new Vector2(110f, 10f));

            // Health, bottom-left.
            RectTransform healthPanel = Panel("Health", root, new Vector2(0f, 0f), new Vector2(36f, 32f), new Vector2(280f, 78f));
            TMP_Text healthIcon = NewText("Icon", healthPanel, "+", 56f, TextAlignmentOptions.Center, Accent, FontStyles.Bold);
            Place(healthIcon.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 18f), new Vector2(44f, 56f));
            TMP_Text healthValue = NewText("Value", healthPanel, "100", 52f, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
            Place(healthValue.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(64f, 18f), new Vector2(200f, 56f));
            (RectTransform healthFill, Image healthFillImage) = Bar("Bar", healthPanel, new Vector2(16f, 10f), new Vector2(248f, 8f), Accent);

            // Ammunition, bottom-right.
            RectTransform ammoPanel = Panel("Ammo", root, new Vector2(1f, 0f), new Vector2(-36f, 32f), new Vector2(300f, 100f));
            TMP_Text weaponName = NewText("WeaponName", ammoPanel, "Weapon", 22f, TextAlignmentOptions.Right, new Color(1f, 1f, 1f, 0.85f), FontStyles.Normal);
            SetAnchors(weaponName.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -36f), new Vector2(-14f, -6f));
            TMP_Text magazine = NewText("Magazine", ammoPanel, "30", 54f, TextAlignmentOptions.BottomRight, Color.white, FontStyles.Bold);
            Place(magazine.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-110f, 12f), new Vector2(160f, 60f));
            TMP_Text reserve = NewText("Reserve", ammoPanel, "/ 90", 28f, TextAlignmentOptions.BottomLeft, new Color(1f, 1f, 1f, 0.7f), FontStyles.Normal);
            Place(reserve.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-12f, 18f), new Vector2(94f, 40f));
            RectTransform reloadBar = NewRect("ReloadBar", ammoPanel);
            SetAnchors(reloadBar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 6f), new Vector2(-14f, 12f));
            Stretch(NewImage("Back", reloadBar, BarBackColor));
            Image reloadFillImage = NewImage("Fill", reloadBar, new Color(1f, 0.85f, 0.35f));
            SetAnchors(reloadFillImage.rectTransform, Vector2.zero, new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            reloadFillImage.rectTransform.pivot = new Vector2(0f, 0.5f);

            // Weapon slots, stacked above the ammo panel.
            RectTransform slots = NewRect("Slots", root);
            Place(slots, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 142f), new Vector2(320f, 90f));
            var slotTexts = new TMP_Text[3];
            for (int i = 0; i < slotTexts.Length; i++)
            {
                TMP_Text slot = NewText($"Slot{i + 1}", slots, "", 22f, TextAlignmentOptions.Right, Color.white, FontStyles.Bold);
                Place(slot.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 60f - i * 28f), new Vector2(320f, 28f));
                slotTexts[i] = slot;
            }

            // Pickup prompt, under the crosshair.
            RectTransform prompt = NewRect("InteractPrompt", root);
            Place(prompt, Center, Center, new Vector2(0f, -110f), new Vector2(520f, 44f));
            Stretch(NewImage("Back", prompt, new Color(0f, 0f, 0f, 0.45f)));
            TMP_Text promptText = Stretch(NewText("Text", prompt, "[E] Pick up", 26f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold));

            // Elimination banner.
            RectTransform deathPanel = NewRect("DeathPanel", root);
            SetAnchors(deathPanel, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -35f), new Vector2(0f, 115f));
            Stretch(NewImage("Back", deathPanel, new Color(0.05f, 0.02f, 0.08f, 0.55f)));
            TMP_Text deathTitle = NewText("Title", deathPanel, "ELIMINATED", 64f, TextAlignmentOptions.Center, new Color(1f, 0.42f, 0.42f), FontStyles.Bold);
            SetAnchors(deathTitle.rectTransform, new Vector2(0f, 0.4f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            TMP_Text deathDetail = NewText("Detail", deathPanel, "by ...", 28f, TextAlignmentOptions.Center, Color.white, FontStyles.Normal);
            SetAnchors(deathDetail.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.4f), Vector2.zero, Vector2.zero);

            // Kill feed, top-right.
            RectTransform feed = NewRect("KillFeed", root);
            Place(feed, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(620f, 170f));
            var feedRows = new TMP_Text[5];
            Material outline = LoadOutlineMaterial();
            for (int i = 0; i < feedRows.Length; i++)
            {
                TMP_Text row = NewText($"Row{i}", feed, "", 22f, TextAlignmentOptions.Right, Color.white, FontStyles.Bold);
                Place(row.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -i * 30f), new Vector2(620f, 28f));
                if (outline != null) row.fontSharedMaterial = outline;
                feedRows[i] = row;
            }
            var killFeed = feed.gameObject.AddComponent<KillFeed>();
            BuilderUtility.Edit(killFeed, so => so.Require("rows").SetObjects(feedRows));

            var hud = hudObject.AddComponent<PlayerHUD>();
            BuilderUtility.Edit(hud, so =>
            {
                so.Require("health").objectReferenceValue = health;
                so.Require("weapons").objectReferenceValue = weapons;
                so.Require("motor").objectReferenceValue = motor;
                so.Require("interactor").objectReferenceValue = interactor;
                so.Require("controller").objectReferenceValue = controller;
                so.Require("crosshairTop").objectReferenceValue = top;
                so.Require("crosshairBottom").objectReferenceValue = bottom;
                so.Require("crosshairLeft").objectReferenceValue = left;
                so.Require("crosshairRight").objectReferenceValue = right;
                so.Require("crosshairLines").objectReferenceValue = linesGroup;
                so.Require("crosshairDot").objectReferenceValue = dotGroup;
                so.Require("hitMarker").objectReferenceValue = hitMarkerGroup;
                so.Require("hitMarkerGraphics").SetObjects(hitLines);
                so.Require("healthText").objectReferenceValue = healthValue;
                so.Require("healthFill").objectReferenceValue = healthFill;
                so.Require("healthFillGraphic").objectReferenceValue = healthFillImage;
                so.Require("weaponNameText").objectReferenceValue = weaponName;
                so.Require("magazineText").objectReferenceValue = magazine;
                so.Require("reserveText").objectReferenceValue = reserve;
                so.Require("reloadBar").objectReferenceValue = reloadBar.gameObject;
                so.Require("reloadFill").objectReferenceValue = reloadFillImage.rectTransform;
                so.Require("slotTexts").SetObjects(slotTexts);
                so.Require("damageVignette").objectReferenceValue = vignette;
                so.Require("damageIndicatorPivot").objectReferenceValue = indicatorPivot;
                so.Require("damageIndicator").objectReferenceValue = indicatorGroup;
                so.Require("scopeOverlay").objectReferenceValue = scopeOverlay;
                so.Require("interactPrompt").objectReferenceValue = prompt.gameObject;
                so.Require("interactText").objectReferenceValue = promptText;
                so.Require("deathPanel").objectReferenceValue = deathPanel.gameObject;
                so.Require("deathDetailText").objectReferenceValue = deathDetail;
            });

            foreach (Transform child in hudObject.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = hudObject.layer;
            return hud;
        }

        // ------------------------------------------------------------------ Element helpers

        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        static RectTransform NewRect(string name, Transform parent)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            return (RectTransform)rectObject.transform;
        }

        static Image NewImage(string name, Transform parent, Color color, Sprite sprite = null)
        {
            RectTransform rect = NewRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text NewText(string name, Transform parent, string text, float size, TextAlignmentOptions alignment, Color color, FontStyles style)
        {
            RectTransform rect = NewRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color;
            label.fontStyle = style;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>Anchors at one point; the pivot matches the anchor so positions read as offsets from that corner.</summary>
        static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        static T Stretch<T>(T graphic) where T : Graphic
        {
            Stretch(graphic.rectTransform);
            return graphic;
        }

        static RectTransform Stretch(RectTransform rect)
        {
            SetAnchors(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return rect;
        }

        static RectTransform Panel(string name, Transform parent, Vector2 corner, Vector2 position, Vector2 size)
        {
            RectTransform panel = NewRect(name, parent);
            Place(panel, corner, corner, position, size);
            Stretch(NewImage("Back", panel, PanelColor));
            return panel;
        }

        static (RectTransform fill, Image fillImage) Bar(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            RectTransform bar = NewRect(name, parent);
            Place(bar, Vector2.zero, Vector2.zero, position, size);
            Stretch(NewImage("Back", bar, BarBackColor));
            Image fill = NewImage("Fill", bar, color);
            SetAnchors(fill.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            // Pivot on the left edge so scaling X shrinks the bar toward the left.
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            return (fill.rectTransform, fill);
        }

        static RectTransform CrosshairLine(string name, Transform parent, Vector2 pivot, Vector2 size)
        {
            Image line = NewImage(name, parent, Color.white);
            Place(line.rectTransform, Center, pivot, Vector2.zero, size);
            AddOutline(line);
            return line.rectTransform;
        }

        static void AddOutline(Graphic graphic)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            outline.effectDistance = new Vector2(1f, -1f);
        }

        // ------------------------------------------------------------------ Assets

        static TMP_FontAsset LoadFont()
        {
            if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset LiberationSans"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) return asset;
            }
            Debug.LogWarning("[Spongykill] No TextMeshPro font found; run Spongykill > Setup > Configure Project first.");
            return null;
        }

        /// <summary>The outlined variant of the default TMP font, so feed text reads over bright scenery.</summary>
        static Material LoadOutlineMaterial()
        {
            foreach (string guid in AssetDatabase.FindAssets("LiberationSans SDF - Outline t:Material"))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null) return material;
            }
            return null;
        }

        static Sprite GetOrCreateScopeSprite()
        {
            const int size = 512;
            const float center = (size - 1) * 0.5f;
            string path = $"{BuildPaths.UITextures}/T_ScopeOverlay.png";
            BuilderUtility.GetOrCreateTexture(path, size, (x, y) =>
            {
                float dx = x - center;
                float dy = y - center;
                float radius = Mathf.Sqrt(dx * dx + dy * dy) / (size * 0.5f);
                float outside = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.955f, 0.975f, radius));
                bool thinLine = Mathf.Abs(dx) < 1.2f || Mathf.Abs(dy) < 1.2f;
                bool thickPost = radius > 0.55f && (Mathf.Abs(dx) < 3.5f || Mathf.Abs(dy) < 3.5f);
                float alpha = Mathf.Max(outside, thinLine || thickPost ? 1f : 0f);
                return new Color32(255, 255, 255, (byte)(alpha * 255f));
            }, sprite: true);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Sprite GetOrCreateVignetteSprite()
        {
            const int size = 256;
            const float center = (size - 1) * 0.5f;
            string path = $"{BuildPaths.UITextures}/T_DamageVignette.png";
            BuilderUtility.GetOrCreateTexture(path, size, (x, y) =>
            {
                float u = (x - center) / center;
                float v = (y - center) / center;
                float edge = Mathf.InverseLerp(0.55f, 1.25f, Mathf.Sqrt(u * u + v * v));
                float alpha = Mathf.Pow(Mathf.SmoothStep(0f, 1f, edge), 1.4f);
                return new Color32(255, 255, 255, (byte)(alpha * 255f));
            }, sprite: true);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
