using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spongykill.Core
{
    public enum ControlsMode
    {
        /// <summary>Touch controls on phones and tablets, keyboard and mouse elsewhere.</summary>
        Auto,
        Touch,
        KeyboardMouse,
    }

    public enum GyroMode
    {
        Off,
        On,
        /// <summary>Gyro aiming only while looking through a scope.</summary>
        ScopedOnly,
    }

    public enum GraphicsPreset
    {
        Low,
        Medium,
        High,
        Custom,
    }

    public enum ShadowSetting
    {
        Off,
        Low,
        High,
    }

    /// <summary>Saved position, size and opacity of one on-screen touch control.</summary>
    [Serializable]
    public struct TouchControlLayout
    {
        public string id;
        /// <summary>Centre of the control as a fraction of the safe area (0..1 on both axes).</summary>
        public Vector2 position;
        public float scale;
    }

    /// <summary>
    /// Every player-facing option, saved as JSON in PlayerPrefs. Read <see cref="Current"/>; after
    /// changing a field call <see cref="NotifyChanged"/> so listeners re-apply, and <see cref="Save"/>
    /// when the change should persist.
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        const string PrefsKey = "spongykill.settings.v1";

        public static readonly Color[] CrosshairColors =
        {
            new Color(0.3f, 1f, 0.45f), Color.white, new Color(1f, 0.92f, 0.25f), new Color(0.35f, 0.95f, 1f),
            new Color(1f, 0.35f, 0.35f), new Color(1f, 0.4f, 0.95f),
        };
        public static readonly string[] CrosshairColorNames = { "Green", "White", "Yellow", "Cyan", "Red", "Pink" };

        public static readonly Color[] OutlineColors =
        {
            new Color(1f, 0.15f, 0.12f), new Color(1f, 0.9f, 0.1f), new Color(1f, 0.25f, 0.95f), Color.white,
        };
        public static readonly string[] OutlineColorNames = { "Red", "Yellow", "Magenta", "White" };

        public static readonly int[] FrameRateOptions = { 30, 60, 90, 120, 0 };

        // ---- Controls
        public ControlsMode controls = ControlsMode.Auto;
        [Tooltip("Multiplier on the base mouse speed (0.08 degrees per count).")]
        public float mouseSensitivity = 1f;
        public float touchSensitivity = 1f;
        [Tooltip("Extra multiplier while looking through a scope.")]
        public float scopeSensitivity = 1f;
        public GyroMode gyro = GyroMode.Off;
        public float gyroSensitivity = 1f;
        public bool gyroInvertX;
        public bool gyroInvertY;
        public bool invertY;
        public bool toggleCrouch;

        // ---- Graphics
        public GraphicsPreset graphicsPreset = GraphicsPreset.High;
        public float renderScale = 1f;
        public ShadowSetting shadows = ShadowSetting.High;
        public bool postProcessing = true;
        public bool antiAliasing = true;
        [Tooltip("Impact bubbles, tracers and muzzle flashes.")]
        public bool effects = true;
        [Tooltip("Bobbing jellyfish and other decorative motion.")]
        public bool ambientMotion = true;
        public int targetFrameRate = 60;
        public bool vSync;
        public bool showFps;

        // ---- Crosshair
        public int crosshairColor;
        public bool crosshairDynamic = true;
        public float crosshairLength = 9f;
        public float crosshairThickness = 2f;
        public float crosshairGap = 4f;
        public bool crosshairDot = true;
        public bool crosshairOutline = true;
        public float crosshairOpacity = 1f;

        // ---- HUD
        public bool enemyOutline = true;
        public int enemyOutlineColor;
        public float enemyOutlineWidth = 2.5f;
        public bool minimapRotate = true;
        public float minimapZoom = 1f;
        public float touchButtonOpacity = 0.55f;
        public List<TouchControlLayout> touchLayout = new List<TouchControlLayout>();

        // ---- Audio
        public float masterVolume = 1f;

        static GameSettings current;

        /// <summary>Raised after any setting changes (from the menu or code).</summary>
        public static event Action Changed;

        public static GameSettings Current
        {
            get
            {
                if (current == null) current = Load();
                return current;
            }
        }

        public Color CrosshairColor => CrosshairColors[Mathf.Clamp(crosshairColor, 0, CrosshairColors.Length - 1)];
        public Color EnemyOutlineColor => OutlineColors[Mathf.Clamp(enemyOutlineColor, 0, OutlineColors.Length - 1)];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            Changed = null;
        }

        public static GameSettings Load()
        {
            var settings = new GameSettings();
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(json, settings);
                }
                catch (ArgumentException exception)
                {
                    Debug.LogWarning($"[Spongykill] Ignoring unreadable settings: {exception.Message}");
                }
            }
            else if (Application.isMobilePlatform)
            {
                settings.ApplyPreset(GraphicsPreset.Medium);
            }
            settings.Clamp();
            return settings;
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>Restores every option to its default (the touch layout included) and notifies listeners.</summary>
        public static void ResetToDefaults()
        {
            current = new GameSettings();
            if (Application.isMobilePlatform) current.ApplyPreset(GraphicsPreset.Medium);
            current.Save();
            NotifyChanged();
        }

        public void ApplyPreset(GraphicsPreset preset)
        {
            graphicsPreset = preset;
            switch (preset)
            {
                case GraphicsPreset.Low:
                    renderScale = 0.7f;
                    shadows = ShadowSetting.Off;
                    postProcessing = false;
                    antiAliasing = false;
                    effects = false;
                    ambientMotion = false;
                    break;
                case GraphicsPreset.Medium:
                    renderScale = 0.85f;
                    shadows = ShadowSetting.Low;
                    postProcessing = true;
                    antiAliasing = false;
                    effects = true;
                    ambientMotion = true;
                    break;
                case GraphicsPreset.High:
                    renderScale = 1f;
                    shadows = ShadowSetting.High;
                    postProcessing = true;
                    antiAliasing = true;
                    effects = true;
                    ambientMotion = true;
                    break;
            }
        }

        public bool TryGetTouchLayout(string id, out TouchControlLayout layout)
        {
            foreach (TouchControlLayout entry in touchLayout)
            {
                if (entry.id != id) continue;
                layout = entry;
                return true;
            }
            layout = default;
            return false;
        }

        public void SetTouchLayout(TouchControlLayout layout)
        {
            for (int i = 0; i < touchLayout.Count; i++)
            {
                if (touchLayout[i].id != layout.id) continue;
                touchLayout[i] = layout;
                return;
            }
            touchLayout.Add(layout);
        }

        void Clamp()
        {
            mouseSensitivity = Mathf.Clamp(mouseSensitivity, 0.05f, 10f);
            touchSensitivity = Mathf.Clamp(touchSensitivity, 0.05f, 10f);
            scopeSensitivity = Mathf.Clamp(scopeSensitivity, 0.1f, 3f);
            gyroSensitivity = Mathf.Clamp(gyroSensitivity, 0.1f, 5f);
            renderScale = Mathf.Clamp(renderScale, 0.5f, 1f);
            crosshairLength = Mathf.Clamp(crosshairLength, 2f, 30f);
            crosshairThickness = Mathf.Clamp(crosshairThickness, 1f, 8f);
            crosshairGap = Mathf.Clamp(crosshairGap, 0f, 30f);
            crosshairOpacity = Mathf.Clamp(crosshairOpacity, 0.1f, 1f);
            enemyOutlineWidth = Mathf.Clamp(enemyOutlineWidth, 1f, 6f);
            minimapZoom = Mathf.Clamp(minimapZoom, 0.5f, 2f);
            touchButtonOpacity = Mathf.Clamp(touchButtonOpacity, 0.15f, 1f);
            masterVolume = Mathf.Clamp01(masterVolume);
            if (touchLayout == null) touchLayout = new List<TouchControlLayout>();
        }
    }
}
