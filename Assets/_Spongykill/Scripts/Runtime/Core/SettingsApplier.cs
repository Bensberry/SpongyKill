using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Spongykill.Core
{
    /// <summary>
    /// Applies the graphics and audio options in <see cref="GameSettings"/>: render scale, shadows,
    /// anti-aliasing, post-processing, frame-rate cap, v-sync and master volume, plus an optional FPS
    /// counter. Created automatically at startup and kept across scenes. The pipeline asset is changed
    /// at runtime only; in the Editor its original values are restored when Play Mode ends.
    /// </summary>
    public class SettingsApplier : MonoBehaviour
    {
        static SettingsApplier instance;

        UniversalRenderPipelineAsset pipeline;
        float originalRenderScale;
        float originalShadowDistance;
        int originalShadowResolution;
        bool capturedOriginals;
        float nextCameraPass;
        float fps;
        GUIStyle fpsStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (instance != null || Application.isBatchMode) return;
            var host = new GameObject("SettingsApplier");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<SettingsApplier>();
        }

        void OnEnable()
        {
            GameSettings.Changed += Apply;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply();
        }

        void OnDisable()
        {
            GameSettings.Changed -= Apply;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RestorePipeline();
        }

        void OnApplicationQuit() => RestorePipeline();

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        void Apply()
        {
            GameSettings settings = GameSettings.Current;
            ApplyPipeline(settings);
            QualitySettings.vSyncCount = settings.vSync ? 1 : 0;
            Application.targetFrameRate = settings.vSync ? -1 : settings.targetFrameRate > 0 ? settings.targetFrameRate : -1;
            AudioListener.volume = settings.masterVolume;
            ApplyCameras(settings);
        }

        void ApplyPipeline(GameSettings settings)
        {
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) return;
            if (!capturedOriginals)
            {
                capturedOriginals = true;
                originalRenderScale = pipeline.renderScale;
                originalShadowDistance = pipeline.shadowDistance;
                originalShadowResolution = pipeline.mainLightShadowmapResolution;
            }
            pipeline.renderScale = Mathf.Clamp(settings.renderScale, 0.5f, 1f);
            pipeline.shadowDistance = settings.shadows == ShadowSetting.High ? 70f : 35f;
            pipeline.mainLightShadowmapResolution = settings.shadows == ShadowSetting.High ? 2048 : 1024;
        }

        void RestorePipeline()
        {
#if UNITY_EDITOR
            // The pipeline is a project asset in the Editor; don't let menu choices leak into it.
            if (pipeline == null || !capturedOriginals) return;
            pipeline.renderScale = originalRenderScale;
            pipeline.shadowDistance = originalShadowDistance;
            pipeline.mainLightShadowmapResolution = originalShadowResolution;
#endif
        }

        /// <summary>Per-camera options; cameras appear when players spawn, so this also runs periodically.</summary>
        static void ApplyCameras(GameSettings settings)
        {
            foreach (Camera camera in Camera.allCameras)
            {
                if (!camera.TryGetComponent(out UniversalAdditionalCameraData data)) continue;
                data.renderPostProcessing = settings.postProcessing;
                data.renderShadows = settings.shadows != ShadowSetting.Off;
                if (data.renderType == CameraRenderType.Base)
                    data.antialiasing = settings.antiAliasing ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            }
        }

        void Update()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            if (Time.unscaledTime < nextCameraPass) return;
            nextCameraPass = Time.unscaledTime + 1f;
            ApplyCameras(GameSettings.Current);
        }

        void OnGUI()
        {
            if (!GameSettings.Current.showFps) return;
            fpsStyle ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 50), fontStyle = FontStyle.Bold };
            fpsStyle.normal.textColor = fps >= 50f ? new Color(0.5f, 1f, 0.6f) : fps >= 30f ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.4f);
            GUI.Label(new Rect(Screen.width * 0.5f - 50f, 4f, 100f, 30f), $"{fps:0} FPS", fpsStyle);
        }
    }
}
