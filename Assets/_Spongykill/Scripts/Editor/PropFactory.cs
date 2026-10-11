using Spongykill.Effects;
using UnityEngine;
using static Spongykill.EditorTools.BuilderUtility;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// Primitive-built Bikini Bottom-style props. Cover props get one box collider on their root and
    /// collider-less visual children, which keeps collision and the NavMesh simple. Landmarks and
    /// decorations have no colliders at all. Positions are the prop's base on the floor.
    /// </summary>
    public static class PropFactory
    {
        public static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        static Transform SolidRoot(Transform parent, string name, Vector3 basePosition, float yaw, Vector3 colliderSize)
        {
            Transform root = Group(parent, name);
            root.SetLocalPositionAndRotation(basePosition, Quaternion.Euler(0f, yaw, 0f));
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.size = colliderSize;
            box.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);
            return root;
        }

        static Transform DecorRoot(Transform parent, string name, Vector3 basePosition, float yaw, float scale = 1f)
        {
            Transform root = Group(parent, name);
            root.SetLocalPositionAndRotation(basePosition, Quaternion.Euler(0f, yaw, 0f));
            root.localScale = Vector3.one * scale;
            return root;
        }

        // ------------------------------------------------------------------ Structure

        /// <summary>
        /// Solid stairs climbing along the local +Z of <paramref name="yaw"/>, starting at <paramref name="bottomFront"/>,
        /// with an invisible ramp across the step noses. The ramp gives smooth movement and a continuous
        /// NavMesh (stepped geometry can fail to stitch across NavMesh tile seams). It sits on the Ignore
        /// Raycast layer so shots pass straight through it.
        /// </summary>
        public static void Steps(Transform parent, string name, Vector3 bottomFront, float yaw, float width, int steps, float rise, float depth, Material material)
        {
            Transform group = Group(parent, name);
            group.SetLocalPositionAndRotation(bottomFront, Quaternion.Euler(0f, yaw, 0f));
            for (int i = 0; i < steps; i++)
            {
                float height = (i + 1) * rise;
                Box(group, $"Step_{i + 1:00}", new Vector3(0f, height / 2f, (i + 0.5f) * depth), new Vector3(width, height, depth), material);
            }

            // The ramp surface runs through every step nose: y = rise * (z / depth + 1), from the floor at
            // z = -depth up to the top step's nose at z = (steps - 1) * depth.
            const float thickness = 0.2f;
            float run = steps * depth;
            float totalRise = steps * rise;
            float angle = Mathf.Atan2(totalRise, run);
            var surfaceMid = new Vector3(0f, totalRise / 2f, (steps - 2) * depth / 2f);
            var normal = new Vector3(0f, Mathf.Cos(angle), -Mathf.Sin(angle));
            var ramp = new GameObject("NavRamp");
            ramp.transform.SetParent(group, false);
            ramp.transform.SetLocalPositionAndRotation(surfaceMid - normal * (thickness / 2f), Quaternion.Euler(-angle * Mathf.Rad2Deg, 0f, 0f));
            ramp.layer = 2; // Ignore Raycast
            ramp.AddComponent<BoxCollider>().size = new Vector3(width, thickness, Mathf.Sqrt(run * run + totalRise * totalRise));
        }

        /// <summary>A solid block filling the plan rectangle from the ground up to <paramref name="top"/>.</summary>
        public static GameObject Platform(Transform parent, string name, float x0, float z0, float x1, float z1, float top, Material material)
        {
            return Box(parent, name, new Vector3((x0 + x1) / 2f, top / 2f, (z0 + z1) / 2f), new Vector3(x1 - x0, top, z1 - z0), material);
        }

        public static GameObject LowWall(Transform parent, Vector3 basePosition, float length, float yaw, Material material, float height = 1.1f)
        {
            GameObject wall = Box(parent, "LowWall", basePosition + Vector3.up * (height / 2f), new Vector3(length, height, 0.4f), material, new Vector3(0f, yaw, 0f));
            Part(wall.transform, "Cap", PrimitiveType.Cube, new Vector3(0f, 0.5f + 0.06f / height, 0f), new Vector3(1.04f, 0.12f / height, 1.4f), Palette.Shell);
            return wall;
        }

        // ------------------------------------------------------------------ Cover

        public static GameObject Crate(Transform parent, Vector3 basePosition, float yaw = 0f, float size = 1.2f)
        {
            GameObject crate = Box(parent, "Crate", basePosition + Vector3.up * (size / 2f), Vector3.one * size, Palette.Pineapple, new Vector3(0f, yaw, 0f));
            // Darker planks across the faces.
            Part(crate.transform, "Band", PrimitiveType.Cube, Vector3.zero, new Vector3(1.02f, 0.18f, 1.02f), Palette.Wood);
            return crate;
        }

        public static void CrateStack(Transform parent, Vector3 basePosition, float yaw)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Crate(parent, basePosition + rotation * new Vector3(-0.62f, 0f, 0f), yaw);
            Crate(parent, basePosition + rotation * new Vector3(0.62f, 0f, 0f), yaw);
            Crate(parent, basePosition + rotation * new Vector3(0f, 1.2f, 0f), yaw + 12f);
        }

        public static void Barrel(Transform parent, Vector3 basePosition, Material material)
        {
            Transform root = SolidRoot(parent, "Barrel", basePosition, 0f, new Vector3(0.8f, 1.1f, 0.8f));
            Part(root, "Body", PrimitiveType.Cylinder, new Vector3(0f, 0.55f, 0f), new Vector3(0.8f, 0.55f, 0.8f), material);
            Part(root, "RingTop", PrimitiveType.Cylinder, new Vector3(0f, 0.9f, 0f), new Vector3(0.84f, 0.04f, 0.84f), Palette.Metal);
            Part(root, "RingBottom", PrimitiveType.Cylinder, new Vector3(0f, 0.2f, 0f), new Vector3(0.84f, 0.04f, 0.84f), Palette.Metal);
        }

        public static void Planter(Transform parent, Vector3 basePosition, Vector3 size, System.Random random)
        {
            Transform root = SolidRoot(parent, "Planter", basePosition, 0f, size);
            Part(root, "Box", PrimitiveType.Cube, new Vector3(0f, size.y / 2f, 0f), size, Palette.Shell);
            Part(root, "Soil", PrimitiveType.Cube, new Vector3(0f, size.y, 0f), new Vector3(size.x - 0.2f, 0.06f, size.z - 0.2f), Palette.Wood);
            Coral(root, new Vector3(0f, size.y, 0f), random, 0.6f);
        }

        /// <summary>A rounded cartoon car with a glass bubble cabin and tail fins, length along local X.</summary>
        public static void BoatCar(Transform parent, Vector3 basePosition, float yaw, Material body)
        {
            Transform root = SolidRoot(parent, "BoatCar", basePosition, yaw, new Vector3(4.4f, 1.5f, 1.9f));
            Part(root, "Hull", PrimitiveType.Capsule, new Vector3(0f, 0.8f, 0f), new Vector3(1.4f, 2.2f, 1.8f), body, new Vector3(0f, 0f, 90f));
            Part(root, "Cabin", PrimitiveType.Sphere, new Vector3(-0.2f, 1.45f, 0f), new Vector3(1.9f, 1.1f, 1.45f), Palette.Glass);
            Part(root, "Fin", PrimitiveType.Cube, new Vector3(-1.9f, 1.45f, 0f), new Vector3(0.7f, 0.9f, 0.12f), body, new Vector3(0f, 0f, -30f));
            Part(root, "Stripe", PrimitiveType.Cube, new Vector3(0.3f, 0.8f, 0f), new Vector3(3f, 0.16f, 1.84f), Palette.Shell);
            foreach (float x in new[] { -1.3f, 1.3f })
                foreach (float z in new[] { -0.85f, 0.85f })
                    Part(root, "Wheel", PrimitiveType.Cylinder, new Vector3(x, 0.35f, z), new Vector3(0.62f, 0.1f, 0.62f), Palette.Ink, new Vector3(90f, 0f, 0f));
        }

        /// <summary>A boxy bus, length along local Z.</summary>
        public static void Bus(Transform parent, Vector3 basePosition, float yaw, float length, Material body)
        {
            Transform root = SolidRoot(parent, "Bus", basePosition, yaw, new Vector3(2.6f, 3.1f, length));
            Part(root, "Body", PrimitiveType.Cube, new Vector3(0f, 1.7f, 0f), new Vector3(2.6f, 2.6f, length), body);
            Part(root, "Roof", PrimitiveType.Capsule, new Vector3(0f, 3.0f, 0f), new Vector3(2.4f, length * 0.5f, 0.5f), Palette.Shell, new Vector3(90f, 0f, 0f));
            foreach (float side in new[] { -1f, 1f })
            {
                Part(root, "Windows", PrimitiveType.Cube, new Vector3(1.31f * side, 2.15f, 0.3f), new Vector3(0.04f, 0.9f, length - 2f), Palette.Glass);
                foreach (float z in new[] { -1f, 1f })
                    Part(root, "Wheel", PrimitiveType.Cylinder, new Vector3(1.2f * side, 0.45f, z * (length / 2f - 1.6f)), new Vector3(0.9f, 0.12f, 0.9f), Palette.Ink, new Vector3(0f, 0f, 90f));
            }
            Part(root, "Windshield", PrimitiveType.Cube, new Vector3(0f, 2.15f, length / 2f + 0.01f), new Vector3(2.2f, 1f, 0.04f), Palette.Glass);
        }

        public static void MarketStall(Transform parent, Vector3 basePosition, float yaw, Material stripeA, Material stripeB)
        {
            Transform root = SolidRoot(parent, "MarketStall", basePosition, yaw, new Vector3(2.6f, 1.1f, 1.2f));
            Part(root, "Counter", PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(2.6f, 1.1f, 1.2f), Palette.Wood);
            foreach (float x in new[] { -1.2f, 1.2f })
                foreach (float z in new[] { -0.5f, 0.5f })
                    Part(root, "Post", PrimitiveType.Cylinder, new Vector3(x, 1.4f, z), new Vector3(0.1f, 1.4f, 0.1f), Palette.Shell);
            for (int i = 0; i < 5; i++)
                Part(root, $"Awning_{i}", PrimitiveType.Cube, new Vector3(-1.12f + i * 0.56f, 2.85f, 0f), new Vector3(0.56f, 0.08f, 1.7f), i % 2 == 0 ? stripeA : stripeB, new Vector3(-12f, 0f, 0f));
            Part(root, "Goods_1", PrimitiveType.Sphere, new Vector3(-0.6f, 1.25f, 0f), Vector3.one * 0.32f, Palette.Coral);
            Part(root, "Goods_2", PrimitiveType.Sphere, new Vector3(0.1f, 1.25f, 0.1f), Vector3.one * 0.3f, Palette.Pineapple);
            Part(root, "Goods_3", PrimitiveType.Sphere, new Vector3(0.7f, 1.25f, -0.1f), Vector3.one * 0.28f, Palette.Anemone);
        }

        public static void Fountain(Transform parent, Vector3 basePosition)
        {
            Transform root = Group(parent, "Fountain");
            root.localPosition = basePosition;
            GameObject basin = Part(root, "Basin", PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), new Vector3(5f, 0.45f, 5f), Palette.Shell);
            AddConvexMeshCollider(basin);
            Part(root, "Water", PrimitiveType.Cylinder, new Vector3(0f, 0.85f, 0f), new Vector3(4.4f, 0.04f, 4.4f), Palette.Glass);
            GameObject column = Part(root, "Column", PrimitiveType.Cylinder, new Vector3(0f, 1.6f, 0f), new Vector3(0.9f, 1.6f, 0.9f), Palette.Rock);
            AddConvexMeshCollider(column);
            Part(root, "Shell", PrimitiveType.Sphere, new Vector3(0f, 3.3f, 0f), new Vector3(1.6f, 0.9f, 1.6f), Palette.Anemone);
            Part(root, "Pearl", PrimitiveType.Sphere, new Vector3(0f, 3.85f, 0f), Vector3.one * 0.55f, Palette.Glove);
        }

        public static void PatioTable(Transform parent, Vector3 basePosition, Material umbrella)
        {
            Transform root = SolidRoot(parent, "PatioTable", basePosition, 0f, new Vector3(1.2f, 0.8f, 1.2f));
            Part(root, "Top", PrimitiveType.Cylinder, new Vector3(0f, 0.78f, 0f), new Vector3(1.3f, 0.04f, 1.3f), Palette.Shell);
            Part(root, "Leg", PrimitiveType.Cylinder, new Vector3(0f, 0.38f, 0f), new Vector3(0.12f, 0.38f, 0.12f), Palette.Metal);
            Part(root, "Pole", PrimitiveType.Cylinder, new Vector3(0f, 1.55f, 0f), new Vector3(0.07f, 0.75f, 0.07f), Palette.Metal);
            Part(root, "Canopy", PrimitiveType.Sphere, new Vector3(0f, 2.3f, 0f), new Vector3(2.4f, 0.5f, 2.4f), umbrella);
        }

        public static void Bench(Transform parent, Vector3 basePosition, float yaw)
        {
            Transform root = SolidRoot(parent, "Bench", basePosition, yaw, new Vector3(2f, 0.5f, 0.6f));
            Part(root, "Seat", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), new Vector3(2f, 0.1f, 0.6f), Palette.Wood);
            Part(root, "Back", PrimitiveType.Cube, new Vector3(0f, 0.8f, -0.27f), new Vector3(2f, 0.5f, 0.08f), Palette.Wood);
            foreach (float x in new[] { -0.85f, 0.85f })
                Part(root, "Leg", PrimitiveType.Cube, new Vector3(x, 0.2f, 0f), new Vector3(0.1f, 0.4f, 0.5f), Palette.Metal);
        }

        public static void LampPost(Transform parent, Vector3 basePosition, float yaw = 0f)
        {
            Transform root = SolidRoot(parent, "LampPost", basePosition, yaw, new Vector3(0.3f, 3.6f, 0.3f));
            Part(root, "Pole", PrimitiveType.Cylinder, new Vector3(0f, 1.8f, 0f), new Vector3(0.18f, 1.8f, 0.18f), Palette.Coral);
            Part(root, "Arm", PrimitiveType.Cube, new Vector3(0.35f, 3.55f, 0f), new Vector3(0.75f, 0.12f, 0.12f), Palette.Coral);
            Part(root, "Lamp", PrimitiveType.Sphere, new Vector3(0.7f, 3.3f, 0f), Vector3.one * 0.5f, Palette.LampGlow, castShadows: false);
        }

        public static void GiantClam(Transform parent, Vector3 basePosition, float yaw)
        {
            Transform root = SolidRoot(parent, "GiantClam", basePosition, yaw, new Vector3(2.8f, 1.6f, 2.4f));
            Part(root, "LowerShell", PrimitiveType.Sphere, new Vector3(0f, 0.5f, 0f), new Vector3(3f, 1f, 2.6f), Palette.Anemone);
            Part(root, "UpperShell", PrimitiveType.Sphere, new Vector3(0f, 1.15f, -0.5f), new Vector3(2.9f, 0.9f, 2.4f), Palette.Anemone, new Vector3(-28f, 0f, 0f));
            Part(root, "Pearl", PrimitiveType.Sphere, new Vector3(0f, 1.05f, 0.4f), Vector3.one * 0.7f, Palette.Glove);
        }

        // ------------------------------------------------------------------ Landmarks (visual only)

        /// <summary>A pineapple-shaped house: lattice skin, a leafy crown, a round door and porthole windows.</summary>
        public static void PineappleHouse(Transform parent, Vector3 basePosition, float yaw, float scale = 1f)
        {
            Transform root = DecorRoot(parent, "PineappleHouse", basePosition, yaw, scale);
            const float radius = 2.3f;
            const float halfHeight = 3.4f;
            Part(root, "Body", PrimitiveType.Sphere, new Vector3(0f, halfHeight, 0f), new Vector3(radius * 2f, halfHeight * 2f, radius * 2f), Palette.PineappleSkin);

            for (int i = 0; i < 9; i++)
            {
                float tilt = i == 0 ? 0f : (i % 2 == 0 ? 40f : 24f);
                Quaternion rotation = Quaternion.Euler(0f, i * 40f, 0f) * Quaternion.Euler(tilt, 0f, 0f);
                Vector3 crown = new Vector3(0f, halfHeight * 2f - 0.3f, 0f);
                Part(root, "Leaf", PrimitiveType.Capsule, crown + rotation * (Vector3.up * 1.5f), new Vector3(0.6f, 1.6f, 0.25f), Palette.Leaf, rotation.eulerAngles);
            }

            // Ellipsoid radius at a given height, used to sit details on the surface.
            float SurfaceRadius(float y) => radius * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((y - halfHeight) / halfHeight, 2f)));

            float doorZ = SurfaceRadius(1.1f) - 0.05f;
            Part(root, "Door", PrimitiveType.Cylinder, new Vector3(0f, 1.1f, doorZ), new Vector3(1.2f, 0.06f, 2.1f), Palette.DeepBlue, new Vector3(90f, 0f, 0f));
            Part(root, "DoorKnob", PrimitiveType.Sphere, new Vector3(0.35f, 1.0f, doorZ + 0.07f), Vector3.one * 0.12f, Palette.Pineapple);
            foreach ((float angle, float y) in new[] { (55f, 3.6f), (-50f, 4.7f), (160f, 3.2f) })
            {
                Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Porthole(root, outward * (SurfaceRadius(y) - 0.05f) + Vector3.up * y, outward, 0.9f);
            }
        }

        public static void Porthole(Transform parent, Vector3 position, Vector3 outward, float diameter)
        {
            Quaternion facing = Quaternion.FromToRotation(Vector3.up, outward);
            Part(parent, "PortholeFrame", PrimitiveType.Cylinder, position, new Vector3(diameter * 1.25f, 0.05f, diameter * 1.25f), Palette.Metal, facing.eulerAngles, castShadows: false);
            Part(parent, "PortholeGlass", PrimitiveType.Cylinder, position + outward * 0.04f, new Vector3(diameter, 0.05f, diameter), Palette.Glass, facing.eulerAngles, castShadows: false);
        }

        public static void Lighthouse(Transform parent, Vector3 basePosition)
        {
            Transform root = DecorRoot(parent, "Lighthouse", basePosition, 0f);
            const int bands = 8;
            const float bandHeight = 2.4f;
            for (int i = 0; i < bands; i++)
            {
                float diameter = Mathf.Lerp(5.5f, 3.4f, i / (float)(bands - 1));
                Part(root, $"Band_{i}", PrimitiveType.Cylinder, new Vector3(0f, (i + 0.5f) * bandHeight, 0f), new Vector3(diameter, bandHeight / 2f, diameter), i % 2 == 0 ? Palette.Coral : Palette.Shell);
            }
            float top = bands * bandHeight;
            Part(root, "Gallery", PrimitiveType.Cylinder, new Vector3(0f, top + 0.15f, 0f), new Vector3(4.6f, 0.15f, 4.6f), Palette.DeepBlue);
            Part(root, "LampRoom", PrimitiveType.Cylinder, new Vector3(0f, top + 1.4f, 0f), new Vector3(2.8f, 1.1f, 2.8f), Palette.Glass);
            Part(root, "Lamp", PrimitiveType.Sphere, new Vector3(0f, top + 1.4f, 0f), Vector3.one * 1.5f, Palette.LampGlow, castShadows: false);
            Part(root, "Cap", PrimitiveType.Sphere, new Vector3(0f, top + 2.7f, 0f), new Vector3(3.2f, 1.6f, 3.2f), Palette.Coral);
        }

        /// <summary>A giant burger on a pole, the sign of the A-site diner.</summary>
        public static void BurgerSign(Transform parent, Vector3 basePosition, float scale = 1f)
        {
            Transform root = DecorRoot(parent, "BurgerSign", basePosition, 0f, scale);
            Part(root, "Pole", PrimitiveType.Cylinder, new Vector3(0f, 4.5f, 0f), new Vector3(0.5f, 4.5f, 0.5f), Palette.Metal);
            const float y = 9.6f;
            Part(root, "BottomBun", PrimitiveType.Sphere, new Vector3(0f, y, 0f), new Vector3(4f, 1.1f, 4f), Palette.Bun);
            Part(root, "Patty", PrimitiveType.Cylinder, new Vector3(0f, y + 0.6f, 0f), new Vector3(4.3f, 0.3f, 4.3f), Palette.Patty);
            Part(root, "Cheese", PrimitiveType.Cube, new Vector3(0f, y + 0.95f, 0f), new Vector3(3.4f, 0.12f, 3.4f), Palette.Pineapple, new Vector3(0f, 45f, 0f));
            Part(root, "Lettuce", PrimitiveType.Cylinder, new Vector3(0f, y + 1.1f, 0f), new Vector3(4.5f, 0.1f, 4.5f), Palette.Kelp);
            Part(root, "TopBun", PrimitiveType.Sphere, new Vector3(0f, y + 1.6f, 0f), new Vector3(4f, 2.2f, 4f), Palette.Bun);
            for (int i = 0; i < 7; i++)
            {
                Vector3 seed = Quaternion.Euler(0f, i * 51f, 0f) * new Vector3(0.9f + (i % 3) * 0.3f, 0f, 0f);
                Part(root, "Sesame", PrimitiveType.Sphere, new Vector3(seed.x, y + 2.55f - (i % 3) * 0.12f, seed.z), new Vector3(0.22f, 0.1f, 0.14f), Palette.Glove);
            }
        }

        /// <summary>The flower-shaped patterns floating in the Bikini Bottom sky.</summary>
        public static void SkyFlower(Transform parent, Vector3 center, float radius, Material material)
        {
            Transform root = DecorRoot(parent, "SkyFlower", center, 0f);
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, i * 72f, 0f) * new Vector3(0f, 0f, radius * 0.55f);
                Part(root, $"Petal_{i}", PrimitiveType.Sphere, offset, new Vector3(radius * 0.75f, 0.2f, radius * 0.75f), material, castShadows: false);
            }
            Part(root, "Center", PrimitiveType.Sphere, Vector3.zero, new Vector3(radius * 0.5f, 0.25f, radius * 0.5f), material, castShadows: false);
        }

        public static void Jellyfish(Transform parent, Vector3 position, float scale)
        {
            Transform root = DecorRoot(parent, "Jellyfish", position, 0f, scale);
            root.gameObject.AddComponent<FloatingBob>();
            Part(root, "Bell", PrimitiveType.Sphere, Vector3.zero, new Vector3(1f, 0.65f, 1f), Palette.Anemone, castShadows: false);
            Part(root, "Spots", PrimitiveType.Sphere, new Vector3(0.2f, 0.18f, 0.25f), Vector3.one * 0.25f, Palette.Coral, castShadows: false);
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, i * 72f, 0f) * new Vector3(0.28f, 0f, 0f);
                Part(root, "Tentacle", PrimitiveType.Capsule, offset + Vector3.down * 0.55f, new Vector3(0.07f, 0.35f, 0.07f), Palette.Anemone, castShadows: false);
            }
        }

        // ------------------------------------------------------------------ Seabed decoration (visual only)

        public static void Kelp(Transform parent, Vector3 basePosition, System.Random random)
        {
            int strands = random.Next(2, 4);
            for (int i = 0; i < strands; i++)
            {
                float height = Range(random, 2.5f, 5f);
                var offset = new Vector3(Range(random, -0.4f, 0.4f), height / 2f, Range(random, -0.4f, 0.4f));
                var tilt = new Vector3(Range(random, -8f, 8f), 0f, Range(random, -8f, 8f));
                Part(parent, "Kelp", PrimitiveType.Capsule, basePosition + offset, new Vector3(0.28f, height / 2f, 0.28f), Palette.Kelp, tilt);
            }
        }

        public static void Coral(Transform parent, Vector3 basePosition, System.Random random, float scale = 1f)
        {
            Material[] colors = { Palette.Coral, Palette.Anemone, Palette.Pineapple };
            int branches = random.Next(3, 6);
            for (int i = 0; i < branches; i++)
            {
                float height = Range(random, 0.8f, 1.8f) * scale;
                var tilt = new Vector3(Range(random, -35f, 35f), Range(random, 0f, 360f), Range(random, -35f, 35f));
                var offset = new Vector3(Range(random, -0.5f, 0.5f) * scale, height * 0.4f, Range(random, -0.5f, 0.5f) * scale);
                Part(parent, "Coral", PrimitiveType.Capsule, basePosition + offset, new Vector3(0.3f * scale, height / 2f, 0.3f * scale), colors[random.Next(colors.Length)], tilt);
            }
        }

        public static void Rock(Transform parent, Vector3 basePosition, Vector3 size)
        {
            Part(parent, "Rock", PrimitiveType.Sphere, basePosition + Vector3.up * (size.y * 0.3f), size, Palette.Rock);
        }

        static void AddConvexMeshCollider(GameObject part)
        {
            var meshCollider = part.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
            meshCollider.convex = true;
        }
    }
}
