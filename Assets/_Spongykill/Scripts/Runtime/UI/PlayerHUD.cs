using Spongykill.Combat;
using Spongykill.Controls;
using Spongykill.Core;
using Spongykill.Player;
using Spongykill.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Spongykill.UI
{
    /// <summary>
    /// Combat HUD for the local player: dynamic crosshair, hit markers, health, ammunition, weapon
    /// slots, damage direction, scope overlay, pickup prompt and the elimination screen.
    /// Text is only rewritten when the value behind it changes.
    /// </summary>
    public class PlayerHUD : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] Health health;
        [SerializeField] PlayerWeapons weapons;
        [SerializeField] PlayerMotor motor;
        [SerializeField] PlayerInteractor interactor;
        [SerializeField] PlayerController controller;

        [Header("Crosshair")]
        [SerializeField] RectTransform crosshairTop;
        [SerializeField] RectTransform crosshairBottom;
        [SerializeField] RectTransform crosshairLeft;
        [SerializeField] RectTransform crosshairRight;
        [SerializeField] CanvasGroup crosshairLines;
        [SerializeField] CanvasGroup crosshairDot;

        [Header("Hit Marker")]
        [SerializeField] CanvasGroup hitMarker;
        [SerializeField] Graphic[] hitMarkerGraphics;
        [SerializeField] Color hitColor = Color.white;
        [SerializeField] Color killColor = new Color(1f, 0.3f, 0.3f);

        [Header("Health")]
        [SerializeField] TMP_Text healthText;
        [SerializeField] RectTransform healthFill;
        [SerializeField] Graphic healthFillGraphic;
        [SerializeField] Color healthyColor = new Color(0.45f, 0.95f, 0.75f);
        [SerializeField] Color lowHealthColor = new Color(1f, 0.38f, 0.35f);

        [Header("Ammo")]
        [SerializeField] TMP_Text weaponNameText;
        [SerializeField] TMP_Text magazineText;
        [SerializeField] TMP_Text reserveText;
        [SerializeField] GameObject reloadBar;
        [SerializeField] RectTransform reloadFill;
        [SerializeField] TMP_Text[] slotTexts;

        [Header("Damage")]
        [SerializeField] Graphic damageVignette;
        [SerializeField] RectTransform damageIndicatorPivot;
        [SerializeField] CanvasGroup damageIndicator;

        [Header("Overlays")]
        [SerializeField] GameObject scopeOverlay;
        [SerializeField] GameObject interactPrompt;
        [SerializeField] TMP_Text interactText;
        [SerializeField] GameObject deathPanel;
        [SerializeField] TMP_Text deathDetailText;

        RectTransform canvasRect;
        Graphic[] crosshairGraphics;
        Outline[] crosshairOutlines;
        Image dotImage;
        float displayedGap;
        float fireKick;
        float hitMarkerAge = 1f;
        float vignetteAlpha;
        float indicatorAge = 1f;
        Vector3 damageSourcePosition;
        string deathSource = "";

        int shownHealth = -1;
        int shownMagazine = -1;
        int shownReserve = -1;
        int shownSlot = -2;
        int shownRespawnSeconds = -1;
        WeaponPickup shownPickup;
        WeaponDefinition shownWeapon;

        void Awake()
        {
            canvasRect = GetComponentInParent<Canvas>().rootCanvas.GetComponent<RectTransform>();
            dotImage = crosshairDot ? crosshairDot.GetComponentInChildren<Image>(true) : null;
            crosshairGraphics = new Graphic[] { crosshairTop.GetComponent<Graphic>(), crosshairBottom.GetComponent<Graphic>(), crosshairLeft.GetComponent<Graphic>(), crosshairRight.GetComponent<Graphic>(), dotImage };
            crosshairOutlines = new Outline[crosshairGraphics.Length];
            for (int i = 0; i < crosshairGraphics.Length; i++)
                if (crosshairGraphics[i]) crosshairOutlines[i] = crosshairGraphics[i].GetComponent<Outline>();
            if (hitMarker) hitMarker.alpha = 0f;
            if (damageIndicator) damageIndicator.alpha = 0f;
            SetVignette(0f);
            if (deathPanel) deathPanel.SetActive(false);
            if (scopeOverlay) scopeOverlay.SetActive(false);
            if (interactPrompt) interactPrompt.SetActive(false);
        }

        void OnEnable()
        {
            GameSettings.Changed += ApplySettings;
            ApplySettings();
            if (weapons)
            {
                weapons.HitConfirmed += OnHitConfirmed;
                weapons.Fired += OnFired;
                weapons.EquippedChanged += OnEquippedChanged;
            }
            if (health)
            {
                health.Damaged += OnDamaged;
                health.Died += OnDied;
                health.Revived += OnRevived;
            }
        }

        void OnDisable()
        {
            GameSettings.Changed -= ApplySettings;
            if (weapons)
            {
                weapons.HitConfirmed -= OnHitConfirmed;
                weapons.Fired -= OnFired;
                weapons.EquippedChanged -= OnEquippedChanged;
            }
            if (health)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
                health.Revived -= OnRevived;
            }
        }

        void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            UpdateHealth();
            UpdateWeapon();
            UpdateCrosshair(deltaTime);
            UpdateFeedback(deltaTime);
            UpdatePrompt();
            UpdateDeathPanel();
        }

        /// <summary>Crosshair look from the settings, and the touch-friendly layout when using touch controls.</summary>
        void ApplySettings()
        {
            GameSettings settings = GameSettings.Current;
            Color color = settings.CrosshairColor;
            color.a = settings.crosshairOpacity;
            for (int i = 0; i < crosshairGraphics.Length; i++)
            {
                if (crosshairGraphics[i]) crosshairGraphics[i].color = color;
                if (crosshairOutlines[i]) crosshairOutlines[i].enabled = settings.crosshairOutline;
            }
            var vertical = new Vector2(settings.crosshairThickness, settings.crosshairLength);
            var horizontal = new Vector2(settings.crosshairLength, settings.crosshairThickness);
            crosshairTop.sizeDelta = vertical;
            crosshairBottom.sizeDelta = vertical;
            crosshairLeft.sizeDelta = horizontal;
            crosshairRight.sizeDelta = horizontal;
            if (dotImage) dotImage.rectTransform.sizeDelta = Vector2.one * (settings.crosshairThickness + 1f);

            // With touch controls the weapon bar shows weapons and ammo, so the slot list and ammo
            // panel (which would sit under the right thumb) go, and the kill feed moves below the
            // scoreboard and menu buttons.
            bool touch = ControlScheme.UseTouch;
            if (slotTexts != null && slotTexts.Length > 0 && slotTexts[0]) slotTexts[0].transform.parent.gameObject.SetActive(!touch);
            Transform ammoPanel = transform.Find("Ammo");
            if (ammoPanel) ammoPanel.gameObject.SetActive(!touch);
            if (transform.Find("KillFeed") is RectTransform feed) feed.anchoredPosition = new Vector2(feed.anchoredPosition.x, touch ? -150f : -24f);
        }

        void UpdateHealth()
        {
            if (health == null) return;
            int value = Mathf.CeilToInt(health.Current);
            if (value == shownHealth) return;
            shownHealth = value;

            float fraction = health.Current / health.Max;
            healthText.SetText("{0}", value);
            healthFill.localScale = new Vector3(Mathf.Clamp01(fraction), 1f, 1f);
            Color color = fraction <= 0.3f ? lowHealthColor : healthyColor;
            healthFillGraphic.color = color;
            healthText.color = fraction <= 0.3f ? lowHealthColor : Color.white;
        }

        void UpdateWeapon()
        {
            WeaponRuntime weapon = weapons ? weapons.Current : null;
            if (weapon == null) return;

            if (weapon.Definition != shownWeapon)
            {
                shownWeapon = weapon.Definition;
                weaponNameText.SetText(shownWeapon.displayName);
                shownMagazine = shownReserve = -1;
            }

            if (weapon.IsMelee)
            {
                if (shownMagazine != int.MinValue)
                {
                    shownMagazine = int.MinValue;
                    magazineText.SetText("-");
                    reserveText.SetText("");
                }
            }
            else
            {
                if (weapon.Magazine != shownMagazine)
                {
                    shownMagazine = weapon.Magazine;
                    magazineText.SetText("{0}", shownMagazine);
                    bool low = shownMagazine <= Mathf.CeilToInt(weapon.Definition.magazineSize * 0.25f);
                    magazineText.color = low ? lowHealthColor : Color.white;
                }
                if (weapon.Reserve != shownReserve)
                {
                    shownReserve = weapon.Reserve;
                    reserveText.SetText("/ {0}", shownReserve);
                }
            }

            bool reloading = weapons.IsReloading;
            if (reloadBar.activeSelf != reloading) reloadBar.SetActive(reloading);
            if (reloading) reloadFill.localScale = new Vector3(weapons.ReloadProgress, 1f, 1f);

            if (weapons.CurrentSlotIndex != shownSlot) RefreshSlots();
        }

        void OnEquippedChanged() => RefreshSlots();

        void RefreshSlots()
        {
            if (weapons == null) return;
            shownSlot = weapons.CurrentSlotIndex;
            for (int i = 0; i < slotTexts.Length; i++)
            {
                WeaponRuntime slot = i < weapons.SlotCount ? weapons.GetSlot(i) : null;
                slotTexts[i].SetText(slot == null ? "" : $"{slot.Definition.displayName}  {i + 1}");
                slotTexts[i].alpha = i == shownSlot ? 1f : 0.4f;
            }
        }

        void UpdateCrosshair(float deltaTime)
        {
            bool dead = health && health.IsDead;
            bool scoped = weapons && weapons.IsScoped;
            if (scopeOverlay && scopeOverlay.activeSelf != (scoped && !dead)) scopeOverlay.SetActive(scoped && !dead);

            float aim = weapons ? weapons.AimBlend : 0f;
            bool sprinting = motor && motor.IsSprinting;
            GameSettings settings = GameSettings.Current;
            crosshairLines.alpha = dead || scoped ? 0f : (1f - aim) * (sprinting ? 0.3f : 1f);
            crosshairDot.alpha = dead || scoped || !settings.crosshairDot ? 0f : 1f;

            // Convert the weapon's spread cone into on-screen pixels so the lines show where shots can land.
            float spread = weapons ? weapons.CurrentSpread : 0f;
            float fieldOfView = weapons ? weapons.WorldFieldOfView : 60f;
            float halfHeight = canvasRect.rect.height * 0.5f;
            float gap = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) * halfHeight;
            fireKick = Mathf.MoveTowards(fireKick, 0f, 60f * deltaTime);
            float baseGap = settings.crosshairGap;
            // A static crosshair keeps the chosen gap; a dynamic one also shows the current spread.
            float target = settings.crosshairDynamic ? Mathf.Max(baseGap, gap) + fireKick : baseGap;
            displayedGap = Mathf.Lerp(displayedGap, target, 1f - Mathf.Exp(-25f * deltaTime));

            crosshairTop.anchoredPosition = new Vector2(0f, displayedGap);
            crosshairBottom.anchoredPosition = new Vector2(0f, -displayedGap);
            crosshairLeft.anchoredPosition = new Vector2(-displayedGap, 0f);
            crosshairRight.anchoredPosition = new Vector2(displayedGap, 0f);
        }

        void UpdateFeedback(float deltaTime)
        {
            hitMarkerAge += deltaTime;
            hitMarker.alpha = Mathf.Clamp01(1f - hitMarkerAge / 0.3f);

            vignetteAlpha = Mathf.MoveTowards(vignetteAlpha, 0f, deltaTime * 0.9f);
            SetVignette(vignetteAlpha);

            indicatorAge += deltaTime;
            damageIndicator.alpha = Mathf.Clamp01(1.2f - indicatorAge);
            if (damageIndicator.alpha > 0f && health)
            {
                Transform player = health.transform;
                Vector3 toSource = damageSourcePosition - player.position;
                toSource.y = 0f;
                float angle = Vector3.SignedAngle(player.forward, toSource, Vector3.up);
                damageIndicatorPivot.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }
        }

        void UpdatePrompt()
        {
            WeaponPickup pickup = interactor ? interactor.Focused : null;
            if (pickup == shownPickup) return;
            shownPickup = pickup;
            interactPrompt.SetActive(pickup != null);
            if (pickup == null) return;
            string verb = weapons && weapons.Owns(pickup.Weapon) ? "Refill" : "Pick up";
            interactText.SetText($"[E]  {verb} {pickup.Weapon.displayName}");
        }

        void UpdateDeathPanel()
        {
            if (!deathPanel.activeSelf || controller == null) return;
            // In a match the server decides when you come back; offline the controller does.
            float remaining = Match.TdmMatch.Current != null ? Match.TdmMatch.Current.PlayerRespawnRemaining : controller.RespawnTimeRemaining;
            int seconds = remaining < 0f ? -1 : Mathf.CeilToInt(remaining);
            if (seconds == shownRespawnSeconds) return;
            shownRespawnSeconds = seconds;
            deathDetailText.SetText(seconds < 0 ? $"by {deathSource}" : $"by {deathSource}  ·  back in {seconds}");
        }

        void OnFired(WeaponRuntime weapon)
        {
            if (!weapon.IsMelee) fireKick = Mathf.Min(fireKick + 5f, 14f);
        }

        void OnHitConfirmed(HitFeedback hit)
        {
            hitMarkerAge = 0f;
            Color color = hit.Killed ? killColor : hitColor;
            foreach (Graphic line in hitMarkerGraphics) line.color = color;
            hitMarker.transform.localScale = Vector3.one * (hit.Headshot ? 1.4f : 1f);
        }

        void OnDamaged(DamageInfo damage)
        {
            vignetteAlpha = Mathf.Clamp(vignetteAlpha + 0.25f + damage.Amount / 60f, 0f, 0.85f);
            if (damage.Instigator == null) return;
            damageSourcePosition = damage.Instigator.transform.position;
            indicatorAge = 0f;
        }

        void OnDied(DamageInfo damage)
        {
            string source = string.IsNullOrEmpty(damage.Source) ? "unknown" : damage.Source;
            TeamMember killer = damage.Instigator ? damage.Instigator.GetComponent<TeamMember>() : null;
            deathSource = killer != null ? $"{killer.DisplayName} · {source}" : source;
            shownRespawnSeconds = int.MinValue;
            deathPanel.SetActive(true);
        }

        void OnRevived()
        {
            deathPanel.SetActive(false);
            vignetteAlpha = 0f;
            indicatorAge = 1f;
        }

        void SetVignette(float alpha)
        {
            if (damageVignette == null) return;
            Color color = damageVignette.color;
            color.a = alpha;
            damageVignette.color = color;
        }
    }
}
