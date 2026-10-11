using System;
using System.Linq;
using Spongykill.AI;
using Spongykill.Characters;
using Spongykill.Combat;
using Spongykill.Core;
using Spongykill.Net;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using static Spongykill.EditorTools.BuilderUtility;
using Object = UnityEngine.Object;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// Builds the shared cartoon operator body as the Bot prefab, the SpongeBob-themed creature skins
    /// (sea sponge, octo, starfish, crab) with team outfits, and the Easy / Normal / Hard difficulty
    /// assets. Skin and difficulty assets are only created when missing, so Inspector tweaks survive.
    /// </summary>
    public static class CharacterBuilder
    {
        const string MaterialFolder = BuildPaths.Root + "/Art/Materials/Characters";
        const string SkinFolder = BuildPaths.Root + "/Data/Skins";
        const string DifficultyFolder = BuildPaths.Root + "/Data/Bots";
        public const string BotPrefabPath = BuildPaths.Root + "/Prefabs/Characters/Bot.prefab";

        public static CharacterSkin[] AttackerSkins { get; private set; }
        public static CharacterSkin[] DefenderSkins { get; private set; }
        public static BotDifficulty Easy { get; private set; }
        public static BotDifficulty Normal { get; private set; }
        public static BotDifficulty Hard { get; private set; }

        [MenuItem("Spongykill/Characters/Build Bot, Skins And Difficulties", priority = 50)]
        public static GameObject BuildAll()
        {
            ProjectSetup.EnsureLayers();
            Palette.Load();
            LoadSkins();
            LoadDifficulties();
            GameObject prefab = BuildBotPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Spongykill] Bot prefab, skins and difficulties built ({BotPrefabPath}).");
            return prefab;
        }

        public static bool Exists() => AssetDatabase.LoadAssetAtPath<GameObject>(BotPrefabPath) != null;

        // ------------------------------------------------------------------ Skins

        public static void LoadSkins()
        {
            ProjectSetup.EnsureFolder(MaterialFolder);
            ProjectSetup.EnsureFolder(SkinFolder);

            Material sponge = Lit(MaterialFolder, "M_Skin_SeaSponge", Color.white, 0.15f,
                SpottedTexture("T_Skin_SeaSponge_v2", new Color32(255, 238, 112, 255), new Color32(222, 200, 72, 255), 70, 5f, 13f, 0.75f, 5), 1.5f);
            Material octo = Lit(MaterialFolder, "M_Skin_Octo", Color.white, 0.35f,
                SpottedTexture("T_Skin_Octo", new Color32(172, 142, 222, 255), new Color32(128, 98, 190, 255), 45, 5f, 12f, 1f, 9), 1.5f);
            Material starfish = Lit(MaterialFolder, "M_Skin_Starfish", Color.white, 0.25f,
                SpottedTexture("T_Skin_Starfish", new Color32(246, 138, 150, 255), new Color32(255, 204, 210, 255), 110, 3f, 6f, 1f, 13), 1.5f);
            Material crab = Lit(MaterialFolder, "M_Skin_Crab", Color.white, 0.4f,
                SpottedTexture("T_Skin_Crab", new Color32(232, 84, 60, 255), new Color32(255, 160, 118, 255), 150, 2f, 4f, 1f, 17), 1.5f);

            Material reefGuard = Lit(MaterialFolder, "M_Outfit_ReefGuard", new Color(0.18f, 0.62f, 0.7f), 0.3f);
            Material brineRaiders = Lit(MaterialFolder, "M_Outfit_BrineRaiders", new Color(0.96f, 0.5f, 0.18f), 0.3f);
            Material gear = Lit(MaterialFolder, "M_Gear", new Color(0.22f, 0.17f, 0.14f), 0.3f);

            DefenderSkins = new[]
            {
                Skin("SK_SeaSponge", "Sea Sponge", Team.Defenders, sponge, reefGuard, gear),
                Skin("SK_Octo", "Octo", Team.Defenders, octo, reefGuard, gear),
            };
            AttackerSkins = new[]
            {
                Skin("SK_Starfish", "Starfish", Team.Attackers, starfish, brineRaiders, gear),
                Skin("SK_Crab", "Crab", Team.Attackers, crab, brineRaiders, gear),
            };
        }

        static CharacterSkin Skin(string assetName, string displayName, Team team, Material skin, Material outfit, Material accent)
        {
            string path = $"{SkinFolder}/{assetName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<CharacterSkin>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<CharacterSkin>();
                asset.displayName = displayName;
                asset.team = team;
                asset.skin = skin;
                asset.outfit = outfit;
                asset.accent = accent;
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        /// <summary>A tileable base colour with scattered elliptical spots (sponge pores, starfish bumps...).</summary>
        static Texture2D SpottedTexture(string name, Color32 baseColor, Color32 spotColor, int spots, float minRadius, float maxRadius, float aspect, int seed)
        {
            const int size = 256;
            var random = new System.Random(seed);
            var spotList = new (float x, float y, float rx, float ry)[spots];
            for (int i = 0; i < spots; i++)
            {
                float radius = minRadius + (float)random.NextDouble() * (maxRadius - minRadius);
                spotList[i] = ((float)random.NextDouble() * size, (float)random.NextDouble() * size, radius, radius * aspect);
            }

            return GetOrCreateTexture($"{BuildPaths.Textures}/{name}.png", size, (x, y) =>
            {
                foreach ((float sx, float sy, float rx, float ry) in spotList)
                {
                    // Wrap-around distance keeps the texture tileable.
                    float dx = Mathf.Abs(x - sx);
                    float dy = Mathf.Abs(y - sy);
                    dx = Mathf.Min(dx, size - dx);
                    dy = Mathf.Min(dy, size - dy);
                    float d = dx * dx / (rx * rx) + dy * dy / (ry * ry);
                    if (d > 1f) continue;
                    // Slightly darker or lighter core for depth.
                    return Color32.Lerp(spotColor, baseColor, d * 0.35f);
                }
                return baseColor;
            }, sprite: false);
        }

        /// <summary>Head and body hit volumes under a "Hitboxes" child, sized by <see cref="HitboxRig"/>.</summary>
        public static HitboxRig CreateHitboxRig(Transform owner, Health health)
        {
            Transform hitboxes = Group(owner, "Hitboxes");
            var rig = hitboxes.gameObject.AddComponent<HitboxRig>();

            var headObject = new GameObject("Hitbox_Head");
            headObject.transform.SetParent(hitboxes, false);
            var headCollider = headObject.AddComponent<SphereCollider>();
            headObject.AddComponent<Hitbox>().Configure(health, HitZone.Head);

            var bodyObject = new GameObject("Hitbox_Body");
            bodyObject.transform.SetParent(hitboxes, false);
            var bodyCollider = bodyObject.AddComponent<CapsuleCollider>();
            bodyObject.AddComponent<Hitbox>().Configure(health, HitZone.Body);

            Edit(rig, so =>
            {
                so.Require("health").objectReferenceValue = health;
                so.Require("head").objectReferenceValue = headCollider;
                so.Require("body").objectReferenceValue = bodyCollider;
            });
            rig.SetHeight(1.8f);
            return rig;
        }

        // ------------------------------------------------------------------ Difficulty

        public static void LoadDifficulties()
        {
            ProjectSetup.EnsureFolder(DifficultyFolder);
            Easy = Difficulty("BD_Easy", d =>
            {
                d.displayName = "Easy";
                d.viewDistance = 45f; d.viewAngle = 110f; d.hearingRadius = 15f; d.reactionTime = 0.7f;
                d.turnSpeed = 150f; d.aimError = 6f; d.aimSettleTime = 1f; d.headshotChance = 0.05f; d.spreadMultiplier = 1.6f;
                d.burstShots = 3; d.burstPause = 0.6f; d.semiAutoDelay = 0.3f;
                d.strafeChance = 0f; d.coverHealthThreshold = 0.3f;
            });
            Normal = Difficulty("BD_Normal", d =>
            {
                d.displayName = "Normal";
                d.viewDistance = 60f; d.viewAngle = 120f; d.hearingRadius = 25f; d.reactionTime = 0.4f;
                d.turnSpeed = 280f; d.aimError = 3.5f; d.aimSettleTime = 0.7f; d.headshotChance = 0.15f; d.spreadMultiplier = 1.2f;
                d.burstShots = 4; d.burstPause = 0.4f; d.semiAutoDelay = 0.15f;
                d.strafeChance = 0.4f; d.coverHealthThreshold = 0.4f;
            });
            Hard = Difficulty("BD_Hard", d =>
            {
                d.displayName = "Hard";
                d.viewDistance = 80f; d.viewAngle = 140f; d.hearingRadius = 35f; d.reactionTime = 0.22f;
                d.turnSpeed = 450f; d.aimError = 2f; d.aimSettleTime = 0.45f; d.headshotChance = 0.35f; d.spreadMultiplier = 1f;
                d.burstShots = 6; d.burstPause = 0.25f; d.semiAutoDelay = 0.08f;
                d.strafeChance = 0.8f; d.coverHealthThreshold = 0.5f;
            });
        }

        static BotDifficulty Difficulty(string assetName, Action<BotDifficulty> defaults)
        {
            string path = $"{DifficultyFolder}/{assetName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<BotDifficulty>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<BotDifficulty>();
            defaults(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ------------------------------------------------------------------ Body (shared by bots and online players)

        /// <summary>The parts of a built body that other components need wired up.</summary>
        public sealed class CharacterBody
        {
            public Transform Visual;
            public Transform AimPivot;
            public Transform WeaponMount;
            public Renderer[] Renderers;
            public CharacterAppearance Appearance;
            public CharacterAnimator Animator;
        }

        /// <summary>
        /// The cartoon operator body under <paramref name="root"/> (feet at the pivot) with its
        /// appearance (skin slots) and procedural animator. Used by bots and by online players' third-person bodies.
        /// </summary>
        public static CharacterBody BuildBody(Transform root, CharacterSkin skin)
        {
            Transform visual = Group(root, "Visual");
            Transform legLeft = Group(visual, "Leg_Left");
            legLeft.localPosition = new Vector3(-0.12f, 0.84f, 0f);
            Transform legRight = Group(visual, "Leg_Right");
            legRight.localPosition = new Vector3(0.12f, 0.84f, 0f);
            Renderer legMeshLeft = Part(legLeft, "Leg", PrimitiveType.Capsule, new Vector3(0f, -0.4f, 0f), new Vector3(0.2f, 0.42f, 0.22f), skin.skin).GetComponent<Renderer>();
            Renderer legMeshRight = Part(legRight, "Leg", PrimitiveType.Capsule, new Vector3(0f, -0.4f, 0f), new Vector3(0.2f, 0.42f, 0.22f), skin.skin).GetComponent<Renderer>();
            Renderer bootLeft = Part(legLeft, "Boot", PrimitiveType.Sphere, new Vector3(0f, -0.79f, 0.05f), new Vector3(0.24f, 0.13f, 0.32f), skin.accent).GetComponent<Renderer>();
            Renderer bootRight = Part(legRight, "Boot", PrimitiveType.Sphere, new Vector3(0f, -0.79f, 0.05f), new Vector3(0.24f, 0.13f, 0.32f), skin.accent).GetComponent<Renderer>();

            Transform upper = Group(visual, "UpperBody");
            upper.localPosition = new Vector3(0f, 0.84f, 0f);
            Renderer torso = Part(upper, "Torso", PrimitiveType.Capsule, new Vector3(0f, 0.3f, 0f), new Vector3(0.58f, 0.38f, 0.48f), skin.skin).GetComponent<Renderer>();
            Renderer vest = Part(upper, "Vest", PrimitiveType.Cube, new Vector3(0f, 0.32f, 0.01f), new Vector3(0.62f, 0.42f, 0.52f), skin.outfit).GetComponent<Renderer>();
            Renderer belt = Part(upper, "Belt", PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0f), new Vector3(0.6f, 0.05f, 0.52f), skin.accent).GetComponent<Renderer>();
            Renderer head = Part(upper, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.77f, 0f), Vector3.one * 0.36f, skin.skin).GetComponent<Renderer>();
            Renderer helmet = Part(upper, "Helmet", PrimitiveType.Sphere, new Vector3(0f, 0.88f, -0.01f), new Vector3(0.4f, 0.22f, 0.4f), skin.outfit).GetComponent<Renderer>();
            foreach (float x in new[] { -0.065f, 0.065f })
            {
                Part(upper, "Eye", PrimitiveType.Sphere, new Vector3(x, 0.8f, 0.15f), Vector3.one * 0.1f, Palette.Glove);
                Part(upper, "Pupil", PrimitiveType.Sphere, new Vector3(x, 0.8f, 0.195f), Vector3.one * 0.045f, Palette.Ink);
            }

            Transform aimPivot = Group(upper, "AimPivot");
            aimPivot.localPosition = new Vector3(0f, 0.5f, 0f);
            Renderer armRight = Part(aimPivot, "Arm_Right", PrimitiveType.Capsule, new Vector3(0.22f, -0.06f, 0.12f), new Vector3(0.12f, 0.2f, 0.12f), skin.skin, new Vector3(80f, 0f, 0f)).GetComponent<Renderer>();
            Renderer armLeft = Part(aimPivot, "Arm_Left", PrimitiveType.Capsule, new Vector3(-0.1f, -0.08f, 0.22f), new Vector3(0.12f, 0.2f, 0.12f), skin.skin, new Vector3(80f, -35f, 0f)).GetComponent<Renderer>();
            Transform weaponMount = Group(aimPivot, "WeaponMount");
            weaponMount.localPosition = new Vector3(0.14f, -0.1f, 0.28f);

            var appearance = root.gameObject.AddComponent<CharacterAppearance>();
            var animator = root.gameObject.AddComponent<CharacterAnimator>();
            Edit(appearance, so =>
            {
                so.Require("skinRenderers").SetObjects(legMeshLeft, legMeshRight, torso, head, armRight, armLeft);
                so.Require("outfitRenderers").SetObjects(vest, helmet);
                so.Require("accentRenderers").SetObjects(bootLeft, bootRight, belt);
            });
            Edit(animator, so =>
            {
                so.Require("visualRoot").objectReferenceValue = visual;
                so.Require("upperBody").objectReferenceValue = upper;
                so.Require("aimPivot").objectReferenceValue = aimPivot;
                so.Require("leftLeg").objectReferenceValue = legLeft;
                so.Require("rightLeg").objectReferenceValue = legRight;
            });

            // Enemy outline shells for the main body parts (eyes and the belt would only add clutter).
            var outline = root.gameObject.AddComponent<CharacterOutline>();
            Renderer[] outlined = visual.GetComponentsInChildren<Renderer>(true).Where(part => part.name != "Eye" && part.name != "Pupil" && part.name != "Belt").ToArray();
            Edit(outline, so =>
            {
                so.Require("parts").SetObjects(outlined);
                so.Require("outlineMaterial").objectReferenceValue = OutlineMaterial();
                so.Require("member").objectReferenceValue = root.GetComponent<TeamMember>();
            });

            return new CharacterBody
            {
                Visual = visual,
                AimPivot = aimPivot,
                WeaponMount = weaponMount,
                Renderers = visual.GetComponentsInChildren<Renderer>(true),
                Appearance = appearance,
                Animator = animator,
            };
        }

        static Material OutlineMaterial()
        {
            ProjectSetup.EnsureFolder(BuildPaths.EffectMaterials);
            return GetOrCreateMaterial($"{BuildPaths.EffectMaterials}/M_CharacterOutline.mat", Shader.Find("Spongykill/CharacterOutline"));
        }

        /// <summary>A floating name shown to teammates.</summary>
        public static NameTag BuildNameTag(Transform root, TeamMember member)
        {
            var tagObject = new GameObject("NameTag");
            tagObject.transform.SetParent(root, false);
            tagObject.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            var tagText = tagObject.AddComponent<TextMesh>();
            tagText.text = "Bot";
            tagText.font = Palette.LabelFont;
            tagText.fontSize = 48;
            tagText.characterSize = 0.04f;
            tagText.anchor = TextAnchor.MiddleCenter;
            tagText.alignment = TextAlignment.Center;
            tagText.color = TeamColors.Defenders;
            tagObject.GetComponent<MeshRenderer>().sharedMaterial = Palette.WorldText;
            var nameTag = tagObject.AddComponent<NameTag>();
            Edit(nameTag, so =>
            {
                so.Require("label").objectReferenceValue = tagText;
                so.Require("owner").objectReferenceValue = member;
            });
            return nameTag;
        }

        /// <summary>Synchronises position and heading only: characters never tilt or scale.</summary>
        public static NetworkTransform AddNetworkTransform(GameObject root, NetworkTransform.AuthorityModes authority)
        {
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = authority;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleZ = false;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            networkTransform.Interpolate = true;
            networkTransform.PositionThreshold = 0.01f;
            networkTransform.RotAngleThreshold = 0.5f;
            return networkTransform;
        }

        /// <summary>Makes sure a saved prefab's NetworkObject has its id hash (normally done when the Inspector validates it).</summary>
        public static void RefreshNetworkObjectHash(GameObject prefab)
        {
            var networkObject = prefab ? prefab.GetComponent<NetworkObject>() : null;
            if (networkObject == null) return;
            typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(networkObject, null);
            EditorUtility.SetDirty(prefab);
        }

        // ------------------------------------------------------------------ Bot prefab

        static GameObject BuildBotPrefab()
        {
            ProjectSetup.EnsureFolder(System.IO.Path.GetDirectoryName(BotPrefabPath)?.Replace('\\', '/'));
            CharacterSkin skin = DefenderSkins[0];

            var root = new GameObject("Bot");
            root.AddComponent<NetworkObject>();
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.speed = 4.8f;
            agent.acceleration = 30f;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = 0.3f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var blocker = root.AddComponent<CapsuleCollider>();
            blocker.center = new Vector3(0f, 0.9f, 0f);
            blocker.radius = 0.35f;
            blocker.height = 1.8f;

            var health = root.AddComponent<Health>();
            var member = root.AddComponent<TeamMember>();
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 3f;
            audio.maxDistance = 60f;

            CharacterBody character = BuildBody(root.transform, skin);
            HitboxRig rig = CreateHitboxRig(root.transform, health);
            BuildNameTag(root.transform, member);

            // ---- Behaviour
            var weapons = root.AddComponent<BotWeapons>();
            var brain = root.AddComponent<BotController>();
            int sightMask = GameLayers.DefaultMaskExcluding(GameLayers.Player, GameLayers.Bot, GameLayers.Viewmodel);
            Edit(weapons, so =>
            {
                so.Require("weaponMount").objectReferenceValue = character.WeaponMount;
                so.Require("audioSource").objectReferenceValue = audio;
                so.Require("hitMask").intValue = sightMask;
            });
            Edit(brain, so =>
            {
                so.Require("difficulty").objectReferenceValue = Normal;
                so.Require("weapons").objectReferenceValue = weapons;
                so.Require("characterAnimator").objectReferenceValue = character.Animator;
                so.Require("hitboxRig").objectReferenceValue = rig;
                so.Require("bodyBlocker").objectReferenceValue = blocker;
                so.Require("sightMask").intValue = sightMask;
                so.Require("coverMask").intValue = 1;
            });
            Edit(member, so => so.Require("hitboxRig").objectReferenceValue = rig);

            // ---- Networking: the server runs the brain; clients get a puppet.
            NetworkTransform networkTransform = AddNetworkTransform(root, NetworkTransform.AuthorityModes.Server);
            var combatant = root.AddComponent<NetworkCombatant>();
            var avatar = root.AddComponent<NetworkAvatar>();
            var networkBot = root.AddComponent<NetworkBot>();
            SetReference(combatant, "appearance", character.Appearance);
            Edit(avatar, so =>
            {
                so.Require("characterAnimator").objectReferenceValue = character.Animator;
                so.Require("weaponMount").objectReferenceValue = character.WeaponMount;
                so.Require("hitboxRig").objectReferenceValue = rig;
                so.Require("audioSource").objectReferenceValue = audio;
            });
            SetReference(networkBot, "networkTransform", networkTransform);

            ProjectSetup.SetLayerRecursively(root, GameLayers.Bot);
            ProjectSetup.SetLayerRecursively(rig.gameObject, GameLayers.Hitbox);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BotPrefabPath);
            Object.DestroyImmediate(root);
            RefreshNetworkObjectHash(prefab);
            return prefab;
        }
    }
}
