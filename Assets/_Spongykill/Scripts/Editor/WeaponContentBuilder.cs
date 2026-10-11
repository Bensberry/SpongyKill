using System;
using Spongykill.Core;
using Spongykill.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// Builds the placeholder arsenal: weapon definition assets, primitive first-person models and the
    /// shared combat-effect materials. Stats are only written when a definition is first created, so
    /// Inspector tuning survives re-runs; "Reset Weapon Stats To Defaults" deliberately overwrites them.
    /// Models are always regenerated. Aiming is CS-style: only the sniper scopes; the rest fire from
    /// the hip with tight first-shot accuracy.
    /// </summary>
    public static class WeaponContentBuilder
    {
        public const string ReefRipper = "WD_ReefRipper";
        public const string BubbleBuzzer = "WD_BubbleBuzzer";
        public const string ShellShocker = "WD_ShellShocker";
        public const string HarpoonLongshot = "WD_HarpoonLongshot";
        public const string ClamPopper = "WD_ClamPopper";
        public const string Spatula = "WD_Spatula";
        public const string Balisong = "WD_Balisong";

        public static readonly string[] All = { ReefRipper, BubbleBuzzer, ShellShocker, HarpoonLongshot, ClamPopper, Spatula, Balisong };

        public static Material TracerMaterial { get; private set; }
        public static Material ImpactMaterial { get; private set; }
        static Material muzzleFlashMaterial;

        struct ViewmodelTuning
        {
            public Vector3 Hip;
            public float SightHeight;
            public float AimDistance;
            public Vector3 Muzzle;
            public Vector3 KickOffset;
            public Vector3 KickRotation;
            public float KickRecovery;
            public bool HasMuzzleFlash;
            public InspectStyle Inspect;
            public float InspectDuration;
            /// <summary>0 keeps the default draw motion (0.3 m drop, 40 degree tilt).</summary>
            public float DrawDrop;
            public float DrawTilt;
        }

        [MenuItem("Spongykill/Weapons/Build Weapons", priority = 20)]
        public static void BuildWeaponsMenu() => BuildWeapons(resetStats: false);

        [MenuItem("Spongykill/Weapons/Reset Weapon Stats To Defaults", priority = 21)]
        public static void ResetWeaponStatsMenu() => BuildWeapons(resetStats: true);

        public static WeaponDefinition Load(string assetName) =>
            AssetDatabase.LoadAssetAtPath<WeaponDefinition>($"{BuildPaths.WeaponData}/{assetName}.asset");

        public static bool AllExist()
        {
            foreach (string weapon in All)
                if (Load(weapon) == null) return false;
            return true;
        }

        public static void BuildWeapons(bool resetStats)
        {
            ProjectSetup.EnsureLayers();
            ProjectSetup.EnsureFolder(BuildPaths.WeaponData);
            ProjectSetup.EnsureFolder(BuildPaths.WeaponPrefabs);
            Palette.Load();
            LoadEffectMaterials();

            Define(ReefRipper, BuildReefRipper(), resetStats, w =>
            {
                Identity(w, "Reef Ripper", WeaponCategory.Rifle, WeaponSlot.Primary);
                Damage(w, damage: 30f, head: 4f, range: 200f, falloffStart: 30f, falloffEnd: 90f, minFactor: 0.75f);
                Firing(w, FireMode.Automatic, rpm: 600f, magazine: 30, reserve: 90, reload: 2.4f, draw: 0.7f);
                Accuracy(w, hip: 0.22f, aim: 0.12f, moving: 2.8f, air: 7f, crouch: 0.75f, perShot: 0.32f, maxBloom: 3.2f, bloomRecovery: 9f);
                Recoil(w, vertical: 0.85f, horizontal: 0.35f, recovery: 12f, aimFactor: 0.8f);
                Aiming(w, fieldOfView: 52f, time: 0.2f, scope: false, moveFactor: 0.65f);
                w.canAim = false;
                Presentation(w, moveSpeed: 0.93f, tracer: new Color(1f, 0.95f, 0.6f), pitch: 140f, length: 0.28f, noise: 0.6f);
            });

            Define(BubbleBuzzer, BuildBubbleBuzzer(), resetStats, w =>
            {
                Identity(w, "Bubble Buzzer", WeaponCategory.SMG, WeaponSlot.Primary);
                Damage(w, damage: 22f, head: 3f, range: 120f, falloffStart: 12f, falloffEnd: 40f, minFactor: 0.6f);
                Firing(w, FireMode.Automatic, rpm: 850f, magazine: 30, reserve: 120, reload: 2.1f, draw: 0.55f);
                Accuracy(w, hip: 0.55f, aim: 0.35f, moving: 1.5f, air: 5f, crouch: 0.8f, perShot: 0.25f, maxBloom: 3.5f, bloomRecovery: 12f);
                Recoil(w, vertical: 0.5f, horizontal: 0.3f, recovery: 15f, aimFactor: 0.85f);
                Aiming(w, fieldOfView: 58f, time: 0.15f, scope: false, moveFactor: 0.75f);
                w.canAim = false;
                Presentation(w, moveSpeed: 1f, tracer: new Color(0.7f, 0.95f, 1f), pitch: 200f, length: 0.18f, noise: 0.55f);
            });

            Define(ShellShocker, BuildShellShocker(), resetStats, w =>
            {
                Identity(w, "Shell Shocker", WeaponCategory.Shotgun, WeaponSlot.Primary);
                Damage(w, damage: 14f, head: 1.5f, range: 45f, falloffStart: 6f, falloffEnd: 20f, minFactor: 0.25f);
                w.pellets = 9;
                Firing(w, FireMode.SemiAutomatic, rpm: 70f, magazine: 7, reserve: 28, reload: 2.8f, draw: 0.7f);
                Accuracy(w, hip: 3.4f, aim: 3.2f, moving: 1f, air: 3f, crouch: 0.9f, perShot: 0f, maxBloom: 0f, bloomRecovery: 10f);
                Recoil(w, vertical: 4.5f, horizontal: 1.2f, recovery: 10f, aimFactor: 0.85f);
                Aiming(w, fieldOfView: 60f, time: 0.2f, scope: false, moveFactor: 0.7f);
                w.canAim = false;
                Presentation(w, moveSpeed: 0.9f, tracer: new Color(1f, 0.8f, 0.5f), pitch: 90f, length: 0.45f, noise: 0.8f);
            });

            Define(HarpoonLongshot, BuildHarpoonLongshot(), resetStats, w =>
            {
                Identity(w, "Harpoon Longshot", WeaponCategory.Sniper, WeaponSlot.Primary);
                Damage(w, damage: 115f, head: 2f, range: 300f, falloffStart: 200f, falloffEnd: 300f, minFactor: 1f);
                Firing(w, FireMode.SemiAutomatic, rpm: 45f, magazine: 5, reserve: 15, reload: 3.2f, draw: 1f);
                Accuracy(w, hip: 6f, aim: 0f, moving: 6f, air: 12f, crouch: 0.8f, perShot: 0f, maxBloom: 0f, bloomRecovery: 10f);
                Recoil(w, vertical: 3f, horizontal: 0.4f, recovery: 8f, aimFactor: 0.9f);
                Aiming(w, fieldOfView: 18f, time: 0.25f, scope: true, moveFactor: 0.5f);
                Presentation(w, moveSpeed: 0.85f, tracer: new Color(0.6f, 1f, 0.9f), pitch: 75f, length: 0.6f, noise: 0.65f);
            });

            Define(ClamPopper, BuildClamPopper(), resetStats, w =>
            {
                Identity(w, "Clam Popper", WeaponCategory.Pistol, WeaponSlot.Secondary);
                Damage(w, damage: 30f, head: 3.5f, range: 150f, falloffStart: 15f, falloffEnd: 45f, minFactor: 0.7f);
                Firing(w, FireMode.SemiAutomatic, rpm: 400f, magazine: 12, reserve: 36, reload: 1.7f, draw: 0.45f);
                Accuracy(w, hip: 0.3f, aim: 0.3f, moving: 1.5f, air: 5f, crouch: 0.8f, perShot: 0.6f, maxBloom: 3f, bloomRecovery: 10f);
                Recoil(w, vertical: 1.4f, horizontal: 0.3f, recovery: 14f, aimFactor: 0.8f);
                Aiming(w, fieldOfView: 58f, time: 0.15f, scope: false, moveFactor: 0.8f);
                w.canAim = false;
                Presentation(w, moveSpeed: 1f, tracer: new Color(1f, 0.9f, 0.7f), pitch: 230f, length: 0.2f, noise: 0.5f);
            });

            Define(Spatula, BuildSpatula(), resetStats, w =>
            {
                Identity(w, "Spatula", WeaponCategory.Melee, WeaponSlot.Melee);
                Damage(w, damage: 55f, head: 1f, range: 2f, falloffStart: 100f, falloffEnd: 200f, minFactor: 1f);
                Firing(w, FireMode.Automatic, rpm: 90f, magazine: 0, reserve: 0, reload: 0f, draw: 0.35f);
                Accuracy(w, hip: 0f, aim: 0f, moving: 0f, air: 0f, crouch: 1f, perShot: 0f, maxBloom: 0f, bloomRecovery: 0f);
                Recoil(w, vertical: 0f, horizontal: 0f, recovery: 10f, aimFactor: 1f);
                w.canAim = false;
                Presentation(w, moveSpeed: 1.08f, tracer: Color.white, pitch: 0f, length: 0.18f, noise: 1f);
            });

            Define(Balisong, BuildBalisong(), resetStats, w =>
            {
                Identity(w, "Balisong", WeaponCategory.Melee, WeaponSlot.Melee);
                Damage(w, damage: 50f, head: 1f, range: 1.8f, falloffStart: 100f, falloffEnd: 200f, minFactor: 1f);
                Firing(w, FireMode.Automatic, rpm: 120f, magazine: 0, reserve: 0, reload: 0f, draw: 0.7f);
                Accuracy(w, hip: 0f, aim: 0f, moving: 0f, air: 0f, crouch: 1f, perShot: 0f, maxBloom: 0f, bloomRecovery: 0f);
                Recoil(w, vertical: 0f, horizontal: 0f, recovery: 10f, aimFactor: 1f);
                w.canAim = false;
                Presentation(w, moveSpeed: 1.1f, tracer: Color.white, pitch: 0f, length: 0.14f, noise: 1f);
            });

            AssetDatabase.SaveAssets();
            Debug.Log($"[Spongykill] Weapons built in {BuildPaths.WeaponData}{(resetStats ? " (stats reset to defaults)" : "")}.");
        }

        public static void LoadEffectMaterials()
        {
            ProjectSetup.EnsureFolder(BuildPaths.EffectMaterials);
            string folder = BuildPaths.EffectMaterials;

            TracerMaterial = BuilderUtility.GetOrCreateMaterial($"{folder}/M_Tracer.mat", Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            TracerMaterial.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(TracerMaterial);

            ImpactMaterial = BuilderUtility.GetOrCreateMaterial($"{folder}/M_ImpactBubble.mat", Shader.Find("Universal Render Pipeline/Particles/Lit"));
            ImpactMaterial.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(ImpactMaterial);

            muzzleFlashMaterial = BuilderUtility.GetOrCreateMaterial($"{folder}/M_MuzzleFlash.mat", Shader.Find("Universal Render Pipeline/Lit"));
            muzzleFlashMaterial.SetColor("_BaseColor", new Color(1f, 0.85f, 0.45f));
            muzzleFlashMaterial.EnableKeyword("_EMISSION");
            muzzleFlashMaterial.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.3f) * 6f);
            muzzleFlashMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(muzzleFlashMaterial);
        }

        static void Define(string assetName, GameObject viewmodel, bool resetStats, Action<WeaponDefinition> applyStats)
        {
            string path = $"{BuildPaths.WeaponData}/{assetName}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            bool created = definition == null;
            if (created)
            {
                definition = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }
            if (created || resetStats) applyStats(definition);
            definition.viewmodelPrefab = viewmodel;
            EditorUtility.SetDirty(definition);
        }

        // ------------------------------------------------------------------ Stat groups

        static void Identity(WeaponDefinition w, string name, WeaponCategory category, WeaponSlot slot)
        {
            w.displayName = name;
            w.category = category;
            w.slot = slot;
        }

        static void Damage(WeaponDefinition w, float damage, float head, float range, float falloffStart, float falloffEnd, float minFactor)
        {
            w.damage = damage;
            w.headMultiplier = head;
            w.pellets = 1;
            w.range = range;
            w.falloffStart = falloffStart;
            w.falloffEnd = falloffEnd;
            w.minDamageFactor = minFactor;
        }

        static void Firing(WeaponDefinition w, FireMode mode, float rpm, int magazine, int reserve, float reload, float draw)
        {
            w.fireMode = mode;
            w.roundsPerMinute = rpm;
            w.magazineSize = magazine;
            w.reserveAmmo = reserve;
            w.reloadTime = reload;
            w.drawTime = draw;
        }

        static void Accuracy(WeaponDefinition w, float hip, float aim, float moving, float air, float crouch, float perShot, float maxBloom, float bloomRecovery)
        {
            w.hipSpread = hip;
            w.aimSpread = aim;
            w.movingSpread = moving;
            w.airborneSpread = air;
            w.crouchSpreadFactor = crouch;
            w.spreadPerShot = perShot;
            w.maxBloom = maxBloom;
            w.bloomRecovery = bloomRecovery;
        }

        static void Recoil(WeaponDefinition w, float vertical, float horizontal, float recovery, float aimFactor)
        {
            w.recoilVertical = vertical;
            w.recoilHorizontal = horizontal;
            w.recoilRecoverySpeed = recovery;
            w.aimRecoilFactor = aimFactor;
        }

        static void Aiming(WeaponDefinition w, float fieldOfView, float time, bool scope, float moveFactor)
        {
            w.canAim = true;
            w.aimFieldOfView = fieldOfView;
            w.aimTime = time;
            w.aimUsesScope = scope;
            w.aimMoveSpeedFactor = moveFactor;
        }

        static void Presentation(WeaponDefinition w, float moveSpeed, Color tracer, float pitch, float length, float noise)
        {
            w.moveSpeedFactor = moveSpeed;
            w.tracerColor = tracer;
            w.shotSoundPitch = pitch;
            w.shotSoundLength = length;
            w.shotSoundNoise = noise;
        }

        // ------------------------------------------------------------------ Placeholder models
        // Models face +Z. Cylinders are 2 units tall along Y, so barrels use scale (d, length / 2, d)
        // rotated 90 degrees about X. Parts named "Glove..." are the player's hands.

        static GameObject BuildReefRipper()
        {
            var tuning = new ViewmodelTuning
            {
                Hip = new Vector3(0.2f, -0.2f, 0.48f), SightHeight = 0.075f, AimDistance = 0.42f,
                Muzzle = new Vector3(0f, 0.015f, 0.53f), KickOffset = new Vector3(0f, 0.004f, -0.04f),
                KickRotation = new Vector3(-4f, 0f, 0f), KickRecovery = 14f, HasMuzzleFlash = true,
                Inspect = InspectStyle.Rifle, InspectDuration = 2.6f,
            };
            return Viewmodel("VM_ReefRipper", tuning, m =>
            {
                P(m, "Receiver", PrimitiveType.Cube, new Vector3(0f, 0f, 0.05f), new Vector3(0.07f, 0.09f, 0.34f), Palette.SeaGlass);
                P(m, "Handguard", PrimitiveType.Cube, new Vector3(0f, 0.005f, 0.29f), new Vector3(0.06f, 0.07f, 0.16f), Palette.Pineapple);
                P(m, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.015f, 0.43f), new Vector3(0.022f, 0.07f, 0.022f), Palette.Metal, Barrel);
                P(m, "MuzzleBrake", PrimitiveType.Cylinder, new Vector3(0f, 0.015f, 0.51f), new Vector3(0.034f, 0.02f, 0.034f), Palette.Coral, Barrel);
                P(m, "Stock", PrimitiveType.Cube, new Vector3(0f, -0.01f, -0.19f), new Vector3(0.055f, 0.08f, 0.16f), Palette.Coral);
                P(m, "Magazine", PrimitiveType.Cube, new Vector3(0f, -0.1f, 0.09f), new Vector3(0.045f, 0.13f, 0.065f), Palette.DeepBlue, new Vector3(12f, 0f, 0f));
                P(m, "Grip", PrimitiveType.Cube, new Vector3(0f, -0.085f, -0.04f), new Vector3(0.04f, 0.1f, 0.05f), Palette.DeepBlue, new Vector3(-15f, 0f, 0f));
                P(m, "Sight", PrimitiveType.Cube, new Vector3(0f, 0.06f, 0.02f), new Vector3(0.03f, 0.03f, 0.08f), Palette.Metal);
                P(m, "Glove_Right", PrimitiveType.Sphere, new Vector3(0f, -0.11f, -0.06f), Vector3.one * 0.075f, Palette.Glove);
                P(m, "Glove_Left", PrimitiveType.Sphere, new Vector3(0f, -0.045f, 0.27f), Vector3.one * 0.07f, Palette.Glove);
            });
        }

        static GameObject BuildBubbleBuzzer()
        {
            var tuning = new ViewmodelTuning
            {
                Hip = new Vector3(0.19f, -0.19f, 0.46f), SightHeight = 0.068f, AimDistance = 0.4f,
                Muzzle = new Vector3(0f, 0.012f, 0.3f), KickOffset = new Vector3(0f, 0.003f, -0.03f),
                KickRotation = new Vector3(-3f, 0f, 0f), KickRecovery = 16f, HasMuzzleFlash = true,
                Inspect = InspectStyle.Rifle, InspectDuration = 2.4f,
            };
            return Viewmodel("VM_BubbleBuzzer", tuning, m =>
            {
                P(m, "Receiver", PrimitiveType.Cube, new Vector3(0f, 0f, 0.04f), new Vector3(0.065f, 0.085f, 0.26f), Palette.Anemone);
                P(m, "Shroud", PrimitiveType.Cylinder, new Vector3(0f, 0.012f, 0.2f), new Vector3(0.036f, 0.035f, 0.036f), Palette.SeaGlass, Barrel);
                P(m, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.012f, 0.25f), new Vector3(0.02f, 0.045f, 0.02f), Palette.Metal, Barrel);
                P(m, "Magazine", PrimitiveType.Cube, new Vector3(0f, -0.12f, 0.08f), new Vector3(0.04f, 0.16f, 0.05f), Palette.Pineapple);
                P(m, "Grip", PrimitiveType.Cube, new Vector3(0f, -0.08f, -0.05f), new Vector3(0.04f, 0.09f, 0.05f), Palette.DeepBlue, new Vector3(-12f, 0f, 0f));
                P(m, "Stock", PrimitiveType.Cube, new Vector3(0f, 0f, -0.14f), new Vector3(0.04f, 0.05f, 0.1f), Palette.Anemone);
                P(m, "Sight", PrimitiveType.Cube, new Vector3(0f, 0.055f, 0f), new Vector3(0.025f, 0.025f, 0.06f), Palette.Metal);
                P(m, "Glove_Right", PrimitiveType.Sphere, new Vector3(0f, -0.11f, -0.06f), Vector3.one * 0.075f, Palette.Glove);
                P(m, "Glove_Left", PrimitiveType.Sphere, new Vector3(0f, -0.11f, 0.15f), Vector3.one * 0.07f, Palette.Glove);
            });
        }

        static GameObject BuildShellShocker()
        {
            var tuning = new ViewmodelTuning
            {
                Hip = new Vector3(0.21f, -0.21f, 0.46f), SightHeight = 0.05f, AimDistance = 0.42f,
                Muzzle = new Vector3(0f, 0.02f, 0.56f), KickOffset = new Vector3(0f, 0.01f, -0.09f),
                KickRotation = new Vector3(-12f, 0f, 0f), KickRecovery = 9f, HasMuzzleFlash = true,
                Inspect = InspectStyle.Rifle, InspectDuration = 2.8f,
            };
            return Viewmodel("VM_ShellShocker", tuning, m =>
            {
                P(m, "Receiver", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.09f, 0.24f), Palette.Orange);
                P(m, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0.33f), new Vector3(0.028f, 0.21f, 0.028f), Palette.Shell, Barrel);
                P(m, "Tube", PrimitiveType.Cylinder, new Vector3(0f, -0.022f, 0.3f), new Vector3(0.02f, 0.16f, 0.02f), Palette.Metal, Barrel);
                P(m, "Pump", PrimitiveType.Cube, new Vector3(0f, -0.022f, 0.26f), new Vector3(0.05f, 0.05f, 0.12f), Palette.Coral);
                P(m, "Stock", PrimitiveType.Cube, new Vector3(0f, -0.02f, -0.2f), new Vector3(0.06f, 0.09f, 0.18f), Palette.Wood);
                P(m, "Grip", PrimitiveType.Cube, new Vector3(0f, -0.08f, -0.08f), new Vector3(0.04f, 0.09f, 0.05f), Palette.Wood, new Vector3(-15f, 0f, 0f));
                P(m, "Bead", PrimitiveType.Sphere, new Vector3(0f, 0.045f, 0.5f), Vector3.one * 0.015f, Palette.Pineapple);
                P(m, "Glove_Right", PrimitiveType.Sphere, new Vector3(0f, -0.1f, -0.08f), Vector3.one * 0.075f, Palette.Glove);
                P(m, "Glove_Left", PrimitiveType.Sphere, new Vector3(0f, -0.06f, 0.26f), Vector3.one * 0.07f, Palette.Glove);
            });
        }

        static GameObject BuildHarpoonLongshot()
        {
            var tuning = new ViewmodelTuning
            {
                Hip = new Vector3(0.21f, -0.22f, 0.44f), SightHeight = 0.08f, AimDistance = 0.25f,
                Muzzle = new Vector3(0f, 0.012f, 0.66f), KickOffset = new Vector3(0f, 0.01f, -0.08f),
                KickRotation = new Vector3(-10f, 0f, 0f), KickRecovery = 8f, HasMuzzleFlash = true,
                Inspect = InspectStyle.Rifle, InspectDuration = 3f,
            };
            return Viewmodel("VM_HarpoonLongshot", tuning, m =>
            {
                P(m, "Receiver", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.065f, 0.08f, 0.3f), Palette.Kelp);
                P(m, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.012f, 0.38f), new Vector3(0.02f, 0.24f, 0.02f), Palette.Shell, Barrel);
                P(m, "HarpoonTip", PrimitiveType.Sphere, new Vector3(0f, 0.012f, 0.63f), new Vector3(0.03f, 0.03f, 0.06f), Palette.Pineapple);
                P(m, "Stock", PrimitiveType.Cube, new Vector3(0f, -0.015f, -0.24f), new Vector3(0.055f, 0.1f, 0.2f), Palette.Kelp);
                P(m, "Scope", PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0.02f), new Vector3(0.034f, 0.11f, 0.034f), Palette.DeepBlue, Barrel);
                P(m, "ScopeRingRear", PrimitiveType.Cube, new Vector3(0f, 0.05f, -0.04f), new Vector3(0.02f, 0.03f, 0.02f), Palette.Metal);
                P(m, "ScopeRingFront", PrimitiveType.Cube, new Vector3(0f, 0.05f, 0.08f), new Vector3(0.02f, 0.03f, 0.02f), Palette.Metal);
                P(m, "Bolt", PrimitiveType.Sphere, new Vector3(0.045f, 0.02f, -0.05f), Vector3.one * 0.025f, Palette.Metal);
                P(m, "Magazine", PrimitiveType.Cube, new Vector3(0f, -0.07f, 0.03f), new Vector3(0.04f, 0.06f, 0.07f), Palette.DeepBlue);
                P(m, "Glove_Right", PrimitiveType.Sphere, new Vector3(0f, -0.1f, -0.1f), Vector3.one * 0.075f, Palette.Glove);
                P(m, "Glove_Left", PrimitiveType.Sphere, new Vector3(0f, -0.05f, 0.22f), Vector3.one * 0.07f, Palette.Glove);
            });
        }

        static GameObject BuildClamPopper()
        {
            var tuning = new ViewmodelTuning
            {
                Hip = new Vector3(0.17f, -0.18f, 0.42f), SightHeight = 0.064f, AimDistance = 0.4f,
                Muzzle = new Vector3(0f, 0.03f, 0.165f), KickOffset = new Vector3(0f, 0.006f, -0.05f),
                KickRotation = new Vector3(-9f, 0f, 0f), KickRecovery = 14f, HasMuzzleFlash = true,
                Inspect = InspectStyle.Pistol, InspectDuration = 2.2f,
            };
            return Viewmodel("VM_ClamPopper", tuning, m =>
            {
                P(m, "Slide", PrimitiveType.Cube, new Vector3(0f, 0.03f, 0.05f), new Vector3(0.035f, 0.045f, 0.2f), Palette.Pineapple);
                P(m, "Frame", PrimitiveType.Cube, new Vector3(0f, 0f, 0.06f), new Vector3(0.032f, 0.025f, 0.16f), Palette.Rock);
                P(m, "Grip", PrimitiveType.Cube, new Vector3(0f, -0.055f, -0.025f), new Vector3(0.034f, 0.1f, 0.05f), Palette.Coral, new Vector3(-12f, 0f, 0f));
                P(m, "BarrelTip", PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.155f), new Vector3(0.016f, 0.01f, 0.016f), Palette.Metal, Barrel);
                P(m, "Sight", PrimitiveType.Cube, new Vector3(0f, 0.058f, 0.13f), new Vector3(0.008f, 0.012f, 0.012f), Palette.Metal);
                P(m, "Glove_Right", PrimitiveType.Sphere, new Vector3(0f, -0.065f, -0.03f), Vector3.one * 0.07f, Palette.Glove);
            });
        }

        static GameObject BuildSpatula()
        {
            var tuning = new ViewmodelTuning
            {
                Hip = new Vector3(0.23f, -0.23f, 0.42f), SightHeight = 0f, AimDistance = 0.38f,
                Muzzle = new Vector3(0f, 0f, 0.35f), KickOffset = new Vector3(-0.1f, 0.03f, 0.1f),
                KickRotation = new Vector3(20f, -55f, -25f), KickRecovery = 9f, HasMuzzleFlash = false,
                Inspect = InspectStyle.Knife, InspectDuration = 2.4f,
            };
            return Viewmodel("VM_Spatula", tuning, m =>
            {
                P(m, "Handle", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.018f, 0.13f, 0.018f), Palette.Wood, Barrel);
                P(m, "Ferrule", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.14f), new Vector3(0.022f, 0.015f, 0.022f), Palette.Metal, Barrel);
                P(m, "Neck", PrimitiveType.Cube, new Vector3(0f, 0f, 0.2f), new Vector3(0.02f, 0.006f, 0.1f), Palette.Metal);
                P(m, "Blade", PrimitiveType.Cube, new Vector3(0f, 0f, 0.3f), new Vector3(0.12f, 0.008f, 0.13f), Palette.Metal);
                P(m, "Glove_Right", PrimitiveType.Sphere, new Vector3(0f, -0.02f, -0.08f), Vector3.one * 0.075f, Palette.Glove);
            }, modelEuler: new Vector3(-20f, -10f, 0f));
        }

        /// <summary>
        /// A butterfly knife: the safe handle sits in the glove; the blade and the free (bite) handle
        /// each pivot on the pin at the top of the safe handle. <see cref="BalisongAnimator"/> flips them.
        /// </summary>
        static GameObject BuildBalisong()
        {
            var tuning = new ViewmodelTuning
            {
                Hip = new Vector3(0.21f, -0.2f, 0.42f), SightHeight = 0f, AimDistance = 0.42f,
                Muzzle = new Vector3(0f, 0f, 0.2f), KickOffset = new Vector3(0f, 0.01f, 0.14f),
                KickRotation = new Vector3(-15f, 0f, 0f), KickRecovery = 10f, HasMuzzleFlash = false,
                Inspect = InspectStyle.Knife, InspectDuration = 2.8f,
                // Stay in view while drawing so the flip-open is visible.
                DrawDrop = 0.06f, DrawTilt = 8f,
            };
            return Viewmodel("VM_Balisong", tuning, m =>
            {
                m.localScale = Vector3.one * 1.4f;
                P(m, "Glove_Right", PrimitiveType.Sphere, new Vector3(0f, -0.02f, -0.075f), Vector3.one * 0.055f, Palette.Glove);

                Transform spin = BuilderUtility.Group(m, "Spin");
                P(spin, "SafeHandle", PrimitiveType.Cube, new Vector3(-0.012f, 0f, -0.06f), new Vector3(0.012f, 0.022f, 0.12f), Palette.Anemone);
                foreach (float z in new[] { -0.03f, -0.06f, -0.09f })
                    P(spin, "Bubble", PrimitiveType.Sphere, new Vector3(-0.019f, 0f, z), Vector3.one * 0.011f, Palette.Glass);
                P(spin, "Pin", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.016f, 0.02f, 0.016f), Palette.Metal, new Vector3(0f, 0f, 90f));

                Transform blade = BuilderUtility.Group(spin, "Blade");
                P(blade, "BladeSteel", PrimitiveType.Cube, new Vector3(0f, 0f, 0.05f), new Vector3(0.004f, 0.024f, 0.1f), Palette.Metal);
                P(blade, "BladeTip", PrimitiveType.Cube, new Vector3(0f, 0.004f, 0.104f), new Vector3(0.004f, 0.017f, 0.017f), Palette.Metal, new Vector3(45f, 0f, 0f));

                Transform bite = BuilderUtility.Group(spin, "BiteHandle");
                P(bite, "BiteGrip", PrimitiveType.Cube, new Vector3(0.012f, 0f, -0.06f), new Vector3(0.012f, 0.022f, 0.12f), Palette.SeaGlass);
                P(bite, "Latch", PrimitiveType.Cube, new Vector3(0.012f, 0f, -0.125f), new Vector3(0.014f, 0.01f, 0.012f), Palette.Metal);
            }, modelEuler: new Vector3(-15f, -12f, 0f), configureRoot: root =>
            {
                var animator = root.AddComponent<BalisongAnimator>();
                BuilderUtility.Edit(animator, so =>
                {
                    so.Require("blade").objectReferenceValue = root.transform.Find("Model/Spin/Blade");
                    so.Require("biteHandle").objectReferenceValue = root.transform.Find("Model/Spin/BiteHandle");
                    so.Require("spin").objectReferenceValue = root.transform.Find("Model/Spin");
                });
            });
        }

        static readonly Vector3 Barrel = new Vector3(90f, 0f, 0f);

        static void P(Transform model, string name, PrimitiveType shape, Vector3 position, Vector3 scale, Material material, Vector3 euler = default) =>
            BuilderUtility.Part(model, name, shape, position, scale, material, euler, castShadows: false);

        static GameObject Viewmodel(string name, ViewmodelTuning tuning, Action<Transform> buildModel, Vector3 modelEuler = default, Action<GameObject> configureRoot = null)
        {
            var root = new GameObject(name);
            Transform model = BuilderUtility.Group(root.transform, "Model");
            model.localRotation = Quaternion.Euler(modelEuler);
            buildModel(model);

            Transform muzzle = BuilderUtility.Group(root.transform, "Muzzle");
            muzzle.localPosition = tuning.Muzzle;
            GameObject flash = null;
            Light flashLight = null;
            if (tuning.HasMuzzleFlash)
            {
                flash = BuilderUtility.Part(muzzle, "MuzzleFlash", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.07f, 0.07f, 0.15f), muzzleFlashMaterial, castShadows: false);
                var lightObject = new GameObject("MuzzleLight");
                lightObject.transform.SetParent(muzzle, false);
                flashLight = lightObject.AddComponent<Light>();
                flashLight.type = LightType.Point;
                flashLight.range = 6f;
                flashLight.intensity = 2.5f;
                flashLight.color = new Color(1f, 0.78f, 0.45f);
                flashLight.shadows = LightShadows.None;
                flashLight.enabled = false;
            }

            var viewmodel = root.AddComponent<WeaponViewmodel>();
            BuilderUtility.Edit(viewmodel, so =>
            {
                so.Require("muzzle").objectReferenceValue = muzzle;
                so.Require("muzzleFlash").objectReferenceValue = flash;
                so.Require("muzzleLight").objectReferenceValue = flashLight;
                so.Require("hipPosition").vector3Value = tuning.Hip;
                so.Require("sightHeight").floatValue = tuning.SightHeight;
                so.Require("aimDistance").floatValue = tuning.AimDistance;
                so.Require("kickOffset").vector3Value = tuning.KickOffset;
                so.Require("kickRotation").vector3Value = tuning.KickRotation;
                so.Require("kickRecovery").floatValue = tuning.KickRecovery;
                so.Require("inspectStyle").enumValueIndex = (int)tuning.Inspect;
                so.Require("inspectDuration").floatValue = tuning.InspectDuration > 0f ? tuning.InspectDuration : 2.6f;
                so.Require("drawDrop").floatValue = tuning.DrawDrop > 0f ? tuning.DrawDrop : 0.3f;
                so.Require("drawTilt").floatValue = tuning.DrawTilt > 0f ? tuning.DrawTilt : 40f;
            });
            configureRoot?.Invoke(root);

            ProjectSetup.SetLayerRecursively(root, GameLayers.Viewmodel);
            // The world camera skips the Viewmodel layer, and that also hides lights on it; keep the
            // muzzle light on Default so the flash lights up the surroundings.
            if (flashLight) flashLight.gameObject.layer = 0;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{BuildPaths.WeaponPrefabs}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
