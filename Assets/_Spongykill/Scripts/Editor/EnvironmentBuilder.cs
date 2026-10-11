using System.Collections.Generic;
using Spongykill.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spongykill.EditorTools
{
    /// <summary>Underwater lighting, fog, post-processing and shared scene services used by every generated scene.</summary>
    public static class EnvironmentBuilder
    {
        const string VolumeProfilePath = BuildPaths.Settings + "/VP_Underwater.asset";

        /// <summary>Sea-water blue shared by the fog and the camera background so distant geometry fades out cleanly.</summary>
        public static Color WaterColor => PlayerPrefabBuilder.WaterColor;

        public static VolumeProfile GetOrCreateVolumeProfile()
        {
            ProjectSetup.EnsureFolder(BuildPaths.Settings);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            // Neutral tonemapping keeps saturated cartoon colours from hue-shifting the way ACES does.
            profile.Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
            Bloom bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0.4f);
            bloom.threshold.Override(1f);
            bloom.scatter.Override(0.6f);
            ColorAdjustments colorAdjustments = profile.Add<ColorAdjustments>();
            colorAdjustments.saturation.Override(15f);
            colorAdjustments.contrast.Override(8f);
            Vignette vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.22f);
            vignette.color.Override(new Color(0.02f, 0.12f, 0.22f));

            // Volume components must live inside the profile asset or they are lost on reload.
            foreach (VolumeComponent component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// Near-neutral ambient for the open scene. A strongly cyan ambient turned yellow and orange
        /// surfaces (sponge skins, pineapple walls) green or olive in shade; the fog carries the water tint.
        /// </summary>
        public static void ApplyAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.80f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.60f, 0.60f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.46f, 0.42f, 0.36f);
        }

        public static void SetupLighting(VolumeProfile profile, float fogDensity = 0.012f)
        {
            var sunObject = new GameObject("Sun");
            sunObject.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.86f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            RenderSettings.sun = sun;

            RenderSettings.skybox = null;
            ApplyAmbient();

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = WaterColor;
            RenderSettings.fogDensity = fogDensity;

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        public static void CreateCombatEffects()
        {
            WeaponContentBuilder.LoadEffectMaterials();
            var effects = new GameObject("Combat Effects").AddComponent<CombatEffects>();
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh sphereMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(sphere);
            BuilderUtility.Edit(effects, so =>
            {
                so.Require("tracerMaterial").objectReferenceValue = WeaponContentBuilder.TracerMaterial;
                so.Require("impactMaterial").objectReferenceValue = WeaponContentBuilder.ImpactMaterial;
                so.Require("impactMesh").objectReferenceValue = sphereMesh;
            });
        }

        /// <summary>Adds a scene to Build Settings (or moves it) at the given index.</summary>
        public static void AddSceneToBuildSettings(string path, int index)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(scene => scene.path == path);
            scenes.Insert(Mathf.Clamp(index, 0, scenes.Count), new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
