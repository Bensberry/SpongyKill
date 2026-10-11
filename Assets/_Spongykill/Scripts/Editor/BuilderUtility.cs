using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Spongykill.EditorTools
{
    /// <summary>Asset locations shared by the content builders.</summary>
    public static class BuildPaths
    {
        public const string Root = "Assets/_Spongykill";
        public const string PlaceholderMaterials = Root + "/Art/Materials/Placeholder";
        public const string EffectMaterials = Root + "/Art/Materials/Effects";
        public const string Textures = Root + "/Art/Textures";
        public const string UITextures = Root + "/Art/UI";
        public const string WeaponData = Root + "/Data/Weapons";
        public const string WeaponPrefabs = Root + "/Prefabs/Weapons";
        /// <summary>The online player (owner-driven, with a third-person body for everyone else).</summary>
        public const string NetPlayerPrefab = Root + "/Prefabs/Player/NetPlayer.prefab";
        public const string Resources = Root + "/Resources";
        public const string NetworkData = Root + "/Data/Network";
        public const string Scenes = Root + "/Scenes";
        public const string Settings = Root + "/Settings";
    }

    /// <summary>Placeholder materials in the underwater cartoon palette, shared by every builder.</summary>
    public static class Palette
    {
        public static Material FloorGrid;
        public static Material Coral;
        public static Material Pineapple;
        public static Material Anemone;
        public static Material SeaGlass;
        public static Material Kelp;
        public static Material Rock;
        public static Material Shell;
        public static Material DeepBlue;
        public static Material PlayerBody;
        public static Material Glove;
        public static Material Metal;
        public static Material Wood;
        public static Material Orange;
        public static Material Urchin;
        public static Material Ink;
        public static Material WorldText;
        public static Material Slate;
        public static Material Tile;
        public static Material Glass;
        public static Material LampGlow;
        public static Material Leaf;
        public static Material PineappleSkin;
        public static Material Bun;
        public static Material Patty;
        public static Material[] SkyFlowers;

        const string WorldTextShaderName = "Spongykill/WorldText";

        public static Font LabelFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>Creates any missing palette materials and refreshes the colours of existing ones.</summary>
        public static void Load()
        {
            ProjectSetup.EnsureFolder(BuildPaths.PlaceholderMaterials);
            ProjectSetup.EnsureFolder(BuildPaths.Textures);
            string folder = BuildPaths.PlaceholderMaterials;

            Texture2D grid = GetOrCreateGridTexture();
            // The grid texture covers 4 m, so a 64 m floor tiles it 16 times.
            FloorGrid = BuilderUtility.Lit(folder, "M_SandGrid", new Color(0.93f, 0.86f, 0.66f), 0.1f, grid, 16f);
            Coral = BuilderUtility.Lit(folder, "M_Coral", new Color(0.94f, 0.42f, 0.38f), 0.25f);
            Pineapple = BuilderUtility.Lit(folder, "M_Pineapple", new Color(0.98f, 0.74f, 0.22f), 0.25f);
            Anemone = BuilderUtility.Lit(folder, "M_Anemone", new Color(0.96f, 0.55f, 0.72f), 0.3f);
            SeaGlass = BuilderUtility.Lit(folder, "M_SeaGlass", new Color(0.36f, 0.78f, 0.80f), 0.35f);
            Kelp = BuilderUtility.Lit(folder, "M_Kelp", new Color(0.36f, 0.66f, 0.30f), 0.2f);
            Rock = BuilderUtility.Lit(folder, "M_LavenderRock", new Color(0.58f, 0.54f, 0.74f), 0.1f);
            Shell = BuilderUtility.Lit(folder, "M_Shell", new Color(0.96f, 0.92f, 0.84f), 0.2f);
            DeepBlue = BuilderUtility.Lit(folder, "M_DeepBlue", new Color(0.16f, 0.36f, 0.62f), 0.2f);
            PlayerBody = BuilderUtility.Lit(folder, "M_PlayerBody", new Color(1f, 0.88f, 0.3f), 0.2f);
            Glove = BuilderUtility.Lit(folder, "M_Glove", new Color(0.97f, 0.97f, 0.94f), 0.3f);
            Metal = BuilderUtility.Lit(folder, "M_Metal", new Color(0.58f, 0.63f, 0.68f), 0.6f);
            Wood = BuilderUtility.Lit(folder, "M_Wood", new Color(0.62f, 0.38f, 0.22f), 0.15f);
            Orange = BuilderUtility.Lit(folder, "M_Orange", new Color(0.99f, 0.52f, 0.18f), 0.3f);
            Urchin = BuilderUtility.Lit(folder, "M_Urchin", new Color(0.38f, 0.2f, 0.5f), 0.4f);
            Ink = BuilderUtility.Lit(folder, "M_Ink", new Color(0.05f, 0.05f, 0.08f), 0.7f);
            WorldText = GetOrCreateWorldTextMaterial(folder);
            Slate = BuilderUtility.Lit(folder, "M_Slate", new Color(0.46f, 0.55f, 0.64f), 0.15f);
            Tile = BuilderUtility.Lit(folder, "M_Tile", new Color(0.97f, 0.86f, 0.78f), 0.25f);
            Glass = BuilderUtility.Lit(folder, "M_Glass", new Color(0.62f, 0.9f, 0.98f), 0.9f);
            LampGlow = BuilderUtility.Lit(folder, "M_LampGlow", new Color(1f, 0.93f, 0.55f), 0.5f);
            LampGlow.EnableKeyword("_EMISSION");
            LampGlow.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.4f) * 2.5f);
            Leaf = BuilderUtility.Lit(folder, "M_PineappleLeaf", new Color(0.3f, 0.62f, 0.25f), 0.25f);
            PineappleSkin = BuilderUtility.Lit(folder, "M_PineappleSkin", Color.white, 0.2f, GetOrCreatePineappleTexture(), 2f);
            Bun = BuilderUtility.Lit(folder, "M_Bun", new Color(0.86f, 0.6f, 0.3f), 0.35f);
            Patty = BuilderUtility.Lit(folder, "M_Patty", new Color(0.42f, 0.24f, 0.14f), 0.2f);
            // Unlit so the flowers read as flat pastel shapes from below instead of shaded undersides.
            SkyFlowers = new[]
            {
                BuilderUtility.Unlit(folder, "M_SkyFlowerBlue", new Color(0.72f, 0.9f, 0.97f)),
                BuilderUtility.Unlit(folder, "M_SkyFlowerPink", new Color(0.97f, 0.82f, 0.92f)),
                BuilderUtility.Unlit(folder, "M_SkyFlowerMint", new Color(0.82f, 0.97f, 0.84f)),
            };
        }

        /// <summary>Orange-yellow skin with a dark diamond lattice and a spot in every diamond.</summary>
        static Texture2D GetOrCreatePineappleTexture()
        {
            const int size = 256;
            const float diamonds = 4f;
            return BuilderUtility.GetOrCreateTexture($"{BuildPaths.Textures}/T_PineappleSkin.png", size, (x, y) =>
            {
                float u = x / (float)size;
                float v = y / (float)size;
                float a = Mathf.Repeat((u + v) * diamonds, 1f);
                float b = Mathf.Repeat((u - v) * diamonds, 1f);
                bool line = a < 0.07f || b < 0.07f;
                bool spot = Mathf.Abs(a - 0.5f) < 0.1f && Mathf.Abs(b - 0.5f) < 0.1f;
                if (line) return new Color32(176, 104, 26, 255);
                if (spot) return new Color32(150, 92, 30, 255);
                return new Color32(245, 178, 48, 255);
            }, sprite: false);
        }

        static Material GetOrCreateWorldTextMaterial(string folder)
        {
            Shader shader = Shader.Find(WorldTextShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Spongykill] Shader '{WorldTextShaderName}' not found; labels will draw through walls.");
                return LabelFont.material;
            }

            Material material = BuilderUtility.GetOrCreateMaterial($"{folder}/M_WorldText.mat", shader);
            material.SetTexture("_MainTex", LabelFont.material.mainTexture);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D GetOrCreateGridTexture()
        {
            // One tile = 4 m x 4 m: a thin line every metre and a heavier line on the tile edge.
            const int size = 512;
            const int cellPixels = size / 4;
            return BuilderUtility.GetOrCreateTexture($"{BuildPaths.Textures}/T_ProtoGrid.png", size, (x, y) =>
            {
                bool major = x < 4 || y < 4;
                bool minor = x % cellPixels < 2 || y % cellPixels < 2;
                byte value = major ? (byte)170 : minor ? (byte)205 : (byte)240;
                return new Color32(value, value, value, 255);
            }, sprite: false);
        }
    }

    public static class BuilderUtility
    {
        public static Material GetOrCreateMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }

        /// <summary>URP Lit material; existing assets get their colour refreshed.</summary>
        public static Material Lit(string folder, string name, Color color, float smoothness, Texture texture = null, float tiling = 1f)
        {
            Material material = GetOrCreateMaterial($"{folder}/{name}.mat", Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", Vector2.one * tiling);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Material Unlit(string folder, string name, Color color)
        {
            Material material = GetOrCreateMaterial($"{folder}/{name}.mat", Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Generates a texture into a PNG asset once; later calls return the existing asset.</summary>
        public static Texture2D GetOrCreateTexture(string path, int size, Func<int, int, Color32> pixel, bool sprite)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            ProjectSetup.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = pixel(x, y);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
            }
            else
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Transform Group(Transform parent, string name)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        /// <summary>A solid cube with a collider, for level geometry.</summary>
        public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, Vector3 euler = default)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.SetLocalPositionAndRotation(center, Quaternion.Euler(euler));
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box;
        }

        /// <summary>A purely visual primitive with no collider.</summary>
        public static GameObject Part(Transform parent, string name, PrimitiveType shape, Vector3 position, Vector3 scale, Material material,
            Vector3 euler = default, bool castShadows = true)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
            part.transform.localScale = scale;
            var meshRenderer = part.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            if (!castShadows) meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            return part;
        }

        public static void Label(Transform parent, string text, Vector3 position, float yaw = 0f)
        {
            var label = new GameObject($"Label ({text.Replace('\n', ' ')})");
            label.transform.SetParent(parent, false);
            label.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var textMesh = label.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.font = Palette.LabelFont;
            textMesh.fontSize = 64;
            textMesh.characterSize = 0.065f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.06f, 0.12f, 0.24f);
            label.GetComponent<MeshRenderer>().sharedMaterial = Palette.WorldText;
        }

        /// <summary>Edits serialized (including private) fields and applies them without an undo step.</summary>
        public static void Edit(Object target, Action<SerializedObject> edit)
        {
            var serialized = new SerializedObject(target);
            edit(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>FindProperty that fails loudly when a field was renamed.</summary>
        public static SerializedProperty Require(this SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
                throw new ArgumentException($"{serialized.targetObject.GetType().Name} has no serialized field '{name}'.");
            return property;
        }

        public static void SetObjects(this SerializedProperty property, params Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        public static void SetReference(Object target, string fieldName, Object value) =>
            Edit(target, serialized => serialized.Require(fieldName).objectReferenceValue = value);
    }
}
