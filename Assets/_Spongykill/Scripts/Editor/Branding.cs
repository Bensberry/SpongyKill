using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// The game's name, logo and icons for every build: app icon (Windows and Android, including
    /// Android's adaptive and round icons), the logo on the startup splash, and the menu logo sprite.
    /// The builds apply this automatically.
    /// </summary>
    public static class Branding
    {
        const string IconPath = BuildPaths.Root + "/Art/Branding/AppIcon.png";
        const string LogoPath = BuildPaths.Resources + "/Logo.png";
        const string AdaptiveBackgroundPath = BuildPaths.Root + "/Art/Branding/AppIconBackground.png";
        const string AdaptiveForegroundPath = BuildPaths.Root + "/Art/Branding/AppIconForeground.png";

        [MenuItem("Spongykill/Build/Apply Branding (Icons, Splash)", priority = 99)]
        public static void Apply()
        {
            PlayerSettings.productName = "Spongykill Ops";
            PlayerSettings.companyName = "Spongykill";

            var icon = Import<Texture2D>(IconPath, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 1024;
            });
            var logo = Import<Sprite>(LogoPath, importer =>
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 1024;
            });
            System.Action<TextureImporter> iconSettings = importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
            };
            var background = Import<Texture2D>(AdaptiveBackgroundPath, iconSettings);
            // Shrunk into the middle so circle and rounded launcher masks don't cut the logo off.
            var foreground = Import<Texture2D>(AdaptiveForegroundPath, iconSettings);
            if (icon == null || logo == null || background == null || foreground == null)
            {
                Debug.LogError("[Spongykill] Branding images missing (AppIcon.png / Logo.png).");
                return;
            }

            // The default icon covers Windows; Android's adaptive icon (both layers) is filled explicitly.
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            foreach (PlatformIconKind kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Adaptive })
            {
                PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (PlatformIcon slot in slots)
                {
                    slot.SetTexture(background, 0);
                    if (slot.maxLayerCount > 1) slot.SetTexture(foreground, 1);
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
            }

            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.backgroundColor = Color.black;
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2.5f, logo) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Spongykill] Branding applied (icons, splash logo, product name).");
        }

        static T Import<T>(string path, System.Action<TextureImporter> configure) where T : Object
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                configure(importer);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}
