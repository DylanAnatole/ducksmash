using System.Collections.Generic;
using UnityEngine;

// Original 3D toy-style art built from primitives, rendered with the DuckSmash/Toon shader
// (2-band cartoon light + glossy highlight + dark outline).
// Every builder returns a root Transform sized in world units (1 sim px = GameController.K).
public static class Art
{
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Shader std, toon;
    static Shader Std => std != null ? std : (std = Shader.Find("Standard"));
    static Shader ToonShader
    {
        get
        {
            if (toon != null) return toon;
            var holder = Resources.Load<Material>("ToonMat");          // keeps the shader in builds
            toon = holder != null ? holder.shader : Shader.Find("DuckSmash/Toon");
            if (toon == null) toon = Std;
            return toon;
        }
    }

    public static readonly Color OutlineCol = new Color(0.2f, 0.13f, 0.28f);

    // Play Mode runs without domain reload, so statics survive between sessions while the
    // materials/meshes they point to are destroyed on exit -> clear caches at every start.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        mats.Clear(); baked.Clear(); bakedMats.Clear();
        std = null; toon = null; lowSphere = null; midSphere = null;
    }

    // Cartoon material. smooth = highlight strength, metal brightens the highlight (for the Iron Duck).
    public static Material Mat(Color c, float smooth = 0.55f, float metal = 0f, float emission = 0f, float outline = 0f)
    {
        string key = ColorUtility.ToHtmlStringRGBA(c) + "|" + smooth + "|" + metal + "|" + emission + "|" + outline;
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(ToonShader) { color = c };
        m.SetFloat("_Gloss", Mathf.Clamp01(smooth + metal * 0.4f));
        m.SetFloat("_Outline", outline);
        m.SetColor("_OutlineColor", Color.Lerp(c, OutlineCol, 0.72f));   // tinted outline, softer than pure ink
        m.SetColor("_ShadowTint", new Color(0.74f, 0.72f, 0.88f));
        if (emission > 0) m.SetColor("_Emission", c * emission * 0.6f);
        m.enableInstancing = true;                              // many identical cookies/pixels -> few draw calls
        if (outline <= 0f) m.EnableKeyword("NO_OUTLINE");       // outline pass collapses to nothing (see Toon.shader)
        mats[key] = m;
        return m;
    }

    public static Material Toon(Color c, Texture tex, Vector2 tiling, float gloss = 0.2f)
    {
        var m = new Material(ToonShader) { color = c, mainTexture = tex, mainTextureScale = tiling };
        m.SetFloat("_Gloss", gloss); m.SetFloat("_Outline", 0f);
        m.SetColor("_ShadowTint", new Color(0.8f, 0.8f, 0.9f));
        return m;
    }

    // Unshared fading material (alpha animated per instance).
    public static Material Fade(Color c, float emission = 0f)
    {
        var m = new Material(Std) { color = c };
        m.SetFloat("_Mode", 2);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        if (emission > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emission); }
        return m;
    }

    public static Material FadeShared(Color c)
    {
        string key = "fade" + ColorUtility.ToHtmlStringRGBA(c);
        if (!mats.TryGetValue(key, out var m) || m == null) { m = Fade(c); mats[key] = m; }
        return m;
    }

    public static Transform Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(t);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.transform.localEulerAngles = euler;
        var rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = m;
        float vol = scale.x * scale.y * scale.z;
        if (vol < 0.004f) { rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false; } // tiny details
        // Unity's built-in sphere is 768 tris; most of ours are small, so use lighter meshes
        if (t == PrimitiveType.Sphere) go.GetComponent<MeshFilter>().sharedMesh = vol < 0.01f ? LowSphere : MidSphere;
        return go.transform;
    }

    // ---------------- mesh baking ----------------
    // Merges all opaque toon parts under `root` into ONE mesh (part colours -> vertex colours),
    // cached by `key` so every cookie / duck / crate of the same kind shares a mesh and instances together.
    // Children whose name is in `keep` (animated / transparent / glowing parts) stay separate.
    static readonly Dictionary<string, Mesh> baked = new Dictionary<string, Mesh>();
    static readonly Dictionary<string, Material> bakedMats = new Dictionary<string, Material>();

    public static void Bake(Transform root, string key, float gloss, float outline, params string[] keep)
    {
        var parts = new List<MeshFilter>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (IsKept(mf.transform, root, keep)) continue;
            var r = mf.GetComponent<Renderer>();
            if (r == null || r.sharedMaterial == null || r.sharedMaterial.shader != ToonShader) continue;   // transparent/other stay
            parts.Add(mf);
        }
        if (parts.Count == 0) return;
        if (!baked.TryGetValue(key, out var mesh) || mesh == null)
        {
            var toRoot = root.worldToLocalMatrix;
            var combine = new List<CombineInstance>();
            foreach (var mf in parts)
            {
                var src = mf.sharedMesh; var m = Object.Instantiate(src);
                var c = mf.GetComponent<Renderer>().sharedMaterial.color;
                var cols = new Color[m.vertexCount]; for (int i = 0; i < cols.Length; i++) cols[i] = c;
                m.colors = cols;
                combine.Add(new CombineInstance { mesh = m, transform = toRoot * mf.transform.localToWorldMatrix });
            }
            mesh = new Mesh { name = "Baked_" + key, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(combine.ToArray(), true, true);
            foreach (var ci in combine) Object.Destroy(ci.mesh);
            baked[key] = mesh;
        }
        foreach (var mf in parts) { mf.gameObject.SetActive(false); Object.Destroy(mf.gameObject); }
        var go = new GameObject("Baked");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = BakedMat(gloss, outline);
    }

    static bool IsKept(Transform t, Transform root, string[] keep)
    {
        for (var x = t; x != null && x != root; x = x.parent)
            foreach (var k in keep) if (x.name == k) return true;
        return false;
    }

    static Material BakedMat(float gloss, float outline)
    {
        string key = gloss + "|" + outline;
        if (bakedMats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(ToonShader) { color = Color.white, enableInstancing = true };
        m.SetFloat("_UseVC", 1); m.SetFloat("_Gloss", gloss); m.SetFloat("_Outline", outline);
        m.SetColor("_OutlineColor", new Color(0.33f, 0.24f, 0.42f)); m.SetColor("_ShadowTint", new Color(0.74f, 0.72f, 0.88f));
        if (outline <= 0f) m.EnableKeyword("NO_OUTLINE");
        bakedMats[key] = m;
        return m;
    }

    static Mesh lowSphere, midSphere;
    static Mesh LowSphere => lowSphere != null ? lowSphere : (lowSphere = UvSphere(8, 6));     // ~80 tris
    static Mesh MidSphere => midSphere != null ? midSphere : (midSphere = UvSphere(16, 11));   // ~320 tris

    static Mesh UvSphere(int lon, int lat)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        for (int y = 0; y <= lat; y++)
            for (int x = 0; x <= lon; x++)
            {
                float a = x / (float)lon * Mathf.PI * 2, b = y / (float)lat * Mathf.PI;
                var p = new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a));
                v.Add(p * 0.5f); n.Add(p); uv.Add(new Vector2(x / (float)lon, 1 - y / (float)lat));
            }
        for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int i = y * (lon + 1) + x, j = i + lon + 1;
                if (y > 0) { tri.Add(i); tri.Add(i + 1); tri.Add(j); }
                if (y < lat - 1) { tri.Add(i + 1); tri.Add(j + 1); tri.Add(j); }
            }
        var m = new Mesh { name = "UvSphere" + lon + "x" + lat };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds();
        return m;
    }

    public static Transform Root(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

    // ---------------- palette ----------------
    static readonly Color Beak = Hex("ff9a1f"), Eye = Hex("231a33"), Cheek = Hex("ff8fa8"),
        CookieTop = Hex("f4b25e"), CookieSide = Hex("d98a3a"), Chip = Hex("6b3a1a"),
        Wood = Hex("c98f55"), WoodDark = Hex("8e5b2c"), WoodLight = Hex("e2ae72"),
        Gold = Hex("ffd34d"), Red = Hex("ff5a4f"), Purple = Hex("8b5cff"), StoneCol = Hex("9aa3b8"), StoneDark = Hex("737c93");

    // ---------------- ducks ----------------
    public static readonly Dictionary<string, Color> DuckBody = new Dictionary<string, Color>
    {
        { "normal", Hex("ffd84a") },
        { "heavy",  Hex("b9c3d6") },
        { "bomb",   Hex("ff6b6b") },
        { "zap",    Hex("5fd0ff") },
        { "rocket", Hex("ff9a3d") },
        { "rainbow", Hex("fff1fa") },
    };

    // Duck faces +Z. Unit size ~1 = diameter; caller scales by radius.
    public static Transform Duck(Transform parent, string kind, out Transform ring)
    {
        var root = Root("Duck_" + kind, parent);
        var body = Root("Body", root);
        Color c = DuckBody[kind];
        bool heavy = kind == "heavy";
        var mb = Mat(c, heavy ? 0.9f : 0.75f, heavy ? 1 : 0, 0, 0.0028f);
        var md = Mat(Color.Lerp(c, Color.black, 0.12f), heavy ? 0.9f : 0.6f, heavy ? 1 : 0);
        var mbFlat = Mat(c, heavy ? 0.9f : 0.75f, heavy ? 1 : 0);   // same colour, no outline (inner parts)

        // body: plump, slightly tilted forward
        Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.3f, -0.06f), new Vector3(0.98f, 0.66f, 1.08f), mb);
        Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.5f, -0.5f), new Vector3(0.42f, 0.5f, 0.34f), mbFlat, new Vector3(-40, 0, 0));   // tail
        Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.68f, -0.62f), new Vector3(0.22f, 0.26f, 0.18f), mbFlat, new Vector3(-55, 0, 0));// tail tip
        Prim(PrimitiveType.Sphere, body, new Vector3(-0.44f, 0.4f, -0.08f), new Vector3(0.16f, 0.32f, 0.56f), md, new Vector3(0, 0, -12)); // wings
        Prim(PrimitiveType.Sphere, body, new Vector3(0.44f, 0.4f, -0.08f), new Vector3(0.16f, 0.32f, 0.56f), md, new Vector3(0, 0, 12));
        // head
        Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.86f, 0.26f), new Vector3(0.66f, 0.64f, 0.62f), mb);
        Prim(PrimitiveType.Sphere, body, new Vector3(0, 1.16f, 0.2f), new Vector3(0.14f, 0.16f, 0.14f), mbFlat);                  // tuft
        // beak (upper + lower)
        var bm = Mat(Beak, 0.6f);
        Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.8f, 0.6f), new Vector3(0.36f, 0.12f, 0.3f), bm);
        Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.73f, 0.56f), new Vector3(0.26f, 0.08f, 0.22f), Mat(Color.Lerp(Beak, Color.black, 0.15f), 0.4f));
        // eyes: white + pupil + highlight
        var white = Mat(Color.white, 0.9f); var pupil = Mat(Eye, 0.9f, 0, 0, 0f); var shine = Mat(Color.white, 1f, 0, 0.4f, 0f);
        foreach (int s in new[] { -1, 1 })
        {
            Prim(PrimitiveType.Sphere, body, new Vector3(s * 0.17f, 0.95f, 0.5f), new Vector3(0.16f, 0.18f, 0.1f), white);
            Prim(PrimitiveType.Sphere, body, new Vector3(s * 0.17f, 0.94f, 0.55f), new Vector3(0.1f, 0.12f, 0.06f), pupil);
            Prim(PrimitiveType.Sphere, body, new Vector3(s * 0.15f, 0.98f, 0.58f), Vector3.one * 0.035f, shine);
            Prim(PrimitiveType.Sphere, body, new Vector3(s * 0.27f, 0.8f, 0.46f), new Vector3(0.12f, 0.06f, 0.06f), Mat(Cheek, 0.3f, 0, 0, 0f));
        }

        if (kind == "bomb")
        {
            Prim(PrimitiveType.Cylinder, body, new Vector3(0, 1.28f, 0.2f), new Vector3(0.06f, 0.1f, 0.06f), Mat(Hex("4a3a2a"), 0.2f));
            Prim(PrimitiveType.Sphere, body, new Vector3(0, 1.42f, 0.2f), Vector3.one * 0.15f, Mat(Gold, 0.5f, 0, 2.5f, 0f)).name = "Spark";
            Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.62f, -0.05f), new Vector3(0.4f, 0.12f, 0.4f), Mat(Hex("2d2d3a"), 0.6f)); // band
        }
        else if (kind == "zap")
        {
            var bolt = Mat(Gold, 0.5f, 0, 1.5f);
            Prim(PrimitiveType.Cube, body, new Vector3(0.06f, 0.66f, -0.18f), new Vector3(0.09f, 0.06f, 0.42f), bolt, new Vector3(0, 32, 0));
            Prim(PrimitiveType.Cube, body, new Vector3(-0.06f, 0.66f, -0.44f), new Vector3(0.09f, 0.06f, 0.32f), bolt, new Vector3(0, -32, 0));
        }
        else if (kind == "rocket")
        {
            // pilot goggles + a little rocket strapped on the back
            var strap = Mat(Hex("4a3550"), 0.4f);
            Prim(PrimitiveType.Cylinder, body, new Vector3(0, 1.0f, 0.26f), new Vector3(0.68f, 0.025f, 0.64f), strap);
            foreach (int s in new[] { -1, 1 })
                Prim(PrimitiveType.Sphere, body, new Vector3(s * 0.15f, 1.04f, 0.5f), new Vector3(0.17f, 0.15f, 0.08f), Mat(Hex("8fe3ff"), 1f, 0, 0.4f));
            var hull = Mat(Color.white, 0.9f, 0, 0, 0.003f); var red = Mat(Hex("ff4d5e"), 0.8f);
            Prim(PrimitiveType.Capsule, body, new Vector3(0, 0.72f, -0.36f), new Vector3(0.24f, 0.3f, 0.24f), hull, new Vector3(-70, 0, 0));
            Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.82f, -0.1f), new Vector3(0.2f, 0.2f, 0.24f), red);              // nose cone
            Prim(PrimitiveType.Cube, body, new Vector3(0.14f, 0.66f, -0.6f), new Vector3(0.04f, 0.18f, 0.16f), red, new Vector3(0, 0, -25));
            Prim(PrimitiveType.Cube, body, new Vector3(-0.14f, 0.66f, -0.6f), new Vector3(0.04f, 0.18f, 0.16f), red, new Vector3(0, 0, 25));
            Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.62f, -0.72f), new Vector3(0.18f, 0.18f, 0.22f), Mat(Gold, 0.5f, 0, 2.5f, 0f)).name = "Spark"; // flame
        }
        else if (kind == "rainbow")
        {
            // pastel stripes over the back + a sparkling star crown
            string[] cols = { "ff6b8a", "ffb347", "ffe066", "6be08a", "6bc8ff", "b38bff" };
            for (int i = 0; i < cols.Length; i++)
                Prim(PrimitiveType.Sphere, body, new Vector3(0, 0.52f + i * 0.005f, 0.2f - i * 0.12f), new Vector3(0.9f - Mathf.Abs(i - 2.5f) * 0.06f, 0.5f, 0.1f), Mat(Hex(cols[i]), 0.8f));
            var gold = Mat(Gold, 0.9f, 1, 0.3f);
            for (int i = 0; i < 3; i++)
                Prim(PrimitiveType.Sphere, body, new Vector3((i - 1) * 0.13f, 1.2f + (i == 1 ? 0.06f : 0), 0.24f), Vector3.one * (i == 1 ? 0.14f : 0.1f), gold);
            Prim(PrimitiveType.Sphere, body, new Vector3(0, 1.38f, 0.24f), Vector3.one * 0.1f, Mat(Color.white, 1f, 0, 2.5f, 0f)).name = "Spark";
        }
        else if (heavy)
        {
            var helm = Mat(Hex("7d879e"), 1f, 1);
            Prim(PrimitiveType.Sphere, body, new Vector3(0, 1.08f, 0.24f), new Vector3(0.72f, 0.36f, 0.68f), helm);
            Prim(PrimitiveType.Cylinder, body, new Vector3(0, 1.0f, 0.24f), new Vector3(0.76f, 0.03f, 0.72f), helm);
        }

        ring = Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.02f, 0), new Vector3(1.45f, 0.01f, 1.45f), FadeShared(new Color(0.37f, 0.9f, 0.37f, 0.55f)));
        ring.name = "Ring";
        return root;
    }

    // ---------------- blocks ----------------
    public static Transform Cookie(Transform parent, float d)
    {
        var root = Root("Cookie", parent);
        Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.1f, 0), new Vector3(d, 0.1f, d), Mat(CookieSide, 0.3f));
        Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.2f, 0), new Vector3(d * 0.94f, 0.12f, d * 0.94f), Mat(CookieTop, 0.35f, 0, 0, 0f));
        var chip = Mat(Chip, 0.7f, 0, 0, 0f);
        Vector2[] pts = { new Vector2(-0.2f, -0.15f), new Vector2(0.18f, -0.2f), new Vector2(0.04f, 0.1f), new Vector2(-0.16f, 0.2f), new Vector2(0.24f, 0.12f), new Vector2(-0.02f, -0.3f) };
        foreach (var p in pts) Prim(PrimitiveType.Sphere, root, new Vector3(p.x * d, 0.25f, p.y * d), new Vector3(0.15f, 0.08f, 0.13f) * d, chip);
        return root;
    }

    public static Transform Ice(Transform parent, float d)
    {
        var root = Cookie(parent, d);
        root.name = "Ice";
        var shell = Prim(PrimitiveType.Cube, root, new Vector3(0, 0.24f, 0), new Vector3(d * 1.1f, 0.48f, d * 1.1f), FadeShared(new Color(0.8f, 0.95f, 1f, 0.62f)));
        shell.name = "IceShell";
        var glint = Mat(Color.white, 1f, 0, 0.6f, 0f);
        Prim(PrimitiveType.Cube, shell, new Vector3(-0.22f, 0.51f, -0.05f), new Vector3(0.07f, 0.01f, 0.65f), glint, new Vector3(0, 40, 0));
        Prim(PrimitiveType.Cube, shell, new Vector3(0.05f, 0.51f, 0.2f), new Vector3(0.05f, 0.01f, 0.35f), glint, new Vector3(0, 40, 0));
        return root;
    }

    public static Transform Crate(Transform parent, float w, float h)
    {
        var root = Root("Crate", parent);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.28f, 0), new Vector3(w, 0.56f, h), Mat(Wood, 0.25f));
        var frame = Mat(WoodDark, 0.2f, 0, 0, 0f);
        float t = Mathf.Min(w, h) * 0.14f;
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.57f, h / 2 - t / 2), new Vector3(w, 0.03f, t), frame);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.57f, -h / 2 + t / 2), new Vector3(w, 0.03f, t), frame);
        Prim(PrimitiveType.Cube, root, new Vector3(w / 2 - t / 2, 0.57f, 0), new Vector3(t, 0.03f, h), frame);
        Prim(PrimitiveType.Cube, root, new Vector3(-w / 2 + t / 2, 0.57f, 0), new Vector3(t, 0.03f, h), frame);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.575f, 0), new Vector3(t, 0.03f, Mathf.Sqrt(w * w + h * h) * 0.78f), frame, new Vector3(0, 45, 0));
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.565f, 0), new Vector3(w * 0.72f, 0.02f, h * 0.72f), Mat(WoodLight, 0.3f, 0, 0, 0f));
        return root;
    }

    public static Transform Chest(Transform parent, float w, float h)
    {
        var root = Root("Chest", parent);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.24f, 0), new Vector3(w, 0.48f, h), Mat(Hex("c7782a"), 0.35f));
        // rounded lid = half-buried cylinder lying along X
        Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.5f, 0), new Vector3(h * 1.02f, w * 0.5f, 0.42f), Mat(Hex("e08e36"), 0.4f), new Vector3(0, 0, 90));
        var gold = Mat(Gold, 0.95f, 1);
        Prim(PrimitiveType.Cube, root, new Vector3(-w * 0.32f, 0.36f, 0), new Vector3(0.08f, 0.74f, h * 1.05f), gold);
        Prim(PrimitiveType.Cube, root, new Vector3(w * 0.32f, 0.36f, 0), new Vector3(0.08f, 0.74f, h * 1.05f), gold);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.42f, -h * 0.53f), new Vector3(0.2f, 0.24f, 0.06f), gold);   // lock
        Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.82f, 0), Vector3.one * 0.18f, Mat(Gold, 1f, 1, 0.6f));   // gem on top
        return root;
    }

    public static Transform Tnt(Transform parent, float d)
    {
        var root = Root("TNT", parent);
        var red = Mat(Red, 0.55f);
        float s = d * 0.36f;
        Vector2[] sticks = { new Vector2(-0.5f, 0.3f), new Vector2(0.5f, 0.3f), new Vector2(0, -0.45f) };
        foreach (var p in sticks) Prim(PrimitiveType.Cylinder, root, new Vector3(p.x * s * 1.05f, 0.27f, p.y * s * 1.05f), new Vector3(s, 0.27f, s), red);
        Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.27f, 0), new Vector3(d * 1.02f, 0.06f, d * 1.02f), Mat(Hex("3a2e2a"), 0.3f));   // strap
        Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.62f, 0), new Vector3(0.05f, 0.1f, 0.05f), Mat(Hex("4a3a2a"), 0.2f, 0, 0, 0f));
        Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.76f, 0), Vector3.one * 0.14f, Mat(Gold, 0.5f, 0, 2.5f, 0f)).name = "Spark";
        return root;
    }

    public static Transform Stone(Transform parent, float w, float h)
    {
        var root = Root("Stone", parent);
        Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.26f, 0), new Vector3(w * 1.05f, 0.55f, h * 1.05f), Mat(StoneCol, 0.2f, 0, 0, 0.0035f));
        Prim(PrimitiveType.Sphere, root, new Vector3(-w * 0.18f, 0.44f, h * 0.1f), new Vector3(w * 0.55f, 0.36f, h * 0.5f), Mat(Color.Lerp(StoneCol, Color.white, 0.2f), 0.2f));
        Prim(PrimitiveType.Sphere, root, new Vector3(w * 0.22f, 0.32f, -h * 0.2f), new Vector3(w * 0.4f, 0.3f, h * 0.4f), Mat(StoneDark, 0.2f));
        return root;
    }

    public static Transform Laser(Transform parent, float d, bool horizontal)
    {
        var root = Root("Laser", parent);
        Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.08f, 0), new Vector3(d, 0.08f, d), Mat(Hex("4b2fa8"), 0.4f));
        Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.32f, 0), new Vector3(d * 0.7f, d * 0.7f, d * 0.7f), Mat(Purple, 1f, 0, 1.2f)).name = "Glow";
        var arrow = Root("Arrow", root);
        var w = Mat(Color.white, 0.6f, 0, 1f);
        Prim(PrimitiveType.Cube, arrow, new Vector3(0, 0.62f, 0), new Vector3(d * 0.95f, 0.05f, 0.07f), w);
        foreach (int s in new[] { -1, 1 })
        {
            Prim(PrimitiveType.Cube, arrow, new Vector3(s * d * 0.38f, 0.62f, 0.07f), new Vector3(0.22f, 0.05f, 0.07f), w, new Vector3(0, s * 40, 0));
            Prim(PrimitiveType.Cube, arrow, new Vector3(s * d * 0.38f, 0.62f, -0.07f), new Vector3(0.22f, 0.05f, 0.07f), w, new Vector3(0, -s * 40, 0));
        }
        if (!horizontal) arrow.localEulerAngles = new Vector3(0, 90, 0);
        return root;
    }

    public static Transform Screw(Transform parent, float d)
    {
        var root = Root("Screw", parent);
        Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.86f, 0), new Vector3(d, 0.16f, d), Mat(Hex("dfe5ef"), 1f, 1, 0, 0.0035f));
        var slot = Mat(Hex("4a5266"), 0.5f, 0, 0, 0f);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.93f, 0), new Vector3(d * 0.7f, 0.03f, 0.05f), slot);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.93f, 0), new Vector3(0.05f, 0.03f, d * 0.7f), slot);
        return root;
    }

    public static Transform Plank(Transform parent, float w, float h)
    {
        var root = Root("Plank", parent);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.75f, 0), new Vector3(w, 0.16f, h), Mat(WoodLight, 0.3f));
        var grain = Mat(Wood, 0.25f, 0, 0, 0f);
        for (int i = -1; i <= 1; i++) Prim(PrimitiveType.Cube, root, new Vector3(i * 0.3f, 0.835f, (i == 0 ? 0.1f : -0.08f) * h), new Vector3(w * 0.7f, 0.01f, 0.025f), grain);
        var post = Mat(WoodDark, 0.2f);
        Prim(PrimitiveType.Cylinder, root, new Vector3(-w * 0.45f, 0.35f, 0), new Vector3(0.12f, 0.35f, 0.12f), post);
        Prim(PrimitiveType.Cylinder, root, new Vector3(w * 0.45f, 0.35f, 0), new Vector3(0.12f, 0.35f, 0.12f), post);
        return root;
    }

    public static Transform Pixel(Transform parent, float s, Color c)
    {
        var root = Root("Pixel", parent);
        Prim(PrimitiveType.Cube, root, new Vector3(0, 0.16f, 0), new Vector3(s, 0.32f, s), Mat(c, 0.5f));
        return root;
    }

    public static Transform Boss(Transform parent, float d)
    {
        var root = Root("Boss", parent);
        var c = Cookie(root, d);
        c.localScale = new Vector3(1, 3f, 1);
        var white = Mat(Color.white, 0.9f); var black = Mat(Eye, 0.9f, 0, 0, 0f); var shine = Mat(Color.white, 1, 0, 0.5f, 0f);
        foreach (int s in new[] { -1, 1 })
        {
            Prim(PrimitiveType.Sphere, root, new Vector3(s * d * 0.18f, 0.86f, d * 0.08f), new Vector3(0.42f, 0.22f, 0.42f), white);
            Prim(PrimitiveType.Sphere, root, new Vector3(s * d * 0.16f, 0.96f, d * 0.13f), new Vector3(0.2f, 0.1f, 0.2f), black);
            Prim(PrimitiveType.Sphere, root, new Vector3(s * d * 0.14f, 1.01f, d * 0.16f), Vector3.one * 0.06f, shine);
            Prim(PrimitiveType.Cube, root, new Vector3(s * d * 0.2f, 1.0f, d * 0.28f), new Vector3(0.48f, 0.09f, 0.11f), Mat(Hex("4a2508"), 0.3f), new Vector3(0, s * -22, 0));
        }
        Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.84f, -d * 0.24f), new Vector3(0.6f, 0.08f, 0.16f), Mat(Hex("4a2508"), 0.3f));   // frown
        var gold = Mat(Gold, 1f, 1);
        Prim(PrimitiveType.Cylinder, root, new Vector3(0, 1.0f, d * 0.4f), new Vector3(0.62f, 0.08f, 0.62f), gold);
        for (int i = 0; i < 5; i++)
        {
            float a = i / 5f * Mathf.PI * 2;
            Prim(PrimitiveType.Cube, root, new Vector3(Mathf.Cos(a) * 0.26f, 1.16f, d * 0.4f + Mathf.Sin(a) * 0.26f), new Vector3(0.1f, 0.24f, 0.1f), gold);
            Prim(PrimitiveType.Sphere, root, new Vector3(Mathf.Cos(a) * 0.26f, 1.3f, d * 0.4f + Mathf.Sin(a) * 0.26f), Vector3.one * 0.09f, Mat(Red, 1f, 0, 0.6f, 0f));
        }
        return root;
    }

    public static Transform Bumper(Transform parent, float w, float h)
    {
        var root = Root("Bumper", parent);
        // pill shape along Z
        Prim(PrimitiveType.Capsule, root, new Vector3(0, 0.3f, 0), new Vector3(Mathf.Max(w, 0.3f), h / 2, Mathf.Max(w, 0.3f)), Mat(Hex("ff7a9a"), 0.8f, 0, 0, 0.0035f), new Vector3(90, 0, 0));
        return root;
    }

    // ---------------- tub / environment ----------------
    // Soft tileable water with bright caustic lines.
    public static Texture2D WaterTex(int size = 256)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color[size * size];
        Color deep = Hex("5cc8ee"), light = Hex("8fe0f7"), line = Hex("e9fbff");
        float k = Mathf.PI * 2 / size;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float a = Mathf.Sin(x * k * 2 + Mathf.Sin(y * k * 3) * 1.6f) + Mathf.Sin(y * k * 2 + Mathf.Sin(x * k * 3) * 1.6f);
                float b = Mathf.Sin((x + y) * k * 3 + Mathf.Sin(x * k * 1) * 2f);
                float caustic = Mathf.Pow(1 - Mathf.Clamp01(Mathf.Abs(a * 0.5f + b * 0.25f)), 6);
                float shade = 0.5f + 0.5f * Mathf.Sin(y * k + Mathf.Sin(x * k) * 0.8f);
                px[y * size + x] = Color.Lerp(Color.Lerp(deep, light, shade * 0.6f), line, caustic * 0.75f);
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    // Pastel bathroom tiles with grout lines.
    // translucent deep-teal band hugging the tub walls (water looks deeper at the edges)
    public static Texture2D EdgeShadeTex(float aspect, int size = 128)
    {
        int W = size, H = Mathf.RoundToInt(size / Mathf.Max(0.1f, aspect));
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[W * H]; var c = new Color(0.08f, 0.32f, 0.55f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float d = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y)) / (float)W;   // distance to edge, in tub widths
                float a = Mathf.Pow(1f - Mathf.Clamp01(d / 0.2f), 1.8f) * 0.6f;
                px[y * W + x] = new Color(c.r, c.g, c.b, a);
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    public static Texture2D TileTex(Color tile, Color grout, int tiles = 4, int size = 256)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color[size * size]; int ts = size / tiles; var rnd = new System.Random(3);
        var shades = new float[tiles * tiles]; for (int i = 0; i < shades.Length; i++) shades[i] = (float)rnd.NextDouble() * 0.04f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int tx = x / ts, ty = y / ts, lx = x % ts, ly = y % ts;
                bool g = lx < 3 || ly < 3;
                float bevel = (lx < 8 || ly < 8) ? 0.03f : (lx > ts - 6 || ly > ts - 6) ? -0.03f : 0;
                var c = g ? grout : tile * (1 - shades[ty * tiles + tx] + bevel);
                c.a = 1; px[y * size + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    public static Texture2D Checker(Color a, Color b, int cells, int size = 256)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
        var px = new Color[size * size]; int cs = size / cells;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = ((x / cs + y / cs) % 2 == 0) ? a : b;
        tex.SetPixels(px); tex.Apply();
        return tex;
    }
}
