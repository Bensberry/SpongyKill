using System.Collections.Generic;
using System.Text;
using Spongykill.AI;
using Spongykill.Core;
using Spongykill.Maps;
using Spongykill.Match;
using Spongykill.UI;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using static Spongykill.EditorTools.BuilderUtility;
using static Spongykill.EditorTools.PropFactory;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// Builds Coral Plaza, the first defuse map: a Bikini Bottom-style downtown whose tactical flow
    /// follows Critical Ops' Plaza (attackers drop from a raised depot into a wide street that splits
    /// into a left lane, a central square and a right boulevard; defenders spawn between sites A and
    /// B), with original geometry. Plan coordinates: X east, Z north, metres; attackers in the north.
    /// </summary>
    public static class CoralPlazaBuilder
    {
        const string MapName = "Coral Plaza";
        const string SceneFolder = BuildPaths.Scenes + "/Maps";
        const string ScenePath = SceneFolder + "/CoralPlaza.unity";
        const string NavMeshFolder = SceneFolder + "/CoralPlaza";
        const string NavMeshPath = NavMeshFolder + "/NavMesh-CoralPlaza.asset";
        const float WallThickness = 0.6f;
        const float SpawnPlatformHeight = 2f;

        // Reference points shared by layout, gameplay markers and the route report.
        static readonly Vector3 SiteACenter = new Vector3(12f, 0f, 18f);
        static readonly Vector3 SiteBCenter = new Vector3(58f, 0f, 14f);
        static readonly Vector3 MidCenter = new Vector3(37f, 0f, 40f);

        [MenuItem("Spongykill/Maps/Build Coral Plaza", priority = 10)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ProjectSetup.ConfigureProject();
            ProjectSetup.EnsureFolder(NavMeshFolder);
            Palette.Load();
            if (!WeaponContentBuilder.AllExist()) WeaponContentBuilder.BuildWeapons(resetStats: false);
            if (!CharacterBuilder.Exists()) CharacterBuilder.BuildAll();
            CharacterBuilder.LoadSkins();
            CharacterBuilder.LoadDifficulties();
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildPaths.NetPlayerPrefab);
            if (playerPrefab == null) playerPrefab = PlayerPrefabBuilder.BuildNetworked();
            NetworkBuilder.BuildAssets();
            VolumeProfile volumeProfile = EnvironmentBuilder.GetOrCreateVolumeProfile();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnvironmentBuilder.SetupLighting(volumeProfile, fogDensity: 0.009f);
            EnvironmentBuilder.CreateCombatEffects();

            var mapRoot = new GameObject("Map_CoralPlaza");
            Transform geometry = Group(mapRoot.transform, "Geometry");
            Transform decor = Group(mapRoot.transform, "Decor");
            Transform gameplay = Group(mapRoot.transform, "Gameplay");

            MapLayout layout = CreateLayout();
            BuildGround(geometry);
            BuildZoneFloors(geometry, layout);
            BuildWalls(geometry, layout);
            BuildElevation(geometry);
            BuildCover(geometry);
            BuildLandmarks(decor);
            BuildAmbience(decor);
            BuildCallouts(decor);
            BakeMinimap(layout, mapRoot);

            MapDefinition map = BuildGameplay(mapRoot, gameplay);
            BuildTacticalPoints(gameplay);
            BotSquadSpawner spawner = BuildBots(gameplay, map);
            // Players are spawned by the match when they deploy; nobody is placed in the scene.
            NetworkObject match = BuildMatch(map, spawner, playerPrefab);

            EditorSceneManager.SaveScene(scene, ScenePath);
            BakeNavMesh(geometry.gameObject);
            // In-scene network objects get their id from the saved scene, so assign it after saving.
            CharacterBuilder.RefreshNetworkObjectHash(match.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnvironmentBuilder.AddSceneToBuildSettings(NetworkBuilder.MenuScenePath, 0);
            EnvironmentBuilder.AddSceneToBuildSettings(ScenePath, 1);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Spongykill] {MapName} built at {ScenePath}.\n{RouteReport(map)}");
        }

        // ------------------------------------------------------------------ Floor plan

        static MapLayout CreateLayout()
        {
            var layout = new MapLayout();

            // Attacker spawn: a raised bus depot with a ticket kiosk splitting the way out.
            MapLayout.Zone depot = layout.AddZone("Bus Depot");
            depot.Wall = Palette.Orange; depot.Trim = Palette.Shell; depot.Floor = Palette.Tile;
            depot.WallHeight = 7f; depot.FloorHeight = SpawnPlatformHeight;
            depot.AddRect(26, 88, 48, 110);
            layout.Carve(34, 90, 40, 96);

            MapLayout.Zone depotStairs = layout.AddZone("Depot Stairs");
            depotStairs.Wall = Palette.Orange; depotStairs.Trim = Palette.Shell; depotStairs.WallHeight = 6f;
            depotStairs.AddRect(28, 84, 46, 88);

            // The wide street every attacker passes through before choosing a lane.
            MapLayout.Zone street = layout.AddZone("Main Street");
            street.Wall = Palette.Shell; street.Trim = Palette.Coral; street.Floor = Palette.Slate; street.WallHeight = 7f;
            street.AddRect(16, 68, 66, 84);

            // Left lane: a hall that splits around a pineapple-house block into two approaches to A.
            MapLayout.Zone alley = layout.AddZone("Kelp Alley");
            alley.Wall = Palette.Kelp; alley.Trim = Palette.Shell; alley.WallHeight = 5f;
            alley.AddRect(16, 54, 26, 68)   // down from the street
                .AddRect(4, 48, 26, 56)     // westward hall
                .AddRect(4, 32, 10, 48)     // outer route ("A long")
                .AddRect(15, 32, 22, 48)    // inner garden route
                .AddRect(5, 28, 9, 32)      // A long door
                .AddRect(16, 28, 20, 32)    // A garden door
                .AddRect(20, 40, 26, 44);   // window into the square

            // Centre: an octagonal town square linking the street, both sites and the boulevard.
            MapLayout.Zone square = layout.AddZone("Town Square");
            square.Wall = Palette.Rock; square.Trim = Palette.Shell; square.Floor = Palette.Tile; square.WallHeight = 5f;
            square.AddPolygon(new Vector2(30, 34), new Vector2(44, 34), new Vector2(50, 40), new Vector2(50, 50),
                    new Vector2(44, 56), new Vector2(30, 56), new Vector2(24, 50), new Vector2(24, 40))
                .AddRect(31, 56, 39, 68)    // entrance from the street
                .AddRect(30, 20, 36, 34)    // south corridor toward the defenders
                .AddRect(50, 44, 62, 50)    // connector to the boulevard
                .AddRect(44, 30, 50, 40)    // "B short"
                .AddRect(45, 26, 49, 30);   // B short door

            // Right lane: market slope then a long boulevard into B.
            MapLayout.Zone boulevard = layout.AddZone("Coral Boulevard");
            boulevard.Wall = Palette.Coral; boulevard.Trim = Palette.Shell; boulevard.WallHeight = 6f;
            boulevard.AddRect(52, 56, 66, 68)
                .AddRect(62, 30, 72, 58)
                .AddRect(64, 26, 70, 30);  // B long door

            MapLayout.Zone siteB = layout.AddZone("Seashell Market");
            siteB.Wall = Palette.SeaGlass; siteB.Trim = Palette.Shell; siteB.Floor = Palette.Tile; siteB.WallHeight = 6f;
            siteB.AddRect(44, 2, 74, 26)
                .AddRect(40, 6, 44, 12);   // back door from the defender spawn

            MapLayout.Zone siteA = layout.AddZone("Barnacle Burger");
            siteA.Wall = Palette.Pineapple; siteA.Trim = Palette.Coral; siteA.Floor = Palette.Tile; siteA.WallHeight = 6f;
            siteA.AddRect(2, 8, 22, 28)
                .AddRect(22, 13, 26, 18);  // A short door from the defender side

            // Defender spawn on the pier between the sites, with a junction toward A and mid.
            MapLayout.Zone pier = layout.AddZone("Lighthouse Pier");
            pier.Wall = Palette.DeepBlue; pier.Trim = Palette.Shell; pier.Floor = Palette.Slate; pier.WallHeight = 5f;
            pier.AddRect(30, 2, 40, 16)
                .AddRect(26, 12, 40, 20);

            return layout;
        }

        static void BuildGround(Transform parent)
        {
            const float size = 170f;
            Material ground = Lit(BuildPaths.PlaceholderMaterials, "M_SandGrid_Map", new Color(0.93f, 0.86f, 0.66f), 0.1f,
                Palette.FloorGrid.GetTexture("_BaseMap"), size / 4f);
            Box(parent, "Ground", new Vector3(37f, -0.5f, 55f), new Vector3(size, 1f, size), ground);
        }

        /// <summary>Painted floors per zone (visual only; the ground and platforms carry the collision).</summary>
        static void BuildZoneFloors(Transform parent, MapLayout layout)
        {
            Transform floors = Group(parent, "Floors");
            for (int z = 0; z < layout.Zones.Count; z++)
            {
                MapLayout.Zone zone = layout.Zones[z];
                if (zone.Floor == null) continue;
                foreach (MapLayout.Shape shape in zone.ShapeList)
                {
                    Rect area = shape.Bounds;
                    // A tiny per-zone lift keeps overlapping floors from z-fighting.
                    float top = zone.FloorHeight + 0.02f + z * 0.002f;
                    GameObject slab = Box(floors, $"Floor_{zone.Name}", new Vector3(area.center.x, top - 0.05f, area.center.y),
                        new Vector3(area.width, 0.1f, area.height), zone.Floor);
                    Object.DestroyImmediate(slab.GetComponent<Collider>());
                }
            }
        }

        static void BuildWalls(Transform parent, MapLayout layout)
        {
            Transform walls = Group(parent, "Walls");
            Transform details = Group(parent, "WallDetails");
            var random = new System.Random(42);
            int index = 0;
            foreach (MapLayout.Wall wall in layout.TraceWalls())
            {
                float length = wall.Length;
                if (length < 0.05f) continue;

                MapLayout.Zone zone = wall.Zone;
                Vector2 mid2 = (wall.Start + wall.End) * 0.5f;
                var outward = new Vector3(wall.Outward.x, 0f, wall.Outward.y);
                var mid = new Vector3(mid2.x, 0f, mid2.y);
                Quaternion rotation = Quaternion.LookRotation(outward);
                float height = zone.WallHeight;

                Vector3 center = mid + outward * (WallThickness / 2f) + Vector3.up * (height / 2f);
                GameObject block = Box(walls, $"Wall_{index++:000}_{zone.Name}", center, new Vector3(length + WallThickness, height, WallThickness), zone.Wall, rotation.eulerAngles);
                Part(block.transform, "Trim", PrimitiveType.Cube, new Vector3(0f, 0.5f + 0.12f / height, 0f),
                    new Vector3(1f + 0.1f / (length + WallThickness), 0.24f / height, 1.4f), zone.Trim ?? Palette.Shell);

                if (zone.Windows && length >= 6f && height >= 4.5f) AddFacadeDetails(details, wall, mid, outward, length, zone, random);
            }
        }

        /// <summary>Porthole windows and the odd round door on the walkable face, so walls read as buildings.</summary>
        static void AddFacadeDetails(Transform parent, MapLayout.Wall wall, Vector3 mid, Vector3 outward, float length, MapLayout.Zone zone, System.Random random)
        {
            var start = new Vector3(wall.Start.x, 0f, wall.Start.y);
            var end = new Vector3(wall.End.x, 0f, wall.End.y);
            Vector3 inward = -outward;
            int windows = Mathf.FloorToInt((length - 2f) / 4.5f);
            int doorSlot = length >= 8f && random.NextDouble() < 0.45 ? random.Next(windows) : -1;

            for (int k = 0; k < windows; k++)
            {
                Vector3 point = Vector3.Lerp(start, end, (k + 1f) / (windows + 1f)) + inward * 0.02f;
                if (k == doorSlot)
                {
                    Quaternion facing = Quaternion.LookRotation(inward);
                    Part(parent, "Door", PrimitiveType.Cylinder, point + Vector3.up * (zone.FloorHeight + 1.15f), new Vector3(1.3f, 0.04f, 2.3f),
                        Palette.Wood, (facing * Quaternion.Euler(90f, 0f, 0f)).eulerAngles, castShadows: false);
                }
                else
                {
                    Porthole(parent, point + Vector3.up * (zone.FloorHeight + 2.7f), inward, 1f);
                }
            }
        }

        // ------------------------------------------------------------------ Height changes

        static void BuildElevation(Transform parent)
        {
            Transform elevation = Group(parent, "Elevation");

            // Raised depot and the stairs down into the street.
            Platform(elevation, "DepotPlatform", 26f, 88f, 48f, 110f, SpawnPlatformHeight, Palette.Orange);
            Steps(elevation, "DepotStairs", new Vector3(37f, 0f, 84f), 0f, 18f, 8, 0.25f, 0.5f, Palette.Shell);

            // Bandstand on the west side of the square: height to look over the fountain.
            Platform(elevation, "Bandstand", 25f, 42f, 30f, 48f, 1.2f, Palette.Coral);
            Steps(elevation, "BandstandStairs", new Vector3(32f, 0f, 45f), 270f, 4f, 4, 0.3f, 0.5f, Palette.Shell);

            // Diner patio in the middle of A: the default plant spot, reachable by two short stairs.
            Platform(elevation, "Patio", 8f, 14f, 16f, 22f, 0.8f, Palette.Wood);
            Steps(elevation, "PatioStairsNorth", new Vector3(12f, 0f, 24f), 180f, 4f, 4, 0.2f, 0.5f, Palette.Shell);
            Steps(elevation, "PatioStairsEast", new Vector3(18f, 0f, 18f), 270f, 3f, 4, 0.2f, 0.5f, Palette.Shell);
        }

        // ------------------------------------------------------------------ Cover

        static void BuildCover(Transform parent)
        {
            Transform cover = Group(parent, "Cover");
            var random = new System.Random(7);

            // Depot: a parked bus along the back wall.
            Bus(cover, new Vector3(37f, SpawnPlatformHeight, 107.6f), 90f, 10f, Palette.Pineapple);

            // Street: parked boat-cars break up the long east-west sightline.
            BoatCar(cover, new Vector3(23f, 0f, 78f), 0f, Palette.Coral);
            BoatCar(cover, new Vector3(45f, 0f, 74.5f), 8f, Palette.SeaGlass);
            BoatCar(cover, new Vector3(59f, 0f, 79f), -6f, Palette.Anemone);
            Planter(cover, new Vector3(29.5f, 0f, 70f), new Vector3(1.6f, 1f, 1.6f), random);
            Planter(cover, new Vector3(40.5f, 0f, 70f), new Vector3(1.6f, 1f, 1.6f), random);
            Bench(cover, new Vector3(50f, 0f, 83.2f), 180f);
            // Arms point away from the wall each lamp stands against; lamps stay clear of the lane openings.
            foreach (Vector3 lamp in new[] { new Vector3(20f, 0f, 83.3f), new Vector3(53f, 0f, 83.3f), new Vector3(50.5f, 0f, 68.7f), new Vector3(27.5f, 0f, 68.7f) })
                LampPost(cover, lamp, lamp.z > 76f ? 90f : 270f);

            // Kelp Alley.
            CrateStack(cover, new Vector3(24.3f, 0f, 61f), 90f);
            Barrel(cover, new Vector3(5f, 0f, 54.8f), Palette.SeaGlass);
            Barrel(cover, new Vector3(6f, 0f, 55f), Palette.Coral);
            Planter(cover, new Vector3(13f, 0f, 52f), new Vector3(2f, 1f, 2f), random);
            Crate(cover, new Vector3(8.6f, 0f, 40f), 10f);
            LowWall(cover, new Vector3(18.5f, 0f, 38f), 3f, 0f, Palette.Kelp);

            // Town square.
            Fountain(cover, MidCenter + new Vector3(1f, 0f, 5f));
            Planter(cover, new Vector3(34.5f, 0f, 62f), new Vector3(2f, 1f, 1.4f), random);
            Bench(cover, new Vector3(41f, 0f, 35.2f), 0f);
            Crate(cover, new Vector3(48.4f, 0f, 47.2f), 20f);
            Crate(cover, new Vector3(46f, 0f, 33f), 0f);

            // Coral Boulevard.
            MarketStall(cover, new Vector3(57f, 0f, 64.5f), 180f, Palette.Coral, Palette.Shell);
            // Parked along the east kerb, clear of the connector from the square at z 44-50.
            Bus(cover, new Vector3(70.6f, 0f, 38f), 0f, 10f, Palette.SeaGlass);
            Planter(cover, new Vector3(63.2f, 0f, 34f), new Vector3(1.6f, 1f, 1.6f), random);
            Barrel(cover, new Vector3(70f, 0f, 53f), Palette.Pineapple);

            // Site B: market stalls, crates and a giant clam in the middle.
            MarketStall(cover, new Vector3(51f, 0f, 19f), 180f, Palette.SeaGlass, Palette.Shell);
            MarketStall(cover, new Vector3(67f, 0f, 8f), 90f, Palette.Anemone, Palette.Shell);
            CrateStack(cover, new Vector3(58f, 0f, 21.5f), 0f);
            Crate(cover, new Vector3(47f, 0f, 6f), 15f);
            Crate(cover, new Vector3(71.5f, 0f, 21f), 0f);
            GiantClam(cover, SiteBCenter + new Vector3(2f, 0f, -2f), 200f);

            // Site A: diner tables on the patio, crates around the doors.
            PatioTable(cover, new Vector3(10f, 0.8f, 16f), Palette.Coral);
            PatioTable(cover, new Vector3(14f, 0.8f, 20.2f), Palette.SeaGlass);
            CrateStack(cover, new Vector3(4.6f, 0f, 25.4f), 90f);
            Crate(cover, new Vector3(20.2f, 0f, 10f), 0f);
            Barrel(cover, new Vector3(3.5f, 0f, 9.8f), Palette.Coral);

            // Defender side.
            Crate(cover, new Vector3(28f, 0f, 18.6f), 0f);
            Barrel(cover, new Vector3(38.6f, 0f, 18.6f), Palette.DeepBlue);
            CrateStack(cover, new Vector3(36.5f, 0f, 3.3f), 0f);
        }

        // ------------------------------------------------------------------ Look

        static void BuildLandmarks(Transform parent)
        {
            Transform landmarks = Group(parent, "Landmarks");

            // Pineapple houses peek over the walls all around town.
            PineappleHouse(landmarks, new Vector3(12.5f, 0f, 40f), 90f);       // between the two A approaches
            PineappleHouse(landmarks, new Vector3(56f, 0f, 37f), 200f, 1.1f);  // block between mid and the boulevard
            PineappleHouse(landmarks, new Vector3(8f, 0f, 80f), 120f, 1.3f);
            PineappleHouse(landmarks, new Vector3(73f, 0f, 78f), 240f, 1.2f);
            PineappleHouse(landmarks, new Vector3(18f, 0f, 102f), 160f, 1.4f);
            PineappleHouse(landmarks, new Vector3(57f, 0f, 100f), 210f, 1.2f);
            PineappleHouse(landmarks, new Vector3(-6f, 0f, 46f), 80f, 1.5f);
            PineappleHouse(landmarks, new Vector3(82f, 0f, 20f), 280f, 1.4f);

            BurgerSign(landmarks, new Vector3(-4f, 0f, 20f), 1.2f);
            Lighthouse(landmarks, new Vector3(35f, 0f, -9f));
        }

        static void BuildAmbience(Transform parent)
        {
            Transform ambience = Group(parent, "Ambience");
            var random = new System.Random(3);

            for (int i = 0; i < 14; i++)
            {
                var center = new Vector3(Range(random, -20f, 95f), Range(random, 42f, 62f), Range(random, -20f, 125f));
                SkyFlower(ambience, center, Range(random, 6f, 12f), Palette.SkyFlowers[i % Palette.SkyFlowers.Length]);
            }

            Vector3[] jellyfish =
            {
                new Vector3(36f, 9f, 46f), new Vector3(41f, 10f, 76f), new Vector3(12f, 8f, 52f), new Vector3(67f, 9f, 44f),
                new Vector3(58f, 10f, 14f), new Vector3(12f, 9f, 18f), new Vector3(28f, 11f, 74f),
            };
            foreach (Vector3 position in jellyfish) Jellyfish(ambience, position, Range(random, 0.8f, 1.3f));

            // Kelp and coral against walls where they don't block paths.
            foreach (Vector3 spot in new[] { new Vector3(4.6f, 0f, 36f), new Vector3(4.6f, 0f, 44f), new Vector3(21.5f, 0f, 34f), new Vector3(71.5f, 0f, 50f), new Vector3(71.5f, 0f, 55f) })
                Kelp(ambience, spot, random);
            foreach (Vector3 spot in new[] { new Vector3(2.8f, 0f, 20f), new Vector3(73f, 0f, 3f), new Vector3(16.8f, 0f, 66f), new Vector3(65f, 0f, 83f), new Vector3(25f, 0f, 49f) })
                Coral(ambience, spot, random, 0.8f);
        }

        /// <summary>Area names painted on walls so callouts are easy to learn.</summary>
        static void BuildCallouts(Transform parent)
        {
            Transform callouts = Group(parent, "Callouts");
            Label(callouts, "MAIN STREET", new Vector3(24f, 4.8f, 83.94f));
            Label(callouts, "BUS DEPOT", new Vector3(37f, 5f, 96.06f), 180f);
            // Lane names on the street's south wall next to each opening, readable from the street.
            Label(callouts, "KELP ALLEY", new Vector3(28.5f, 4.2f, 68.06f), 180f);
            Label(callouts, "TOWN SQUARE", new Vector3(42.5f, 4.2f, 68.06f), 180f);
            Label(callouts, "CORAL BLVD", new Vector3(49.5f, 3.4f, 68.06f), 180f);
            Label(callouts, "SEASHELL MARKET  B", new Vector3(58f, 4.2f, 2.06f), 180f);
            Label(callouts, "BARNACLE BURGER  A", new Vector3(12f, 4.2f, 8.06f), 180f);
            Label(callouts, "LIGHTHOUSE PIER", new Vector3(35f, 3.6f, 2.06f), 180f);
        }

        // ------------------------------------------------------------------ Gameplay markers

        static MapDefinition BuildGameplay(GameObject mapRoot, Transform parent)
        {
            SpawnArea attackers = CreateSpawn(parent, "Spawn_Attackers", Team.Attackers, 180f,
                new Vector3(30f, SpawnPlatformHeight, 101f), new Vector3(33.5f, SpawnPlatformHeight, 103f), new Vector3(37f, SpawnPlatformHeight, 100.5f),
                new Vector3(40.5f, SpawnPlatformHeight, 103f), new Vector3(44f, SpawnPlatformHeight, 101f));
            SpawnArea defenders = CreateSpawn(parent, "Spawn_Defenders", Team.Defenders, 0f,
                new Vector3(32f, 0f, 4.5f), new Vector3(35f, 0f, 6f), new Vector3(38f, 0f, 4.5f), new Vector3(33f, 0f, 10f), new Vector3(37.5f, 0f, 10f));

            BombSite siteA = CreateSite(parent, "A", SiteACenter, new Vector3(12f, 3f, 12f));
            BombSite siteB = CreateSite(parent, "B", SiteBCenter, new Vector3(16f, 3f, 14f));

            var map = mapRoot.AddComponent<MapDefinition>();
            map.Configure(MapName, attackers, defenders, new[] { siteA, siteB });
            return map;
        }

        static SpawnArea CreateSpawn(Transform parent, string name, Team team, float yaw, params Vector3[] points)
        {
            Transform root = Group(parent, name);
            var area = root.gameObject.AddComponent<SpawnArea>();
            area.Configure(team);
            for (int i = 0; i < points.Length; i++)
            {
                Transform point = Group(root, $"Point_{i + 1}");
                point.SetPositionAndRotation(points[i], Quaternion.Euler(0f, yaw, 0f));
            }
            return area;
        }

        static BombSite CreateSite(Transform parent, string siteName, Vector3 center, Vector3 size)
        {
            Transform root = Group(parent, $"BombSite_{siteName}");
            root.position = center;
            var site = root.gameObject.AddComponent<BombSite>();
            site.Configure(siteName, size);

            // A big painted letter on the floor, readable from the attackers' side.
            float floorY = siteName == "A" ? 0.86f : 0.06f;
            var letter = new GameObject($"FloorLetter_{siteName}");
            letter.transform.SetParent(root, false);
            letter.transform.SetPositionAndRotation(new Vector3(center.x, floorY, center.z), Quaternion.Euler(90f, 180f, 0f));
            var text = letter.AddComponent<TextMesh>();
            text.text = siteName;
            text.font = Palette.LabelFont;
            text.fontSize = 64;
            text.characterSize = 0.7f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.95f, 0.3f, 0.32f);
            letter.GetComponent<MeshRenderer>().sharedMaterial = Palette.WorldText;
            return site;
        }

        /// <summary>
        /// Where bots go. Defenders hold angles watching the doors attackers come through; attackers pass
        /// an approach point on their chosen lane, then hold inside the site. Yaw 0 faces north.
        /// </summary>
        static void BuildTacticalPoints(Transform parent)
        {
            Transform root = Group(parent, "TacticalPoints");
            void Point(TacticalRole role, string site, float x, float y, float z, float yaw)
            {
                Transform point = Group(root, $"{role}_{site}_{root.childCount + 1:00}");
                point.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f));
                point.gameObject.AddComponent<TacticalPoint>().Configure(role, site);
            }

            // Defenders.
            Point(TacticalRole.DefenderHold, "A", 12f, 0.8f, 17f, 0f);     // patio, watching both north doors
            Point(TacticalRole.DefenderHold, "A", 3.5f, 0f, 13f, 35f);     // back corner
            Point(TacticalRole.DefenderHold, "A", 20.5f, 0f, 24f, 330f);   // close angle on the garden door
            Point(TacticalRole.DefenderHold, "B", 52f, 0f, 9f, 10f);       // deep, sees both B doors
            Point(TacticalRole.DefenderHold, "B", 68f, 0f, 14f, 330f);     // on B long
            Point(TacticalRole.DefenderHold, "B", 47.5f, 0f, 20f, 0f);     // tight on B short
            Point(TacticalRole.DefenderHold, "B", 62f, 0f, 4f, 350f);      // back of the market
            Point(TacticalRole.DefenderHold, "Mid", 27.5f, 1.2f, 45f, 40f); // bandstand over the square
            Point(TacticalRole.DefenderHold, "Mid", 33f, 0f, 30f, 0f);     // top of the south corridor
            Point(TacticalRole.DefenderHold, "Mid", 46f, 0f, 36f, 320f);   // B short looking into mid

            // Attackers: lane approaches, then spots inside each site.
            Point(TacticalRole.AttackerApproach, "A", 7f, 0f, 40f, 180f);     // Kelp Alley, outer
            Point(TacticalRole.AttackerApproach, "A", 18f, 0f, 40f, 180f);    // Kelp Alley, garden
            Point(TacticalRole.AttackerApproach, "A", 33f, 0f, 25f, 180f);    // through mid to A short
            Point(TacticalRole.AttackerApproach, "B", 67f, 0f, 40f, 180f);    // Coral Boulevard
            Point(TacticalRole.AttackerApproach, "B", 47f, 0f, 33f, 180f);    // B short
            Point(TacticalRole.AttackerApproach, "B", 34f, 0f, 25f, 180f);    // through mid to the pier door
            Point(TacticalRole.AttackerSiteHold, "A", 12f, 0.8f, 19f, 180f);
            Point(TacticalRole.AttackerSiteHold, "A", 5f, 0f, 12f, 90f);      // watching A short
            Point(TacticalRole.AttackerSiteHold, "A", 19f, 0f, 26f, 180f);
            Point(TacticalRole.AttackerSiteHold, "B", 58f, 0f, 16f, 180f);
            Point(TacticalRole.AttackerSiteHold, "B", 50f, 0f, 6f, 270f);     // watching the pier door
            Point(TacticalRole.AttackerSiteHold, "B", 70f, 0f, 22f, 225f);
        }

        static BotSquadSpawner BuildBots(Transform parent, MapDefinition map)
        {
            var spawner = Group(parent, "Bots").gameObject.AddComponent<BotSquadSpawner>();
            var botPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterBuilder.BotPrefabPath);
            Edit(spawner, so =>
            {
                so.Require("map").objectReferenceValue = map;
                so.Require("botPrefab").objectReferenceValue = botPrefab.GetComponent<BotController>();
                so.Require("difficulty").objectReferenceValue = CharacterBuilder.Normal;
                so.Require("difficultyLevels").SetObjects(CharacterBuilder.Easy, CharacterBuilder.Normal, CharacterBuilder.Hard);
                so.Require("attackerSkins").SetObjects(CharacterBuilder.AttackerSkins);
                so.Require("defenderSkins").SetObjects(CharacterBuilder.DefenderSkins);
                so.Require("primaryWeapons").SetObjects(
                    WeaponContentBuilder.Load(WeaponContentBuilder.ReefRipper),
                    WeaponContentBuilder.Load(WeaponContentBuilder.BubbleBuzzer),
                    WeaponContentBuilder.Load(WeaponContentBuilder.ShellShocker),
                    WeaponContentBuilder.Load(WeaponContentBuilder.HarpoonLongshot));
                SerializedProperty weights = so.Require("primaryWeights");
                float[] values = { 5f, 3f, 1.5f, 1f };
                weights.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++) weights.GetArrayElementAtIndex(i).floatValue = values[i];
                so.Require("secondaryWeapon").objectReferenceValue = WeaponContentBuilder.Load(WeaponContentBuilder.ClamPopper);
            });
            return spawner;
        }

        /// <summary>
        /// The team deathmatch: a scene-placed network object running the rules on the server, its
        /// screens, and a camera that overlooks the map while you pick a loadout.
        /// </summary>
        static NetworkObject BuildMatch(MapDefinition map, BotSquadSpawner spawner, GameObject playerPrefab)
        {
            var root = new GameObject("Match");
            var networkObject = root.AddComponent<NetworkObject>();
            var match = root.AddComponent<TdmMatch>();
            root.AddComponent<TdmMatchUI>();

            var cameraObject = new GameObject("SpectatorCamera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.SetPositionAndRotation(new Vector3(37f, 34f, 82f), Quaternion.Euler(42f, 180f, 0f));
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlayerPrefabBuilder.WaterColor;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cameraObject.AddComponent<AudioListener>();

            Edit(match, so =>
            {
                so.Require("map").objectReferenceValue = map;
                so.Require("botSpawner").objectReferenceValue = spawner;
                so.Require("playerPrefab").objectReferenceValue = playerPrefab.GetComponent<NetworkObject>();
                so.Require("spectatorCamera").objectReferenceValue = cameraObject;
                so.Require("primaries").SetObjects(
                    WeaponContentBuilder.Load(WeaponContentBuilder.ReefRipper),
                    WeaponContentBuilder.Load(WeaponContentBuilder.BubbleBuzzer),
                    WeaponContentBuilder.Load(WeaponContentBuilder.ShellShocker),
                    WeaponContentBuilder.Load(WeaponContentBuilder.HarpoonLongshot));
                so.Require("secondaries").SetObjects(WeaponContentBuilder.Load(WeaponContentBuilder.ClamPopper));
                so.Require("melees").SetObjects(
                    WeaponContentBuilder.Load(WeaponContentBuilder.Balisong),
                    WeaponContentBuilder.Load(WeaponContentBuilder.Spatula));
            });
            return networkObject;
        }

        // ------------------------------------------------------------------ Minimap

        const float MinimapPixelsPerMetre = 4f;
        const string MinimapTexturePath = BuildPaths.UITextures + "/T_Minimap_CoralPlaza.png";

        /// <summary>
        /// Draws the radar image from the floor plan and the built colliders: floors (lighter when
        /// raised), cover, walls, with tints for both spawns and the two sites.
        /// </summary>
        static void BakeMinimap(MapLayout layout, GameObject mapRoot)
        {
            Rect area = Rect.zero;
            bool first = true;
            foreach (MapLayout.Zone zone in layout.Zones)
            foreach (MapLayout.Shape shape in zone.ShapeList)
            {
                area = first ? shape.Bounds : Rect.MinMaxRect(Mathf.Min(area.xMin, shape.Bounds.xMin), Mathf.Min(area.yMin, shape.Bounds.yMin),
                    Mathf.Max(area.xMax, shape.Bounds.xMax), Mathf.Max(area.yMax, shape.Bounds.yMax));
                first = false;
            }
            area = Rect.MinMaxRect(area.xMin - 3f, area.yMin - 3f, area.xMax + 3f, area.yMax + 3f);
            int width = Mathf.CeilToInt(area.width * MinimapPixelsPerMetre);
            int height = Mathf.CeilToInt(area.height * MinimapPixelsPerMetre);

            Physics.SyncTransforms();
            int mask = GameLayers.DefaultMaskExcluding(GameLayers.Player, GameLayers.Bot, GameLayers.Hitbox, GameLayers.Viewmodel) & ~(1 << 2);
            var outside = new Color32(10, 26, 40, 225);
            var wall = new Color32(190, 222, 230, 255);
            var cover = new Color32(26, 50, 68, 245);
            var walkable = new bool[width * height];
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var point = new Vector2(area.xMin + (x + 0.5f) / MinimapPixelsPerMetre, area.yMin + (y + 0.5f) / MinimapPixelsPerMetre);
                    int index = y * width + x;
                    if (!layout.IsWalkable(point))
                    {
                        pixels[index] = outside;
                        continue;
                    }
                    walkable[index] = true;
                    MapLayout.Zone zone = layout.ZoneAt(point);
                    float floor = FloorHeightAt(point, zone != null ? zone.FloorHeight : 0f, mask);
                    bool blocked = Physics.CheckBox(new Vector3(point.x, floor + 1.1f, point.y), new Vector3(0.1f, 0.45f, 0.1f), Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
                    pixels[index] = blocked ? cover : FloorColour(zone, floor);
                }
            }

            // Walls: the first unwalkable pixel next to a walkable one.
            var edged = (Color32[])pixels.Clone();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (walkable[index]) continue;
                bool edge = (x > 0 && walkable[index - 1]) || (x < width - 1 && walkable[index + 1])
                    || (y > 0 && walkable[index - width]) || (y < height - 1 && walkable[index + width]);
                if (edge) edged[index] = wall;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(edged);
            texture.Apply();
            ProjectSetup.EnsureFolder(BuildPaths.UITextures);
            System.IO.File.WriteAllBytes(MinimapTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(MinimapTexturePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(MinimapTexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();

            var data = mapRoot.AddComponent<MinimapData>();
            data.Configure(AssetDatabase.LoadAssetAtPath<Texture2D>(MinimapTexturePath), area.min, area.size);
        }

        /// <summary>The highest surface under a point that's still at floor level (stairs and platforms included).</summary>
        static float FloorHeightAt(Vector2 point, float zoneFloor, int mask)
        {
            float best = zoneFloor;
            bool found = false;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(point.x, 30f, point.y), Vector3.down, 40f, mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.point.y > zoneFloor + 2.4f) continue;
                if (!found || hit.point.y > best) best = hit.point.y;
                found = true;
            }
            return best;
        }

        static Color32 FloorColour(MapLayout.Zone zone, float floor)
        {
            var colour = new Color(0.36f, 0.52f, 0.6f);
            string name = zone != null ? zone.Name : "";
            if (name.Contains("Depot")) colour = Color.Lerp(colour, TeamColors.Attackers, 0.35f);
            else if (name.Contains("Pier")) colour = Color.Lerp(colour, TeamColors.Defenders, 0.35f);
            else if (name.Contains("Burger") || name.Contains("Market")) colour = Color.Lerp(colour, new Color(0.95f, 0.75f, 0.45f), 0.3f);
            // Higher ground reads lighter.
            colour = Color.Lerp(colour, Color.white, Mathf.Clamp01(floor / 3f) * 0.35f);
            colour.a = 0.95f;
            return colour;
        }

        // ------------------------------------------------------------------ Navigation

        static void BakeNavMesh(GameObject geometry)
        {
            var surface = geometry.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            // Everything but characters and viewmodels; this includes Ignore Raycast, where the stair ramps live.
            int excluded = 0;
            foreach (string layerName in new[] { GameLayers.Player, GameLayers.Bot, GameLayers.Hitbox, GameLayers.Viewmodel })
                excluded |= 1 << LayerMask.NameToLayer(layerName);
            surface.layerMask = ~excluded;
            surface.BuildNavMesh();

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath) != null) AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
        }

        /// <summary>Walking distances and times between key points, measured on the baked NavMesh.</summary>
        public static string RouteReport(MapDefinition map)
        {
            Vector3 attackers = map.SpawnFor(Team.Attackers).Points[2].position;
            Vector3 defenders = map.SpawnFor(Team.Defenders).Points[1].position;
            Vector3 siteA = map.BombSites[0].transform.position;
            Vector3 siteB = map.BombSites[1].transform.position;
            (string name, Vector3 from, Vector3 to)[] routes =
            {
                ("Attackers -> A", attackers, siteA),
                ("Attackers -> B", attackers, siteB),
                ("Attackers -> Mid", attackers, MidCenter),
                ("Defenders -> A", defenders, siteA),
                ("Defenders -> B", defenders, siteB),
                ("Defenders -> Mid", defenders, MidCenter),
                ("Rotate A -> B", siteA, siteB),
            };

            const float walkSpeed = 4.8f;
            var report = new StringBuilder("Routes on the NavMesh (walking at 4.8 m/s):\n");
            var path = new NavMeshPath();
            foreach ((string name, Vector3 from, Vector3 to) in routes)
            {
                bool ok = NavMesh.SamplePosition(from, out NavMeshHit start, 3f, NavMesh.AllAreas)
                    & NavMesh.SamplePosition(to, out NavMeshHit end, 3f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete;
                if (!ok)
                {
                    report.AppendLine($"  {name}: NO PATH ({path.status})");
                    continue;
                }
                float length = 0f;
                for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                report.AppendLine($"  {name}: {length:0} m, {length / walkSpeed:0.0} s");
            }
            return report.ToString();
        }
    }
}
