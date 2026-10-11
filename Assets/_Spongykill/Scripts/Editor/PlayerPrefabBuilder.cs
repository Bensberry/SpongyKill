using Spongykill.Combat;
using Spongykill.Core;
using Spongykill.Net;
using Spongykill.Player;
using Spongykill.UI;
using Spongykill.Weapons;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// Builds the NetPlayer prefab: controller, health and hitboxes, weapons with a viewmodel overlay
    /// camera, pickup interaction, the HUD, networking and a third-person body that everyone else sees.
    /// Overwrites the existing prefab.
    /// </summary>
    public static class PlayerPrefabBuilder
    {
        /// <summary>Sea-water blue shared by the fog and the camera background so distant geometry fades out cleanly.</summary>
        public static readonly Color WaterColor = new Color(0.17f, 0.52f, 0.68f);

        const float ViewmodelFieldOfView = 60f;

        [MenuItem("Spongykill/Rebuild Player Prefab", priority = 1)]
        public static GameObject Build() => BuildNetworked();

        public static GameObject BuildNetworked()
        {
            const string path = BuildPaths.NetPlayerPrefab;
            if (!ProjectSetup.ConfigureProject())
                Debug.LogWarning("[Spongykill] TextMeshPro resources are missing; the HUD text will not render until they are imported.");
            Palette.Load();
            if (!WeaponContentBuilder.AllExist()) WeaponContentBuilder.BuildWeapons(resetStats: false);
            ProjectSetup.EnsureFolder(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'));

            var root = new GameObject("NetPlayer") { tag = "Player" };
            root.AddComponent<NetworkObject>();

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.skinWidth = 0.05f;
            controller.center = new Vector3(0f, 0.9f + controller.skinWidth, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.35f;
            controller.minMoveDistance = 0f;

            var audioSource = root.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            var health = root.AddComponent<Health>();
            var member = root.AddComponent<TeamMember>();
            member.Configure(Team.Attackers, "You");
            // Only the owner marks itself local, once ownership is known.
            BuilderUtility.Edit(member, so => so.Require("isLocalPlayer").boolValue = false);
            root.AddComponent<PlayerInputHandler>();
            var motor = root.AddComponent<PlayerMotor>();
            var look = root.AddComponent<PlayerLook>();
            var weapons = root.AddComponent<PlayerWeapons>();
            var interactor = root.AddComponent<PlayerInteractor>();
            var playerController = root.AddComponent<PlayerController>();
            var fallDamage = root.AddComponent<FallDamage>();

            Transform cameraRoot = BuilderUtility.Group(root.transform, "CameraRoot");
            cameraRoot.localPosition = new Vector3(0f, 1.62f, 0f);
            (Camera worldCamera, Transform viewmodelRoot) = CreateCameras(cameraRoot);

            // Shadow-only body: the player sees their own shadow without the capsule blocking the view.
            GameObject body = BuilderUtility.Part(root.transform, "BodyVisual", PrimitiveType.Capsule, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Palette.PlayerBody);
            body.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            HitboxRig hitboxRig = CharacterBuilder.CreateHitboxRig(root.transform, health);

            ProjectSetup.SetLayerRecursively(root, GameLayers.Player);
            ProjectSetup.SetLayerRecursively(hitboxRig.gameObject, GameLayers.Hitbox);
            ProjectSetup.SetLayerRecursively(viewmodelRoot.gameObject, GameLayers.Viewmodel);

            BuilderUtility.Edit(motor, so =>
            {
                so.Require("cameraRoot").objectReferenceValue = cameraRoot;
                so.Require("bodyVisual").objectReferenceValue = body.transform;
                so.Require("obstructionMask").intValue = GameLayers.DefaultMaskExcluding(GameLayers.Player, GameLayers.Viewmodel, GameLayers.Hitbox);
            });
            BuilderUtility.SetReference(look, "cameraPivot", worldCamera.transform);
            BuilderUtility.Edit(weapons, so =>
            {
                so.Require("startingLoadout").SetObjects(
                    WeaponContentBuilder.Load(WeaponContentBuilder.ReefRipper),
                    WeaponContentBuilder.Load(WeaponContentBuilder.ClamPopper),
                    WeaponContentBuilder.Load(WeaponContentBuilder.Balisong));
                so.Require("worldCamera").objectReferenceValue = worldCamera;
                so.Require("viewmodelRoot").objectReferenceValue = viewmodelRoot;
                so.Require("audioSource").objectReferenceValue = audioSource;
                so.Require("hitMask").intValue = GameLayers.DefaultMaskExcluding(GameLayers.Player, GameLayers.Bot, GameLayers.Viewmodel);
            });
            BuilderUtility.SetReference(interactor, "viewPoint", worldCamera.transform);
            BuilderUtility.SetReference(playerController, "audioSource", audioSource);

            PlayerHUD hud = HudBuilder.Build(root.transform, health, weapons, motor, interactor, playerController);

            Behaviour[] ownerOnly = { root.GetComponent<PlayerInputHandler>(), motor, look, weapons, interactor, playerController, fallDamage };
            AddNetworking(root, member, hitboxRig, body.GetComponent<Renderer>(), worldCamera.gameObject, hud.gameObject, ownerOnly);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            CharacterBuilder.RefreshNetworkObjectHash(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Spongykill] Player prefab built at {path}.");
            return prefab;
        }

        /// <summary>
        /// The online pieces: owner-authoritative movement sync, identity/health/stats, a third-person
        /// body with a name tag for everyone else, and the owner-only switch.
        /// </summary>
        static void AddNetworking(GameObject root, TeamMember member, HitboxRig hitboxRig, Renderer shadowBody,
            GameObject ownerCamera, GameObject hud, Behaviour[] ownerOnly)
        {
            if (CharacterBuilder.AttackerSkins == null) CharacterBuilder.LoadSkins();
            CharacterBuilder.CharacterBody character = CharacterBuilder.BuildBody(root.transform, CharacterBuilder.AttackerSkins[0]);
            CharacterBuilder.BuildNameTag(root.transform, member);

            var voice = new GameObject("RemoteAudio").AddComponent<AudioSource>();
            voice.transform.SetParent(root.transform, false);
            voice.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 3f;
            voice.maxDistance = 60f;

            NetworkTransform networkTransform = CharacterBuilder.AddNetworkTransform(root, NetworkTransform.AuthorityModes.Owner);
            var combatant = root.AddComponent<NetworkCombatant>();
            var avatar = root.AddComponent<NetworkAvatar>();
            var player = root.AddComponent<NetworkPlayer>();
            BuilderUtility.SetReference(combatant, "appearance", character.Appearance);
            BuilderUtility.Edit(avatar, so =>
            {
                so.Require("characterAnimator").objectReferenceValue = character.Animator;
                so.Require("weaponMount").objectReferenceValue = character.WeaponMount;
                so.Require("hitboxRig").objectReferenceValue = hitboxRig;
                so.Require("audioSource").objectReferenceValue = voice;
                so.Require("animateWhenOwner").boolValue = true;
            });
            BuilderUtility.Edit(player, so =>
            {
                so.Require("ownerOnlyComponents").SetObjects(ownerOnly);
                so.Require("ownerOnlyObjects").SetObjects(ownerCamera, hud);
                so.Require("thirdPersonRenderers").SetObjects(character.Renderers);
                so.Require("ownerShadowRenderers").SetObjects(shadowBody);
                so.Require("networkTransform").objectReferenceValue = networkTransform;
                so.Require("lineOfSightMask").intValue = GameLayers.DefaultMaskExcluding(GameLayers.Player, GameLayers.Bot, GameLayers.Hitbox, GameLayers.Viewmodel);
            });
            ProjectSetup.SetLayerRecursively(character.Visual.gameObject, GameLayers.Player);
        }

        /// <summary>
        /// World camera plus an overlay camera that draws only the Viewmodel layer with its own field of
        /// view, so held weapons never clip into walls and don't stretch when aiming zooms the world.
        /// </summary>
        static (Camera worldCamera, Transform viewmodelRoot) CreateCameras(Transform cameraRoot)
        {
            int viewmodelLayer = LayerMask.NameToLayer(GameLayers.Viewmodel);

            var cameraObject = new GameObject("PlayerCamera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(cameraRoot, false);
            var worldCamera = cameraObject.AddComponent<Camera>();
            worldCamera.fieldOfView = 68f;
            worldCamera.nearClipPlane = 0.03f;
            worldCamera.farClipPlane = 400f;
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = WaterColor;
            if (viewmodelLayer >= 0) worldCamera.cullingMask = ~(1 << viewmodelLayer);
            cameraObject.AddComponent<AudioListener>();

            var viewmodelCameraObject = new GameObject("ViewmodelCamera");
            viewmodelCameraObject.transform.SetParent(cameraObject.transform, false);
            var viewmodelCamera = viewmodelCameraObject.AddComponent<Camera>();
            viewmodelCamera.fieldOfView = ViewmodelFieldOfView;
            viewmodelCamera.nearClipPlane = 0.01f;
            viewmodelCamera.farClipPlane = 10f;
            viewmodelCamera.cullingMask = viewmodelLayer >= 0 ? 1 << viewmodelLayer : 0;

            UniversalAdditionalCameraData viewmodelData = viewmodelCamera.GetUniversalAdditionalCameraData();
            viewmodelData.renderType = CameraRenderType.Overlay;
            viewmodelData.renderShadows = false;
            viewmodelData.renderPostProcessing = true;

            UniversalAdditionalCameraData worldData = worldCamera.GetUniversalAdditionalCameraData();
            worldData.renderPostProcessing = true;
            worldData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            worldData.antialiasingQuality = AntialiasingQuality.High;
            worldData.cameraStack.Clear();
            worldData.cameraStack.Add(viewmodelCamera);

            Transform viewmodelRoot = BuilderUtility.Group(cameraObject.transform, "ViewmodelRoot");
            return (worldCamera, viewmodelRoot);
        }
    }
}
