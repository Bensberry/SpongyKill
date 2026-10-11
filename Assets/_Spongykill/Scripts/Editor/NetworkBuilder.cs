using System.Collections.Generic;
using System.Linq;
using Spongykill.Net;
using Spongykill.UI;
using Spongykill.Weapons;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// Online plumbing: the shared weapon/skin catalog, the online config (matchmaker URL and token),
    /// the list of network prefabs, and the main menu scene that owns the NetworkManager. Build order
    /// in the player: main menu first, then the map.
    /// </summary>
    public static class NetworkBuilder
    {
        public const string MenuScenePath = BuildPaths.Scenes + "/MainMenu.unity";
        const string CatalogPath = BuildPaths.Resources + "/" + GameCatalog.ResourcePath + ".asset";
        const string OnlineConfigPath = BuildPaths.Resources + "/" + OnlineConfig.ResourcePath + ".asset";
        const string PrefabListPath = BuildPaths.NetworkData + "/NetworkPrefabs.asset";

        [MenuItem("Spongykill/Online/Build Main Menu And Network Setup", priority = 5)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildAssets();
            BuildMainMenuScene();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Spongykill] Main menu and network setup built ({MenuScenePath}).");
        }

        /// <summary>Catalog, online config and prefab list (needs the weapon, bot and player prefabs).</summary>
        public static NetworkPrefabsList BuildAssets()
        {
            ProjectSetup.EnsureFolder(BuildPaths.Resources);
            ProjectSetup.EnsureFolder(BuildPaths.NetworkData);
            if (!WeaponContentBuilder.AllExist()) WeaponContentBuilder.BuildWeapons(resetStats: false);
            if (!CharacterBuilder.Exists()) CharacterBuilder.BuildAll();
            CharacterBuilder.LoadSkins();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BuildPaths.NetPlayerPrefab) == null) PlayerPrefabBuilder.BuildNetworked();

            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<GameCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            WeaponDefinition[] weapons = WeaponContentBuilder.All.Select(WeaponContentBuilder.Load).ToArray();
            catalog.Configure(weapons, CharacterBuilder.AttackerSkins, CharacterBuilder.DefenderSkins);
            EditorUtility.SetDirty(catalog);

            // Never overwrite: the user puts their Edgegap matchmaker URL and token here.
            if (AssetDatabase.LoadAssetAtPath<OnlineConfig>(OnlineConfigPath) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<OnlineConfig>(), OnlineConfigPath);

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
            }
            // Start clean so a rebuilt prefab never leaves a stale entry behind.
            foreach (NetworkPrefab entry in list.PrefabList.ToList()) list.Remove(entry);
            foreach (string path in new[] { BuildPaths.NetPlayerPrefab, CharacterBuilder.BotPrefabPath })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && !list.Contains(prefab)) list.Add(new NetworkPrefab { Prefab = prefab });
            }
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            return list;
        }

        /// <summary>A little Bikini Bottom-style corner behind the title screen.</summary>
        static void BuildBackdrop()
        {
            Palette.Load();
            EnvironmentBuilder.SetupLighting(EnvironmentBuilder.GetOrCreateVolumeProfile(), fogDensity: 0.018f);
            var root = new GameObject("Backdrop").transform;
            var random = new System.Random(11);
            BuilderUtility.Box(root, "Sand", new Vector3(0f, -0.5f, 10f), new Vector3(140f, 1f, 140f), Palette.FloorGrid);
            PropFactory.PineappleHouse(root, new Vector3(7f, 0f, 12f), 215f, 1.1f);
            PropFactory.PineappleHouse(root, new Vector3(-16f, 0f, 30f), 160f, 0.8f);
            PropFactory.BoatCar(root, new Vector3(-4f, 0f, 3f), 35f, Palette.Coral);
            PropFactory.LampPost(root, new Vector3(1.5f, 0f, 1f));
            PropFactory.GiantClam(root, new Vector3(-9f, 0f, 12f), 20f);
            PropFactory.CrateStack(root, new Vector3(-12f, 0f, 2f), 15f);
            foreach (Vector3 spot in new[] { new Vector3(12f, 0f, 4f), new Vector3(14f, 0f, 9f), new Vector3(-1f, 0f, 18f), new Vector3(-20f, 0f, 14f), new Vector3(3f, 0f, 24f) })
                PropFactory.Kelp(root, spot, random);
            foreach (Vector3 spot in new[] { new Vector3(10f, 0f, 1f), new Vector3(-6f, 0f, -2f), new Vector3(-14f, 0f, 20f), new Vector3(18f, 0f, 16f) })
                PropFactory.Coral(root, spot, random, 1.1f);
            PropFactory.Rock(root, new Vector3(16f, 0f, 22f), new Vector3(3f, 1.6f, 2.4f));
            PropFactory.Rock(root, new Vector3(-22f, 0f, 6f), new Vector3(2.4f, 1.2f, 2f));
            foreach (Vector3 spot in new[] { new Vector3(2f, 9f, 16f), new Vector3(-8f, 11f, 22f), new Vector3(13f, 8f, 20f), new Vector3(-15f, 7f, 8f) })
                PropFactory.Jellyfish(root, spot, PropFactory.Range(random, 0.8f, 1.3f));
            for (int i = 0; i < 10; i++)
            {
                var center = new Vector3(PropFactory.Range(random, -60f, 60f), PropFactory.Range(random, 38f, 55f), PropFactory.Range(random, -20f, 90f));
                PropFactory.SkyFlower(root, center, PropFactory.Range(random, 6f, 11f), Palette.SkyFlowers[i % Palette.SkyFlowers.Length]);
            }
        }

        static void BuildMainMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Load after creating the scene: a new scene unloads assets nothing references yet.
            NetworkPrefabsList prefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);

            BuildBackdrop();
            var cameraObject = new GameObject("MenuCamera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlayerPrefabBuilder.WaterColor;
            camera.fieldOfView = 55f;
            camera.farClipPlane = 300f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<Spongykill.Effects.MenuCameraDrift>().Configure(new Vector3(-3f, 3.5f, 9f), 23f, 2.5f, -150f);

            var networkObject = new GameObject("NetworkManager");
            var transport = networkObject.AddComponent<UnityTransport>();
            transport.ConnectionData.Port = NetSession.DefaultPort;
            var manager = networkObject.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                ConnectionApproval = true,
                EnableSceneManagement = true,
                TickRate = 30,
                ProtocolVersion = (ushort)NetSession.ProtocolVersion,
                ClientConnectionBufferTimeout = 15,
            };
            // Assigned through serialization: a plain field assignment here doesn't survive the scene save.
            BuilderUtility.Edit(manager, so =>
            {
                SerializedProperty lists = so.Require("NetworkConfig.Prefabs.NetworkPrefabsLists");
                lists.arraySize = prefabs != null ? 1 : 0;
                if (prefabs != null) lists.GetArrayElementAtIndex(0).objectReferenceValue = prefabs;
            });
            var session = networkObject.AddComponent<NetSession>();
            BuilderUtility.SetReference(session, "networkPrefabs", prefabs);
            networkObject.AddComponent<EdgegapServerHooks>();

            new GameObject("MainMenu").AddComponent<MainMenu>();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            EnvironmentBuilder.AddSceneToBuildSettings(MenuScenePath, 0);
        }
    }
}
