using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Duck Smash gameplay: top-down tub, slingshot a duck, billiard pushes, smash blocks.
// Simulation runs in "sim pixels" (540x960 board, y down) and is mirrored to 3D views.
public class GameController : MonoBehaviour
{
    // ---------- constants ----------
    public const float K = 0.02f;           // sim px -> world units
    const float CY = 510f;                  // sim y of world z=0
    const float TX = 28, TY = 150, TW = 484, TH = 720, TR = 46;
    const float MAX_PULL = 150, MAX_SPEED = 1300, DRAG = 0.48f, FRICTION = 16, FEVER_MAX = 70, ROCKET_SPEED = 1450, ROCKET_TIME = 0.75f;
    const int CONTINUE_PRICE = 60;
    static readonly Dictionary<string, int> PRICE = new Dictionary<string, int> { { "hammer", 80 }, { "bomb", 100 }, { "moves", 120 } };
    static readonly Dictionary<string, string> BNAME = new Dictionary<string, string> { { "hammer", "Búa" }, { "bomb", "Bom nước" }, { "moves", "+3 Lượt" } };

    public static Vector3 W(float x, float y, float h = 0) => new Vector3((x - 270) * K, h, (CY - y) * K);
    public static Vector3 W(Vector2 p, float h = 0) => W(p.x, p.y, h);

    // ---------- model ----------
    class Duck
    {
        public Vector2 p, v; public float r; public string kind; public bool hot, charged = true, frenzy, clone;
        public float squash, zapCd, face = -Mathf.PI / 2; public Transform view, ring, body;
        public int Dmg => kind == "heavy" ? 3 : 1;
        // special (combo-reward) ducks are one-use: after their power fires they turn back into a normal duck
        public bool special, spent, rainbowReady; public float rocketT, spawnT, stretch, pullVis, bankCd; public TrailRenderer trail;
    }
    // combo tiers: smashing this many in ONE shot hatches a special duck
    static readonly (int need, string kind, string name, Color col)[] TIERS =
    {
        (25, "rainbow", "VỊT CẦU VỒNG", new Color(1f, 0.55f, 0.85f)),
        (16, "rocket",  "VỊT TÊN LỬA",  new Color(1f, 0.55f, 0.2f)),
        (10, "bomb",    "VỊT BOM",      new Color(1f, 0.4f, 0.35f)),
    };
    const int ZAP_PUSHES = 3;   // 3+ billiard knocks in one shot -> lightning duck
    class Block
    {
        public string type; public Vector2 p; public float w, h, r; public bool rect; public int hp, max; public bool alive = true;
        public float hit, falling; public Block under, plankRef; public List<Block> screws; public string dir; public Color color;
        public float mvCx, mvAmp, mvSp; public int pid; public Transform view; public Vector3 baseScale = Vector3.one;
        public Renderer[] rends; public bool flashing; public float appear = 1, delay;
        public bool shelf;
        public bool Prop => type == "glass" || type == "vase" || type == "soap" || type == "plates";
        public bool Goal => type == "cookie" || type == "chest" || type == "pixel" || type == "ice" || type == "boss" || Prop;
        public bool Hard => type == "stone" || type == "plank";
    }
    class Bumper { public float x, y, w, h, pulse; public Transform view; }
    class Timer { public float t; public Action fn; }
    class Bit { public PrimitiveType type; public Transform t; public Vector3 v, spin, scale0; public float life, max; public bool grav; }
    class Ring { public Transform t; public Material m; public float life, max, r0, r1; public Color c; }
    class Pop { public Text t; public Vector3 world; public float life, max, rise; public bool big; }
    class Zap { public LineRenderer lr; public Vector2 a, b; public float life; }

    // ---------- state ----------
    Camera cam; Transform world, fx;
    readonly List<Duck> ducks = new List<Duck>();
    readonly List<Block> blocks = new List<Block>();
    readonly List<Bumper> bumpers = new List<Bumper>();
    readonly List<Timer> timers = new List<Timer>();
    readonly List<Bit> bits = new List<Bit>(); readonly Stack<Transform> bitPool = new Stack<Transform>();
    readonly Dictionary<PrimitiveType, Stack<Transform>> bitPools = new Dictionary<PrimitiveType, Stack<Transform>>();
    readonly List<Ring> rings = new List<Ring>();
    readonly List<Pop> pops = new List<Pop>();
    readonly List<Zap> zaps = new List<Zap>();
    public bool Active { get; private set; }
    int level, moves, startMoves, shotSmash, streak, pushes, score, stars, reward;
    static float fever; static bool frenzyReady;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { fever = 0; frenzyReady = false; trailMat = null; }
    bool moving, frenzyShot, bossKilled, rewarded;
    string state = "play", target, hint;
    float freeze, shake, comboPulse, t, lastW, lastH;
    Duck drag; Vector2 dragPt, dragSm; int powerStep, shotBanks; bool lastWarned;
    bool aimVisible = true;
    float swoop, flashA, hudTick; Image flashImg; int comboShown = -1; Block bossBlock;
    int piggyGain, dominoFalls, shotClears, piggyAdded; string levelArt; bool newArt;
    bool HasNext => level < LevelLibrary.Count || (level >= LevelLibrary.SmashLevel && level < LevelLibrary.SmashLevel + LevelLibrary.SmashCount - 1);
    class Line { public List<Block> m; public bool hz, done; public float c, a, b; }
    readonly List<Line> lines = new List<Line>();
    readonly Dictionary<Block, List<Line>> lineOf = new Dictionary<Block, List<Line>>();

    // rows (7+) and columns (6+) of contiguous breakable blocks become "lines" that clear with a bang
    void BuildLines()
    {
        lines.Clear(); lineOf.Clear();
        var cand = blocks.Where(b => !b.Hard && b.type != "screw" && b.type != "boss" && b.type != "chest" && b.under == null && b.mvAmp <= 0).ToList();
        foreach (bool hz in new[] { true, false })
        {
            var sorted = cand.OrderBy(b => hz ? b.p.y : b.p.x).ToList();
            var cur = new List<Block>(); float c0 = 0;
            foreach (var b in sorted)
            {
                float v = hz ? b.p.y : b.p.x;
                if (cur.Count > 0 && Mathf.Abs(v - c0) > 6) { SplitLine(cur, hz); cur = new List<Block>(); }
                if (cur.Count == 0) c0 = v;
                cur.Add(b);
            }
            SplitLine(cur, hz);
        }
    }

    void SplitLine(List<Block> g, bool hz)
    {
        g = g.OrderBy(b => hz ? b.p.x : b.p.y).ToList();
        var run = new List<Block>();
        for (int i = 0; i <= g.Count; i++)
        {
            bool brk = i == g.Count || (run.Count > 0 && (hz ? g[i].p.x - run[run.Count - 1].p.x : g[i].p.y - run[run.Count - 1].p.y) > 50);
            if (brk)
            {
                if (run.Count >= (hz ? 7 : 6))
                {
                    var ln = new Line { m = run, hz = hz, c = run.Average(b => hz ? b.p.y : b.p.x), a = hz ? run[0].p.x : run[0].p.y, b = hz ? run[run.Count - 1].p.x : run[run.Count - 1].p.y };
                    lines.Add(ln);
                    foreach (var b in run) { if (!lineOf.TryGetValue(b, out var l)) lineOf[b] = l = new List<Line>(); l.Add(ln); }
                }
                run = new List<Block>();
            }
            if (i < g.Count) run.Add(g[i]);
        }
    }

    void LineClear(Line ln)
    {
        shotClears++;
        var mid = ln.hz ? new Vector2((ln.a + ln.b) / 2, ln.c) : new Vector2(ln.c, (ln.a + ln.b) / 2);
        var gold = new Color(1f, 0.85f, 0.35f);
        PopText(shotClears >= 2 ? "CLEAR x" + shotClears + "!" : "CLEAR!", mid + new Vector2(0, -30), gold, 64 + Mathf.Min(shotClears, 5) * 8);
        int bonus = 150 * shotClears; score += bonus; comboPulse = 0.3f;
        Sfx.Play("laser", 0.7f, 1.3f + shotClears * 0.05f); Sfx.Play("combo", 0.6f, 1f + shotClears * 0.1f);
        Flash(0.15f + Mathf.Min(shotClears, 4) * 0.05f); Shake(0.25f); freeze = Mathf.Max(freeze, 0.04f);
        // golden sweep along the cleared line
        var beam = Art.Prim(PrimitiveType.Cube, fx, Vector3.zero, Vector3.one, Art.Fade(new Color(1f, 0.9f, 0.5f, 0.9f), 1.5f));
        float len = (ln.b - ln.a + 40) * K;
        beam.position = W(mid, 0.4f);
        beam.localScale = ln.hz ? new Vector3(len, 0.12f, 0.45f) : new Vector3(0.45f, 0.12f, len);
        rings.Add(new Ring { t = beam, m = beam.GetComponent<Renderer>().sharedMaterial, life = 0.45f, max = 0.45f, r0 = -1, c = new Color(1f, 0.9f, 0.5f, 0.9f) });
        for (int i = 0; i <= 10; i++) { float f = i / 10f; var q = ln.hz ? new Vector2(Mathf.Lerp(ln.a, ln.b, f), ln.c) : new Vector2(ln.c, Mathf.Lerp(ln.a, ln.b, f)); FxKit.Sparkle(W(q, 0.3f), gold, 2); }
        // shockwave chips the rows right next to it (can chain into more clears)
        Later(0.06f, () =>
        {
            foreach (var o in blocks.ToArray())
            {
                if (!o.alive || Covered(o)) continue;
                bool near = ln.hz ? Mathf.Abs(o.p.y - ln.c) < 46 && o.p.x > ln.a - 24 && o.p.x < ln.b + 24
                                  : Mathf.Abs(o.p.x - ln.c) < 46 && o.p.y > ln.a - 24 && o.p.y < ln.b + 24;
                if (near) Damage(o, 1);
            }
        });
    }
    void Flash(float a) => flashA = Mathf.Max(flashA, a);
    Transform aimTarget, aimGrab; readonly List<Transform> pushDots = new List<Transform>(); LineRenderer band2;
    MaterialPropertyBlock mpb; static Material trailMat;
    Vector3 camBase; Material waterMat;
    readonly HashSet<Block> aimHits = new HashSet<Block>();
    Text aimCount; Image[] starPreview; float movesPulse;
    // "show" extras: slow-mo, camera punch, Duck Rush, intro card, tutorial hand, coin flights
    float introMax = 1.8f;
    float slowTimer, camZoom, introTimer, rushTimer, popAnim, displayScore, scorePulse, bubbleTimer;
    int rushLeft, rushCoins, movesAtClear = -1;
    class Dying { public Transform t; public float life; public Vector3 s0; }
    readonly List<Dying> dying = new List<Dying>();
    class Coin { public RectTransform rt; public Vector2 from; public float life; }
    readonly List<Coin> coins = new List<Coin>();
    RectTransform introRoot, handRt; Text introTitle, introBody;

    // ---------- UI ----------
    RectTransform hud, feverFill, bossFill, bossBar, hintPanel, popupRoot;
    Text movesTxt, goalsTxt, levelTxt, scoreTxt, feverTxt, streakTxt, hintTxt, comboTxt, bigTxt, bossTxt, popTitle, popBody, popStars, popReward;
    Button popA, popB; float bigLife; Image[] popStarImgs;
    readonly Dictionary<string, Text> boostTxt = new Dictionary<string, Text>();
    readonly Dictionary<string, Image> boostImg = new Dictionary<string, Image>();
    Transform aimRoot; readonly List<Transform> aimDots = new List<Transform>(); Transform aimX; LineRenderer band;

    // =====================================================================
    public void Init(Camera c, Transform canvas)
    {
        cam = c;
        BuildHud(canvas);
        hud.gameObject.SetActive(false);
    }

    public void StartLevel(int n)
    {
        Active = true; hud.gameObject.SetActive(true);
        Build(n);
    }

    public void Close()
    {
        Active = false;
        if (hud != null) hud.gameObject.SetActive(false);
        if (world != null) Destroy(world.gameObject);
        if (fx != null) Destroy(fx.gameObject);
        world = fx = null;
    }

    public void Restart() => StartLevel(level);

    // =====================================================================
    // LEVEL BUILD
    // =====================================================================
    void Build(int n)
    {
        Close(); Active = true; hud.gameObject.SetActive(true);
        level = n;
        var L = LevelLibrary.Get(n);
        world = new GameObject("World").transform; fx = new GameObject("FX").transform;
        ducks.Clear(); blocks.Clear(); bumpers.Clear(); timers.Clear(); bits.Clear(); bitPool.Clear(); bitPools.Clear(); rings.Clear(); zaps.Clear(); dying.Clear();
        foreach (var c in coins) if (c.rt) Destroy(c.rt.gameObject);
        coins.Clear(); slowTimer = camZoom = rushTimer = 0; rushLeft = rushCoins = 0; movesAtClear = -1; displayScore = 0;
        foreach (var p in pops) if (p.t) Destroy(p.t.gameObject);
        pops.Clear();
        moves = startMoves = L.moves; state = "play"; moving = false; shotSmash = streak = pushes = score = 0;
        frenzyShot = bossKilled = rewarded = false; target = null; hint = L.hint; freeze = shake = 0; drag = null;

        FxKit.Clear();
        BuildTub();
        foreach (var d in L.ducks) NewDuck(new Vector2(d.x, d.y), d.kind);
        foreach (var d in L.blocks)
        {
            var b = new Block
            {
                type = d.type, p = new Vector2(d.x, d.y), w = d.w, h = d.h, r = d.r, rect = d.rect || d.type == "plank" || d.type == "pixel",
                dir = d.dir, color = d.color, pid = d.pid, mvCx = d.x, mvAmp = d.mvAmp, mvSp = d.mvSpeed, shelf = d.shelf,
            };
            b.hp = b.max = d.hp > 0 ? d.hp : HP(d.type);
            blocks.Add(b);
        }
        // planks: link screws, mark blocks under them, drop blocks overlapping screws
        var screws = blocks.Where(b => b.type == "screw").ToList();
        blocks.RemoveAll(b => b.type != "screw" && b.type != "plank" && screws.Any(s => Vector2.Distance(s.p, b.p) < s.r + 14));
        foreach (var pl in blocks.Where(b => b.type == "plank").ToList())
        {
            pl.screws = screws.Where(s => s.pid == pl.pid).ToList();
            foreach (var s in pl.screws) s.plankRef = pl;
            foreach (var b in blocks)
                if (b.type != "screw" && b.type != "plank" && Mathf.Abs(b.p.x - pl.p.x) < pl.w / 2 + 4 && Mathf.Abs(b.p.y - pl.p.y) < pl.h / 2 + 4) b.under = pl;
        }
        blocks.Sort((a, b) => (a.type == "screw" ? 0 : 1).CompareTo(b.type == "screw" ? 0 : 1));
        // golden bonus cookies (+1 move): a reason to aim at a specific spot
        if (n >= 4)
        {
            var rng = new System.Random(n * 31 + 7);
            var pool = blocks.Where(b => (b.type == "cookie" || b.type == "soap" || b.type == "glass") && b.under == null).ToList();
            int golds = Mathf.Min(pool.Count / 12, 1);
            for (int i = 0; i < golds; i++) { var g = pool[rng.Next(pool.Count)]; pool.Remove(g); g.type = "gold"; }
        }
        // build-in animation: blocks drop onto the water row by row
        foreach (var b in blocks) { b.appear = 0; b.delay = 0.15f + (b.p.y - TY) / TH * 0.7f + UnityEngine.Random.value * 0.08f; }
        foreach (var b in blocks) MakeView(b);
        swoop = 1f; flashA = 0; piggyGain = 0; levelArt = L.art; hudTick = 0; comboShown = -1;
        bossBlock = blocks.FirstOrDefault(b => b.type == "boss");
        BuildLines();
        foreach (var bd in L.bumpers)
        {
            var bu = new Bumper { x = bd.x, y = bd.y, w = bd.w, h = bd.h };
            bu.view = Art.Bumper(world, bd.w * K, bd.h * K); bu.view.position = W(bd.x + bd.w / 2, bd.y + bd.h / 2);
            bumpers.Add(bu);
        }
        BuildAim();
        ShowIntro(L);
        popupRoot.gameObject.SetActive(false);
        bossBar.gameObject.SetActive(L.boss);
        FitCamera(true);
        RefreshHud();
    }

    static int HP(string t)
    {
        switch (t)
        {
            case "crate": case "ice": case "vase": return 2;
            case "plates": return 3;
            case "chest": return 3;
            case "stone": case "plank": return 999999;
            case "boss": return 30;
            default: return 1;
        }
    }

    void BuildTub()
    {
        // floor outside
        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(ground.GetComponent<Collider>());
        ground.transform.SetParent(world, false); ground.transform.position = new Vector3(0, -0.35f, 0); ground.transform.eulerAngles = new Vector3(90, 0, 0); ground.transform.localScale = new Vector3(90, 90, 1);
        var gm = Art.Toon(Color.white, Art.TileTex(new Color(1f, 0.93f, 0.9f), new Color(0.93f, 0.82f, 0.82f)), new Vector2(22.5f, 22.5f), 0f);
        ground.GetComponent<Renderer>().sharedMaterial = gm;
        // tub base (white porcelain)
        var porcelain = Art.Mat(new Color(0.96f, 0.97f, 1f), 0.85f, 0, 0, 0.0025f);
        var porcelainFlat = Art.Mat(new Color(0.96f, 0.97f, 1f), 0.85f, 0, 0, 0f);
        float w = TW * K, h = TH * K, rim = 0.36f;
        Art.Prim(PrimitiveType.Cube, world, new Vector3(0, -0.2f, 0), new Vector3(w + rim * 2, 0.3f, h + rim * 2), porcelain);
        // water
        var water = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(water.GetComponent<Collider>());
        water.transform.SetParent(world, false); water.transform.position = new Vector3(0, -0.04f, 0); water.transform.eulerAngles = new Vector3(90, 0, 0);
        water.transform.localScale = new Vector3(w, h, 1);
        var wm = Art.Toon(Color.white, Art.WaterTex(), new Vector2(TW / 260f, TH / 260f), 0.3f);
        waterMat = wm;
        water.GetComponent<Renderer>().sharedMaterial = wm;
        // rim walls
        float rh = 0.5f;
        // rounded porcelain lips (capsules) along each edge
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(0, rh / 2, h / 2 + rim / 2), new Vector3(rh, (w + rim * 2) / 2, rim * 1.3f), porcelain, new Vector3(0, 0, 90));
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(0, rh / 2, -h / 2 - rim / 2), new Vector3(rh, (w + rim * 2) / 2, rim * 1.3f), porcelain, new Vector3(0, 0, 90));
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(w / 2 + rim / 2, rh / 2, 0), new Vector3(rim * 1.3f, (h + rim * 2) / 2, rh), porcelain, new Vector3(90, 0, 0));
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(-w / 2 - rim / 2, rh / 2, 0), new Vector3(rim * 1.3f, (h + rim * 2) / 2, rh), porcelain, new Vector3(90, 0, 0));
        // rounded corner fillers (visual only)
        float cr = TR * K;
        foreach (var s in new[] { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, -1) })
        {
            var c = new Vector3(s.x * (w / 2 - cr), 0, s.y * (h / 2 - cr));
            for (int i = 0; i <= 6; i++)
            {
                float a = i / 6f * Mathf.PI / 2;
                var dir = new Vector3(s.x * Mathf.Cos(a), 0, s.y * Mathf.Sin(a));
                // fill the corner outside the arc with porcelain blocks
                var pos = c + dir * (cr + 0.12f);
                Art.Prim(PrimitiveType.Cylinder, world, pos + Vector3.up * 0.2f, new Vector3(0.36f, 0.25f, 0.36f), porcelainFlat);
            }
            Art.Prim(PrimitiveType.Cube, world, new Vector3(s.x * (w / 2 - cr * 0.3f), 0.2f, s.y * (h / 2 - cr * 0.3f)), new Vector3(cr * 0.6f, 0.5f, cr * 0.6f), porcelainFlat);
            // soap foam clusters in the corners
            var foam = Art.Mat(new Color(1f, 1f, 1f), 0.9f);
            var fc = new Vector3(s.x * (w / 2 - 0.35f), 0.02f, s.y * (h / 2 - 0.35f));
            for (int i = 0; i < 6; i++)
            {
                var o = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0, UnityEngine.Random.Range(-0.3f, 0.3f));
                float sz = UnityEngine.Random.Range(0.12f, 0.26f);
                Art.Prim(PrimitiveType.Sphere, world, fc + o, new Vector3(sz, sz * 0.6f, sz), foam);
            }
        }
        // chrome faucet on the top rim
        var chrome = Art.Mat(new Color(0.86f, 0.9f, 0.96f), 1f, 1, 0, 0.003f);
        float zt = h / 2 + rim / 2;
        Art.Prim(PrimitiveType.Cylinder, world, new Vector3(0, 0.6f, zt), new Vector3(0.28f, 0.2f, 0.28f), chrome);
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(0, 0.82f, zt - 0.3f), new Vector3(0.16f, 0.36f, 0.16f), chrome, new Vector3(90, 0, 0));
        Art.Prim(PrimitiveType.Cylinder, world, new Vector3(0, 0.72f, zt - 0.62f), new Vector3(0.14f, 0.08f, 0.14f), chrome);
        foreach (int sx in new[] { -1, 1 })
        {
            Art.Prim(PrimitiveType.Cylinder, world, new Vector3(sx * 0.55f, 0.62f, zt), new Vector3(0.16f, 0.12f, 0.16f), chrome);
            Art.Prim(PrimitiveType.Sphere, world, new Vector3(sx * 0.55f, 0.78f, zt), new Vector3(0.24f, 0.12f, 0.24f), Art.Mat(sx < 0 ? new Color(1f, 0.45f, 0.45f) : new Color(0.45f, 0.7f, 1f), 0.9f, 0, 0, 0.003f));
        }
        // soap bar on the right rim
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(w / 2 + rim / 2, 0.62f, h * 0.18f), new Vector3(0.3f, 0.22f, 0.22f), Art.Mat(new Color(1f, 0.72f, 0.82f), 0.9f, 0, 0, 0.003f), new Vector3(90, 0, 90));
        // deeper-looking water toward the walls
        var depth = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(depth.GetComponent<Collider>());
        depth.transform.SetParent(world, false); depth.transform.position = new Vector3(0, -0.03f, 0); depth.transform.eulerAngles = new Vector3(90, 0, 0);
        depth.transform.localScale = new Vector3(w, h, 1);
        var dm = Art.Fade(Color.white); dm.mainTexture = Art.EdgeShadeTex(w / h); depth.GetComponent<Renderer>().sharedMaterial = dm;
        depth.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        BuildDecor(w, h, rim);
    }

    // bathroom props around the tub (baked into one mesh -> 1 draw call)
    void BuildDecor(float w, float h, float rim)
    {
        var root = Art.Root("Decor", world);
        float top = h / 2 + rim + 1.0f, bottom = -h / 2 - rim - 1.3f;
        // fluffy bath mat with stripes below the tub
        var mat = Art.Mat(new Color(0.62f, 0.84f, 0.95f), 0.1f);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, -0.3f, bottom), new Vector3(6.4f, 0.12f, 2.0f), mat);
        foreach (float x in new[] { -3.2f, 3.2f }) Art.Prim(PrimitiveType.Cylinder, root, new Vector3(x, -0.3f, bottom), new Vector3(2.0f, 0.06f, 2.0f), mat);
        var stripe = Art.Mat(new Color(1f, 1f, 1f), 0.1f);
        for (int i = -2; i <= 2; i++) Art.Prim(PrimitiveType.Cube, root, new Vector3(i * 1.2f, -0.235f, bottom), new Vector3(0.28f, 0.02f, 1.7f), stripe);
        // shelf behind the faucet: bottles, cup, plant, towels
        var shelf = Art.Mat(new Color(0.98f, 0.95f, 0.92f), 0.6f);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, -0.1f, top + 0.3f), new Vector3(w + 2.5f, 0.5f, 1.6f), shelf);
        void Bottle(float x, Color body, Color cap, float hgt)
        {
            Art.Prim(PrimitiveType.Cylinder, root, new Vector3(x, 0.15f + hgt / 2, top), new Vector3(0.55f, hgt / 2, 0.55f), Art.Mat(body, 0.8f));
            Art.Prim(PrimitiveType.Sphere, root, new Vector3(x, 0.15f + hgt, top), new Vector3(0.55f, 0.3f, 0.55f), Art.Mat(body, 0.8f));
            Art.Prim(PrimitiveType.Cylinder, root, new Vector3(x, 0.32f + hgt, top), new Vector3(0.26f, 0.14f, 0.26f), Art.Mat(cap, 0.7f));
            Art.Prim(PrimitiveType.Cube, root, new Vector3(x, 0.15f + hgt * 0.5f, top - 0.27f), new Vector3(0.34f, hgt * 0.45f, 0.02f), Art.Mat(Color.white, 0.3f));   // label
        }
        Bottle(-3.4f, new Color(0.55f, 0.82f, 1f), new Color(0.25f, 0.45f, 0.85f), 1.2f);
        Bottle(-2.7f, new Color(1f, 0.7f, 0.82f), new Color(0.9f, 0.35f, 0.55f), 0.9f);
        Bottle(2.6f, new Color(0.7f, 0.92f, 0.6f), new Color(0.35f, 0.65f, 0.3f), 1.05f);
        // toothbrush cup
        Art.Prim(PrimitiveType.Cylinder, root, new Vector3(-1.6f, 0.45f, top), new Vector3(0.45f, 0.3f, 0.45f), Art.Mat(new Color(1f, 0.85f, 0.4f), 0.7f));
        Art.Prim(PrimitiveType.Cylinder, root, new Vector3(-1.68f, 0.95f, top), new Vector3(0.07f, 0.35f, 0.07f), Art.Mat(new Color(0.4f, 0.75f, 1f), 0.6f), new Vector3(0, 0, 12));
        Art.Prim(PrimitiveType.Cylinder, root, new Vector3(-1.5f, 0.95f, top), new Vector3(0.07f, 0.35f, 0.07f), Art.Mat(new Color(1f, 0.5f, 0.6f), 0.6f), new Vector3(0, 0, -10));
        // potted plant on the right
        Art.Prim(PrimitiveType.Cylinder, root, new Vector3(3.6f, 0.5f, top), new Vector3(0.8f, 0.35f, 0.8f), Art.Mat(new Color(0.93f, 0.55f, 0.4f), 0.4f));
        var leaf = Art.Mat(new Color(0.45f, 0.8f, 0.45f), 0.5f); var leaf2 = Art.Mat(new Color(0.35f, 0.68f, 0.4f), 0.5f);
        for (int i = 0; i < 7; i++)
        {
            float a = i / 7f * Mathf.PI * 2;
            Art.Prim(PrimitiveType.Sphere, root, new Vector3(3.6f + Mathf.Cos(a) * 0.35f, 1.2f + (i % 2) * 0.2f, top + Mathf.Sin(a) * 0.3f), new Vector3(0.35f, 0.75f, 0.2f), i % 2 == 0 ? leaf : leaf2, new Vector3(Mathf.Sin(a) * 30, -a * Mathf.Rad2Deg, Mathf.Cos(a) * 30));
        }
        // rolled towels
        var towelA = Art.Mat(new Color(1f, 0.78f, 0.86f), 0.1f); var towelB = Art.Mat(new Color(0.75f, 0.88f, 1f), 0.1f);
        Art.Prim(PrimitiveType.Capsule, root, new Vector3(1.2f, 0.45f, top), new Vector3(0.6f, 0.55f, 0.6f), towelA, new Vector3(0, 0, 90));
        Art.Prim(PrimitiveType.Capsule, root, new Vector3(1.6f, 0.45f, top - 0.1f), new Vector3(0.6f, 0.55f, 0.6f), towelB, new Vector3(0, 0, 90));
        Art.Prim(PrimitiveType.Capsule, root, new Vector3(1.4f, 0.95f, top - 0.05f), new Vector3(0.55f, 0.5f, 0.55f), towelA, new Vector3(0, 0, 90));
        // slippers on the mat
        var slip = Art.Mat(new Color(1f, 0.85f, 0.45f), 0.3f);
        foreach (float x in new[] { 3.6f, 4.3f }) Art.Prim(PrimitiveType.Capsule, root, new Vector3(x, -0.15f, bottom + 0.2f), new Vector3(0.5f, 0.55f, 0.25f), slip, new Vector3(90, 0, 0));
        Art.Bake(root, "decor", 0.5f, 0.002f);
    }

    void MakeView(Block b)
    {
        float d = b.r * 2 * K;
        switch (b.type)
        {
            case "cookie": b.view = Art.Cookie(world, d); break;
            case "gold": b.view = Art.GoldCookie(world, d); break;
            case "glass": b.view = Art.Glass(world, d); break;
            case "vase": b.view = Art.Vase(world, d); break;
            case "soap": b.view = Art.Soap(world, d); break;
            case "plates": b.view = Art.Plates(world, d); break;
            case "bathbomb": b.view = Art.BathBomb(world, d); break;
            case "domino": b.view = Art.Domino(world, b.w * K, b.h * K); break;
            case "ice": b.view = Art.Ice(world, d); break;
            case "crate": b.view = Art.Crate(world, b.w * K, b.h * K); break;
            case "chest": b.view = Art.Chest(world, b.w * K, b.h * K); break;
            case "tnt": b.view = Art.Tnt(world, d); break;
            case "stone": b.view = Art.Stone(world, b.w * K, b.h * K); break;
            case "laser": b.view = Art.Laser(world, d, b.dir == "h"); break;
            case "screw": b.view = Art.Screw(world, d); break;
            case "plank": b.view = Art.Plank(world, b.w * K, b.h * K); break;
            case "pixel": b.view = Art.Pixel(world, b.w * K, b.color); break;
            case "boss": b.view = Art.Boss(world, d); break;
        }
        // merge the primitive parts into one shared mesh per block kind (huge draw-call saving)
        string key = b.type + "_" + Mathf.RoundToInt(b.r * 2) + "_" + Mathf.RoundToInt(b.w) + "x" + Mathf.RoundToInt(b.h)
                     + (b.type == "pixel" ? ColorUtility.ToHtmlStringRGB(b.color) : "") + (b.type == "laser" ? b.dir : "");
        float outline = b.type == "stone" || b.type == "screw" ? 0.003f : 0f;
        float gloss = b.type == "screw" ? 0.9f : b.type == "chest" ? 0.7f : 0.4f;
        Art.Bake(b.view, key, gloss, outline, "Spark", "IceShell", "Arrow", "Glow", "Plate1", "Plate2", "Plate3");
        b.view.position = W(b.p);
        b.baseScale = b.view.localScale;
        b.rends = b.view.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.activeSelf && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Emission")).ToArray();
        if (App.Mobile) foreach (var r in b.view.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // phones: only ducks + decor cast shadows
    }

    Duck NewDuck(Vector2 p, string kind)
    {
        var d = new Duck { p = p, kind = kind, special = kind == "rocket" || kind == "rainbow" };
        BuildDuckView(d);
        ducks.Add(d);
        return d;
    }

    void BuildDuckView(Duck d)
    {
        if (d.view) Destroy(d.view.gameObject);
        d.r = d.kind == "heavy" ? 20 : 17;
        d.view = Art.Duck(world, d.kind, out d.ring);
        d.body = d.view.Find("Body");
        Art.Bake(d.body, "duck_" + d.kind, d.kind == "heavy" ? 0.9f : 0.75f, 0.002f, "Spark");
        d.view.localScale = Vector3.one * d.r * 2 * K;
        d.view.position = W(d.p);
        // water streak behind a fast duck
        if (trailMat == null) trailMat = new Material(Shader.Find("Sprites/Default"));
        var tg = new GameObject("Trail"); tg.transform.SetParent(d.view, false); tg.transform.localPosition = new Vector3(0, 0.15f, -0.2f);
        var tr = tg.AddComponent<TrailRenderer>(); tr.sharedMaterial = trailMat; tr.time = 0.28f; tr.minVertexDistance = 0.08f;
        tr.widthCurve = new AnimationCurve(new Keyframe(0, 0.42f), new Keyframe(1, 0f)); tr.numCapVertices = 2;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tr.receiveShadows = false;
        var top = d.kind == "rocket" ? new Color(1f, 0.7f, 0.25f, 0.85f) : d.kind == "rainbow" ? new Color(1f, 0.6f, 0.9f, 0.8f) : new Color(1f, 1f, 1f, 0.6f);
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(top, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(top.a, 0), new GradientAlphaKey(0, 1) });
        tr.colorGradient = g; tr.emitting = false; d.trail = tr;
    }

    // turn a duck into another kind with a little "poof" (special hatch / special used up)
    void SetKind(Duck d, string kind, bool special)
    {
        d.kind = kind; d.special = special; d.spent = false; d.charged = true;
        BuildDuckView(d);
        d.squash = 0.25f; FxKit.Smoke(W(d.p), 0.6f, 6); FxKit.Sparkle(W(d.p), Color.white, 8);
    }

    // =====================================================================
    // UPDATE
    // =====================================================================
    void Update()
    {
        if (!Active) return;
        FitCamera(false);
        float rdt = Mathf.Min(Time.unscaledDeltaTime, 0.033f) * (Time.timeScale > 0 ? 1 : 0);
        UpdateShow(rdt);
        HandleInput();
        slowTimer = Mathf.Max(0, slowTimer - rdt);
        Tick(Mathf.Min(Time.deltaTime, 0.033f) * (slowTimer > 0 ? 0.3f : 1f));
    }

    // ---------- debug hooks (used for automated testing via MCP) ----------
    public void DebugShoot(int duckIndex, float vx, float vy) { introTimer = 0; introRoot.gameObject.SetActive(false); if (state == "play" && !moving && duckIndex < ducks.Count) Launch(ducks[duckIndex], new Vector2(vx, vy)); }
    public void DebugRun(float seconds) { for (float s = 0; s < seconds; s += 1 / 60f) Tick(1 / 60f); }
    public string DebugInfo() => $"lvl {level} state {state} moves {moves} moving {moving} goals {GoalsLeft()} smash {shotSmash} ducks {ducks.Count} fever {fever:0} streak {streak}";
    public int DebugDuckCount => ducks.Count;
    public Vector2 DebugDuckPos(int i) => ducks[i].p;
    public Vector2 DebugRandomTarget() { var a = blocks.Where(b => b.alive && !Covered(b) && !b.Hard).ToList(); return a.Count == 0 ? new Vector2(270, 300) : a[UnityEngine.Random.Range(0, a.Count)].p; }
    public bool DebugMoving => moving;
    public string DebugState => state;

    void Tick(float dt)
    {
        t += dt;
        if (freeze > 0) freeze -= dt;
        else
        {
            for (int i = timers.Count - 1; i >= 0; i--) { var tm = timers[i]; tm.t -= dt; if (tm.t <= 0) { timers.RemoveAt(i); tm.fn(); } }
            foreach (var b in blocks)
            {
                if (b.falling > 0) b.falling += dt;
                if (b.mvAmp > 0 && b.alive) b.p.x = b.mvCx + Mathf.Sin(t * b.mvSp) * b.mvAmp;
            }
            foreach (var d in ducks) { d.zapCd = Mathf.Max(0, d.zapCd - dt); d.bankCd = Mathf.Max(0, d.bankCd - dt); }
            if (moves > 1) lastWarned = false;
            if (state == "play" && !moving && moves == 1 && timers.Count == 0 && !lastWarned && GoalsLeft() > 0)
            {   // make the final shot feel tense
                lastWarned = true; BigText("LƯỢT CUỐI!", new Color(1f, 0.4f, 0.35f)); Sfx.Play("squeak", 0.7f, 0.7f); movesPulse = 0.5f;
            }
            if (state == "play" || state == "rush" || moving) for (int i = 0; i < 6; i++) Step(dt / 6f);
            if (moving && timers.Count == 0 && ducks.All(d => d.v.magnitude < 14)) { foreach (var d in ducks) d.v = Vector2.zero; EndShot(); }
            if (state == "play" && !moving && timers.Count == 0)
            {
                if (GoalsLeft() == 0) StartRush();
                else if (moves <= 0) Lose();
            }
            if (state == "rush") RushTick(dt);
        }
        camZoom = Mathf.Max(0, camZoom - dt * 1.4f);
        swoop = Mathf.Max(0, swoop - dt / 1.3f);
        float sw = swoop * swoop * (3 - 2 * swoop);   // smoothstep ease: camera glides in at level start
        cam.fieldOfView = 36f * (1 - camZoom * 0.1f + sw * 0.18f);
        bubbleTimer -= dt;
        if (bubbleTimer <= 0) { bubbleTimer = 0.25f; FxKit.Bubble(W(UnityEngine.Random.Range(TX + 20, TX + TW - 20), UnityEngine.Random.Range(TY + 20, TY + TH - 20))); }
        shake = Mathf.Max(0, shake - dt); comboPulse = Mathf.Max(0, comboPulse - dt);
        if (waterMat != null) waterMat.mainTextureOffset = new Vector2(Mathf.Sin(t * 0.15f) * 0.05f, t * 0.02f);
        UpdateViews(dt);
        UpdateFx(dt);
        UpdateAim();
        RefreshHud();
        cam.transform.position = camBase + (shake > 0 ? UnityEngine.Random.insideUnitSphere * shake * 0.45f : Vector3.zero);
    }

    int GoalsLeft() => blocks.Count(b => b.alive && b.Goal);
    void Later(float t, Action fn) => timers.Add(new Timer { t = t, fn = fn });
    static bool Covered(Block b) => b.under != null && b.under.alive;
    void Shake(float a) => shake = Mathf.Max(shake, a);

    // ---------- physics ----------
    struct Hit { public Vector2 n; public float pen; }

    static bool CircleRect(Vector2 p, float r, Block b, out Hit hit)
    {
        hit = default;
        float hx = b.w / 2, hy = b.h / 2;
        float cx = Mathf.Clamp(p.x, b.p.x - hx, b.p.x + hx), cy = Mathf.Clamp(p.y, b.p.y - hy, b.p.y + hy);
        var d = new Vector2(p.x - cx, p.y - cy); float dist = d.magnitude;
        if (dist >= r) return false;
        if (dist == 0)
        {
            float ox = hx - Mathf.Abs(p.x - b.p.x), oy = hy - Mathf.Abs(p.y - b.p.y);
            if (ox < oy) hit = new Hit { n = new Vector2(Mathf.Sign(p.x - b.p.x), 0), pen = ox + r };
            else hit = new Hit { n = new Vector2(0, Mathf.Sign(p.y - b.p.y)), pen = oy + r };
            return true;
        }
        hit = new Hit { n = d / dist, pen = r - dist };
        return true;
    }
    static bool CircleRect(Vector2 p, float r, Bumper b, out Hit hit)
    {
        var tmp = new Block { p = new Vector2(b.x + b.w / 2, b.y + b.h / 2), w = b.w, h = b.h };
        return CircleRect(p, r, tmp, out hit);
    }
    static bool CircleCircle(Vector2 p, float r, Vector2 c, float rc, out Hit hit)
    {
        hit = default; var d = p - c; float dist = d.magnitude;
        if (dist >= r + rc || dist == 0) return false;
        hit = new Hit { n = d / dist, pen = r + rc - dist }; return true;
    }
    static bool Collide(Vector2 p, float r, Block b, out Hit hit) => b.rect ? CircleRect(p, r, b, out hit) : CircleCircle(p, r, b.p, b.r, out hit);
    static void Reflect(ref Vector2 v, Vector2 n, float rest) { float vn = Vector2.Dot(v, n); if (vn < 0) v -= (1 + rest) * vn * n; }

    // rounded-rect tub walls
    static bool TubWalls(ref Vector2 p, ref Vector2 v, float r, float rest = 0.94f)
    {
        float L = TX + r, R = TX + TW - r, T = TY + r, B = TY + TH - r, cr = TR; bool hit = false;
        Vector2[] corners = { new Vector2(TX + cr, TY + cr), new Vector2(TX + TW - cr, TY + cr), new Vector2(TX + cr, TY + TH - cr), new Vector2(TX + TW - cr, TY + TH - cr) };
        foreach (var c in corners)
        {
            bool inX = c.x < 270 ? p.x < c.x : p.x > c.x, inY = c.y < 510 ? p.y < c.y : p.y > c.y;
            if (inX && inY)
            {
                var d = p - c; float dist = d.magnitude, lim = cr - r;
                if (dist > lim) { var n = -d / dist; p = c - n * lim; Reflect(ref v, n, rest); hit = true; }
                return hit;
            }
        }
        if (p.x < L) { p.x = L; v.x = Mathf.Abs(v.x) * rest; hit = true; }
        if (p.x > R) { p.x = R; v.x = -Mathf.Abs(v.x) * rest; hit = true; }
        if (p.y < T) { p.y = T; v.y = Mathf.Abs(v.y) * rest; hit = true; }
        if (p.y > B) { p.y = B; v.y = -Mathf.Abs(v.y) * rest; hit = true; }
        return hit;
    }

    void Step(float dt)
    {
        for (int di = 0; di < ducks.Count; di++)
        {
            var d = ducks[di];
            d.p += d.v * dt;
            float sp = d.v.magnitude;
            if (d.rocketT > 0) { d.rocketT -= dt; if (sp > 1) d.v *= ROCKET_SPEED / sp; }   // rocket: full thrust, no friction
            else if (sp > 0) { float ns = Mathf.Max(0, sp * Mathf.Exp(-DRAG * dt) - (sp < 150 ? 108 : FRICTION) * dt); d.v *= ns / sp; }
            if (TubWalls(ref d.p, ref d.v, d.r) && sp > 200)
            {
                d.squash = 0.15f; Splash(d.p, 5); Sfx.Play("splash", Mathf.Clamp01(sp / 1200f) * 0.6f, UnityEngine.Random.Range(0.9f, 1.15f));
                // bank shot: the shot duck gets a kick off the wall and the rest of the shot scores more
                if (moving && d.hot && !d.clone && sp > 380 && d.bankCd <= 0 && shotBanks < 3)
                {
                    d.bankCd = 0.12f; shotBanks++;
                    d.v *= Mathf.Min(1.1f, MAX_SPEED * 1.1f / Mathf.Max(1, sp));
                    PopText("NẢY BĂNG x" + (shotBanks + 1), d.p, new Color(0.55f, 0.9f, 1f), 44 + shotBanks * 6);
                    Sfx.Play("squeak", 0.5f, 1.2f + shotBanks * 0.15f); FxKit.Sparkle(W(d.p, 0.3f), new Color(0.6f, 0.9f, 1f), 6);
                }
            }
            foreach (var m in bumpers)
                if (CircleRect(d.p, d.r, m, out var bh))
                {
                    d.p += bh.n * bh.pen; Reflect(ref d.v, bh.n, 1f);
                    float s = d.v.magnitude; if (s > 0) d.v *= Mathf.Max(s, 650) / s; m.pulse = 0.2f;
                }
            bool fast = d.v.magnitude > 70;
            foreach (var b in blocks)
            {
                if (!b.alive || b.falling > 0 || Covered(b)) continue;
                if (!Collide(d.p, d.r, b, out var hit)) continue;
                if (b.type == "plank" && fast) foreach (var s in b.screws) if (s.alive && Vector2.Distance(s.p, d.p) < d.r + s.r + 16) Damage(s, 99);
                if (fast && d.kind == "bomb" && d.charged) { d.charged = false; d.spent = true; Explode(d.p, d.special ? 120 : 100, 3); PopText("BÙM!", d.p, new Color(1f, 0.35f, 0.2f), 70); }
                if (fast && d.kind == "zap" && d.zapCd <= 0 && !b.Hard) { d.zapCd = 0.15f; d.spent = true; ZapFrom(b); }
                if (fast && d.rainbowReady) { d.rainbowReady = false; d.spent = true; RainbowBurst(d); }
                if (d.rocketT > 0 && !b.Hard && b.type != "boss")
                {   // rocket drills straight through everything breakable
                    Damage(b, 3); FxKit.Flame(W(b.p)); continue;
                }
                if (d.frenzy && !b.Hard && b.type != "boss") { if (Damage(b, 2)) { d.v *= 0.98f; continue; } }
                bool broke = fast && Damage(b, d.Dmg);
                if (broke && b.type != "chest") { d.v *= b.type == "pixel" ? 0.975f : 0.95f; continue; }
                d.p += hit.n * hit.pen; Reflect(ref d.v, hit.n, b.Hard ? 0.9f : 0.7f);
                if (fast) d.squash = 0.12f;
            }
        }
        // duck vs duck: billiard push
        for (int i = 0; i < ducks.Count; i++)
            for (int j = i + 1; j < ducks.Count; j++)
            {
                var a = ducks[i]; var b = ducks[j];
                if (a.clone || b.clone) continue;
                var dlt = b.p - a.p; float dist = dlt.magnitude, min = a.r + b.r;
                if (dist >= min || dist == 0) continue;
                var n = dlt / dist; float pen = min - dist;
                a.p -= n * pen / 2; b.p += n * pen / 2;
                float vn = Vector2.Dot(b.v - a.v, n);
                if (vn < 0)
                {
                    float j2 = -(1 + 0.96f) * vn / 2; a.v -= j2 * n; b.v += j2 * n;
                    if (-vn > 220)
                    {
                        a.hot = b.hot = true; a.squash = b.squash = 0.2f; var mid = (a.p + b.p) / 2;
                        Splash(mid, 8); FxKit.Sparkle(W(mid), Color.white, 6); Shake(0.15f); freeze = Mathf.Max(freeze, 0.03f); pushes++; shotSmash++; comboPulse = 0.18f;
                        PopText(pushes >= 2 ? "CỐC! x" + pushes : "CỐC!", mid, Color.white, 46 + Mathf.Min(20, pushes * 4));
                        Sfx.Play(pushes % 2 == 1 ? "quack" : "squeak", 0.9f, 1f + pushes * 0.08f);
                    }
                }
            }
    }

    // ---------- damage ----------
    bool Damage(Block b, int dmg)
    {
        if (!b.alive || Covered(b)) return false;
        if (b.Hard) { b.hit = 0.12f; return false; }
        if (b.type == "boss") { dmg = Mathf.Min(dmg, 4); PopText("-" + dmg, b.p + new Vector2(UnityEngine.Random.Range(-40, 40), -60), new Color(1f, 0.35f, 0.2f), 56); Shake(0.2f); Sfx.Play("push", 1, 0.7f); }
        b.hp -= dmg; b.hit = 0.15f;
        if (b.hp <= 0) { DestroyBlock(b); return true; }
        if (b.type == "ice")
        {
            var shell = b.view.Find("IceShell"); if (shell) { Debris(b.p, new Color(0.85f, 0.95f, 1f), 10, 0.5f); Destroy(shell.gameObject); }
            PopText("RẮC!", b.p, new Color(0.75f, 0.92f, 1f), 40); Sfx.Play("ice");
            return false;
        }
        if (b.type == "crate") { b.view.localEulerAngles = new Vector3(0, UnityEngine.Random.Range(-12f, 12f), 0); Debris(b.p, new Color(0.69f, 0.48f, 0.27f), 4, 0.5f); }
        if (b.type == "plates")
        {   // top plate flies off and smashes
            var top = b.view.Find("Plate" + (b.hp + 1));
            if (top) { SpawnBit(top.position, Color.white, 0.3f, 5f, 0.7f, PrimitiveType.Cylinder); Destroy(top.gameObject); }
            Debris(b.p, Color.white, 8, 0.9f); PopText("XOẢNG!", b.p, new Color(0.7f, 0.85f, 1f), 40);
            Sfx.Play("crash", 0.5f, 1.7f); Sfx.Play("ice", 0.4f, 1.6f);
        }
        if (b.type == "vase")
        {   // cracks and wobbles
            b.view.localEulerAngles = new Vector3(UnityEngine.Random.Range(-10f, 10f), 0, UnityEngine.Random.Range(-10f, 10f));
            Debris(b.p, new Color(0.5f, 0.85f, 0.78f), 5, 0.5f); PopText("RẮC!", b.p, new Color(0.6f, 0.95f, 0.85f), 40); Sfx.Play("ice", 0.5f, 0.9f);
        }
        return false;
    }

    void DestroyBlock(Block b)
    {
        b.alive = false; shotSmash++; if (!LevelLibrary.IsSandbox(level)) piggyGain++;
        // Block-Blast style: emptying a whole row / column fires a line clear
        if (lineOf.TryGetValue(b, out var myLines))
            foreach (var ln in myLines)
                if (!ln.done && ln.m.All(x => !x.alive)) { ln.done = true; var L2 = ln; Later(0.04f, () => LineClear(L2)); }
        int pts = 10 * (1 + streak) * (1 + shotBanks); score += pts; comboPulse = 0.18f;   // bank shots multiply score
        if (shotSmash <= 3) PopText("+" + pts, b.p, Color.white, 40);
        Shake(b.type == "pixel" ? 0.06f : 0.12f);
        float pitch = 1f + Mathf.Min(shotSmash, 25) * 0.025f;
        if (b.type == "crate" || b.type == "screw" || b.type == "domino") Sfx.Play("wood", 0.7f, pitch);
        else if (b.type == "glass" || b.type == "plates" || b.type == "vase") { Sfx.Play("ice", 0.75f, pitch * 1.35f); Sfx.Play("crash", 0.35f, pitch * 1.6f); }
        else Sfx.Play("crunch", 0.7f, pitch);
        Sfx.Play("pop", 0.35f, pitch);
        Debris(b.p, BlockColor(b), b.type == "pixel" ? 4 : b.type == "boss" ? 40 : 9, b.type == "boss" ? 1.6f : 1f);
        if (b.view) dying.Add(new Dying { t = b.view, life = 0.12f, s0 = b.view.localScale });
        if (state == "rush") { rushCoins += 2; SpawnCoin(b.p); }
        else if (shotSmash % 4 == 0 || b.type == "chest" || b.type == "boss") SpawnCoin(b.p);
        if (b.Goal && state == "play" && GoalsLeft() == 0)
        {   // last target: slow-motion + camera punch
            slowTimer = 0.75f; camZoom = 1f; BigText("SẠCH BÓNG!", new Color(0.55f, 0.95f, 0.6f)); Sfx.Play("combo", 1f, 1.3f);
        }
        switch (b.type)
        {
            case "tnt": Explode(b.p, 95, 3); break;
            case "glass":   // shatters into a spray of shards
                for (int i = 0; i < 5; i++) SpawnBit(W(b.p, 0.5f), new Color(0.75f, 0.92f, 1f), UnityEngine.Random.Range(0.06f, 0.14f), 6f, 0.7f, PrimitiveType.Cube);
                FxKit.Sparkle(W(b.p, 0.4f), new Color(0.8f, 0.95f, 1f), 8); FxKit.Splash(W(b.p), 6, 1.2f);
                if (UnityEngine.Random.value < 0.3f) PopText("XOẢNG!", b.p, new Color(0.7f, 0.9f, 1f), 44);
                break;
            case "vase":
                for (int i = 0; i < 4; i++) SpawnBit(W(b.p, 0.4f), new Color(0.5f, 0.85f, 0.78f), UnityEngine.Random.Range(0.14f, 0.24f), 5f, 0.8f, PrimitiveType.Sphere);
                if (UnityEngine.Random.value < 0.35f) { SpawnCoin(b.p); PopText("+xu", b.p, new Color(1f, 0.85f, 0.3f), 40); }
                break;
            case "soap":    // pops into a cloud of bubbles
                for (int i = 0; i < 10; i++) FxKit.Bubble(W(b.p + UnityEngine.Random.insideUnitCircle * 20));
                FxKit.Splash(W(b.p), 8, 1.1f); Sfx.Play("pop", 0.6f, 1.4f);
                break;
            case "plates":
                for (int i = 0; i < 4; i++) SpawnBit(W(b.p, 0.3f), Color.white, UnityEngine.Random.Range(0.12f, 0.2f), 6f, 0.7f, PrimitiveType.Cylinder);
                PopText("XOẢNG!", b.p, new Color(0.7f, 0.85f, 1f), 50);
                break;
            case "bathbomb":   // fizzy blast + bubble cloud
                Explode(b.p, 85, 2); PopText("XÌ XÈO!", b.p, new Color(0.85f, 0.7f, 1f), 64);
                for (int i = 0; i < 18; i++) FxKit.Bubble(W(b.p + UnityEngine.Random.insideUnitCircle * 70));
                FxKit.Sparkle(W(b.p), new Color(1f, 0.6f, 0.85f), 10);
                break;
            case "domino":     // knock the neighbours over -> the whole chain falls
            {
                dominoFalls++;
                var next = blocks.Where(o => o.alive && o.type == "domino" && o.pid == b.pid && Vector2.Distance(o.p, b.p) < 34).ToList();
                foreach (var o in next) { var nb = o; Later(0.07f, () => { if (nb.alive) { Damage(nb, 99); Sfx.Play("wood", 0.5f, 1f + Mathf.Min(dominoFalls, 30) * 0.03f); } }); }
                if (next.Count == 0 && dominoFalls > 1)
                {   // end of the line: the last tile slams into whatever is there
                    var at = b.p; Shake(0.3f); AddRing(at, 55, new Color(1f, 0.95f, 0.8f, 0.7f)); PopText("ĐỔ!", at, Color.white, 60);
                    Later(0.05f, () => { foreach (var o in blocks.ToArray()) if (o.alive && o.type != "domino" && Vector2.Distance(o.p, at) < 55) Damage(o, 2); });
                }
                break;
            }
            case "gold":
                if (state == "play")
                {
                    moves++; movesPulse = 0.45f; SpawnCoin(b.p); SpawnCoin(b.p + Vector2.one * 12);
                    PopText("+1 LƯỢT!", b.p + new Vector2(0, -40), new Color(1f, 0.85f, 0.3f), 60);
                    FxKit.StarBurst(W(b.p, 0.4f), 16); Sfx.Play("coin", 0.9f, 1.2f); Sfx.Play("star", 0.6f, 1.4f);
                }
                break;
            case "laser": FireLaser(b); break;
            case "screw":
                Splash(b.p, 10);
                var pl = b.plankRef;
                if (pl != null && pl.falling <= 0 && pl.screws.All(s => !s.alive)) { pl.falling = 0.0001f; PopText("RƠI XUỐNG!", pl.p, new Color(1f, 0.88f, 0.3f), 56); Later(0.35f, () => Crash(pl)); }
                break;
            case "chest":
                freeze = 0.06f; Shake(0.3f); PopText("+2 Vịt!", b.p, new Color(1f, 0.88f, 0.3f), 56); Sfx.Play("coin"); FxKit.StarBurst(W(b.p, 0.5f));
                for (int i = 0; i < 2; i++)
                {
                    var nd = NewDuck(b.p + new Vector2(i == 0 ? -14 : 14, 0), "normal");
                    float a = Mathf.PI / 2 + (i == 0 ? 0.6f : -0.6f); nd.v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 420;
                }
                break;
            case "boss":
                freeze = 0.15f; Shake(0.9f); slowTimer = 1f; Flash(0.7f); camZoom = 1f; BigText("BOSS HẠ GỤC!", new Color(1f, 0.88f, 0.3f)); bossKilled = true; Sfx.Play("boom"); Sfx.Vibrate();
                for (int i = 0; i < 6; i++) { int k = i; Later(k * 0.08f, () => { var q = b.p + UnityEngine.Random.insideUnitCircle * 60; AddRing(q, 70, new Color(1f, 0.7f, 0.3f, 0.7f)); Debris(q, new Color(0.94f, 0.63f, 0.29f), 18, 1.4f); Sfx.Play("boom", 0.5f, 1.2f); }); }
                break;
        }
    }

    static Color BlockColor(Block b)
    {
        switch (b.type)
        {
            case "cookie": case "ice": case "boss": return new Color(0.94f, 0.63f, 0.29f);
            case "crate": return new Color(0.69f, 0.48f, 0.27f);
            case "chest": case "gold": return new Color(1f, 0.8f, 0.25f);
            case "glass": return new Color(0.75f, 0.92f, 1f);
            case "vase": return new Color(0.5f, 0.85f, 0.78f);
            case "soap": return new Color(1f, 0.66f, 0.78f);
            case "plates": return new Color(0.98f, 0.98f, 1f);
            case "bathbomb": return new Color(0.79f, 0.65f, 1f);
            case "domino": return new Color(1f, 0.97f, 0.9f);
            case "tnt": return new Color(0.91f, 0.28f, 0.23f);
            case "laser": return new Color(0.7f, 0.53f, 1f);
            case "screw": return new Color(0.82f, 0.85f, 0.9f);
            case "pixel": return b.color;
            default: return Color.gray;
        }
    }

    void Explode(Vector2 p, float R, int dmg)
    {
        freeze = Mathf.Max(freeze, 0.05f); Shake(0.45f); Flash(0.28f);
        AddRing(p, R * 1.3f, new Color(1f, 0.75f, 0.35f, 0.5f));
        FxKit.Explosion(W(p), R * K * 0.6f);
        Debris(p, new Color(1f, 0.7f, 0.28f), 16, 1.5f); Debris(p, new Color(1f, 0.35f, 0.2f), 10, 1.2f);
        Sfx.Play("boom", 0.8f, UnityEngine.Random.Range(0.9f, 1.1f)); Sfx.Vibrate();
        Later(0.07f, () =>
        {
            foreach (var b in blocks.ToArray()) if (b.alive && Vector2.Distance(b.p, p) < R + 20) Damage(b, dmg);
            PushDucks(p, R + 60, 700);
        });
    }

    void PushDucks(Vector2 p, float R, float force)
    {
        foreach (var d in ducks)
        {
            var dl = d.p - p; float dist = Mathf.Max(1, dl.magnitude);
            if (dist < R) { d.v += dl / dist * (1 - dist / R) * force; d.hot = true; }
        }
    }

    void FireLaser(Block b)
    {
        Shake(0.35f); freeze = Mathf.Max(freeze, 0.04f);
        PopText(b.dir == "h" ? "XOÁ HÀNG!" : "XOÁ CỘT!", b.p, new Color(0.88f, 0.8f, 1f), 56);
        Sfx.Play("laser");
        for (int i = 0; i <= 12; i++) { var q = hzLine(b, i / 12f); FxKit.Sparkle(W(q), new Color(0.8f, 0.7f, 1f), 2); }
        // beam visual
        var beam = Art.Prim(PrimitiveType.Cube, fx, Vector3.zero, Vector3.one, Art.Fade(new Color(0.85f, 0.75f, 1f, 0.9f), 1.5f));
        bool hz = b.dir == "h";
        beam.position = hz ? new Vector3(0, 0.4f, W(b.p).z) : new Vector3(W(b.p).x, 0.4f, 0);
        beam.localScale = hz ? new Vector3(TW * K, 0.15f, 0.35f) : new Vector3(0.35f, 0.15f, TH * K);
        rings.Add(new Ring { t = beam, m = beam.GetComponent<Renderer>().sharedMaterial, life = 0.4f, max = 0.4f, r0 = -1, c = new Color(0.85f, 0.75f, 1f, 0.9f) });
        Later(0.06f, () =>
        {
            foreach (var o in blocks.ToArray())
                if (o.alive && (hz ? Mathf.Abs(o.p.y - b.p.y) < 22 : Mathf.Abs(o.p.x - b.p.x) < 22)) Damage(o, 99);
        });
    }

    static Vector2 hzLine(Block b, float f) => b.dir == "h" ? new Vector2(Mathf.Lerp(TX, TX + TW, f), b.p.y) : new Vector2(b.p.x, Mathf.Lerp(TY, TY + TH, f));

    void Crash(Block pl)
    {
        pl.alive = false; pl.falling = 0; Shake(0.6f); Flash(0.3f); freeze = Mathf.Max(freeze, 0.07f);
        if (pl.view) Destroy(pl.view.gameObject);
        AddRing(pl.p, pl.w * 0.55f, new Color(0.85f, 0.65f, 0.4f, 0.7f));
        for (int i = 0; i < 6; i++) Debris(pl.p + new Vector2(UnityEngine.Random.Range(-pl.w / 2, pl.w / 2), 0), new Color(0.72f, 0.51f, 0.29f), 4, 1.2f);
        int k = 0;
        foreach (var o in blocks.ToArray().OrderBy(o => Mathf.Abs(o.p.x - pl.p.x)))
            if (o.alive && Mathf.Abs(o.p.x - pl.p.x) < pl.w / 2 + 26 && Mathf.Abs(o.p.y - pl.p.y) < pl.h / 2 + 30)
            {
                if (o.under == pl) o.under = null;
                if (pl.shelf) { var it = o; Later(0.03f * k++, () => { if (it.alive) { Damage(it, 99); Splash(it.p, 4); } }); }   // shelf: things tumble off one after another
                else Damage(o, 99);
            }
        if (pl.shelf) { BigText("ĐỔ KỆ!", new Color(1f, 0.75f, 0.4f)); Sfx.Play("crash", 1f, 0.8f); }
        PushDucks(pl.p, pl.w * 0.6f, 600);
        BigText("CRASH!", new Color(1f, 0.7f, 0.3f)); Sfx.Play("crash"); Sfx.Vibrate();
        FxKit.Smoke(W(pl.p), pl.w * K * 0.45f, 16); FxKit.Splash(W(pl.p), 20, 1.6f);
    }

    void ZapFrom(Block from)
    {
        var near = blocks.Where(o => o.alive && o != from && !Covered(o) && !o.Hard && Vector2.Distance(o.p, from.p) < 150)
            .OrderBy(o => Vector2.Distance(o.p, from.p)).Take(3).ToList();
        foreach (var o in near)
        {
            Bolt(from.p, o.p, new Color(0.75f, 0.95f, 1f), 0.08f);
            var target = o; Later(0.05f, () => Damage(target, 1));
        }
        if (near.Count > 0) Sfx.Play("zap", 0.7f);
    }

    void Bolt(Vector2 a, Vector2 b, Color c, float width)
    {
        var go = new GameObject("Zap"); go.transform.SetParent(fx, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = LineMat; lr.startColor = lr.endColor = c;
        lr.widthMultiplier = width; lr.positionCount = 7; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        zaps.Add(new Zap { lr = lr, a = a, b = b, life = 0.25f });
    }

    static Material LineMat { get { if (trailMat == null) trailMat = new Material(Shader.Find("Sprites/Default")); return trailMat; } }

    // ---------- special ducks ----------
    void RainbowBurst(Duck d)
    {
        var targets = blocks.Where(b => b.alive && !Covered(b) && !b.Hard && b.falling <= 0)
            .OrderBy(b => b.Goal ? 0 : 1).ThenBy(_ => UnityEngine.Random.value).Take(16).ToList();
        BigText("CẦU VỒNG!", new Color(1f, 0.55f, 0.85f)); Sfx.Play("frenzy", 0.9f, 1.3f); Shake(0.4f); Flash(0.45f); freeze = Mathf.Max(freeze, 0.06f);
        FxKit.StarBurst(W(d.p, 0.6f), 24); AddRing(d.p, 120, new Color(1f, 0.7f, 0.95f, 0.6f));
        var from = d.p;
        for (int i = 0; i < targets.Count; i++)
        {
            var b = targets[i]; var col = Color.HSVToRGB(i / (float)targets.Count, 0.55f, 1f);
            Later(0.04f * i + 0.05f, () =>
            {
                if (!b.alive) return;
                Bolt(from, b.p, col, 0.1f); FxKit.Sparkle(W(b.p), col, 4);
                Damage(b, 2); Sfx.Play("zap", 0.4f, 1f + UnityEngine.Random.value * 0.4f);
            });
        }
    }

    void HatchSpecial(string kind, string name, Color col)
    {
        // a special never adds a duck: it transforms ONE random plain duck (no plain duck -> no reward)
        var normals = ducks.Where(x => !x.clone && !x.special && x.kind == "normal" && x.spawnT <= 0).ToList();
        if (normals.Count == 0) return;
        var d = normals[UnityEngine.Random.Range(0, normals.Count)];
        SetKind(d, kind, true);
        d.spawnT = 0.45f;   // little hop + splash so the player sees which duck changed
        BigText(name + "!", col); Sfx.Play("star", 1f, 1.3f); Sfx.Play("quack", 0.8f, 1.3f);
        FxKit.StarBurst(W(d.p, 0.8f), 18);
        PopText("COMBO THƯỞNG", d.p + new Vector2(0, -60), col, 40);
    }

    // next locked-in-reach tier for the live combo text; maxed = best unlocked tier already reached
    (string kind, string name, int need, bool maxed) NextTier(int n)
    {
        bool any = false; string best = null;
        for (int i = TIERS.Length - 1; i >= 0; i--)
        {
            if (!LevelLibrary.Unlocked(TIERS[i].kind, level)) continue;
            any = true;
            if (n < TIERS[i].need) return (TIERS[i].kind, TIERS[i].name, TIERS[i].need, false);
            best = TIERS[i].name;
        }
        return any ? (null, best, 0, true) : (null, null, 0, false);
    }

    // ---------- shooting ----------
    void Launch(Duck d, Vector2 v)
    {
        d.v = v; d.hot = true; moves--; moving = true; shotSmash = 0; hint = ""; pushes = 0; frenzyShot = false; shotBanks = 0; shotClears = 0; dominoFalls = 0;
        foreach (var k in ducks) k.charged = true;
        d.stretch = 0.22f; d.pullVis = 0;
        Sfx.Play("shoot"); Sfx.Play("squeak", 0.5f, 1.3f); FxKit.Splash(W(d.p), 6); FxKit.Ripple(W(d.p), 1.1f);
        if (d.special) d.spent = true;
        if (d.kind == "rocket")
        {
            d.rocketT = ROCKET_TIME; d.v = v.normalized * ROCKET_SPEED;
            BigText("PHÓNG!", new Color(1f, 0.55f, 0.2f)); Sfx.Play("boom", 0.6f, 1.4f); Shake(0.25f);
        }
        if (d.kind == "rainbow") d.rainbowReady = true;
        if (frenzyReady)
        {
            frenzyReady = false; fever = 0; frenzyShot = true; d.frenzy = true; BigText("DUCK FRENZY!", new Color(1f, 0.5f, 0.75f)); Shake(0.3f); Sfx.Play("frenzy"); FxKit.StarBurst(W(d.p, 0.5f), 20);
            float baseA = Mathf.Atan2(v.y, v.x), sp = v.magnitude;
            foreach (float off in new[] { -0.3f, -0.15f, 0.15f, 0.3f })
            {
                var c = NewDuck(d.p, "normal"); c.clone = true; c.frenzy = true; c.hot = true;
                c.v = new Vector2(Mathf.Cos(baseA + off), Mathf.Sin(baseA + off)) * sp;
            }
        }
    }

    void EndShot()
    {
        foreach (var d in ducks) { d.hot = false; d.frenzy = false; d.rocketT = 0; d.rainbowReady = false; }
        foreach (var c in ducks.Where(d => d.clone).ToList()) { Splash(c.p, 10); Destroy(c.view.gameObject); ducks.Remove(c); }
        // one-use special ducks that fired turn back into plain ducks
        foreach (var d in ducks.Where(x => x.special && x.spent).ToList()) SetKind(d, "normal", false);
        foreach (var d in ducks) d.spent = false;
        moving = false;
        int n = shotSmash;
        if (n >= 25) BigText("DUCKTASTROPHE!", new Color(1f, 0.35f, 0.2f));
        else if (n >= 15) BigText("MEGA SMASH!", new Color(1f, 0.88f, 0.3f));
        else if (n >= 8) BigText("SMASH!", Color.white);
        // combo reward: hatch a special duck
        if (state == "play" && GoalsLeft() > 0)
        {
            bool given = false;
            foreach (var tr in TIERS) if (n >= tr.need && LevelLibrary.Unlocked(tr.kind, level)) { HatchSpecial(tr.kind, tr.name, tr.col); given = true; break; }
            if (!given && pushes >= ZAP_PUSHES && LevelLibrary.Unlocked("zap", level)) HatchSpecial("zap", "VỊT SÉT", new Color(0.45f, 0.85f, 1f));
        }
        int bonus = n >= 30 ? 1 : 0;
        if (bonus > 0 && state == "play" && GoalsLeft() > 0)
        {
            moves += bonus; movesPulse = 0.35f;
            PopText("+" + bonus + " LƯỢT!", new Vector2(130, 175), new Color(0.45f, 0.95f, 0.5f), 72);
            Sfx.Play("star", 1f, 1.2f); FxKit.StarBurst(W(270, 200, 0.5f), 16);
        }
        if (n >= 6) { streak++; Sfx.Play("combo", 0.8f, 1f + streak * 0.06f); if (streak >= 2) PopText("CHUỖI x" + (streak + 1), new Vector2(270, 620), new Color(1f, 0.7f, 0.28f), 64); }
        else if (streak > 0) { PopText("Mất chuỗi…", new Vector2(270, 620), new Color(0.8f, 0.84f, 0.9f), 44); streak = 0; }
        if (!frenzyShot && !frenzyReady)
        {
            fever = Mathf.Min(FEVER_MAX, fever + n);
            if (fever >= FEVER_MAX) { frenzyReady = true; BigText("FRENZY SẴN SÀNG!", new Color(1f, 0.5f, 0.75f)); }
        }
    }

    // ---------- win / lose ----------
    void Win()
    {
        state = "win";
        int left = movesAtClear >= 0 ? movesAtClear : moves;
        stars = left >= Mathf.CeilToInt(startMoves * 0.5f) ? 3 : left >= Mathf.CeilToInt(startMoves * 0.2f) ? 2 : 1;
        score += left * 150;
        if (!rewarded && LevelLibrary.IsSandbox(level)) { rewarded = true; reward = 0; }   // test levels: no progress saved
        newArt = false; piggyAdded = 0;
        if (!LevelLibrary.IsSandbox(level) && state == "win")
        {
            int before = SaveData.Piggy; SaveData.Piggy = before + piggyGain; piggyAdded = SaveData.Piggy - before; piggyGain = 0;
            if (!string.IsNullOrEmpty(levelArt) && !SaveData.HasArt(levelArt)) { SaveData.SetArt(levelArt); newArt = true; }
        }
        if (!rewarded)
        {
            rewarded = true;
            bool first = SaveData.GetStars(level) == 0;
            reward = 20 + stars * 10 + left * 5 + rushCoins + (bossKilled ? 100 : 0) + (first ? 20 : 0);
            SaveData.Coins += reward; SaveData.SetStars(level, stars);
            SaveData.MaxLevel = Mathf.Min(LevelLibrary.Count, Mathf.Max(SaveData.MaxLevel, level + 1));
            if (bossKilled) foreach (var k in PRICE.Keys) SaveData.SetBoost(k, SaveData.GetBoost(k) + 1);
        }
        Sfx.Play("win");
        FxKit.Confetti(new Vector3(0, 0, 0), TW * K, 160);
        for (int i = 0; i < stars; i++) { int k = i; Later(0.35f + k * 0.25f, () => Sfx.Play("star", 0.8f, 1f + k * 0.15f)); }
        ShowPopup(true);
    }

    // ---------- Duck Rush: leftover moves become free smashing shots ----------
    void StartRush()
    {
        movesAtClear = moves;
        Win();
    }

    void RushTick(float dt)
    {
        if (moving || timers.Count > 0) return;
        rushTimer -= dt; if (rushTimer > 0) return;
        var targets = blocks.Where(b => b.alive && !Covered(b) && !b.Hard).ToList();
        if (rushLeft <= 0 || ducks.Count == 0) { Win(); return; }
        rushLeft--; moves--;
        var d = ducks[UnityEngine.Random.Range(0, ducks.Count)];
        Vector2 dir;
        if (targets.Count > 0) dir = (targets[UnityEngine.Random.Range(0, targets.Count)].p - d.p).normalized;
        else
        {   // board already clean: fireworks shot, each leftover move pays out directly
            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2); dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            rushCoins += 5; SpawnCoin(d.p); SpawnCoin(d.p + Vector2.one * 10); FxKit.StarBurst(W(d.p, 0.5f), 10); Sfx.Play("coin", 0.6f, 1.1f);
        }
        d.v = dir * 1300; d.hot = true; d.frenzy = true; d.charged = true;
        moving = true; shotSmash = 0; pushes = 0;
        Sfx.Play("shoot"); Sfx.Play("squeak", 0.5f, 1.4f); FxKit.Splash(W(d.p), 6);
        PopText("+" + (moves + 1), d.p, new Color(1f, 0.85f, 0.3f), 50);
        rushTimer = 0.12f;
    }

    // ---------- intro card / tutorial hand / coin flights / win anim (real-time) ----------
    void ShowIntro(LevelDef L)
    {
        var g = new List<string>();
        int cookies = blocks.Count(b => b.type == "cookie" || b.type == "ice"), chests = blocks.Count(b => b.type == "chest"), pixels = blocks.Count(b => b.type == "pixel");
        if (L.boss) g.Add("Hạ Boss Vua Bánh");
        if (cookies > 0) g.Add("Phá " + cookies + " bánh");
        if (chests > 0) g.Add("Mở " + chests + " rương");
        if (pixels > 0) g.Add("Phá tranh " + pixels + " ô");
        introTitle.text = L.boss ? "BOSS · Màn " + level : LevelLibrary.Title(level);
        int props = blocks.Count(b => b.Prop);
        if (props > 0) g.Add("Đập vỡ " + props + " món đồ");
        // reminder of the combo ducks the player has already unlocked
        var known = LevelLibrary.Specials.Where(s => LevelLibrary.Unlocked(s.kind, level) && LevelLibrary.NewSpecialAt(level) != s).ToList();
        string combos = known.Count == 0 ? "" : "\n<size=30>" + string.Join("\n", known.Select(s => s.how + " → " + Cap(s.name))) + "</size>";
        introBody.text = string.Join("\n", g) + "\n\n" + moves + " lượt bắn" + combos;
        introMax = introTimer = known.Count >= 3 ? 2.6f : 1.8f;
        introRoot.sizeDelta = new Vector2(760, 520 + known.Count * 40);

        // unlock level: teach how to earn the new duck and how it works, and give one free copy to try
        var nw = LevelLibrary.NewSpecialAt(level);
        if (nw != null)
        {
            introTitle.text = "VỊT MỚI!";
            introBody.text = "<size=56><color=#ff7a3d>" + nw.name + "</color></size>\n" +
                "<size=34><b>Cách mở:</b> " + nw.how + "\n→ 1 vịt thường ngẫu nhiên hoá " + Cap(nw.name) + "\n" +
                "<b>Tác dụng:</b> " + nw.use + "\n(chỉ dùng 1 lần)\n\n" +
                "<color=#3fa34d>Tặng 1 con có vòng vàng — bắn thử ngay!</color></size>\n<size=28>(chạm để bắt đầu)</size>";
            introRoot.sizeDelta = new Vector2(820, 640);
            introMax = introTimer = 6f;
            var pool = ducks.Where(x => x.kind == "normal").ToList(); if (pool.Count == 0) pool = ducks.Where(x => !x.special).ToList();
            if (pool.Count > 0) SetKind(pool[UnityEngine.Random.Range(0, pool.Count)], nw.kind, true);
            hint = "Bắn con " + Cap(nw.name) + " (vòng vàng) để xem sức mạnh! " + nw.how + " để có thêm.";
        }
        introBody.verticalOverflow = VerticalWrapMode.Overflow;
        introRoot.gameObject.SetActive(true);
    }

    // "VỊT TÊN LỬA" -> "Vịt Tên Lửa" (culture-free so it is safe on WebGL)
    static string Cap(string s)
    {
        var c = s.ToLowerInvariant().ToCharArray();
        for (int i = 0; i < c.Length; i++) if (i == 0 || c[i - 1] == ' ') c[i] = char.ToUpperInvariant(c[i]);
        return new string(c);
    }

    void UpdateShow(float rdt)
    {
        // intro card: pops in, then fades out (tap to skip)
        if (introTimer > 0)
        {
            introTimer -= rdt;
            float a = Mathf.Clamp01(introTimer / 0.3f), inS = Mathf.Clamp01((introMax - introTimer) / 0.25f);
            introRoot.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, Mathf.Sin(inS * Mathf.PI * 0.5f));
            foreach (var gr in introRoot.GetComponentsInChildren<Graphic>()) { var c = gr.color; c.a = gr is Text ? a : Mathf.Min(a, 0.96f); gr.color = c; }
            if (introTimer <= 0) introRoot.gameObject.SetActive(false);
        }
        // tutorial hand on the first levels: shows the pull-back gesture
        bool showHand = level <= 2 && state == "play" && !moving && drag == null && introTimer <= 0 && moves == startMoves && ducks.Count > 0;
        handRt.gameObject.SetActive(showHand);
        if (showHand)
        {
            var d = ducks[ducks.Count - 1];
            float k = Mathf.Repeat(Time.unscaledTime, 1.6f) / 1.2f;
            var a = WorldToHud(W(d.p, 0.4f)); var b = a + new Vector2(0, -230);
            handRt.anchoredPosition = Vector2.Lerp(a, b, Mathf.SmoothStep(0, 1, Mathf.Clamp01(k)));
            handRt.localScale = Vector3.one * (k < 0.1f ? 0.85f : 1f);
            handRt.GetComponent<CanvasGroup>().alpha = k > 1 ? 0 : 0.95f;
        }
        // coins flying to the score
        var target = WorldToHudFromRect(scoreTxt.rectTransform);
        for (int i = coins.Count - 1; i >= 0; i--)
        {
            var c = coins[i]; c.life += rdt; float f = Mathf.Clamp01(c.life / 0.6f);
            var mid = (c.from + target) / 2 + new Vector2(-180, 120);
            var p1 = Vector2.Lerp(c.from, mid, f); var p2 = Vector2.Lerp(mid, target, f);
            c.rt.anchoredPosition = Vector2.Lerp(p1, p2, f);
            c.rt.localScale = Vector3.one * (f < 0.15f ? f / 0.15f * 1.3f : Mathf.Lerp(1.3f, 0.7f, f));
            if (f >= 1) { Destroy(c.rt.gameObject); coins.RemoveAt(i); scorePulse = 0.2f; Sfx.Play("coin", 0.25f, 1.2f + UnityEngine.Random.value * 0.2f); }
        }
        scorePulse = Mathf.Max(0, scorePulse - rdt);
        displayScore = Mathf.MoveTowards(displayScore, score, Mathf.Max(30, Mathf.Abs(score - displayScore) * 6) * rdt);
        // win popup: stars pop one by one, coins count up
        if (popupRoot.gameObject.activeSelf && state == "win")
        {
            popAnim += rdt;
            for (int i = 0; i < 3; i++)
            {
                float st = popAnim - 0.35f - i * 0.25f;
                float sc = st <= 0 ? 0 : st < 0.18f ? Mathf.Lerp(0, 1.45f, st / 0.18f) : Mathf.Lerp(1.45f, 1f, Mathf.Clamp01((st - 0.18f) / 0.15f));
                popStarImgs[i].rectTransform.localScale = Vector3.one * sc * (i == 1 ? 1.2f : 1f);
                popStarImgs[i].rectTransform.localEulerAngles = new Vector3(0, 0, st > 0 && st < 0.33f ? (1 - st / 0.33f) * 40 : 0);
            }
            popReward.text = "+" + Mathf.RoundToInt(Mathf.Lerp(0, reward, Mathf.Clamp01((popAnim - 0.4f) / 0.9f))) + " xu";
        }
    }

    void SpawnCoin(Vector2 simPos)
    {
        if (coins.Count > 25) return;
        var img = UIKit.Dot(hud, new Color(1f, 0.82f, 0.25f), "Coin");
        var o = img.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.75f, 0.45f, 0.05f); o.effectDistance = new Vector2(4, -4);
        img.raycastTarget = false;
        var rt = img.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(56, 56);
        var from = WorldToHud(W(simPos, 0.5f)); rt.anchoredPosition = from;
        coins.Add(new Coin { rt = rt, from = from });
    }

    Camera UiCam => App.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam;

    Vector2 WorldToHud(Vector3 world)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(hud, cam.WorldToScreenPoint(world), UiCam, out var lp);
        return lp;
    }

    Vector2 WorldToHudFromRect(RectTransform rt)
    {
        var sp = RectTransformUtility.WorldToScreenPoint(UiCam, rt.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(hud, sp, UiCam, out var lp);
        return lp;
    }

    void Lose() { state = "lose"; Sfx.Play("lose"); ShowPopup(false); }

    // ---------- boosters ----------
    void UseBooster(string k)
    {
        if (moving || state != "play") return;
        if (target == k) { target = null; return; }
        if (SaveData.GetBoost(k) <= 0)
        {
            if (SaveData.Coins >= PRICE[k]) { SaveData.Coins -= PRICE[k]; SaveData.SetBoost(k, 1); PopText("Mua " + BNAME[k] + "!", new Vector2(270, 840), new Color(1f, 0.88f, 0.3f), 44); Sfx.Play("coin"); }
            else { PopText("Không đủ xu!", new Vector2(270, 840), new Color(1f, 0.35f, 0.2f), 44); return; }
        }
        if (k == "moves") { SaveData.SetBoost(k, SaveData.GetBoost(k) - 1); moves += 3; PopText("+3 LƯỢT!", new Vector2(120, 200), new Color(0.4f, 0.9f, 0.4f), 56); return; }
        target = k;
    }

    void ApplyTarget(Vector2 p)
    {
        string k = target; target = null; SaveData.SetBoost(k, SaveData.GetBoost(k) - 1); shotSmash = 0;
        if (k == "bomb") { Explode(p, 120, 3); PopText("ẦM!", p, new Color(0.3f, 0.75f, 1f), 70); Splash(p, 30); return; }
        Block best = null; float bd = 1e9f;
        foreach (var b in blocks)
        {
            if (!b.alive || Covered(b)) continue;
            float dd = Vector2.Distance(b.p, p) - (b.rect ? b.w / 2 : b.r);
            if (dd < bd) { bd = dd; best = b; }
        }
        if (best == null || bd > 30) { SaveData.SetBoost(k, SaveData.GetBoost(k) + 1); PopText("Trượt!", p, Color.white, 40); return; }
        freeze = 0.08f; Shake(0.5f); PopText("BỐP!", best.p, new Color(1f, 0.88f, 0.3f), 70); Sfx.Play("crash", 0.7f, 1.4f);
        if (best.type == "plank") { best.falling = 0.0001f; var pl = best; Later(0.2f, () => Crash(pl)); }
        else if (best.type == "stone") { best.alive = false; Debris(best.p, Color.gray, 10, 1); Destroy(best.view.gameObject); }
        else Damage(best, 99);
    }

    // =====================================================================
    // INPUT
    // =====================================================================
    bool ScreenToSim(Vector3 screen, out Vector2 sim)
    {
        var ray = cam.ScreenPointToRay(screen);
        var plane = new Plane(Vector3.up, new Vector3(0, 0.25f, 0));
        sim = default;
        if (!plane.Raycast(ray, out float dist)) return false;
        var w = ray.GetPoint(dist);
        sim = new Vector2(w.x / K + 270, CY - w.z / K);
        return true;
    }

    void HandleInput()
    {
        if (introTimer > 0) { if (Input.GetMouseButtonDown(0)) introTimer = Mathf.Min(introTimer, 0.25f); return; }
        if (state != "play") return;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject() ||
                      (Input.touchCount > 0 && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId));
        if (Input.GetMouseButtonDown(0) && !overUI && !moving && ScreenToSim(Input.mousePosition, out var p))
        {
            if (target != null) { if (p.y > TY && p.y < TY + TH) ApplyTarget(p); return; }
            Duck best = null; float bd = 1e9f;
            // generous grab radius: fingers are fat
            foreach (var d in ducks) { if (d.spawnT > 0) continue; float dd = Vector2.Distance(d.p, p); if (dd < d.r + 55 && dd < bd) { bd = dd; best = d; } }
            drag = best; dragPt = dragSm = p;
            if (best != null) { best.squash = 0.12f; Sfx.Play("squeak", 0.35f, 1.5f); }
        }
        if (drag != null && Input.GetMouseButton(0) && ScreenToSim(Input.mousePosition, out var q)) dragPt = q;
        if (drag != null && Input.GetMouseButtonUp(0))
        {
            var pv = Pull(); var d = drag; drag = null; d.pullVis = 0;
            if (pv.z >= 18) { float sp = MAX_SPEED * Mathf.Pow(pv.z / MAX_PULL, 0.8f); Launch(d, new Vector2(pv.x, pv.y) * sp); }
        }
    }

    // (dirX, dirY, power)
    Vector3 Pull() => PullFrom(dragPt);
    Vector3 PullFrom(Vector2 pt)
    {
        var dl = drag.p - pt; float l = dl.magnitude;
        if (l < 1) return Vector3.zero;
        return new Vector3(dl.x / l, dl.y / l, Mathf.Min(l, MAX_PULL));
    }

    static Color PowerColor(float pw) => pw < 0.5f ? Color.Lerp(Color.white, new Color(1f, 0.9f, 0.3f), pw / 0.5f)
                                                   : Color.Lerp(new Color(1f, 0.9f, 0.3f), new Color(1f, 0.38f, 0.25f), (pw - 0.5f) / 0.5f);

    void BuildAim()
    {
        aimRoot = Art.Root("Aim", fx); aimDots.Clear(); pushDots.Clear(); aimVisible = true;
        if (mpb == null) mpb = new MaterialPropertyBlock();
        var m = Art.Mat(Color.white, 0.5f, 0, 0.6f, 0.0035f);   // outlined so the dots pop on the bright water
        for (int i = 0; i < 48; i++)
        {
            var dt = Art.Prim(PrimitiveType.Sphere, aimRoot, Vector3.zero, Vector3.one * 0.14f, m);
            dt.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dt.gameObject.SetActive(false); aimDots.Add(dt);
        }
        for (int i = 0; i < 6; i++)
        {
            var dt = Art.Prim(PrimitiveType.Sphere, aimRoot, Vector3.zero, Vector3.one * 0.1f, Art.Mat(new Color(0.55f, 0.9f, 1f), 0.5f, 0, 0.8f));
            dt.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dt.gameObject.SetActive(false); pushDots.Add(dt);
        }
        aimX = Art.Prim(PrimitiveType.Cube, aimRoot, Vector3.zero, new Vector3(0.3f, 0.05f, 0.08f), Art.Mat(Color.white, 0.5f, 0, 1f), new Vector3(0, 45, 0));
        Art.Prim(PrimitiveType.Cube, aimX, Vector3.zero, new Vector3(1, 1, 1), aimX.GetComponent<Renderer>().sharedMaterial, new Vector3(0, 90, 0)).localScale = new Vector3(1, 1, 1);
        aimX.gameObject.SetActive(false);
        band = MakeLine("BandL", 2); band2 = MakeLine("BandR", 2);
        // landing ring: crisp circle where the duck will stop / hit
        aimTarget = Art.Root("Target", aimRoot);
        var ring = aimTarget.gameObject.AddComponent<LineRenderer>(); ring.sharedMaterial = LineMat; ring.useWorldSpace = false; ring.loop = true;
        ring.positionCount = 28; ring.widthMultiplier = 0.09f; ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 28; i++) { float a = i / 28f * Mathf.PI * 2; ring.SetPosition(i, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))); }
        aimTarget.gameObject.SetActive(false);
        // finger knob at the end of the slingshot band
        aimGrab = Art.Prim(PrimitiveType.Sphere, aimRoot, Vector3.zero, new Vector3(0.32f, 0.12f, 0.32f), Art.Mat(Color.white, 0.8f, 0, 0.3f, 0.004f));
        aimGrab.gameObject.SetActive(false);
    }

    LineRenderer MakeLine(string name, int n)
    {
        var go = new GameObject(name); go.transform.SetParent(aimRoot, false);
        var lr = go.AddComponent<LineRenderer>(); lr.sharedMaterial = LineMat; lr.positionCount = n; lr.enabled = false;
        lr.numCapVertices = 4; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return lr;
    }

    readonly List<Vector2> aimPath = new List<Vector2>();
    Vector2 dragDir;

    void UpdateAim()
    {
        if ((drag == null || moving) && !aimVisible) { powerStep = 0; return; }
        aimVisible = drag != null && !moving;
        foreach (var d in aimDots) d.gameObject.SetActive(false);
        foreach (var d in pushDots) d.gameObject.SetActive(false);
        aimX.gameObject.SetActive(false); aimTarget.gameObject.SetActive(false); aimGrab.gameObject.SetActive(false);
        band.enabled = band2.enabled = false; aimCount.gameObject.SetActive(false);
        if (drag == null || moving) { powerStep = 0; return; }
        // smoothed finger: the band and the dotted line glide instead of jittering
        float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        dragSm = Vector2.Lerp(dragSm, dragPt, 1 - Mathf.Exp(-26f * udt));
        var pv = PullFrom(dragSm);
        float pw = pv.z / MAX_PULL;
        drag.pullVis = Mathf.Lerp(drag.pullVis, pv.z < 12 ? 0 : pw, 1 - Mathf.Exp(-20f * udt));
        if (pv.z < 12) { powerStep = 0; return; }
        var dir = new Vector2(pv.x, pv.y); dragDir = dir; drag.face = Mathf.Atan2(dir.y, dir.x);
        int step = Mathf.Min(4, Mathf.FloorToInt(pw * 4.999f));
        if (step != powerStep) { Sfx.Play("click", 0.22f, 0.8f + step * 0.14f); powerStep = step; }
        Color pc = PowerColor(pw);

        // slingshot: two rubber strands from the duck's sides to the finger knob
        var grab = drag.p - dir * (pv.z + 6);
        var side = new Vector2(-dir.y, dir.x) * drag.r * 0.85f;
        float bw = Mathf.Lerp(0.1f, 0.045f, pw);   // stretched band gets thinner
        var bc = Color.Lerp(new Color(1f, 0.55f, 0.6f), new Color(1f, 0.3f, 0.3f), pw);
        SetBand(band, drag.p + side - dir * 4, grab, bc, bw); SetBand(band2, drag.p - side - dir * 4, grab, bc, bw);
        aimGrab.gameObject.SetActive(true); aimGrab.position = W(grab, 0.32f);
        aimGrab.localScale = new Vector3(0.32f, 0.12f, 0.32f) * (1 + Mathf.Sin(Time.unscaledTime * 12) * 0.06f * pw);

        // trajectory: bounce off the walls, carve through what it will break, stop at the first thing it won't
        aimPath.Clear(); aimPath.Add(drag.p);
        Vector2 gp = drag.p, gv = dir; float len = 260 + pv.z * 4.5f, stepL = 5; Duck hitDuck = null;
        aimHits.Clear(); int dmg = drag.kind == "rocket" ? 3 : drag.Dmg; bool pierce = frenzyReady;
        for (float s = 0; s < len; s += stepL)
        {
            gp += gv * stepL;
            if (TubWalls(ref gp, ref gv, drag.r, 1f)) aimPath.Add(gp);
            gv.Normalize();
            foreach (var o in ducks) if (o != drag && Vector2.Distance(o.p, gp) < o.r + drag.r) { hitDuck = o; break; }
            bool stop = hitDuck != null;
            if (!stop)
                foreach (var b in blocks)
                {
                    if (!b.alive || Covered(b) || b.falling > 0 || aimHits.Contains(b) || !Collide(gp, drag.r, b, out _)) continue;
                    bool breaks = !b.Hard && b.type != "boss" && (b.type != "chest" || dmg >= 3) && (pierce ? b.hp <= 2 : b.hp <= dmg);
                    if (breaks) { aimHits.Add(b); if (drag.kind != "rocket") len -= 10; } else { stop = true; break; }
                }
            if (stop) break;
        }
        aimPath.Add(gp);

        // marching dots along the path (they flow toward the target)
        float total = 0; for (int i = 1; i < aimPath.Count; i++) total += Vector2.Distance(aimPath[i - 1], aimPath[i]);
        const float gap = 22f; float at = drag.r + 8 + Mathf.Repeat(Time.unscaledTime * 80f, gap), walked = 0; int used = 0;
        for (int i = 1; i < aimPath.Count && used < aimDots.Count; i++)
        {
            Vector2 a = aimPath[i - 1], b = aimPath[i]; float seg = Vector2.Distance(a, b);
            while (at <= walked + seg && used < aimDots.Count)
            {
                var q = Vector2.Lerp(a, b, seg > 0 ? (at - walked) / seg : 0);
                var dot = aimDots[used++]; dot.gameObject.SetActive(true); dot.position = W(q, 0.32f);
                float f = at / Mathf.Max(1, total);
                dot.localScale = new Vector3(1, 0.6f, 1) * Mathf.Lerp(0.22f, 0.12f, f);
                var c = Color.Lerp(pc, Color.white, f * 0.5f);
                mpb.SetColor("_Color", c); mpb.SetColor("_Emission", c * 0.45f); dot.GetComponent<Renderer>().SetPropertyBlock(mpb);
                at += gap;
            }
            walked += seg;
        }

        // landing / impact ring
        aimTarget.gameObject.SetActive(true); aimTarget.position = W(gp, 0.08f);
        aimTarget.localScale = Vector3.one * drag.r * K * (1.05f + Mathf.Sin(Time.unscaledTime * 9) * 0.06f);
        var tl = aimTarget.GetComponent<LineRenderer>(); tl.startColor = tl.endColor = new Color(pc.r, pc.g, pc.b, 0.9f);
        // billiard preview: where the knocked duck will go
        if (hitDuck != null)
        {
            var n = (hitDuck.p - gp).normalized;
            for (int i = 0; i < pushDots.Count; i++)
            {
                var q = hitDuck.p + n * (hitDuck.r + 14 + i * 20);
                pushDots[i].gameObject.SetActive(true); pushDots[i].position = W(q, 0.32f);
                pushDots[i].localScale = Vector3.one * Mathf.Lerp(0.13f, 0.06f, i / (float)pushDots.Count);
            }
        }
        aimCount.gameObject.SetActive(aimHits.Count > 0);
        if (aimHits.Count > 0)
        {
            aimCount.text = "x" + aimHits.Count;
            aimCount.rectTransform.anchoredPosition = WorldToHud(W(gp, 0.6f)) + new Vector2(0, 70);
            aimCount.color = aimHits.Count >= 10 ? new Color(0.45f, 0.95f, 0.5f) : Color.white;
        }
    }

    static void SetBand(LineRenderer lr, Vector2 a, Vector2 b, Color c, float w)
    {
        lr.enabled = true; lr.SetPosition(0, W(a, 0.34f)); lr.SetPosition(1, W(b, 0.34f));
        lr.startColor = lr.endColor = c; lr.widthMultiplier = w;
    }

    // =====================================================================
    // VIEWS + FX
    // =====================================================================
    void UpdateViews(float dt)
    {
        bool selectable = state == "play" && !moving && target == null;
        foreach (var d in ducks)
        {
            d.squash = Mathf.Max(0, d.squash - dt); d.stretch = Mathf.Max(0, d.stretch - dt);
            float spd = d.v.magnitude;
            if (spd > 30) d.face = Mathf.LerpAngle(d.face * Mathf.Rad2Deg, Mathf.Atan2(d.v.y, d.v.x) * Mathf.Rad2Deg, 1 - Mathf.Exp(-25 * dt)) * Mathf.Deg2Rad;
            bool dragged = d == drag && !moving;
            float pw = dragged ? d.pullVis : 0;
            // hatching special duck drops in from above with a bounce
            float drop = 0;
            if (d.spawnT > 0)
            {
                d.spawnT -= dt; float f = Mathf.Clamp01(d.spawnT / 0.6f); drop = f * f * 6f;
                if (d.spawnT <= 0) { d.squash = 0.3f; Splash(d.p, 14); FxKit.Ripple(W(d.p), 1.3f); Sfx.Play("splash", 0.8f, 1.2f); Shake(0.15f); }
            }
            // while aiming the duck leans back toward the finger and trembles at full power
            var lean = dragged ? -dragDir * pw * 10f : Vector2.zero;
            var jit = dragged && pw > 0.95f ? UnityEngine.Random.insideUnitCircle * 1.2f : Vector2.zero;
            // victory: every duck hops and spins
            float hop = 0;
            if (state == "win") { hop = Mathf.Abs(Mathf.Sin(t * 7f + d.p.x * 0.05f)) * 0.55f; d.face += dt * 4f; }
            d.view.position = W(d.p + lean + jit) + Vector3.up * (Mathf.Sin(t * 3 + d.p.x) * 0.02f + drop + hop);
            // sim y is down, world z is up: direction (cos, -sin)
            d.view.rotation = Quaternion.Euler(0, Mathf.Atan2(Mathf.Cos(d.face), -Mathf.Sin(d.face)) * Mathf.Rad2Deg, 0);
            float s = d.squash * 1.6f, st = d.stretch * 1.8f;
            d.body.localRotation = dragged ? Quaternion.Euler(-pw * 14f, 0, 0)
                : spd < 20 ? Quaternion.Euler(Mathf.Sin(t * 2.3f + d.p.y) * 4, Mathf.Sin(t * 1.7f + d.p.x) * 12, 0) : Quaternion.Euler(Mathf.Min(10, spd / 90f), 0, 0);
            // squash on hits, stretch along the shot on launch, squish down + elongate while being pulled
            d.body.localScale = new Vector3((1 + s) * (1 - st * 0.35f) * (1 - pw * 0.12f), (1 - s) * (1 - pw * 0.1f), (1 + s) * (1 + st) * (1 + pw * 0.22f));
            d.ring.gameObject.SetActive(selectable || d.frenzy || (frenzyReady && !moving));
            if (d.ring.gameObject.activeSelf)
            {
                float pulse = 1.45f + Mathf.Sin(t * 5 + d.p.x) * 0.08f + (dragged ? 0.25f + pw * 0.25f : 0);
                d.ring.localScale = new Vector3(pulse, 0.01f, pulse);
                Color rc = frenzyReady || d.frenzy ? new Color(1f, 0.5f, 0.75f, 0.6f)
                         : dragged ? (Color)(Color32)Color.Lerp(new Color(1, 1, 1, 0.7f), new Color(1f, 0.45f, 0.3f, 0.75f), powerStep / 4f)
                         : d.special ? new Color(1f, 0.8f, 0.3f, 0.6f) : new Color(0.37f, 0.9f, 0.37f, 0.55f);
                d.ring.GetComponent<Renderer>().sharedMaterial = Art.FadeShared(rc);
            }
            var spark = d.body.Find("Spark"); if (spark) spark.gameObject.SetActive((d.charged || d.special) && Mathf.Sin(t * 14) > -0.3f);
            if (spd > 120 && UnityEngine.Random.value < 0.5f) FxKit.Wake(W(d.p - d.v.normalized * d.r));
            if (d.trail) d.trail.emitting = spd > 260 && d.spawnT <= 0;
            if (spd < 20 && d.spawnT <= 0 && UnityEngine.Random.value < dt * 0.5f) FxKit.Ripple(W(d.p), 0.75f);   // lazy idle ripples
            if (d.rocketT > 0) { FxKit.Flame(W(d.p - d.v.normalized * d.r * 1.2f, 0.3f)); if (UnityEngine.Random.value < 0.3f) FxKit.Smoke(W(d.p - d.v.normalized * d.r * 1.6f), 0.3f, 1); }
            if (d.special && !moving && UnityEngine.Random.value < dt * 4) FxKit.Sparkle(W(d.p, 0.5f), d.kind == "rainbow" ? Color.HSVToRGB(UnityEngine.Random.value, 0.5f, 1f) : new Color(1f, 0.85f, 0.4f), 1);
        }
        bool fewLeft = state == "play" && !moving && GoalsLeft() <= 3;
        foreach (var b in blocks)
        {
            if (!b.alive || b.view == null) continue;
            b.hit = Mathf.Max(0, b.hit - dt);
            float k = 1 + b.hit * (b.type == "boss" ? 0.4f : 1.2f);
            b.view.localScale = b.baseScale * k;
            // floating goodies bob gently on the bath water
            bool floats = b.type == "cookie" || b.type == "gold" || b.type == "ice" || b.type == "pixel" || b.type == "chest" || b.type == "tnt" || b.type == "laser" || b.type == "boss";
            float lift = 0;
            if (b.appear < 1)
            {   // level build-in: drop from above with a little overshoot squash
                if (b.delay > 0) { b.delay -= dt; b.view.localScale = Vector3.zero; continue; }
                b.appear = Mathf.Min(1, b.appear + dt / 0.28f);
                float e = b.appear; lift = (1 - e) * (1 - e) * 5f;
                b.view.localScale = b.baseScale * (e < 1 ? Mathf.Lerp(0.5f, 1f, e) : 1f);
                if (b.appear >= 1) { b.hit = 0.08f; if (UnityEngine.Random.value < 0.25f) FxKit.Ripple(W(b.p), 0.7f); }
            }
            if (b.under != null && b.under.shelf && b.under.view != null)
            {   // standing on a shelf (sinks with it while it collapses)
                float fall = b.under.falling > 0 ? Mathf.Min(1, b.under.falling / 0.35f) : 0;
                lift += 0.84f * (1 - fall); floats = false;
                if (fall > 0) b.view.localEulerAngles = new Vector3(fall * 40f, 0, fall * 25f);
            }
            if (b.mvAmp > 0 || floats || lift > 0 || b.appear < 1) b.view.position = W(b.p, lift + (floats ? Mathf.Sin(t * 2.2f + b.p.x * 0.031f + b.p.y * 0.017f) * 0.035f : 0));
            else if (b.type != "plank") b.view.position = W(b.p);
            // white flash on hit (property block keeps the shared baked material untouched)
            // last few targets glow softly so the player can find them
            bool glow = (fewLeft && b.Goal && !Covered(b)) || b.type == "gold";
            if (b.hit > 0 || b.flashing || glow)
            {
                b.flashing = b.hit > 0 || glow;
                var e = b.hit > 0 ? Color.white * Mathf.Min(1f, b.hit * 6f) * 0.55f
                      : glow ? new Color(1f, 0.85f, 0.35f) * (0.18f + 0.18f * Mathf.Sin(t * 6f)) : Color.clear;
                mpb.Clear(); mpb.SetColor("_Emission", e);
                foreach (var r in b.rends) if (r) r.SetPropertyBlock(b.flashing ? mpb : null);
            }
            if (b.type == "plank" && b.falling > 0) { float f = Mathf.Min(1, b.falling / 0.35f); b.view.position = W(b.p, -f * 0.6f); }
            if (b.type == "laser") b.view.Find("Arrow").localScale = Vector3.one * (1 + Mathf.Sin(t * 8) * 0.08f);
            if (b.type == "screw") b.view.localEulerAngles = new Vector3(0, t * 30 + b.p.x, 0);
        }
        for (int i = dying.Count - 1; i >= 0; i--)
        {
            var dd = dying[i]; dd.life -= dt;
            if (dd.t == null || dd.life <= 0) { if (dd.t) Destroy(dd.t.gameObject); dying.RemoveAt(i); continue; }
            float f = 1 - dd.life / 0.12f;   // quick puff-up then vanish
            dd.t.localScale = dd.s0 * (f < 0.4f ? Mathf.Lerp(1f, 1.35f, f / 0.4f) : Mathf.Lerp(1.35f, 0f, (f - 0.4f) / 0.6f));
        }
        foreach (var m in bumpers) { m.pulse = Mathf.Max(0, m.pulse - dt); m.view.localScale = new Vector3(1 + m.pulse * 2, 1, 1); }
    }

    void Splash(Vector2 p, int n) => FxKit.Splash(W(p), Mathf.Max(3, n), n > 12 ? 1.4f : 1f);
    void Debris(Vector2 p, Color c, int n, float power)
    {
        int chunks = Mathf.Clamp(n / 3, 1, App.Mobile ? 2 : 4);   // a few solid 3D chunks + many cheap particles
        for (int i = 0; i < chunks; i++) SpawnBit(W(p, 0.3f), c, UnityEngine.Random.Range(0.1f, 0.22f), 4.5f * power, UnityEngine.Random.Range(0.6f, 1f), PrimitiveType.Cube);
        FxKit.Crumbs(W(p), c, Mathf.RoundToInt(n * (App.Mobile ? 0.6f : 1.1f)));
        FxKit.Sparkle(W(p), c, Mathf.Clamp(n / 3, 2, App.Mobile ? 3 : 6));
    }

    void SpawnBit(Vector3 pos, Color c, float size, float speed, float life, PrimitiveType type)
    {
        if (bits.Count > (App.Mobile ? 40 : 140)) return;
        // reuse pooled chunks: creating + destroying GameObjects per debris piece was the main hitch on big smashes
        Transform tr;
        if (!bitPools.TryGetValue(type, out var pool)) bitPools[type] = pool = new Stack<Transform>();
        if (pool.Count > 0 && pool.Peek() != null) { tr = pool.Pop(); tr.gameObject.SetActive(true); tr.position = pos; tr.localScale = Vector3.one * size; tr.rotation = Quaternion.identity; }
        else
        {
            if (pool.Count > 0) pool.Clear();
            tr = Art.Prim(type, fx, pos, Vector3.one * size, Art.Mat(c, 0.4f));
            var rr = tr.GetComponent<Renderer>(); rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;
        }
        tr.GetComponent<Renderer>().sharedMaterial = Art.Mat(c, 0.4f);
        var dir = UnityEngine.Random.insideUnitCircle.normalized;
        bits.Add(new Bit { type = type, t = tr, v = new Vector3(dir.x, UnityEngine.Random.Range(0.6f, 1.6f), dir.y) * speed * UnityEngine.Random.Range(0.4f, 1f), spin = UnityEngine.Random.insideUnitSphere * 720, scale0 = tr.localScale, life = life, max = life, grav = true });
    }

    void AddRing(Vector2 p, float radius, Color c)
    {
        var tr = Art.Prim(PrimitiveType.Cylinder, fx, W(p, 0.3f), Vector3.one, Art.Fade(c, 0.8f));
        rings.Add(new Ring { t = tr, m = tr.GetComponent<Renderer>().sharedMaterial, life = 0.35f, max = 0.35f, r0 = 0.2f, r1 = radius * 2 * K, c = c });
    }

    void UpdateFx(float dt)
    {
        for (int i = bits.Count - 1; i >= 0; i--)
        {
            var b = bits[i]; b.life -= dt;
            if (b.life <= 0) { if (b.t) { b.t.gameObject.SetActive(false); bitPools[b.type].Push(b.t); } bits.RemoveAt(i); continue; }
            b.v.y -= 14 * dt; b.t.position += b.v * dt;
            if (b.t.position.y < 0) { b.t.position = new Vector3(b.t.position.x, 0, b.t.position.z); b.v *= 0.5f; b.v.y = Mathf.Abs(b.v.y) * 0.3f; }
            b.t.Rotate(b.spin * dt); b.t.localScale = b.scale0 * Mathf.Clamp01(b.life / b.max * 1.5f);
        }
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            var r = rings[i]; r.life -= dt; float f = 1 - r.life / r.max;
            if (r.life <= 0) { Destroy(r.t.gameObject); if (r.m) Destroy(r.m); rings.RemoveAt(i); continue; }
            if (r.r0 >= 0) { float s = Mathf.Lerp(r.r0, r.r1, Mathf.Sqrt(f)); r.t.localScale = new Vector3(s, 0.05f + (1 - f) * 0.3f, s); }
            var c = r.c; c.a *= 1 - f; r.m.color = c;
        }
        for (int i = zaps.Count - 1; i >= 0; i--)
        {
            var z = zaps[i]; z.life -= dt;
            if (z.life <= 0) { Destroy(z.lr.gameObject); zaps.RemoveAt(i); continue; }
            for (int k = 0; k < 7; k++)
            {
                var q = Vector2.Lerp(z.a, z.b, k / 6f) + (k > 0 && k < 6 ? UnityEngine.Random.insideUnitCircle * 14 : Vector2.zero);
                z.lr.SetPosition(k, W(q, 0.5f));
            }
        }
        // floating texts
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            var p = pops[i]; p.life -= dt;
            if (p.life <= 0) { Destroy(p.t.gameObject); pops.RemoveAt(i); continue; }
            p.rise += dt * 90;
            var sp = cam.WorldToScreenPoint(p.world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(hud, sp, null, out var lp);
            var rt = p.t.rectTransform; rt.anchoredPosition = lp + new Vector2(0, p.rise);
            var c = p.t.color; c.a = Mathf.Clamp01(p.life / p.max * 2.5f); p.t.color = c;
        }
        // big center text
        if (bigLife > 0)
        {
            bigLife -= dt;
            float s = 1 + Mathf.Max(0, bigLife - 1.15f) * 4;
            bigTxt.rectTransform.localScale = Vector3.one * s;
            var c = bigTxt.color; c.a = Mathf.Clamp01(bigLife * 2.5f); bigTxt.color = c;
        }
    }

    void PopText(string s, Vector2 simPos, Color c, int size)
    {
        // too many floating labels at once is unreadable and each one is an outlined UI mesh: drop the oldest
        if (pops.Count >= (App.Mobile ? 6 : 10)) { if (pops[0].t) Destroy(pops[0].t.gameObject); pops.RemoveAt(0); }
        var tx = UIKit.Outlined(UIKit.Label(hud, s, size, c), new Color(0.23f, 0.18f, 0.42f), 4);
        var rt = tx.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(600, 120);
        pops.Add(new Pop { t = tx, world = W(simPos, 0.6f), life = 0.9f, max = 0.9f });
        if (pops.Count > 30) { Destroy(pops[0].t.gameObject); pops.RemoveAt(0); }
    }

    void BigText(string s, Color c) { bigTxt.text = s; bigTxt.color = c; bigLife = 1.4f; }

    // =====================================================================
    // CAMERA
    // =====================================================================
    void FitCamera(bool force)
    {
        if (!force && Screen.width == lastW && Screen.height == lastH) return;
        lastW = Screen.width; lastH = Screen.height;
        cam.fieldOfView = 36;
        var rot = Quaternion.Euler(62, 0, 0); cam.transform.rotation = rot;
        var focus = new Vector3(0, 0, -0.25f);
        float w = TW * K / 2 + 0.45f, h = TH * K / 2 + 0.45f;
        Vector3[] corners = { new Vector3(-w, 0.5f, h), new Vector3(w, 0.5f, h), new Vector3(-w, 0, -h), new Vector3(w, 0, -h) };
        for (float dist = 12; dist < 80; dist += 0.25f)
        {
            cam.transform.position = focus - rot * Vector3.forward * dist;
            bool ok = true;
            foreach (var c in corners)
            {
                var v = cam.WorldToViewportPoint(c);
                if (v.x < 0.02f || v.x > 0.98f || v.y < 0.135f || v.y > 0.83f) { ok = false; break; }
            }
            if (ok) break;
        }
        camBase = cam.transform.position;
        QualitySettings.shadowDistance = (camBase - focus).magnitude + 14f;   // just enough to cover the whole tub
    }

    // =====================================================================
    // HUD
    // =====================================================================
    static readonly Color Purple = new Color(0.6f, 0.53f, 0.93f), PurpleDark = new Color(0.49f, 0.42f, 0.84f), Ink = new Color(0.23f, 0.18f, 0.42f), Yellow = new Color(1f, 0.8f, 0.2f);

    void BuildHud(Transform canvas)
    {
        hud = UIKit.Stretch(UIKit.Rect("GameHUD", canvas));
        // soft purple vignette: frames the tub and pulls the eye to the middle
        var vig = UIKit.Stretch(UIKit.Rect("Vignette", hud)).gameObject.AddComponent<RawImage>();
        vig.texture = VignetteTex(); vig.raycastTarget = false; vig.color = new Color(0.35f, 0.2f, 0.45f, 0.55f);
        flashImg = UIKit.Stretch(UIKit.Rect("Flash", hud)).gameObject.AddComponent<Image>();
        flashImg.color = new Color(1f, 0.97f, 0.9f, 0); flashImg.raycastTarget = false;
        // top bar
        var top = UIKit.Panel(hud, PurpleDark, "TopBar").rectTransform; UIKit.Place(top, 0.5f, 1, 0, -150, 1040, 250);
        var topIn = UIKit.Panel(top, Purple, "Inner").rectTransform; UIKit.Stretch(topIn, 0, 10, 0, 0);
        var mb = UIKit.Panel(topIn, Color.white, "Moves").rectTransform; UIKit.Place(mb, 0, 0.5f, 120, 10, 170, 170);
        movesTxt = UIKit.Label(mb, "10", 84, Ink); movesTxt.rectTransform.offsetMin = new Vector2(0, 20);
        var ml = UIKit.Label(mb, "LƯỢT", 26, PurpleDark); ml.rectTransform.offsetMax = new Vector2(0, -120);
        var sp = UIKit.Rect("StarPreview", topIn); UIKit.Place(sp, 0, 0.5f, 120, -97, 170, 40);
        starPreview = UIKit.StarRow(sp, 3, 36, 6);
        aimCount = UIKit.Outlined(UIKit.Label(hud, "", 64, Color.white), Ink, 4);
        aimCount.rectTransform.anchorMin = aimCount.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); aimCount.rectTransform.sizeDelta = new Vector2(300, 100);
        aimCount.gameObject.SetActive(false);
        goalsTxt = UIKit.Label(topIn, "", 40, Color.white, TextAnchor.MiddleLeft); UIKit.Place(goalsTxt.rectTransform, 0, 0.5f, 470, 10, 450, 200);
        goalsTxt.horizontalOverflow = HorizontalWrapMode.Wrap; goalsTxt.lineSpacing = 1.1f;
        levelTxt = UIKit.Label(topIn, "", 34, Color.white, TextAnchor.MiddleRight); UIKit.Place(levelTxt.rectTransform, 1, 0.5f, -265, 50, 260, 60);
        scoreTxt = UIKit.Label(topIn, "", 40, Yellow, TextAnchor.MiddleRight); UIKit.Place(scoreTxt.rectTransform, 1, 0.5f, -265, -20, 260, 60);
        var gear = UIKit.Btn(topIn, "II", Yellow, Ink, 44, () => App.OpenSettings(true), "Settings");
        UIKit.Place((RectTransform)gear.transform, 1, 0.5f, -65, 10, 100, 100);
        // fever bar
        var fb = UIKit.Panel(hud, new Color(0.23f, 0.18f, 0.42f, 0.45f), "Fever").rectTransform; UIKit.Place(fb, 0.5f, 1, 0, -305, 1000, 44);
        var ff = UIKit.Panel(fb, new Color(1f, 0.49f, 0.71f), "Fill"); feverFill = ff.rectTransform;
        feverFill.anchorMin = Vector2.zero; feverFill.anchorMax = new Vector2(0, 1); feverFill.offsetMin = feverFill.offsetMax = Vector2.zero; feverFill.pivot = new Vector2(0, 0.5f);
        feverTxt = UIKit.Label(fb, "FRENZY", 28, Color.white);
        streakTxt = UIKit.Label(fb, "", 30, new Color(1f, 0.5f, 0f), TextAnchor.MiddleLeft); streakTxt.rectTransform.offsetMin = new Vector2(20, 0);
        // boss bar
        bossBar = UIKit.Panel(hud, new Color(0.23f, 0.18f, 0.42f, 0.7f), "BossBar").rectTransform; UIKit.Place(bossBar, 0.5f, 1, 0, -360, 700, 40);
        var bf = UIKit.Panel(bossBar, new Color(1f, 0.35f, 0.2f), "Fill"); bossFill = bf.rectTransform;
        bossFill.anchorMin = Vector2.zero; bossFill.anchorMax = Vector2.one; bossFill.offsetMin = new Vector2(4, 4); bossFill.offsetMax = new Vector2(-4, -4);
        bossTxt = UIKit.Label(bossBar, "BOSS", 26, Color.white);
        // combo + big text
        comboTxt = UIKit.Outlined(UIKit.Label(hud, "", 90, Yellow), Ink, 6); UIKit.Place(comboTxt.rectTransform, 0.5f, 0, 0, 540, 1000, 220); comboTxt.lineSpacing = 0.85f; comboTxt.verticalOverflow = VerticalWrapMode.Overflow;
        bigTxt = UIKit.Outlined(UIKit.Label(hud, "", 110, Color.white), new Color(1f, 0.5f, 0.75f), 8); UIKit.Place(bigTxt.rectTransform, 0.5f, 0.5f, 0, 120, 1000, 200);
        bigTxt.color = new Color(1, 1, 1, 0);
        // hint
        hintPanel = UIKit.Panel(hud, new Color(1, 1, 1, 0.93f), "Hint").rectTransform; UIKit.Place(hintPanel, 0.5f, 0, 0, 290, 980, 90);
        hintTxt = UIKit.Label(hintPanel, "", 34, Ink); hintTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        // booster bar
        var bar = UIKit.Rect("Boosters", hud); UIKit.Place(bar, 0.5f, 0, 0, 125, 1060, 190);
        var mapBtn = UIKit.Btn(bar, "BẢN ĐỒ", PurpleDark, Color.white, 30, App.ShowMap, "Map");
        UIKit.Place((RectTransform)mapBtn.transform, 0, 0.5f, 100, 0, 180, 170);
        string[] keys = { "hammer", "bomb", "moves" };
        for (int i = 0; i < 3; i++)
        {
            string k = keys[i];
            var b = UIKit.Btn(bar, "", Yellow, Ink, 36, () => UseBooster(k), "Boost_" + k);
            UIKit.Place((RectTransform)b.transform, 0, 0.5f, 335 + i * 270, 0, 250, 170);
            var lbl = UIKit.BtnText(b); lbl.lineSpacing = 1.05f;
            boostTxt[k] = lbl; boostImg[k] = b.GetComponent<Image>();
        }
        // popup
        popupRoot = UIKit.Stretch(UIKit.Rect("Popup", hud));
        var dim = popupRoot.gameObject.AddComponent<Image>(); dim.color = new Color(0.11f, 0.08f, 0.25f, 0.75f);
        var card = UIKit.Panel(popupRoot, Purple, "Card").rectTransform; UIKit.Place(card, 0.5f, 0.5f, 0, 0, 900, 1000);
        popTitle = UIKit.Outlined(UIKit.Label(card, "", 90, Color.white), Ink, 5); UIKit.Place(popTitle.rectTransform, 0.5f, 1, 0, -120, 860, 140);
        popStars = UIKit.Label(card, "", 130, Yellow); UIKit.Place(popStars.rectTransform, 0.5f, 1, 0, -290, 860, 170);
        popStarImgs = UIKit.StarRow(popStars.transform, 3, 150, 24);
        popBody = UIKit.Label(card, "", 44, Color.white); UIKit.Place(popBody.rectTransform, 0.5f, 1, 0, -440, 860, 120);
        popReward = UIKit.Outlined(UIKit.Label(card, "", 64, Yellow), Ink, 4); UIKit.Place(popReward.rectTransform, 0.5f, 1, 0, -540, 860, 100);
        popA = UIKit.Btn(card, "", new Color(0.37f, 0.83f, 0.37f), Color.white, 50, OnPopA, "A"); UIKit.Place((RectTransform)popA.transform, 0.5f, 0, 0, 280, 640, 150);
        popB = UIKit.Btn(card, "", Yellow, Ink, 44, OnPopB, "B"); UIKit.Place((RectTransform)popB.transform, 0.5f, 0, 0, 110, 640, 130);
        // level intro card
        introRoot = UIKit.Rect("Intro", hud); UIKit.Place(introRoot, 0.5f, 0.5f, 0, 60, 760, 520);
        var ic = UIKit.Panel(introRoot, new Color(1f, 0.97f, 0.94f, 0.96f), "IntroCard"); UIKit.Stretch(ic.rectTransform);
        var ish = ic.gameObject.AddComponent<Shadow>(); ish.effectColor = new Color(0.3f, 0.2f, 0.5f, 0.3f); ish.effectDistance = new Vector2(0, -12);
        introTitle = UIKit.Outlined(UIKit.Label(introRoot, "", 80, Purple), Ink, 3); UIKit.Place(introTitle.rectTransform, 0.5f, 1, 0, -95, 700, 120);
        introBody = UIKit.Label(introRoot, "", 46, Ink); UIKit.Place(introBody.rectTransform, 0.5f, 0.5f, 0, -60, 700, 320);
        introRoot.gameObject.SetActive(false);
        // tutorial hand (white fingertip)
        var hand = UIKit.Dot(hud, Color.white, "Hand"); hand.raycastTarget = false; handRt = hand.rectTransform;
        var hcg = hand.gameObject.AddComponent<CanvasGroup>(); hcg.blocksRaycasts = false; hcg.interactable = false;
        handRt.anchorMin = handRt.anchorMax = new Vector2(0.5f, 0.5f); handRt.sizeDelta = new Vector2(90, 90);
        var ho = hand.gameObject.AddComponent<Outline>(); ho.effectColor = new Color(0.23f, 0.18f, 0.42f, 0.9f); ho.effectDistance = new Vector2(5, -5);
        var hl = UIKit.Label(hand.transform, "KÉO", 26, Ink); hl.rectTransform.offsetMin = new Vector2(0, -110); hl.rectTransform.offsetMax = new Vector2(0, -80);
        handRt.gameObject.SetActive(false);
        popupRoot.gameObject.SetActive(false);
    }

    static Texture2D VignetteTex()
    {
        const int S = 64; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2 - 1, dy = (y + 0.5f) / S * 2 - 1;
                float d = Mathf.Sqrt(dx * dx * 0.9f + dy * dy * 0.75f);
                px[y * S + x] = new Color(1, 1, 1, Mathf.SmoothStep(0, 1, Mathf.Clamp01((d - 0.75f) / 0.6f)));
            }
        tex.SetPixels(px); tex.Apply(); return tex;
    }

    void RefreshHud()
    {
        if (!Active) return;
        flashA = Mathf.Max(0, flashA - Time.unscaledDeltaTime * 2.5f);
        flashImg.color = new Color(1f, 0.97f, 0.9f, flashA);
        movesTxt.text = moves.ToString();
        movesPulse = Mathf.Max(0, movesPulse - Time.unscaledDeltaTime);
        movesTxt.rectTransform.localScale = Vector3.one * (1 + movesPulse * 1.2f);
        movesTxt.color = moves <= 2 && state == "play" ? new Color(1f, 0.35f, 0.3f) : Ink;
        int proj = moves >= Mathf.CeilToInt(startMoves * 0.5f) ? 3 : moves >= Mathf.CeilToInt(startMoves * 0.2f) ? 2 : 1;
        for (int i = 0; i < 3; i++) starPreview[i].color = i < proj ? Yellow : new Color(1, 1, 1, 0.35f);
        // goal counts, boosters and titles only change on smashes: refresh them ~8x/s instead of every frame (GC + text rebuilds)
        hudTick -= Time.unscaledDeltaTime;
        bool slow = hudTick <= 0; if (slow) hudTick = 0.12f;
        var boss = bossBlock;
        if (slow) {
        int cookies = blocks.Count(b => b.alive && (b.type == "cookie" || b.type == "ice"));
        int chests = blocks.Count(b => b.alive && b.type == "chest"), pixels = blocks.Count(b => b.alive && b.type == "pixel");
        var g = new List<string>();
        if (boss != null) g.Add(boss.alive ? "Boss " + Mathf.Max(0, boss.hp) : "Boss xong");
        if (blocks.Any(b => b.type == "cookie" || b.type == "ice")) g.Add("Bánh " + (cookies == 0 ? "xong" : cookies.ToString()));
        if (blocks.Any(b => b.type == "chest")) g.Add("Rương " + (chests == 0 ? "xong" : chests.ToString()));
        if (blocks.Any(b => b.type == "pixel")) g.Add("Pixel " + (pixels == 0 ? "xong" : pixels.ToString()));
        goalsTxt.text = "MỤC TIÊU\n" + string.Join("   ", g);
        levelTxt.text = LevelLibrary.Title(level);
        if (blocks.Any(b => b.Prop)) { int pl = blocks.Count(b => b.alive && b.Prop); g.Add("Đồ " + (pl == 0 ? "xong" : pl.ToString())); goalsTxt.text = "MỤC TIÊU\n" + string.Join("   ", g); }
        foreach (var k in PRICE.Keys)
        {
            int n = SaveData.GetBoost(k);
            boostTxt[k].text = BNAME[k] + "\n" + (n > 0 ? "còn " + n : PRICE[k] + " xu");
            boostImg[k].color = target == k ? new Color(0.37f, 0.83f, 0.37f) : Yellow;
        }
        }
        scoreTxt.text = Mathf.RoundToInt(displayScore).ToString("N0");
        scoreTxt.rectTransform.localScale = Vector3.one * (1 + scorePulse * 1.5f);
        float fr = frenzyReady ? 1 : fever / FEVER_MAX;
        feverFill.anchorMax = new Vector2(fr, 1);
        feverFill.GetComponent<Image>().color = frenzyReady ? Color.HSVToRGB(t * 0.8f % 1, 0.6f, 1f) : new Color(1f, 0.49f, 0.71f);
        feverTxt.text = frenzyReady ? "DUCK FRENZY SẴN SÀNG — BẮN ĐI!" : "FRENZY";
        streakTxt.text = streak > 0 ? "CHUỖI x" + (streak + 1) : "";
        if (boss != null) { bossFill.anchorMax = new Vector2(Mathf.Clamp01((float)boss.hp / boss.max), 1); bossTxt.text = boss.alive ? "VUA BÁNH  " + Mathf.Max(0, boss.hp) + "/" + boss.max : "HẠ GỤC!"; }
        string ht = target == "hammer" ? "Chạm vào một khối để đập vỡ (chạm Búa để huỷ)" : target == "bomb" ? "Chạm chỗ muốn thả bom nước" : moving ? "" : hint;
        hintPanel.gameObject.SetActive(!string.IsNullOrEmpty(ht) && state == "play"); hintTxt.text = ht;
        bool showCombo = moving && shotSmash >= 4;
        comboTxt.gameObject.SetActive(showCombo);
        if (showCombo)
        {   // live progress toward the next special duck
            if (comboShown != shotSmash * 10 + shotBanks) {
            comboShown = shotSmash * 10 + shotBanks;
            var nt = NextTier(shotSmash);
            string sub = nt.kind != null ? "\n<size=44>còn " + (nt.need - shotSmash) + " → " + nt.name + "</size>"
                       : nt.maxed ? "\n<size=44>" + nt.name + "!</size>" : "";
            comboTxt.text = "x" + shotSmash + " SMASH" + sub + (shotBanks > 0 ? "\n<size=40>BĂNG x" + (shotBanks + 1) + " ĐIỂM</size>" : "");
            }
            comboTxt.color = shotSmash >= 25 ? Color.HSVToRGB(t * 1.5f % 1f, 0.55f, 1f) : shotSmash >= 16 ? new Color(1f, 0.35f, 0.2f) : shotSmash >= 10 ? new Color(1f, 0.6f, 0.3f) : Yellow;
            comboTxt.rectTransform.localScale = Vector3.one * (1 + comboPulse * 2.5f + Mathf.Min(shotSmash, 30) * 0.006f);
            comboTxt.rectTransform.localEulerAngles = new Vector3(0, 0, Mathf.Sin(t * 30f) * comboPulse * 40f);   // wobble on every smash
        }
    }

    void ShowPopup(bool win)
    {
        popupRoot.gameObject.SetActive(true); popAnim = 0;
        introTimer = 0; introRoot.gameObject.SetActive(false); popupRoot.SetAsLastSibling();
        if (win)
        {
            popTitle.text = bossKilled ? "HẠ BOSS!" : (level <= LevelLibrary.Onboarding ? "Giỏi lắm!" : "Well Done!");
            popStars.text = "";
            for (int i = 0; i < 3; i++) { popStarImgs[i].gameObject.SetActive(true); popStarImgs[i].color = i < stars ? Yellow : new Color(0.49f, 0.42f, 0.84f); popStarImgs[i].rectTransform.localScale = Vector3.one * (i == 1 ? 1.2f : 1f); }
            popBody.text = "Điểm " + score.ToString("N0") + (bossKilled ? "\nRương Boss: +1 mỗi booster" : "")
                + (piggyAdded > 0 ? "\n<size=36>Ống heo +" + piggyAdded + "  (" + SaveData.Piggy + "/" + SaveData.PiggyMax + ")</size>" : "")
                + (newArt ? "\n<size=36><color=#ffe066>Tranh \"" + LevelLibrary.ArtName(levelArt) + "\" vào Album!</color></size>" : "");
            popBody.verticalOverflow = VerticalWrapMode.Overflow;
            popReward.text = "+0 xu";
            UIKit.BtnText(popA).text = HasNext ? "MÀN TIẾP" : "VỀ BẢN ĐỒ";
            UIKit.BtnText(popB).text = "Bản đồ";
        }
        else
        {
            popTitle.text = "Hết lượt!";
            foreach (var im in popStarImgs) im.gameObject.SetActive(false);
            popStars.text = "";
            popBody.text = "Còn " + GoalsLeft() + " mục tiêu nữa thôi…\nBạn có " + SaveData.Coins + " xu";
            popReward.text = "";
            UIKit.BtnText(popA).text = "+5 lượt — " + CONTINUE_PRICE + " xu";
            popA.GetComponent<Image>().color = SaveData.Coins >= CONTINUE_PRICE ? new Color(0.37f, 0.83f, 0.37f) : new Color(0.7f, 0.7f, 0.75f);
            UIKit.BtnText(popB).text = "Chơi lại";
        }
        if (win) popA.GetComponent<Image>().color = new Color(0.37f, 0.83f, 0.37f);
    }

    void OnPopA()
    {
        if (state == "win") { if (HasNext) StartLevel(level + 1); else App.ShowMap(); }
        else if (state == "lose" && SaveData.Coins >= CONTINUE_PRICE)
        {
            SaveData.Coins -= CONTINUE_PRICE; moves = 5; state = "play"; popupRoot.gameObject.SetActive(false); Sfx.Play("coin");
        }
    }

    void OnPopB()
    {
        if (state == "win") App.ShowMap();
        else Restart();
    }

    public int Level => level;
}
