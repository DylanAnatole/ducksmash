using UnityEngine;

// Runtime-built particle effects (no assets). Each effect is one pooled ParticleSystem
// that we Emit() into at arbitrary positions, so bursts are cheap.
public static class FxKit
{
    static Transform root;
    static ParticleSystem sparkle, crumbs, drops, ripple, fire, smoke, flash, sparks, wake, confetti, stars, bubbles;
    static Texture2D softTex, starTex, ringTex, squareTex;
    static int budgetFrame, emitted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { root = null; }

    public static void Init(Transform parent)
    {
        if (root != null) return;
        root = new GameObject("FxKit").transform; root.SetParent(parent, false);
        softTex = MakeSoft(); starTex = MakeStar(); ringTex = MakeRing(); squareTex = MakeSquare();

        sparkle = Make("Sparkle", starTex, 600, 0f, ParticleSystemRenderMode.Billboard, sizeCurve: Shrink());
        stars = Make("Stars", starTex, 300, 0.6f, ParticleSystemRenderMode.Billboard, sizeCurve: Shrink());
        crumbs = Make("Crumbs", squareTex, 900, 2.2f, ParticleSystemRenderMode.Billboard, sizeCurve: Shrink(0.6f), rotate: true);
        drops = Make("Drops", softTex, 800, 2.6f, ParticleSystemRenderMode.Stretch, sizeCurve: Shrink());
        ripple = Make("Ripple", ringTex, 200, 0f, ParticleSystemRenderMode.HorizontalBillboard, sizeCurve: Grow());
        fire = Make("Fire", softTex, 400, -0.15f, ParticleSystemRenderMode.Billboard, sizeCurve: Grow(0.5f), additive: true);
        smoke = Make("Smoke", softTex, 300, -0.08f, ParticleSystemRenderMode.Billboard, sizeCurve: Grow(0.3f));
        flash = Make("Flash", softTex, 40, 0f, ParticleSystemRenderMode.Billboard, sizeCurve: Grow(0.6f), additive: true);
        sparks = Make("Sparks", softTex, 600, 1.2f, ParticleSystemRenderMode.Stretch, sizeCurve: Shrink(), additive: true);
        wake = Make("Wake", softTex, 600, 0f, ParticleSystemRenderMode.HorizontalBillboard, sizeCurve: Grow(0.4f));
        confetti = Make("Confetti", squareTex, 600, 0.9f, ParticleSystemRenderMode.Billboard, sizeCurve: null, rotate: true);
        bubbles = Make("Bubbles", ringTex, 120, -0.02f, ParticleSystemRenderMode.Billboard, sizeCurve: Grow(0.6f));
        var r = drops.GetComponent<ParticleSystemRenderer>(); r.velocityScale = 0.06f; r.lengthScale = 1.2f;
        var rs = sparks.GetComponent<ParticleSystemRenderer>(); rs.velocityScale = 0.05f; rs.lengthScale = 1.5f;
    }

    // ---------------- public effects ----------------
    public static void Sparkle(Vector3 p, Color c, int n = 6)
    {
        for (int i = 0; i < n; i++)
            Emit(sparkle, p + Vector3.up * 0.2f, Random.onUnitSphere * Random.Range(1.5f, 3.5f) + Vector3.up, Color.Lerp(c, Color.white, 0.5f), Random.Range(0.15f, 0.32f), Random.Range(0.25f, 0.45f));
    }

    public static void Crumbs(Vector3 p, Color c, int n = 8)
    {
        for (int i = 0; i < n; i++)
        {
            var d = Random.insideUnitCircle.normalized;
            Emit(crumbs, p + Vector3.up * 0.25f, new Vector3(d.x, Random.Range(1.2f, 2.6f), d.y) * Random.Range(1.2f, 3.2f),
                Color.Lerp(c, Color.black, Random.Range(0f, 0.25f)), Random.Range(0.08f, 0.18f), Random.Range(0.5f, 0.9f));
        }
    }

    public static void Splash(Vector3 p, int n = 8, float power = 1f)
    {
        for (int i = 0; i < n; i++)
        {
            var d = Random.insideUnitCircle.normalized;
            Emit(drops, p + Vector3.up * 0.1f, new Vector3(d.x * 1.5f, Random.Range(2.5f, 4.5f), d.y * 1.5f) * power,
                new Color(0.92f, 0.99f, 1f, 0.95f), Random.Range(0.06f, 0.12f), Random.Range(0.4f, 0.65f));
        }
        Emit(ripple, p + Vector3.up * 0.02f, Vector3.zero, new Color(1, 1, 1, 0.7f), 0.5f * power, 0.5f);
    }

    public static void Ripple(Vector3 p, float size = 0.6f) => Emit(ripple, p + Vector3.up * 0.02f, Vector3.zero, new Color(1, 1, 1, 0.55f), size, 0.6f);

    public static void Wake(Vector3 p) => Emit(wake, p + Vector3.up * 0.02f, Vector3.zero, new Color(1, 1, 1, 0.45f), Random.Range(0.25f, 0.4f), 0.55f);

    public static void Explosion(Vector3 p, float radius)
    {
        Emit(flash, p + Vector3.up * 0.4f, Vector3.zero, new Color(1f, 0.95f, 0.8f, 1f), radius * 1.6f, 0.18f);
        // phones: big soft fire/smoke sprites are pure overdraw, so emit far fewer
        int fires = App.Mobile ? 7 : 18, smokes = App.Mobile ? 3 : 10, sparkN = App.Mobile ? 8 : 20;
        for (int i = 0; i < fires; i++)
        {
            var d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * 0.8f + 0.2f;
            Emit(fire, p + d * 0.2f + Vector3.up * 0.3f, d * Random.Range(1.5f, 4f) * radius,
                Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.35f, 0.15f), Random.value), Random.Range(0.5f, 0.9f) * radius, Random.Range(0.3f, 0.5f));
        }
        for (int i = 0; i < smokes; i++)
        {
            var d = Random.onUnitSphere; d.y = Mathf.Abs(d.y);
            Emit(smoke, p + d * 0.3f + Vector3.up * 0.4f, d * Random.Range(0.6f, 1.5f) * radius + Vector3.up * 0.6f,
                new Color(0.55f, 0.5f, 0.6f, 0.55f), Random.Range(0.6f, 1f) * radius, Random.Range(0.7f, 1.1f));
        }
        for (int i = 0; i < sparkN; i++)
        {
            var d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) + 0.3f;
            Emit(sparks, p + Vector3.up * 0.3f, d.normalized * Random.Range(5f, 9f), new Color(1f, 0.85f, 0.4f), Random.Range(0.06f, 0.1f), Random.Range(0.3f, 0.55f));
        }
    }

    // short-lived jet flame (rocket duck exhaust, rocket impacts)
    public static void Flame(Vector3 p) =>
        Emit(fire, p + Vector3.up * 0.15f, Random.insideUnitSphere * 0.5f + Vector3.up * 0.8f,
            Color.Lerp(new Color(1f, 0.9f, 0.35f), new Color(1f, 0.4f, 0.15f), Random.value), Random.Range(0.28f, 0.45f), Random.Range(0.18f, 0.3f));

    public static void Smoke(Vector3 p, float size, int n = 8)
    {
        for (int i = 0; i < n; i++)
        {
            var d = Random.insideUnitCircle;
            Emit(smoke, p + new Vector3(d.x, 0.2f, d.y) * size * 0.5f, new Vector3(d.x, 0.8f, d.y) * 0.8f, new Color(0.85f, 0.78f, 0.7f, 0.6f), Random.Range(0.5f, 0.9f) * size, Random.Range(0.6f, 1f));
        }
    }

    public static void StarBurst(Vector3 p, int n = 14)
    {
        for (int i = 0; i < n; i++)
            Emit(stars, p, Random.onUnitSphere * Random.Range(2f, 5f) + Vector3.up * 2f, new Color(1f, 0.88f, 0.35f), Random.Range(0.25f, 0.45f), Random.Range(0.6f, 1f));
    }

    public static void Confetti(Vector3 center, float width, int n = 120)
    {
        Color[] cols = { new Color(1f, 0.5f, 0.7f), new Color(1f, 0.85f, 0.3f), new Color(0.4f, 0.85f, 1f), new Color(0.5f, 0.9f, 0.5f), new Color(0.7f, 0.55f, 1f) };
        for (int i = 0; i < n; i++)
        {
            var p = center + new Vector3(Random.Range(-width, width) * 0.5f, Random.Range(2f, 5f), Random.Range(-2f, 3f));
            Emit(confetti, p, new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1.5f), Random.Range(-1f, 1f)), cols[i % cols.Length], Random.Range(0.12f, 0.22f), Random.Range(1.6f, 2.6f));
        }
    }

    // ambient soap bubbles drifting up from the bath water
    public static void Bubble(Vector3 p) =>
        Emit(bubbles, p + Vector3.up * 0.05f, new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.25f, 0.5f), Random.Range(-0.1f, 0.1f)),
            new Color(1f, 1f, 1f, 0.7f), Random.Range(0.12f, 0.28f), Random.Range(1.6f, 2.6f));

    public static void Clear()
    {
        foreach (var ps in new[] { sparkle, crumbs, drops, ripple, fire, smoke, flash, sparks, wake, confetti, stars, bubbles }) if (ps) ps.Clear();
    }

    // ---------------- plumbing ----------------
    static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, Color c, float size, float life)
    {
        if (ps == null) return;
        // per-frame budget: a 40-block smash must not emit thousands of particles in one frame
        if (Time.frameCount != budgetFrame) { budgetFrame = Time.frameCount; emitted = 0; }
        if (++emitted > (App.Mobile ? 70 : 220)) return;
        var ep = new ParticleSystem.EmitParams
        {
            position = pos, velocity = vel, startColor = c, startSize = size, startLifetime = life,
            rotation = Random.Range(0f, 360f), angularVelocity = Random.Range(-360f, 360f),
        };
        ps.Emit(ep, 1);
    }

    static ParticleSystem Make(string name, Texture2D tex, int max, float gravity, ParticleSystemRenderMode mode, AnimationCurve sizeCurve, bool additive = false, bool rotate = false)
    {
        var go = new GameObject(name); go.transform.SetParent(root, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = true;   // keeps the system "playing" so manual Emit() always simulates
        main.maxParticles = App.Mobile ? Mathf.Max(30, max / 3) : max;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = gravity;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
        col.color = g;
        if (sizeCurve != null) { var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve); }
        var limit = ps.limitVelocityOverLifetime; limit.enabled = gravity > 1f || name == "Fire" || name == "Smoke"; limit.dampen = 0.08f; limit.limit = 0.5f;
        if (rotate) { var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f); }
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        var mat = new Material(Shader.Find(additive ? "Legacy Shaders/Particles/Additive" : "Sprites/Default")) { mainTexture = tex };
        if (mat.shader == null || !mat.shader.isSupported) mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        r.sharedMaterial = mat;
        r.sortingFudge = additive ? -10 : 0;
        ps.Play();
        return ps;
    }

    static AnimationCurve Shrink(float start = 1f) => new AnimationCurve(new Keyframe(0, start), new Keyframe(0.15f, 1), new Keyframe(1, 0));
    static AnimationCurve Grow(float start = 0.3f) => new AnimationCurve(new Keyframe(0, start), new Keyframe(1, 1));

    static Texture2D NewTex(int s) => new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

    static Texture2D MakeSoft()
    {
        int s = 64; var t = NewTex(s); var px = new Color[s * s];
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                px[y * s + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - d), 1.6f));
            }
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D MakeRing()
    {
        int s = 128; var t = NewTex(s); var px = new Color[s * s];
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                px[y * s + x] = new Color(1, 1, 1, Mathf.Clamp01(1 - Mathf.Abs(d - 0.82f) * 9f));
            }
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D MakeStar()
    {
        int s = 64; var t = NewTex(s); var px = new Color[s * s];
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
            {
                var v = new Vector2(x + 0.5f - s / 2f, y + 0.5f - s / 2f) / (s / 2f);
                float a = Mathf.Atan2(v.y, v.x), r = v.magnitude;
                float edge = 0.45f + 0.55f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2.5f)), 3);   // 5-point star
                float alpha = Mathf.Clamp01((edge - r) * 6f) + Mathf.Clamp01(1 - r * 2.5f);
                px[y * s + x] = new Color(1, 1, 1, Mathf.Clamp01(alpha));
            }
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D MakeSquare()
    {
        int s = 16; var t = NewTex(s); var px = new Color[s * s];
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++) px[y * s + x] = new Color(1, 1, 1, (x == 0 || y == 0 || x == s - 1 || y == s - 1) ? 0.6f : 1f);
        t.SetPixels(px); t.Apply(); return t;
    }
}
