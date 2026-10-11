using System.IO;
using Spongykill.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spongykill.EditorTools
{
    /// <summary>One-time project configuration shared by the content builders.</summary>
    public static class ProjectSetup
    {
        const string TextMeshProFolder = "Assets/TextMesh Pro";

        static readonly (int index, string name)[] RequiredLayers =
        {
            (8, GameLayers.Player),
            (9, GameLayers.Bot),
            (10, GameLayers.Hitbox),
            (11, GameLayers.Viewmodel),
        };

        /// <summary>Layers, physics collisions and TextMeshPro resources. Returns false if something is missing.</summary>
        [MenuItem("Spongykill/Setup/Configure Project", priority = 100)]
        public static bool ConfigureProject()
        {
            EnsureLayers();
            EnsureLayerCollisions();
            EnsureNavMeshAgent();
            bool hasTextMeshPro = EnsureTextMeshProEssentials();
            AssetDatabase.SaveAssets();
            return hasTextMeshPro;
        }

        /// <summary>Regenerates every built asset and scene: weapons, characters, the player, the map and the menu.</summary>
        [MenuItem("Spongykill/Rebuild Everything", priority = 2)]
        public static void RebuildEverything()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ConfigureProject();
            WeaponContentBuilder.BuildWeapons(resetStats: false);
            CharacterBuilder.BuildAll();
            PlayerPrefabBuilder.Build();
            CoralPlazaBuilder.Build();
            NetworkBuilder.BuildMenu();
        }

        public static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            foreach ((int index, string name) in RequiredLayers)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(index);
                if (slot.stringValue == name) continue;
                if (!string.IsNullOrEmpty(slot.stringValue))
                {
                    Debug.LogWarning($"[Spongykill] Layer {index} is already named '{slot.stringValue}'; expected '{name}'. Leaving it unchanged.");
                    continue;
                }
                slot.stringValue = name;
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Hitboxes and first-person viewmodels are queried by raycasts only; they must never push or
        /// block anything physically (including the character that owns them).
        /// </summary>
        public static void EnsureLayerCollisions()
        {
            int hitbox = LayerMask.NameToLayer(GameLayers.Hitbox);
            int viewmodel = LayerMask.NameToLayer(GameLayers.Viewmodel);
            for (int other = 0; other < 32; other++)
            {
                if (hitbox >= 0) Physics.IgnoreLayerCollision(hitbox, other, true);
                if (viewmodel >= 0) Physics.IgnoreLayerCollision(viewmodel, other, true);
            }
        }

        /// <summary>
        /// Sizes the default NavMesh agent like the player's CharacterController (step 0.35 m), so bots
        /// can only path where a player could walk: no climbing onto 0.8 m patios or the sides of stair ramps.
        /// </summary>
        public static void EnsureNavMeshAgent()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset")[0]);
            SerializedProperty humanoid = settings.FindProperty("m_Settings").GetArrayElementAtIndex(0);
            humanoid.FindPropertyRelative("agentRadius").floatValue = 0.4f;
            humanoid.FindPropertyRelative("agentHeight").floatValue = 1.8f;
            humanoid.FindPropertyRelative("agentClimb").floatValue = 0.35f;
            humanoid.FindPropertyRelative("agentSlope").floatValue = 45f;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Imports the TMP Essential Resources (default font and shaders) without a dialog.</summary>
        public static bool EnsureTextMeshProEssentials()
        {
            if (AssetDatabase.IsValidFolder(TextMeshProFolder)) return true;

            var uguiPackage = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            string package = uguiPackage == null ? null : Path.Combine(uguiPackage.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            if (package == null || !File.Exists(package))
            {
                Debug.LogError("[Spongykill] Could not find TMP Essential Resources. Import them via Window > TextMeshPro > Import TMP Essential Resources.");
                return false;
            }

            UnityEditor.AssetPackage.Package.Import(package, false);
            AssetDatabase.Refresh();
            return AssetDatabase.IsValidFolder(TextMeshProFolder);
        }

        /// <summary>Creates an Assets/... folder and any missing parents.</summary>
        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static void SetLayerRecursively(GameObject root, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) return;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }
    }
}
