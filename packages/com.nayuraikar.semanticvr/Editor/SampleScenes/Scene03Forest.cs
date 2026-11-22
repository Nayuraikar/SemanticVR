using SemanticVR.Core;
using UnityEngine;
using static SemanticVR.Editor.SceneKit;
using C = SemanticVR.Editor.DefaultClassLibrary;

namespace SemanticVR.Editor
{
    /// <summary>03_Forest: red berries on green bushes, fruit trees with unripe/ripening/ripe fruit, and a harvest table in the foreground.</summary>
    public static class Scene03Forest
    {
        public static void Build()
        {
            var scene = Begin("03_Forest");
            LightAndSkybox();
            var env = Ignore(Group("Environment", null)).transform;
            var sem = Group("Semantic", null).transform;

            Floor(env, sem);
            ForestTrees(sem, env);
            BerryBushes(sem);
            FruitTrees(sem, env);
            HarvestTable(sem, env);
            SkyDome(sem);
            Rig(new Vector3(0, 0, -3f), 0f, 2.5f);
            Save(scene);
            Debug.Log("[SemanticVR] Built " + ScenePath);
        }

        static void Floor(Transform env, Transform sem)
        {
            var ground = Mat("FarGround", "#FFFFFF", 0.08f);
            ground.SetTexture("_BaseMap", CheckerTexture()); ground.SetTextureScale("_BaseMap", new Vector2(40, 40));
            Prim(PrimitiveType.Plane, "FarGround", env, new Vector3(0, -0.1f, 40), new Vector3(40, 1, 40), ground);
            var path = Mat("DirtPath", Soil, 0.05f);
            for (int i = 0; i < 8; i++)
                Box("Path", env, new Vector3(Mathf.Sin(i * 0.7f) * 1.2f - 0.4f, 0.1f, -4f + i * 7f), new Vector3(2.4f, 0.04f, 7.4f), path, new Vector3(0, Mathf.Sin(i * 0.7f + 0.5f) * 8f, 0));

            var g = Group("Grass", sem).transform;
            int n = 0;
            for (int ix = -3; ix <= 3; ix++)
                for (int iz = 0; iz < 6; iz++)
                {
                    if (Mathf.Abs(ix) == 0 && iz < 2) continue; // leave the path/foreground open
                    string state = ((ix * 5 + iz * 3) % 7 == 0) ? "Dry" : "Green";
                    TaggedPrim(PrimitiveType.Cube, $"Floor_{ix}_{iz}", g, new Vector3(ix * 8.2f, 0.03f, 3f + iz * 8.2f), new Vector3(8.1f, 0.08f, 8.1f), C.Grass, state, null, 0.04f);
                    n++;
                }
        }

        static void ForestTrees(Transform sem, Transform env)
        {
            var g = Group("Trees", sem).transform;
            var trunk = Mat("Trunk", Trunk, 0.1f);
            var rng = Rng(21);
            for (int i = 0; i < 20; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * rng.Range(8f, 22f), z = rng.Range(6f, 48f), s = rng.Range(0.9f, 1.5f);
                var t = Group("Tree", g, new Vector3(x, 0, z)).transform;
                Ignore(Prim(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0, 2.0f * s, 0), new Vector3(0.45f * s, 2.0f * s, 0.45f * s), trunk));
                TaggedPrim(PrimitiveType.Sphere, "Canopy", t, new Vector3(0, 4.8f * s, 0), Vector3.one * 4.2f * s, C.Foliage, i % 3 == 0 ? "Dark" : "Leaf", null, 0.1f);
                TaggedPrim(PrimitiveType.Sphere, "Canopy", t, new Vector3(0.7f * s, 6.6f * s, 0.3f), Vector3.one * 2.9f * s, C.Foliage, i % 3 == 0 ? "Leaf" : "Dark", null, 0.1f);
            }
        }

        /// <summary>Directions on the camera-facing part of a sphere (the rig looks along +Z, so visible normals have z &lt; 0).</summary>
        static Vector3 FacingDir(System.Random rng)
        {
            for (int k = 0; k < 64; k++)
            {
                var d = new Vector3(rng.Range(-1f, 1f), rng.Range(-0.15f, 1f), rng.Range(-1f, 0.15f));
                if (d.sqrMagnitude > 0.05f) return d.normalized;
            }
            return Vector3.back;
        }

        static void BerryBushes(Transform sem)
        {
            var g = Group("BerryBushes", sem).transform;
            var rng = Rng(7);
            var spots = new (float x, float z, float ripe)[] { (-3.2f, 6f, 0.7f), (2.6f, 7.5f, 0.5f), (-6.5f, 10f, 0.8f), (6.5f, 11f, 0.3f), (0.4f, 13f, 0.6f), (-2.0f, 17f, 0.5f), (4.5f, 18f, 0.7f), (-8.2f, 15f, 0.4f) };
            for (int b = 0; b < spots.Length; b++)
            {
                float R = rng.Range(0.85f, 1.1f);
                var bush = Group($"Bush_{b + 1}", g, new Vector3(spots[b].x, 0, spots[b].z)).transform;
                var center = new Vector3(0, R * 0.9f, 0);
                TaggedPrim(PrimitiveType.Sphere, "Foliage", bush, center, Vector3.one * R * 2f, C.Foliage, "Leaf", null, 0.1f);
                TaggedPrim(PrimitiveType.Sphere, "Foliage", bush, center + new Vector3(0.55f * R, -0.2f * R, 0.1f), Vector3.one * R * 1.4f, C.Foliage, "Dark", null, 0.1f);
                TaggedPrim(PrimitiveType.Sphere, "Foliage", bush, center + new Vector3(-0.6f * R, -0.25f * R, 0.0f), Vector3.one * R * 1.3f, C.Foliage, "Leaf", null, 0.1f);
                int count = 12 + (b % 3) * 2;
                for (int k = 0; k < count; k++)
                {
                    bool ripe = rng.NextDouble() < spots[b].ripe;
                    TaggedPrim(PrimitiveType.Sphere, ripe ? "Berry_Ripe" : "Berry_Unripe", bush, center + FacingDir(rng) * (R * 0.98f), Vector3.one * 0.12f,
                               C.Berry, ripe ? "Ripe" : "Unripe", null, 0.55f);
                }
            }
        }

        static void FruitTrees(Transform sem, Transform env)
        {
            var g = Group("FruitTrees", sem).transform;
            var trunk = Mat("Trunk", Trunk, 0.1f);
            var rng = Rng(33);
            var spots = new[] { new Vector3(-5.5f, 0, 24f), new Vector3(5.0f, 0, 26f), new Vector3(0.3f, 0, 34f) };
            string[] states = { "Unripe", "Ripening", "Ripe" };
            for (int t = 0; t < spots.Length; t++)
            {
                var tree = Group($"FruitTree_{t + 1}", g, spots[t]).transform;
                Ignore(Prim(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0, 1.3f, 0), new Vector3(0.38f, 1.3f, 0.38f), trunk));
                var c = new Vector3(0, 3.3f, 0); float R = 1.9f;
                TaggedPrim(PrimitiveType.Sphere, "Canopy", tree, c, Vector3.one * R * 2f, C.Foliage, "Leaf", null, 0.1f);
                TaggedPrim(PrimitiveType.Sphere, "Canopy", tree, c + new Vector3(1.1f, -0.5f, 0.2f), Vector3.one * R * 1.2f, C.Foliage, "Dark", null, 0.1f);
                for (int k = 0; k < 16; k++)
                {
                    double r = rng.NextDouble();
                    string st = r < 0.38 ? states[0] : r < 0.62 ? states[1] : states[2];
                    TaggedPrim(PrimitiveType.Sphere, "Fruit_" + st, tree, c + FacingDir(rng) * (R * 1.0f), Vector3.one * 0.24f, C.Fruit, st, null, 0.5f);
                }
            }
        }

        static void HarvestTable(Transform sem, Transform env)
        {
            var g = Group("HarvestTable", sem, new Vector3(-0.9f, 0, 3.0f)).transform;
            var wood = Mat("TableWood", Wood, 0.25f); var crate = Mat("CrateWood", "#8B6B4A", 0.2f);
            var decor = Ignore(Group("Furniture", g)).transform;
            Box("Top", decor, new Vector3(0, 0.8f, 0), new Vector3(2.0f, 0.07f, 0.95f), wood);
            foreach (float lx in new[] { -0.9f, 0.9f }) foreach (float lz in new[] { -0.4f, 0.4f })
                Box("Leg", decor, new Vector3(lx, 0.4f, lz), new Vector3(0.07f, 0.8f, 0.07f), wood);
            for (int i = 0; i < 2; i++)
            {   // two shallow crates, left = ripe, right = unripe
                float cx = i == 0 ? -0.5f : 0.5f;
                Box("CrateBase", decor, new Vector3(cx, 0.86f, 0), new Vector3(0.82f, 0.04f, 0.62f), crate);
                Box("CrateFront", decor, new Vector3(cx, 0.92f, -0.3f), new Vector3(0.82f, 0.1f, 0.03f), crate);
                Box("CrateBack", decor, new Vector3(cx, 0.92f, 0.3f), new Vector3(0.82f, 0.1f, 0.03f), crate);
                Box("CrateL", decor, new Vector3(cx - 0.4f, 0.92f, 0), new Vector3(0.03f, 0.1f, 0.62f), crate);
                Box("CrateR", decor, new Vector3(cx + 0.4f, 0.92f, 0), new Vector3(0.03f, 0.1f, 0.62f), crate);
            }
            var rng = Rng(3);
            for (int i = 0; i < 2; i++)
                for (int ix = 0; ix < 4; ix++)
                    for (int iz = 0; iz < 3; iz++)
                    {
                        string st = i == 0 ? "Ripe" : (iz == 2 && ix % 2 == 0 ? "Ripening" : "Unripe");
                        float px = (i == 0 ? -0.5f : 0.5f) + (ix - 1.5f) * 0.17f + rng.Range(-0.012f, 0.012f), pz = (iz - 1f) * 0.17f + rng.Range(-0.012f, 0.012f);
                        TaggedPrim(PrimitiveType.Sphere, "Fruit_" + st, g, new Vector3(px, 0.99f, pz), Vector3.one * 0.15f, C.Fruit, st, null, 0.5f);
                    }
        }
    }
}
