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
        public RectTransform tag; public string tagKind;
        public bool inWhirl;
        public bool special, spent, rainbowReady; public float rocketT, spawnT, stretch, pullVis, bankCd, portalCd; public TrailRenderer trail; public Color ringCol = Color.clear;
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
        public Renderer[] rends; public bool flashing, scaled; public Transform pips; public RectTransform tag; public float appear = 1, delay; public Vector2 hitDir;
        public bool shelf; public Vector2 vel; public float mvPhase, wob;   // vel: float rings skate across the tub after a knock
        public bool Prop => type == "glass" || type == "vase" || type == "soap" || type == "plates";
        public bool Goal => !Hard;   // single objective: smash EVERYTHING breakable in the tub (only stone + shelf boards stay)
        public bool Hard => type == "stone" || type == "plank";
    }
    class Bumper { public float x, y, w, h, pulse, x0, y0, amp, speed; public bool vertical; public Transform view; }
    float padClock;   // sim time that drives sliding pads (advances only in Step, so the sim stays deterministic)
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
    class Whirl { public Vector2 p; public float r; public Transform view; public float spin; }
    readonly List<Whirl> whirls = new List<Whirl>();
    class Portal { public Vector2 a, b; public Transform sa, sb; public Color c; }
    readonly List<Portal> portals = new List<Portal>();
    const float PORTAL_R = 24f;
    class Flow { public Rect r; public Vector2 dir; public Material m; public Mesh mesh; public float tu, tv; readonly Vector2[] uv = new Vector2[4];
        // the shared material ignores tiling/offset, so the chevrons are tiled + scrolled through the quad's own UVs
        public void Scroll(float off) { if (!mesh) return; uv[0] = new Vector2(off, 0); uv[1] = new Vector2(off + tu, 0); uv[2] = new Vector2(off, tv); uv[3] = new Vector2(off + tu, tv); mesh.uv = uv; } }
    readonly List<Flow> flows = new List<Flow>();
    const float FLOW_ACC = 1500f;   // px/s^2 along the current
    Vector2 FlowAt(Vector2 p)
    {
        for (int i = 0; i < flows.Count; i++) if (flows[i].r.Contains(p)) return flows[i].dir;
        return Vector2.zero;
    }
    bool portalTaught;
    const float WHIRL_TURN = 11f;   // rad/s at the centre: a full-power pass bends ~25-30 deg, a soft one much more
    // how fast (rad/s) the water turns a moving duck at this point; 0 outside every whirlpool
    float WhirlTurn(Vector2 p, out float depth)
    {
        float w = 0; depth = 0;
        for (int i = 0; i < whirls.Count; i++)
        {
            var wh = whirls[i]; float dist = Vector2.Distance(wh.p, p);
            if (dist >= wh.r) continue;
            float k = 1 - dist / wh.r; w += WHIRL_TURN * k; depth = Mathf.Max(depth, k);
        }
        return w;
    }
    static Vector2 Rot(Vector2 v, float a) { float c = Mathf.Cos(a), s = Mathf.Sin(a); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }
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
    static void ResetStatics() { fever = 0; frenzyReady = false; trailMat = null; pipMat = null; contactMat = null; portalHole = null; flowTex = null; tagDuckIcon = null; templates.Clear(); tplRoot = null; }

    // Building a block from primitives + baking it costs ~0.5 ms; cloning a cached template is ~20x cheaper.
    // Every block/duck kind is built ONCE (under an inactive root) and Instantiate()d afterwards.
    static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
    static Transform tplRoot;
    static Transform TplRoot { get { if (tplRoot == null) { var go = new GameObject("Templates"); go.SetActive(false); tplRoot = go.transform; } return tplRoot; } }
    static Transform Spawn(string key, Func<Transform, Transform> build, Transform parent)
    {
        if (!templates.TryGetValue(key, out var tpl) || tpl == null) { tpl = build(TplRoot).gameObject; templates[key] = tpl; }
        return Instantiate(tpl, parent).transform;
    }
    bool moving, frenzyShot, bossKilled, rewarded, hardLevel;
    string state = "play", target, hint;
    float freeze, shake, comboPulse, t, lastW, lastH;
    Duck drag; Vector2 dragPt, dragSm; int powerStep, shotBanks; bool lastWarned;
    Image curtain; float curtainA;
    Transform popCard; float loseAnim; int leftAtWin; int projShown = -1, starLostIdx = -1, shownMovesMax; float starLossT; RawImage sunburst;
    static Texture2D SunburstTex()
    {
        const int S = 256; var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2 - 1, dy = (y + 0.5f) / S * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float ray = Mathf.SmoothStep(0.15f, 0.55f, 0.5f + 0.5f * Mathf.Cos(ang * 12f));   // 12 soft rays
                float fade = Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 4f);
                px[y * S + x] = new Color(1, 1, 1, ray * fade);
            }
        tex.SetPixels(px); tex.Apply(); return tex;
    }
    RectTransform feverBar, feverGhost; Text feverHint, feverPop; float feverShown, feverPopT; int feverShownInt = -1, feverMode;
    Text powerTxt; int shownPower = -1; bool halfShown; RawImage popArt; GameObject popArtBg;
    bool aimVisible = true; RectTransform dynLayer, objFill; int goalTotal, shownLeft = -1; float goalPulse; int shownMoves = -1, shownScore = -1, shownStreak = -1;
    float swoop, flashA, hudTick; Image flashImg; int comboShown = -1; Block bossBlock;
    int dominoFalls, shotClears; string levelArt; bool newArt;
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
        Flash(0.15f + Mathf.Min(shotClears, 4) * 0.05f); Shake(0.25f); HitStop(0.04f);
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
    void Flash(float a) => flashA = Mathf.Max(flashA, a * 0.3f);
    Transform aimTarget, aimGrab; readonly List<Transform> pushDots = new List<Transform>(), frenzyDots = new List<Transform>(); LineRenderer band2;
    MaterialPropertyBlock mpb; static Material trailMat;
    Vector3 camBase; Material waterMat;
    readonly HashSet<Block> aimHits = new HashSet<Block>();
    Text aimCount; Image[] starPreview; float movesPulse;
    // "show" extras: slow-mo, camera punch, Duck Rush, intro card, tutorial hand, coin flights
    float introMax = 1.8f;
    float slowTimer, camZoom, introTimer, rushTimer, popAnim, displayScore, scorePulse, bubbleTimer;
    int rushLeft, rushCoins, movesAtClear = -1;
    class Dying { public Transform t; public float life, max = 0.12f; public Vector3 s0, p0, dir, axis; public Quaternion r0; public bool topple; }
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

    Transform tubRoot, bitRoot;
    public void Close()
    {
        Active = false;
        if (hud != null) hud.gameObject.SetActive(false);
        if (tubRoot != null) tubRoot.gameObject.SetActive(false);
        ClearTags();
        if (cam != null) cam.cullingMask = 0;   // map screen: don't keep rendering the 3D bath behind the UI
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
        Sfx.SetTrack(LevelLibrary.IsHard(n) || ((n - 1) / 10) % 2 == 1 ? 1 : 0);   // levels 11-20 + hard levels get the bouncier loop
        world = new GameObject("World").transform; fx = new GameObject("FX").transform;
        foreach (var bt in bits) if (bt.t) { bt.t.gameObject.SetActive(false); bitPools[bt.type].Push(bt.t); }   // debris pool persists across levels
        foreach (var w in whirls) if (w.view) Destroy(w.view.gameObject);
        whirls.Clear(); portals.Clear(); portalTaught = false;
        foreach (var fl in flows) { if (fl.m) Destroy(fl.m); if (fl.mesh) Destroy(fl.mesh); }
        flows.Clear();
        padClock = 0;
        ducks.Clear(); blocks.Clear(); bumpers.Clear(); timers.Clear(); bits.Clear(); bitPool.Clear(); rings.Clear(); zaps.Clear(); dying.Clear();
        foreach (var c in coins) if (c.rt) Destroy(c.rt.gameObject);
        coins.Clear(); slowTimer = camZoom = rushTimer = 0; rushLeft = rushCoins = 0; movesAtClear = -1; displayScore = 0;
        foreach (var p in pops) RecyclePop(p.t);
        pops.Clear();
        projShown = -1; starLossT = 0;
        fever = 0; frenzyReady = false; feverShown = 0; feverShownInt = -1; feverMode = 0;   // frenzy starts empty in every level: no banking it for the next one
        moves = startMoves = L.moves; state = "play"; moving = false; shotSmash = streak = pushes = score = 0;
        frenzyShot = bossKilled = rewarded = false; hardLevel = L.hard; target = null; hint = level <= LevelLibrary.Onboarding || LevelLibrary.IsSandbox(level) ? L.hint : ""; freeze = shake = 0; drag = null;

        FxKit.Clear(); ClearTags();
        cam.cullingMask = ~0;
        EnsureTub();
        tubRoot.gameObject.SetActive(true);
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
            int golds = blocks.Any(b => b.type == "gold") ? 0 : Mathf.Min(pool.Count / 12, 1);   // one gold per level (a drifting one counts)
            for (int i = 0; i < golds; i++) { var g = pool[rng.Next(pool.Count)]; pool.Remove(g); g.type = "gold"; }
        }
        // build-in animation: blocks drop onto the water row by row
        foreach (var b in blocks) { b.appear = 0; b.delay = 0.15f + (b.p.y - TY) / TH * 0.7f + UnityEngine.Random.value * 0.08f; }
        foreach (var b in blocks) MakeView(b);
        foreach (var cb in blocks) if (cb.type == "chest" && cb.view != null) cb.tag = MakeTag("+2 vịt", new Color(1f, 0.7f, 0.2f), true);
        if (level <= 19) foreach (var cb in blocks) if (cb.type == "ring" && cb.view != null) cb.tag = MakeTag("ĐẨY TRƯỢT", new Color(1f, 0.42f, 0.4f), false);
        if (level <= 16) foreach (var cb in blocks) if (cb.type == "gold" && cb.mvAmp > 0 && cb.view != null) cb.tag = MakeTag("+1 LƯỢT", new Color(0.95f, 0.65f, 0.1f), false);
        // water colour by chapter: the bath changes as you progress (blue -> teal -> lavender bubble bath; hard = deeper)
        if (waterMat != null)
            waterMat.SetColor("_Shift", LevelLibrary.IsSandbox(level) || level <= 12 ? new Color(1, 1, 1, 0)
                : hardLevel ? new Color(0.25f, 0.35f, 0.8f, 0.45f)
                : level <= 18 ? new Color(0.3f, 0.85f, 0.78f, 0.6f) : level <= 25 ? new Color(0.72f, 0.5f, 1f, 0.55f) : new Color(1f, 0.55f, 0.68f, 0.5f));
        if (floorMat != null)
        {   // the bathroom tiles follow the chapter too (subtle tint, so the screen doesn't look the same for 30 levels)
            var fc = LevelLibrary.IsSandbox(level) || level <= 12 ? Color.white : hardLevel ? new Color(0.9f, 0.92f, 1f)
                   : level <= 18 ? new Color(0.9f, 1f, 0.95f) : level <= 25 ? new Color(0.95f, 0.91f, 1f) : new Color(1f, 0.92f, 0.94f);
            floorMat.color = fc; if (floorMat.HasProperty("_Color")) floorMat.SetColor("_Color", fc);
        }
        boosterBusy = false; if (boosterCo != null) { StopCoroutine(boosterCo); boosterCo = null; }
        swoop = 1f; flashA = 0; curtainA = 1f; goalTotal = GoalsLeft(); shownLeft = -1; halfShown = false; idleT = 0; hintReady = false; hintSearch = -1; HideHint(); shownMoves = shownScore = shownStreak = -1; levelArt = L.art; hudTick = 0; comboShown = -1;
        bossBlock = blocks.FirstOrDefault(b => b.type == "boss");
        RefreshComboLabels(); comboShownFill = comboShownGhost = -1; if (guideRoot && guideRoot.gameObject.activeSelf) { guideRoot.gameObject.SetActive(false); Time.timeScale = 1f; }
        BuildLines();
        foreach (var bd in L.bumpers)
        {
            var bu = new Bumper { x = bd.x, y = bd.y, w = bd.w, h = bd.h, x0 = bd.x, y0 = bd.y, amp = bd.amp, speed = bd.speed, vertical = bd.vertical };
            bu.view = Art.Bumper(world, bd.w * K, bd.h * K); bu.view.position = W(bd.x + bd.w / 2, bd.y + bd.h / 2);
            bumpers.Add(bu);
        }
        foreach (var wd in L.whirls)
        {
            var w = new Whirl { p = new Vector2(wd.x, wd.y), r = wd.z };
            w.view = Art.Prim(PrimitiveType.Quad, world, W(w.p, 0.025f), new Vector3(w.r * 2 * K, w.r * 2 * K, 1f), Art.SwirlMat(), new Vector3(90, 0, 0));
            var sh = w.view.GetComponent<Renderer>(); sh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sh.receiveShadows = false;
            whirls.Add(w);
        }
        foreach (var fd in L.flows)
        {
            var fl = new Flow { r = new Rect(fd.x, fd.y, fd.w, fd.h), dir = new Vector2(fd.dx, fd.dy) };
            bool horiz = Mathf.Abs(fd.dx) > 0.5f;
            float along = horiz ? fd.w : fd.h, across = horiz ? fd.h : fd.w;
            fl.m = Art.Fade(new Color(1f, 1f, 1f, 0.8f)); fl.m.mainTexture = FlowTex(); fl.m.renderQueue = 2980;
            fl.tu = along / 44f; fl.tv = Mathf.Max(1, Mathf.Round(across / 44f));
            // the quad's local +x is the flow direction; texture chevrons point along +u
            float yaw = horiz ? (fd.dx > 0 ? 0 : 180) : (fd.dy > 0 ? 90 : -90);
            var q = Art.Prim(PrimitiveType.Quad, world, W(fd.x + fd.w / 2, fd.y + fd.h / 2, 0.026f), new Vector3(along * K, across * K, 1f), fl.m, new Vector3(90, yaw, 0));
            var qr = q.GetComponent<Renderer>(); qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; qr.receiveShadows = false;
            fl.mesh = q.GetComponent<MeshFilter>().mesh; fl.Scroll(0);
            flows.Add(fl);
        }
        int pIdx = 0;
        foreach (var pd in L.portals)
        {
            var pt = new Portal { a = new Vector2(pd.x, pd.y), b = new Vector2(pd.z, pd.w), c = pIdx++ % 2 == 0 ? new Color(1f, 0.62f, 0.3f) : new Color(0.35f, 0.82f, 1f) };
            pt.sa = PortalView(pt.a, pt.c); pt.sb = PortalView(pt.b, pt.c);
            portals.Add(pt);
        }
        if (aimRoot == null) BuildAim(); else aimVisible = true;
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
            case "crate": case "ice": case "vase": case "ring": return 2;
            case "plates": return 3;
            case "chest": return 3;
            case "stone": case "plank": return 999999;
            case "boss": return 30;
            default: return 1;
        }
    }

    Material floorMat;
    void BuildTub()
    {
        // floor outside
        var ground = Art.NewPrim(PrimitiveType.Quad);
        ground.transform.SetParent(world, false); ground.transform.position = new Vector3(0, -0.35f, 0); ground.transform.eulerAngles = new Vector3(90, 0, 0); ground.transform.localScale = new Vector3(90, 90, 1);
        var gm = Art.Toon(Color.white, Art.TileTex(new Color(1f, 0.93f, 0.9f), new Color(0.96f, 0.88f, 0.87f)), new Vector2(22.5f, 22.5f), 0f);
        ground.GetComponent<Renderer>().sharedMaterial = gm; floorMat = gm;
        // soft shadow under the tub so it sits on the floor instead of floating on it
        var tubShadow = Art.NewPrim(PrimitiveType.Quad); tubShadow.transform.SetParent(world, false);
        tubShadow.transform.position = new Vector3(0.25f, -0.33f, -0.35f); tubShadow.transform.eulerAngles = new Vector3(90, 0, 0);
        tubShadow.transform.localScale = new Vector3(TW * K + 2.4f, TH * K + 2.4f, 1);
        var tsm = Art.Fade(new Color(0.38f, 0.2f, 0.36f, 0.8f)); tsm.mainTexture = Art.SoftShadowTex((TW * K + 2.4f) / (TH * K + 2.4f));
        var tsr = tubShadow.GetComponent<Renderer>(); tsr.sharedMaterial = tsm; tsr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tsr.receiveShadows = false;
        // tub base (white porcelain)
        var porcelain = Art.Mat(new Color(0.96f, 0.97f, 1f), 0.85f, 0, 0, 0.0025f);
        var porcelainFlat = Art.Mat(new Color(0.96f, 0.97f, 1f), 0.85f, 0, 0, 0f);
        float w = TW * K, h = TH * K, rim = 0.36f;
        Art.Prim(PrimitiveType.Cube, world, new Vector3(0, -0.2f, 0), new Vector3(w + rim * 2, 0.3f, h + rim * 2), porcelain);
        // water
        var water = Art.NewPrim(PrimitiveType.Quad);
        water.transform.SetParent(world, false); water.transform.position = new Vector3(0, -0.04f, 0); water.transform.eulerAngles = new Vector3(90, 0, 0);
        water.transform.localScale = new Vector3(w, h, 1);
        var wm = Art.Water(Art.WaterTex(), new Vector2(TW / 260f, TH / 260f));   // animated on the GPU
        waterMat = wm;
        water.GetComponent<Renderer>().sharedMaterial = wm;
        // rim walls
        float rh = 0.5f;
        // rounded porcelain lips (capsules) along each edge
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(0, rh / 2, h / 2 + rim / 2), new Vector3(rh, (w + rim * 2) / 2, rim * 1.3f), porcelain, new Vector3(0, 0, 90));
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(0, rh / 2, -h / 2 - rim / 2), new Vector3(rh, (w + rim * 2) / 2, rim * 1.3f), porcelain, new Vector3(0, 0, 90));
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(w / 2 + rim / 2, rh / 2, 0), new Vector3(rim * 1.3f, (h + rim * 2) / 2, rh), porcelain, new Vector3(90, 0, 0));
        Art.Prim(PrimitiveType.Capsule, world, new Vector3(-w / 2 - rim / 2, rh / 2, 0), new Vector3(rim * 1.3f, (h + rim * 2) / 2, rh), porcelain, new Vector3(90, 0, 0));
        // pastel trim along the top of the rim: gives the tub a crisp edge even without outlines (phones)
        var trim = Art.Mat(new Color(0.49f, 0.78f, 0.96f), 0.7f);
        float tl = TR * K;
        Art.Prim(PrimitiveType.Cube, world, new Vector3(0, rh + 0.005f, h / 2 + rim / 2), new Vector3(w - 2 * tl, 0.03f, 0.1f), trim);
        Art.Prim(PrimitiveType.Cube, world, new Vector3(0, rh + 0.005f, -h / 2 - rim / 2), new Vector3(w - 2 * tl, 0.03f, 0.1f), trim);
        Art.Prim(PrimitiveType.Cube, world, new Vector3(w / 2 + rim / 2, rh + 0.005f, 0), new Vector3(0.1f, 0.03f, h - 2 * tl), trim);
        Art.Prim(PrimitiveType.Cube, world, new Vector3(-w / 2 - rim / 2, rh + 0.005f, 0), new Vector3(0.1f, 0.03f, h - 2 * tl), trim);
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
        if (!App.Mobile) {
        var depth = Art.NewPrim(PrimitiveType.Quad);
        depth.transform.SetParent(world, false); depth.transform.position = new Vector3(0, -0.03f, 0); depth.transform.eulerAngles = new Vector3(90, 0, 0);
        depth.transform.localScale = new Vector3(w, h, 1);
        var dm = Art.Fade(Color.white); dm.mainTexture = Art.EdgeShadeTex(w / h); depth.GetComponent<Renderer>().sharedMaterial = dm;
        depth.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
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

    // Build the templates of the common block + duck kinds one per frame right after boot,
    // so entering a level never has to build them mid-transition.
    void EnsureTub()
    {
        if (tubRoot == null)
        {
            var saved = world; world = tubRoot = new GameObject("Tub").transform; BuildTub(); world = saved;
            // pre-create debris chunks once so the first big smash doesn't instantiate them mid-explosion
            bitRoot = Art.Root("Debris", tubRoot);
            foreach (var (pt, cnt) in new[] { (PrimitiveType.Cube, App.Mobile ? 40 : 100), (PrimitiveType.Sphere, 12), (PrimitiveType.Cylinder, 16) })
            {
                if (!bitPools.TryGetValue(pt, out var pool)) bitPools[pt] = pool = new Stack<Transform>();
                for (int i = 0; i < cnt; i++)
                {
                    var tr = Art.Prim(pt, bitRoot, Vector3.zero, Vector3.one * 0.1f, Art.Mat(Color.white, 0.4f));
                    var rr = tr.GetComponent<Renderer>(); rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;
                    tr.gameObject.SetActive(false); pool.Push(tr);
                }
            }
        }
    }

    public System.Collections.IEnumerator Prewarm()
    {
        yield return null;
        if (tubRoot == null) { EnsureTub(); if (!Active) tubRoot.gameObject.SetActive(false); }
        var specs = new List<Block>
        {
            new Block { type = "glass", r = 16 }, new Block { type = "soap", r = 16 }, new Block { type = "vase", r = 17 },
            new Block { type = "plates", r = 17 }, new Block { type = "bathbomb", r = 17 }, new Block { type = "gold", r = 16 },
            new Block { type = "tnt", r = 17 }, new Block { type = "screw", r = 12 }, new Block { type = "ring", r = 20 }, new Block { type = "ice", r = 16 },
            new Block { type = "stone", rect = true, w = 36, h = 36 }, new Block { type = "chest", rect = true, w = 50, h = 40 },
            new Block { type = "domino", rect = true, w = 10, h = 32 }, new Block { type = "domino", rect = true, w = 32, h = 10 },
            new Block { type = "laser", r = 16, dir = "h" }, new Block { type = "laser", r = 16, dir = "v" },
        };
        foreach (var b in specs)
        {
            yield return null;
            var saved = world; world = TplRoot; MakeView(b); if (b.view) Destroy(b.view.gameObject); world = saved;
        }
        foreach (var k in new[] { "normal", "heavy", "bomb", "zap" })
        {
            yield return null;
            var saved = world; world = TplRoot; var d = new Duck { kind = k }; BuildDuckView(d); if (d.view) Destroy(d.view.gameObject); world = saved;
        }
    }

    Transform BuildBlockArt(Block b, Transform parent)
    {
        float d = b.r * 2 * K; Transform v = null;
        switch (b.type)
        {
            case "cookie": v = Art.Cookie(parent, d); break;
            case "gold": v = Art.GoldCookie(parent, d); break;
            case "glass": v = Art.Glass(parent, d); break;
            case "vase": v = Art.Vase(parent, d); break;
            case "soap": v = Art.Soap(parent, d); break;
            case "plates": v = Art.Plates(parent, d); break;
            case "bathbomb": v = Art.BathBomb(parent, d); break;
            case "ring": v = Art.FloatRing(parent, d); break;
            case "domino": v = Art.Domino(parent, b.w * K, b.h * K); break;
            case "ice": v = Art.Ice(parent, d); break;
            case "crate": v = Art.Crate(parent, b.w * K, b.h * K); break;
            case "chest": v = Art.Chest(parent, b.w * K, b.h * K); break;
            case "tnt": v = Art.Tnt(parent, d); break;
            case "stone": v = Art.Stone(parent, b.w * K, b.h * K); break;
            case "laser": v = Art.Laser(parent, d, b.dir == "h"); break;
            case "screw": v = Art.Screw(parent, d); break;
            case "plank": v = Art.Plank(parent, b.w * K, b.h * K); break;
            case "pixel": v = Art.Pixel(parent, b.w * K, b.color); break;
            default: v = Art.Boss(parent, d); break;
        }
        return v;
    }

    void MakeView(Block b)
    {
        // one shared mesh per block kind (instanced), one template per kind (cloned)
        string key = b.type + "_" + Mathf.RoundToInt(b.r * 2) + "_" + Mathf.RoundToInt(b.w) + "x" + Mathf.RoundToInt(b.h)
                     + (b.type == "pixel" ? ColorUtility.ToHtmlStringRGB(b.color) : "") + (b.type == "laser" ? b.dir : "");
        b.view = Spawn(key, parent =>
        {
            var v = BuildBlockArt(b, parent);
            float outline = b.type == "stone" || b.type == "screw" ? 0.0022f : b.type == "pixel" || b.type == "domino" ? 0f : 0.0016f;   // thin silhouette: blocks read apart in a crowd (desktop; phones skip outlines)
            float gloss = b.type == "screw" ? 0.9f : b.type == "chest" ? 0.7f : 0.4f;
            Art.Bake(v, key, gloss, outline, "Spark", "IceShell", "Arrow", "Glow", "Plate1", "Plate2", "Plate3");
            if (b.type == "plates")   // each plate: one baked, instanced mesh
                for (int i = 1; i <= 3; i++) { var pc = v.Find("Plate" + i); if (pc) Art.Bake(pc, "plate_" + Mathf.RoundToInt(b.r * 2), 0.9f, 0.003f); }
            if (App.Mobile) foreach (var r in v.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            if (b.type != "pixel" && b.type != "plank" && b.type != "screw" && b.type != "domino")
            {   // soft contact shadow on the water: blocks sit IN the bath instead of hovering, and read apart from the caustics
                if (contactMat == null) { contactMat = Art.Fade(new Color(0.06f, 0.1f, 0.3f, 0.34f)); contactMat.mainTexture = BlobTex(); }
                float sx = (b.rect ? b.w : b.r * 2) * K * 1.45f, sz = (b.rect ? b.h : b.r * 2) * K * 1.45f;
                var sh = Art.Prim(PrimitiveType.Quad, v, new Vector3(0.04f, 0.015f, -0.04f), new Vector3(sx, sz, 1f), contactMat, new Vector3(90, 0, 0));
                sh.name = "Contact"; var sr = sh.GetComponent<Renderer>(); sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sr.receiveShadows = false;
            }
            return v;
        }, world);
        b.view.position = W(b.p);
        b.baseScale = b.view.localScale;
        b.rends = b.view.GetComponentsInChildren<Renderer>().Where(r => r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Emission")).ToArray();
        AddPips(b);
    }

    // ---------- hit-count pips: one dark dot per hit still needed, floating above multi-hit blocks ----------
    static Material pipMat, contactMat;
    float lastBreakRing = -9f;
    // round soft blob (radial falloff) for contact shadows
    static Texture2D BlobTex()
    {
        const int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float dx = (x + 0.5f) / S * 2 - 1, dy = (y + 0.5f) / S * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(1 - r); px[y * S + x] = new Color(1, 1, 1, a * a * (3 - 2 * a));
        }
        t.SetPixels(px); t.Apply(); return t;
    }
    void AddPips(Block b)
    {
        if (b.Hard || b.type == "boss" || b.max <= 1 || b.max > 6) return;
        if (pipMat == null) pipMat = Art.Mat(new Color(1f, 0.97f, 0.9f), 0.8f, 0, 0.25f, 0.002f);   // cream dots with a thin outline: readable without black "eyes" everywhere
        float top = b.type == "plates" ? 0.5f : b.type == "chest" ? 1.15f : b.type == "crate" ? 1.0f : 1.02f;
        var root = Art.Root("Pips", b.view); root.localPosition = new Vector3(0, top, 0);
        for (int i = 0; i < b.max; i++)
        {
            var pip = Art.Prim(PrimitiveType.Sphere, root, new Vector3((i - (b.max - 1) / 2f) * 0.17f, 0, 0), new Vector3(0.14f, 0.07f, 0.14f), pipMat);
            pip.name = "Pip" + i; var r = pip.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }
        b.pips = root;
    }
    static void UpdatePips(Block b)
    {
        if (b.pips == null) return;
        for (int i = 0; i < b.pips.childCount; i++) b.pips.GetChild(i).gameObject.SetActive(i < b.hp);
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
        string kind = d.kind;
        d.view = Spawn("duck_" + kind, parent =>
        {
            var v = Art.Duck(parent, kind, out _);
            Art.Bake(v.Find("Body"), "duck_" + kind, kind == "heavy" ? 0.9f : 0.75f, 0.002f, "Spark");
            if (App.Mobile)
            {   // no realtime shadows on phones: a soft dark disc under the duck instead
                foreach (var r in v.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
                Art.Prim(PrimitiveType.Cylinder, v, new Vector3(0.08f, 0.012f, -0.06f), new Vector3(1.05f, 0.004f, 1.05f), Art.FadeShared(new Color(0.12f, 0.2f, 0.4f, 0.28f))).name = "Blob";
            }
            return v;
        }, world);
        d.body = d.view.Find("Body"); d.ring = d.view.Find("Ring"); d.ringCol = Color.clear;
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
                if (b.mvAmp > 0 && b.alive) { b.mvPhase += dt * b.mvSp; b.p.x = b.mvCx + Mathf.Sin(b.mvPhase) * b.mvAmp; }   // phase accumulates: speed can change without a jump
            }
            foreach (var d in ducks) { d.zapCd = Mathf.Max(0, d.zapCd - dt); d.bankCd = Mathf.Max(0, d.bankCd - dt); }
            if (moves > 1) lastWarned = false;
            if (state == "play" && !moving && moves == 1 && timers.Count == 0 && !lastWarned && GoalsLeft() > 0)
            {   // make the final shot feel tense
                lastWarned = true; BigText("LƯỢT CUỐI!", new Color(1f, 0.4f, 0.35f)); Sfx.Play("squeak", 0.7f, 0.7f); movesPulse = 0.5f;
            }
            if (state == "play" || state == "rush" || moving) for (int i = 0; i < 6; i++) Step(dt / 6f);
            if (moving)
            {   // pacing: once every duck is just drifting, brake harder so the turn ends without a dull wait
                float mx = 0; for (int i = 0; i < ducks.Count; i++) mx = Mathf.Max(mx, ducks[i].v.sqrMagnitude);
                if (mx < 70 * 70) { float k = Mathf.Exp(-6f * dt); for (int i = 0; i < ducks.Count; i++) ducks[i].v *= k; }
                shotClock += dt;
                if (shotClock > 8f)
                {   // safety net: currents / pads must never keep a turn going forever
                    float k = Mathf.Exp(-2.5f * (shotClock - 8f) * dt);
                    for (int i = 0; i < ducks.Count; i++) ducks[i].v *= k;
                    for (int i = 0; i < blocks.Count; i++) if (blocks[i].alive) blocks[i].vel *= k;
                }
            }
            if (moving && timers.Count == 0 && AllStill()) { foreach (var d in ducks) d.v = Vector2.zero; EndShot(); }
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
        // pulling the band tightens the view a touch (tension); releasing kicks the camera along the shot
        float tension = drag != null && !moving ? drag.pullVis : 0f;
        camTension = Mathf.Lerp(camTension, tension, 1 - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        cam.fieldOfView = 36f * (1 - camZoom * 0.1f + sw * 0.18f - camTension * 0.035f);
        camKick = Vector3.Lerp(camKick, Vector3.zero, 1 - Mathf.Exp(-7f * dt));
        bubbleTimer -= dt;
        if (bubbleTimer <= 0) { bubbleTimer = App.Mobile ? 0.7f : 0.25f; FxKit.Bubble(W(UnityEngine.Random.Range(TX + 20, TX + TW - 20), UnityEngine.Random.Range(TY + 20, TY + TH - 20))); }
        shake = Mathf.Max(0, shake - dt); comboPulse = Mathf.Max(0, comboPulse - dt);

        UpdateViews(dt);
        UpdateFx(dt);
        UpdateAim();
        RefreshHud();
        cam.transform.position = camBase + camKick + (shake > 0 ? UnityEngine.Random.insideUnitSphere * shake * 0.3f : Vector3.zero);
    }

    bool AllStill()
    {
        for (int i = 0; i < ducks.Count; i++) if (ducks[i].v.sqrMagnitude >= 14 * 14) return false;
        for (int i = 0; i < blocks.Count; i++) if (blocks[i].alive && blocks[i].vel.sqrMagnitude >= 1) return false;
        return true;
    }
    int GoalsLeft() { int c = 0; for (int i = 0; i < blocks.Count; i++) { var b = blocks[i]; if (b.alive && b.Goal) c++; } return c; }
    void Later(float t, Action fn) => timers.Add(new Timer { t = t, fn = fn });
    static bool Covered(Block b) => b.under != null && b.under.alive;
    // Feedback: "too much shaking / stutter on combos". Shake is much smaller and capped, off when the
    // Vibration setting is off; hitstops are short and at most one every 0.3 s (chains used to stack them).
    void Shake(float a) { if (!SaveData.Vibration) return; shake = Mathf.Max(shake, Mathf.Min(0.2f, a * 0.4f)); }
    float lastStop = -9f;
    void HitStop(float a) { if (t - lastStop < 0.3f) return; lastStop = t; freeze = Mathf.Max(freeze, Mathf.Min(a, 0.05f)); }

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
    static readonly Block bumperTmp = new Block();
    static bool CircleRect(Vector2 p, float r, Bumper b, out Hit hit)
    {
        var tmp = bumperTmp; tmp.p = new Vector2(b.x + b.w / 2, b.y + b.h / 2); tmp.w = b.w; tmp.h = b.h;   // reused: this ran 36x per frame and allocated each time
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
    static readonly Vector2[] Corners = { new Vector2(TX + TR, TY + TR), new Vector2(TX + TW - TR, TY + TR), new Vector2(TX + TR, TY + TH - TR), new Vector2(TX + TW - TR, TY + TH - TR) };
    static bool TubWalls(ref Vector2 p, ref Vector2 v, float r, float rest = 0.94f)
    {
        float L = TX + r, R = TX + TW - r, T = TY + r, B = TY + TH - r, cr = TR; bool hit = false;
        var corners = Corners;
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

    void SlideRings(float dt)
    {
        for (int bi = 0; bi < blocks.Count; bi++)
        {
            var b = blocks[bi];
            if (!b.alive || b.vel.sqrMagnitude < 1) continue;
            b.p += b.vel * dt;
            var v = b.vel; if (TubWalls(ref b.p, ref v, b.r, 0.8f)) Sfx.Play("push", 0.4f, 1.3f); b.vel = v;
            b.vel *= Mathf.Exp(-1.4f * dt);
            if (whirls.Count > 0) { float om = WhirlTurn(b.p, out var dep); if (om > 0) b.vel = Rot(b.vel, om * dt) * Mathf.Exp(-1.6f * dep * dt); }   // rings swirl too
            if (flows.Count > 0) { var fdir = FlowAt(b.p); if (fdir != Vector2.zero) b.vel += fdir * FLOW_ACC * 0.8f * dt; }   // ...and ride the currents
            if (b.vel.sqrMagnitude < 30 * 30) { b.vel = Vector2.zero; continue; }
            if (b.tag) { tags.Remove(b.tag); Destroy(b.tag.gameObject); b.tag = null; }   // it's been shown what it does
            if (b.vel.sqrMagnitude > 200 * 200 && UnityEngine.Random.value < dt * 30f) FxKit.Wake(W(b.p));
            for (int i = 0; i < blocks.Count; i++)
            {
                var o = blocks[i]; if (o == b || !o.alive || o.falling > 0 || Covered(o)) continue;
                if (!Collide(b.p, b.r, o, out var h)) continue;
                o.hitDir = b.vel;
                if (Damage(o, 1)) { b.vel *= 0.92f; continue; }
                b.p += h.n * h.pen; var vv = b.vel; Reflect(ref vv, h.n, 0.6f); b.vel = vv;
            }
            foreach (var dk in ducks)
            {   // a skating ring nudges ducks it bumps into
                var dl = dk.p - b.p; float dist = dl.magnitude, min = dk.r + b.r;
                if (dist >= min || dist < 0.01f) continue;
                var n = dl / dist; dk.p += n * (min - dist); dk.v += n * Mathf.Max(0, Vector2.Dot(b.vel, n)) * 0.8f; b.vel *= 0.6f;
            }
        }
    }

    void Step(float dt)
    {
        padClock += dt;
        foreach (var m in bumpers)
            if (m.amp > 0)
            {
                float o = Mathf.Sin(padClock * m.speed) * m.amp;
                if (m.vertical) m.y = m.y0 + o; else m.x = m.x0 + o;
            }
        SlideRings(dt);
        if (whirls.Count > 0)
            for (int di = 0; di < ducks.Count; di++)
            {
                var d = ducks[di]; if (d.v.sqrMagnitude < 40 * 40 || d.rocketT > 0) continue;   // resting ducks stay put; rockets fly straight
                float om = WhirlTurn(d.p, out var dep);
                if (om <= 0) { d.inWhirl = false; continue; }
                if (!d.inWhirl) { d.inWhirl = true; Sfx.Play("splash", 0.6f, 0.7f); FxKit.Ripple(W(d.p), 0.9f); FxKit.Splash(W(d.p), 4, 0.7f); }
                d.v = Rot(d.v, om * dt) * Mathf.Exp(-1.6f * dep * dt);   // turn + a little drag so nobody circles forever
            }
        if (flows.Count > 0)
            for (int di = 0; di < ducks.Count; di++)
            {   // currents carry moving ducks along (resting ducks stay put so aiming is fair)
                var d = ducks[di]; if (d.v.sqrMagnitude < 30 * 30 || d.rocketT > 0) continue;
                var fdir = FlowAt(d.p); if (fdir == Vector2.zero) continue;
                d.v += fdir * FLOW_ACC * dt;
                float sp = d.v.magnitude; if (sp > MAX_SPEED) d.v *= MAX_SPEED / sp;
                if (UnityEngine.Random.value < dt * 10f) FxKit.Wake(W(d.p));
            }
        if (portals.Count > 0)
            for (int di = 0; di < ducks.Count; di++)
            {
                var d = ducks[di]; d.portalCd = Mathf.Max(0, d.portalCd - dt);
                if (d.portalCd > 0 || d.v.sqrMagnitude < 60 * 60) continue;
                foreach (var pt in portals)
                {
                    bool inA = (d.p - pt.a).sqrMagnitude < PORTAL_R * PORTAL_R, inB = !inA && (d.p - pt.b).sqrMagnitude < PORTAL_R * PORTAL_R;
                    if (!inA && !inB) continue;
                    Vector2 from = inA ? pt.a : pt.b, to = inA ? pt.b : pt.a;
                    d.p = to + d.v.normalized * 4f; d.portalCd = 0.6f; d.squash = 0.25f;
                    if (d.trail) d.trail.Clear();
                    TeleportFx(from, to, pt.c);
                    if (!portalTaught) { portalTaught = true; PopText("CHUI CỐNG!", to + new Vector2(0, -50), pt.c, 52); }
                    break;
                }
            }
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
                    PopText("NẢY!", d.p, new Color(0.55f, 0.9f, 1f), 44 + shotBanks * 6);
                    Sfx.Play("squeak", 0.5f, 1.2f + shotBanks * 0.15f); FxKit.Sparkle(W(d.p, 0.3f), new Color(0.6f, 0.9f, 1f), 6);
                }
            }
            foreach (var m in bumpers)
                if (CircleRect(d.p, d.r, m, out var bh))
                {
                    d.p += bh.n * bh.pen; Reflect(ref d.v, bh.n, 1f);
                    float s = d.v.magnitude; if (s > 0) d.v *= Mathf.Max(s, 650) / s;
                    if (m.pulse <= 0.05f) { Sfx.Play("boing", 0.75f, UnityEngine.Random.Range(0.95f, 1.1f)); FxKit.Sparkle(W(d.p, 0.3f), new Color(1f, 0.6f, 0.75f), 4); }
                    m.pulse = 0.2f;
                }
            bool fast = d.v.magnitude > 70;
            foreach (var b in blocks)
            {
                if (!b.alive || b.falling > 0 || Covered(b)) continue;
                if (!Collide(d.p, d.r, b, out var hit)) continue;
                b.hitDir = d.v.sqrMagnitude > 1 ? d.v : -hit.n;
                if (b.type == "plank" && fast) foreach (var s in b.screws) if (s.alive && Vector2.Distance(s.p, d.p) < d.r + s.r + 16) Damage(s, 99);
                if (fast && d.kind == "bomb" && d.charged) { d.charged = false; d.spent = true; Explode(d.p, d.special ? 120 : 100, 3); PopText("BÙM!", d.p, new Color(1f, 0.35f, 0.2f), 70); }
                if (fast && d.kind == "zap" && d.zapCd <= 0 && !b.Hard) { d.zapCd = 0.15f; d.spent = true; ZapFrom(b); }
                if (fast && d.rainbowReady) { d.rainbowReady = false; d.spent = true; RainbowBurst(d); }
                if (d.rocketT > 0 && !b.Hard && b.type != "boss")
                {   // rocket drills straight through everything breakable
                    Damage(b, 3); FxKit.Flame(W(b.p)); continue;
                }
                if (b.type == "ring" && fast && b.hp > 1 && d.rocketT <= 0 && !d.frenzy)
                {   // float ring: the first knock sends it skating across the water, smashing whatever it meets
                    b.hp--; b.hit = 0.15f; UpdatePips(b); b.vel = d.v * 0.9f; b.hitDir = d.v;
                    if (b.tag) { tags.Remove(b.tag); Destroy(b.tag.gameObject); b.tag = null; }
                    d.p += hit.n * hit.pen; Reflect(ref d.v, hit.n, 0.4f); d.v *= 0.6f; d.squash = 0.12f;
                    Sfx.Play("squeak", 0.8f, 0.75f); Splash(b.p, 6); PopText("TRƯỢT!", b.p, new Color(1f, 0.5f, 0.45f), 44);
                    continue;
                }
                if (d.frenzy && !b.Hard && b.type != "boss") { if (Damage(b, 2)) { d.v *= 0.98f; continue; } }
                bool broke = fast && Damage(b, d.Dmg);
                if (broke && b.type != "chest") { d.v *= b.type == "pixel" ? 0.975f : 0.95f; continue; }
                if (b.Hard && fast && t - lastClank > 0.08f)
                {   // unbreakable: a bright clank so it reads as "solid", not as a missed smash
                    lastClank = t; var cp = d.p - hit.n * d.r;
                    Sfx.Play("clank", Mathf.Clamp01(d.v.magnitude / 1100f) * 0.8f, UnityEngine.Random.Range(0.9f, 1.15f));
                    FxKit.Sparkle(W(cp, 0.35f), new Color(1f, 0.92f, 0.6f), 5); b.hit = 0.1f;
                }
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
                    float j2 = -(1 + 1f) * vn / 2; a.v -= j2 * n; b.v += j2 * n;
                    if (-vn > 220)
                    {   // billiards feel: a bit of extra energy and a small random spread, so a pack of ducks bursts apart
                        float cap = MAX_SPEED * 1.1f;
                        a.v = Rot(a.v, UnityEngine.Random.Range(-0.06f, 0.06f)) * 1.08f; b.v = Rot(b.v, UnityEngine.Random.Range(-0.06f, 0.06f)) * 1.08f;
                        if (a.v.sqrMagnitude > cap * cap) a.v = a.v.normalized * cap; if (b.v.sqrMagnitude > cap * cap) b.v = b.v.normalized * cap;
                        a.hot = b.hot = true; a.squash = b.squash = 0.2f; var mid = (a.p + b.p) / 2;
                        Splash(mid, 8); FxKit.Sparkle(W(mid), Color.white, 8); AddRing(mid, 38 + Mathf.Min(pushes, 6) * 6, new Color(0.55f, 0.95f, 1f, 0.75f)); Shake(0.15f); HitStop(0.03f); pushes++; shotSmash++; comboPulse = 0.18f;
                        PopText(LevelLibrary.Unlocked("zap", level) && pushes == ZAP_PUSHES ? "ĐỦ 3 CỐC → VỊT SÉT!" : LevelLibrary.Unlocked("zap", level) && pushes < ZAP_PUSHES ? "CỐC! " + pushes + "/" + ZAP_PUSHES : pushes > 1 ? "CỐC x" + pushes + "!" : "CỐC!", mid, Color.white, 46 + Mathf.Min(20, pushes * 4));
                        Sfx.Play(pushes % 2 == 1 ? "quack" : "squeak", 0.9f, 1f + pushes * 0.08f); Sfx.Play("thud", 0.5f, 1.35f);   // the "clack" has weight
                    }
                }
            }
    }

    // ---------- damage ----------
    bool Damage(Block b, int dmg)
    {
        if (!b.alive || Covered(b)) return false;
        if (b.Hard) { b.hit = 0.12f; return false; }
        if (b.type == "boss" && b.hp > b.max / 2 && b.hp - Mathf.Min(dmg, 4) <= b.max / 2)
        {   // half health: the king gets angry and moves faster
            b.mvSp *= 1.7f; BigText("VUA NỔI GIẬN!", new Color(1f, 0.3f, 0.25f)); Flash(0.4f); Shake(0.4f);
            Sfx.Play("boom", 0.6f, 0.7f); FxKit.Smoke(W(b.p, 1.2f), 0.8f, 10); b.hit = 0.4f;
        }
        if (b.type == "boss") { dmg = Mathf.Min(dmg, 4); PopText("-" + dmg, b.p + new Vector2(UnityEngine.Random.Range(-40, 40), -60), new Color(1f, 0.35f, 0.2f), 56); Shake(0.2f); Sfx.Play("push", 1, 0.7f); }
        b.hp -= dmg; b.hit = 0.15f; UpdatePips(b);
        if (b.hp <= 0) { DestroyBlock(b); return true; }
        if (b.type == "ice")
        {
            var shell = b.view.Find("IceShell"); if (shell) { Debris(b.p, new Color(0.85f, 0.95f, 1f), 10, 0.5f); FxKit.Sparkle(W(b.p, 0.5f), new Color(0.85f, 0.97f, 1f), 8); Destroy(shell.gameObject); }
            PopText("RẮC!", b.p, new Color(0.75f, 0.92f, 1f), 40); Sfx.Play("ice");
            return false;
        }
        if (b.type == "crate") { b.view.localEulerAngles = new Vector3(0, UnityEngine.Random.Range(-12f, 12f), 0); Debris(b.p, new Color(0.69f, 0.48f, 0.27f), 4, 0.5f); }
        if (b.type == "plates")
        {   // top plate flies off and smashes
            var top = b.view.Find("Plate" + (b.hp + 1));
            if (top) { SpawnBit(top.position, Color.white, 0.3f, 5f, 0.7f, PrimitiveType.Cylinder); Destroy(top.gameObject); }
            Debris(b.p, Color.white, 8, 0.9f); PopText("XOẢNG!", b.p, new Color(0.7f, 0.85f, 1f), 40);
            Sfx.Play("glass", 0.6f, 1.25f); Sfx.Play("thud", 0.4f, 1.2f);
            FxKit.Shockwave(W(b.p), 1.1f, new Color(0.85f, 0.93f, 1f, 0.6f), 0.22f);
        }
        if (b.type == "vase")
        {   // cracks and wobbles
            b.view.localEulerAngles = new Vector3(UnityEngine.Random.Range(-10f, 10f), 0, UnityEngine.Random.Range(-10f, 10f));
            Debris(b.p, new Color(1f, 0.68f, 0.45f), 5, 0.5f); PopText("RẮC!", b.p, new Color(0.6f, 0.95f, 0.85f), 40); Sfx.Play("crunch", 0.5f, 1.3f); Sfx.Play("thud", 0.45f, 1.1f);
            FxKit.Shockwave(W(b.p), 1.1f, new Color(1f, 0.85f, 0.7f, 0.6f), 0.22f);
        }
        return false;
    }

    void DestroyBlock(Block b)
    {
        b.alive = false; shotSmash++; Sfx.Buzz(10);
        if (shotSmash == 1 && state == "play") HitStop(0.035f);
        if (b.tag) { tags.Remove(b.tag); Destroy(b.tag.gameObject); b.tag = null; }
        if (state == "play")
            foreach (var tr in TIERS)
                if (shotSmash == tr.need && LevelLibrary.Unlocked(tr.kind, level))
                { PopText("ĐỦ " + tr.need + " → " + Cap(tr.name) + "!", new Vector2(270, 265), tr.col, 58); Sfx.Play("star", 0.9f, 1.4f);
                  slowTimer = Mathf.Max(slowTimer, tr.need >= 25 ? 0.7f : 0.3f); camZoom = Mathf.Max(camZoom, tr.need >= 25 ? 0.9f : 0.5f); }   // milestone moment: brief slow-mo + push-in
        // Block-Blast style: emptying a whole row / column fires a line clear
        if (lineOf.TryGetValue(b, out var myLines))
            foreach (var ln in myLines)
                if (!ln.done && ln.m.All(x => !x.alive)) { ln.done = true; var L2 = ln; Later(0.04f, () => LineClear(L2)); }
        int pts = 10 * (1 + streak) * (1 + shotBanks); score += pts; comboPulse = 0.18f;   // bank shots multiply score
        if (shotSmash <= 3) PopText("+" + pts, b.p, Color.white, 40);
        Shake(b.type == "pixel" ? 0.06f : 0.12f);
        float pitch = PopPitch(shotSmash);
        if (b.type == "crate" || b.type == "screw" || b.type == "domino") Sfx.Play("wood", 0.7f, pitch);
        else if (b.type == "glass" || b.type == "plates" || b.type == "vase") Sfx.Play("glass", 0.8f, 0.85f + pitch * 0.18f);
        else Sfx.Play("crunch", 0.7f, pitch);
        if (b.type != "pixel" && b.type != "domino") Sfx.Play("thud", 0.55f, 0.9f + Mathf.Min(shotSmash, 12) * 0.02f);
        Sfx.Play("pop", 0.35f, pitch);
        Debris(b.p, BlockColor(b), b.type == "pixel" ? 4 : b.type == "boss" ? 40 : 9, b.type == "boss" ? 1.6f : 1f);
        FxKit.PopFlash(W(b.p, 0.45f), b.type == "pixel" ? 0.45f : 0.75f);
        for (int i = 0; i < blocks.Count; i++)
        {   // neighbours shudder as the shock passes through the water
            var o = blocks[i]; if (!o.alive || o == b || o.Hard) continue;
            float dd = (o.p - b.p).sqrMagnitude; if (dd < 75 * 75) o.wob = Mathf.Max(o.wob, 0.22f * (1 - Mathf.Sqrt(dd) / 75f));
        }
        if (b.type != "pixel") { var bc = Color.Lerp(BlockColor(b), Color.white, 0.35f); bc.a = 0.75f; FxKit.Shockwave(W(b.p), 1.35f, bc, 0.28f); }   // a soft ring in the block's own colour
        // material read: soap fizzes into bubbles, glass and china throw bright glints
        if (b.type == "soap") { int nb = App.Mobile ? 2 : 4; for (int i = 0; i < nb; i++) FxKit.Bubble(W(b.p + UnityEngine.Random.insideUnitCircle * 14f, 0.2f)); }
        else if (b.type == "glass" || b.type == "vase" || b.type == "plates") FxKit.Sparkle(W(b.p, 0.45f), new Color(0.9f, 0.97f, 1f), App.Mobile ? 3 : 5);
        if (b.view)
        {
            bool tall = b.Prop || b.type == "domino" || b.type == "bathbomb" || b.type == "tnt" || b.type == "pixel" || b.type == "gold" || b.type == "chest";   // tip over + sink
            if (tall)
            {   // knocked over in the direction it was hit, then sinks into the bath
                var hd = b.hitDir.sqrMagnitude > 0.01f ? b.hitDir.normalized : UnityEngine.Random.insideUnitCircle.normalized;
                var wd = new Vector3(hd.x, 0, -hd.y);
                dying.Add(new Dying { t = b.view, life = 0.32f, max = 0.32f, s0 = b.view.localScale, p0 = b.view.position, dir = wd, axis = Vector3.Cross(Vector3.up, wd), r0 = b.view.rotation, topple = true });
            }
            else dying.Add(new Dying { t = b.view, life = 0.12f, s0 = b.view.localScale });
        }
        if (state == "rush") { rushCoins += 2; SpawnCoin(b.p); }
        else if (shotSmash % 4 == 0 || b.type == "chest" || b.type == "boss") SpawnCoin(b.p);
        if (b.Goal && state == "play" && GoalsLeft() == 0)
        {   // last target: slow-motion + camera punch
            slowTimer = 0.75f; camZoom = 1f; BigText("SẠCH BÓNG!", new Color(0.55f, 0.95f, 0.6f)); for (int k = 0; k < 7; k++) FxKit.Sparkle(W(UnityEngine.Random.Range(TX + 40, TX + TW - 40), UnityEngine.Random.Range(TY + 60, TY + TH - 120), 0.3f), new Color(0.85f, 1f, 0.9f), 6); Sfx.Play("combo", 1f, 1.3f);
        }
        switch (b.type)
        {
            case "tnt": Explode(b.p, 95, 3); break;
            case "glass":   // shatters into a spray of shards
                for (int i = 0; i < 5; i++) SpawnBit(W(b.p, 0.5f), new Color(0.56f, 0.86f, 0.69f), UnityEngine.Random.Range(0.06f, 0.14f), 6f, 0.7f, PrimitiveType.Cube);
                FxKit.Sparkle(W(b.p, 0.4f), new Color(0.8f, 0.95f, 1f), 8); FxKit.Splash(W(b.p), 6, 1.2f);
                if (UnityEngine.Random.value < 0.3f) PopText("XOẢNG!", b.p, new Color(0.7f, 0.9f, 1f), 44);
                break;
            case "vase":
                for (int i = 0; i < 4; i++) SpawnBit(W(b.p, 0.4f), new Color(1f, 0.68f, 0.45f), UnityEngine.Random.Range(0.14f, 0.24f), 5f, 0.8f, PrimitiveType.Sphere);
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
                Explode(b.p, 85, 2); PopText("XÌ XÈO!", b.p, new Color(0.85f, 0.7f, 1f), 64); Sfx.Play("fizz", 0.7f, UnityEngine.Random.Range(0.95f, 1.1f));
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
                HitStop(0.06f); Shake(0.3f); PopText("+2 Vịt!", b.p, new Color(1f, 0.88f, 0.3f), 56); Sfx.Play("coin"); FxKit.StarBurst(W(b.p, 0.5f));
                for (int i = 0; i < 2; i++)
                {
                    var nd = NewDuck(b.p + new Vector2(i == 0 ? -14 : 14, 0), "normal");
                    float a = Mathf.PI / 2 + (i == 0 ? 0.6f : -0.6f); nd.v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 420;
                }
                break;
            case "boss":
                HitStop(0.15f); Shake(0.9f); slowTimer = 1f; Flash(0.7f); camZoom = 1f; BigText("HẠ VUA BÁNH QUY!", new Color(1f, 0.88f, 0.3f)); FxKit.Confetti(W(b.p, 1f), 3f, 80); bossKilled = true; Sfx.Play("boom"); Sfx.Vibrate();
                for (int i = 0; i < 6; i++) { int k = i; Later(k * 0.08f, () => { var q = b.p + UnityEngine.Random.insideUnitCircle * 60; AddRing(q, 70, new Color(1f, 0.7f, 0.3f, 0.7f)); Debris(q, new Color(0.94f, 0.63f, 0.29f), 18, 1.4f); Sfx.Play("boom", 0.5f, 1.2f); }); }
                break;
        }
    }

    static readonly int[] PENTA = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24 };
    static float PopPitch(int n)
    {
        n = Mathf.Max(0, n - 1);
        // climb the scale, then keep bouncing between the top three notes (no jarring drop, no shrill top)
        int semis = n < 8 ? PENTA[n] : PENTA[6 + (n - 8) % 3];
        return Mathf.Pow(2f, semis / 12f) * 0.85f;
    }

    // easing: overshoot then settle (the "pop" in pop-ups)
    static float BackOut(float x) { x = Mathf.Clamp01(x); const float c1 = 2.2f, c3 = c1 + 1f; float m = x - 1f; return 1f + c3 * m * m * m + c1 * m * m; }

    static Color BlockColor(Block b)
    {
        switch (b.type)
        {
            case "ring": return new Color(1f, 0.38f, 0.36f);
            case "cookie": case "ice": case "boss": return new Color(0.94f, 0.63f, 0.29f);
            case "crate": return new Color(0.69f, 0.48f, 0.27f);
            case "chest": case "gold": return new Color(1f, 0.8f, 0.25f);
            case "glass": return new Color(0.56f, 0.86f, 0.69f);
            case "vase": return new Color(1f, 0.68f, 0.45f);
            case "soap": return new Color(1f, 0.61f, 0.78f);
            case "plates": return new Color(0.75f, 0.88f, 1f);
            case "bathbomb": return new Color(0.65f, 0.42f, 1f);
            case "domino": return new Color(1f, 0.97f, 0.9f);
            case "tnt": return new Color(0.91f, 0.28f, 0.23f);
            case "laser": return new Color(0.7f, 0.53f, 1f);
            case "screw": return new Color(0.82f, 0.85f, 0.9f);
            case "pixel": return b.color;
            default: return Color.gray;
        }
    }

    float lastBoomT = -9f, shotClock; int boomChain;
    void Explode(Vector2 p, float R, int dmg)
    {
        HitStop(0.05f); Shake(0.45f); Flash(0.28f);
        boomChain = t - lastBoomT < 0.5f ? boomChain + 1 : 1; lastBoomT = t;
        if (state == "play" && (boomChain == 3 || boomChain == 6 || boomChain == 10))
        {   // chain reaction: give it a moment of slow motion so the player can enjoy it
            BigText(boomChain >= 10 ? "SIÊU NỔ x" + boomChain + "!" : boomChain >= 6 ? "NỔ LIÊN HOÀN x" + boomChain + "!" : "NỔ DÂY CHUYỀN!", new Color(1f, 0.6f, 0.3f));
            slowTimer = Mathf.Max(slowTimer, 0.35f + boomChain * 0.03f); camZoom = Mathf.Max(camZoom, 0.55f);
            Sfx.Play("combo", 0.9f, 0.9f + boomChain * 0.04f);
        }
        AddRing(p, R * 1.3f, new Color(1f, 0.75f, 0.35f, 0.5f));
        FxKit.Explosion(W(p), R * K * 0.6f);
        Debris(p, new Color(1f, 0.7f, 0.28f), 16, 1.5f); Debris(p, new Color(1f, 0.35f, 0.2f), 10, 1.2f);
        Sfx.Play("boom", 0.8f, UnityEngine.Random.Range(0.9f, 1.1f)); Sfx.Vibrate();
        FxKit.Shockwave(W(p), R * K * 2.9f, new Color(1f, 0.78f, 0.42f, 0.7f), 0.38f);
        FxKit.Splash(W(p), App.Mobile ? 8 : 16, 1.5f);
        FxKit.Embers(W(p, 0.3f), R * K, App.Mobile ? 6 : 16);
        camZoom = Mathf.Max(camZoom, 0.3f);
        Later(0.09f, () => { if (world != null) { FxKit.Shockwave(W(p), R * K * 2.1f, new Color(1f, 0.95f, 0.85f, 0.45f), 0.3f); FxKit.PopFlash(W(p, 0.6f), R * K * 1.2f); } });
        Later(0.07f, () =>
        {
            foreach (var b in blocks.ToArray()) if (b.alive && Vector2.Distance(b.p, p) < R + 20) { b.hitDir = b.p - p; Damage(b, dmg); }
            PushDucks(p, R + 60, 700);
            foreach (var b in blocks)
                if (b.alive && b.type == "ring")
                {   // blast wave shoves surviving float rings away: explosions can launch a ring into the pile
                    var dl = b.p - p; float dist = Mathf.Max(1, dl.magnitude);
                    if (dist < R + 60) b.vel += dl / dist * (1 - dist / (R + 60)) * 900;
                }
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
        Shake(0.35f); HitStop(0.04f);
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
        pl.alive = false; pl.falling = 0; Shake(0.6f); Flash(0.3f); HitStop(0.07f);
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
        BigText("CẦU VỒNG!", new Color(1f, 0.55f, 0.85f)); Sfx.Play("frenzy", 0.9f, 1.3f); Shake(0.4f); Flash(0.45f); HitStop(0.06f);
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

    // ---------- floating UI tags (who is special, what a chest gives) ----------
    readonly List<RectTransform> tags = new List<RectTransform>();
    static Texture2D tagDuckIcon;
    RectTransform MakeTag(string text, Color bg, bool duckIcon)
    {
        var p = UIKit.Panel(dynLayer, bg, "Tag"); p.raycastTarget = false;
        var o = p.gameObject.AddComponent<Outline>(); o.effectColor = new Color(1, 1, 1, 0.9f); o.effectDistance = new Vector2(2, -2);
        var rt = p.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(duckIcon ? 150 : 24 + text.Length * 17, 44);
        var l = UIKit.Label(rt, text, 26, Color.white); l.raycastTarget = false;
        if (duckIcon)
        {
            if (tagDuckIcon == null) tagDuckIcon = LevelLibrary.ArtTexture("duck", false);
            var ic = UIKit.Rect("Icon", rt).gameObject.AddComponent<RawImage>(); ic.texture = tagDuckIcon; ic.raycastTarget = false;
            UIKit.Place(ic.rectTransform, 0, 0.5f, 30, 2, 40, 34);
            l.rectTransform.offsetMin = new Vector2(44, 0);
        }
        tags.Add(rt); return rt;
    }
    void ClearTags() { foreach (var tg in tags) if (tg) Destroy(tg.gameObject); tags.Clear(); }
    static string KindTag(string kind) => kind == "bomb" ? "BOM" : kind == "zap" ? "SÉT" : kind == "rocket" ? "TÊN LỬA" : kind == "rainbow" ? "CẦU VỒNG" : kind == "heavy" ? "SẮT" : null;
    static Color KindColor(string kind) => kind == "bomb" ? new Color(1f, 0.38f, 0.32f) : kind == "zap" ? new Color(0.25f, 0.62f, 1f)
        : kind == "rocket" ? new Color(1f, 0.55f, 0.2f) : kind == "rainbow" ? new Color(0.85f, 0.4f, 0.9f) : new Color(0.45f, 0.5f, 0.62f);

    readonly List<(RectTransform rt, Vector2 from, Duck d, float t)> hatchFliers = new List<(RectTransform, Vector2, Duck, float)>();
    void UpdateHatchFliers(float rdt)
    {
        for (int i = hatchFliers.Count - 1; i >= 0; i--)
        {
            var f = hatchFliers[i]; f.t += rdt / 0.55f; hatchFliers[i] = f;
            if (f.rt == null || f.d == null || f.d.view == null || f.t >= 1f)
            {
                if (f.rt) Destroy(f.rt.gameObject);
                if (f.d != null && f.d.view != null) { FxKit.StarBurst(W(f.d.p, 0.8f), 10); Sfx.Play("star", 0.7f, 1.6f); }
                hatchFliers.RemoveAt(i); continue;
            }
            var to = WorldToHud(W(f.d.p, 0.6f));
            var mid = (f.from + to) / 2 + new Vector2(160, 120);
            float e = f.t * f.t * (3 - 2 * f.t);
            f.rt.anchoredPosition = Vector2.Lerp(Vector2.Lerp(f.from, mid, e), Vector2.Lerp(mid, to, e), e);
            f.rt.localScale = Vector3.one * (1f + Mathf.Sin(f.t * Mathf.PI) * 0.6f);
            f.rt.localEulerAngles = new Vector3(0, 0, f.t * 540f);
        }
    }

    void HatchSpecial(string kind, string name, Color col)
    {
        // a special never adds a duck: it transforms ONE random plain duck (no plain duck -> no reward)
        var normals = ducks.Where(x => !x.clone && !x.special && x.kind == "normal" && x.spawnT <= 0).ToList();
        if (normals.Count == 0) return;
        var d = normals[UnityEngine.Random.Range(0, normals.Count)];
        SetKind(d, kind, true);
        d.spawnT = 0.45f;
        var star = UIKit.Dot(dynLayer, new Color(1f, 0.85f, 0.25f), "HatchStar"); star.raycastTarget = false;
        var so = star.gameObject.AddComponent<Outline>(); so.effectColor = Color.white; so.effectDistance = new Vector2(3, -3);
        star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); star.rectTransform.sizeDelta = new Vector2(46, 46);
        var from = WorldToHudFromRect(comboBar != null && comboBar.gameObject.activeInHierarchy ? comboBar : scoreTxt.rectTransform);
        hatchFliers.Add((star.rectTransform, from, d, 0f));   // little hop + splash so the player sees which duck changed
        BigText(name + "!", col); Sfx.Play("star", 1f, 1.3f); Sfx.Play("quack", 0.8f, 1.3f);
        FxKit.StarBurst(W(d.p, 0.8f), 18);
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
        d.v = v; d.hot = true; moves--; moving = true; shotClock = 0; shotSmash = 0; hint = ""; pushes = 0; frenzyShot = false; shotBanks = 0; shotClears = 0; dominoFalls = 0;
        foreach (var k in ducks) k.charged = true;
        d.stretch = 0.22f; d.pullVis = 0;
        Sfx.Play("shoot"); Sfx.Play("squeak", 0.5f, 1.3f); Sfx.Buzz(18);
        FxKit.Shockwave(W(d.p), 1.5f, new Color(0.85f, 0.97f, 1f, 0.7f), 0.25f);   // launch ring on the water
        { var vn = v.normalized; camKick = new Vector3(vn.x, 0, -vn.y) * Mathf.Clamp01(v.magnitude / MAX_SPEED) * 0.16f; } FxKit.Splash(W(d.p), 6); FxKit.Ripple(W(d.p), 1.1f);
        if (d.special) d.spent = true;
        if (moves == 0 && !d.frenzy && state == "play" && GoalsLeft() <= 6)
        {   // last shot and so close: the duck drills through everything (frustration saver, feels great)
            d.frenzy = true; BigText("CƠ HỘI CUỐI!", new Color(1f, 0.85f, 0.3f)); Sfx.Play("frenzy", 0.8f, 1.2f); FxKit.StarBurst(W(d.p, 0.5f), 14);
        }
        if (d.kind == "rocket")
        {
            d.rocketT = ROCKET_TIME; d.v = v.normalized * ROCKET_SPEED;
            BigText("PHÓNG!", new Color(1f, 0.55f, 0.2f)); Sfx.Play("boom", 0.6f, 1.4f); Shake(0.25f);
        }
        if (d.kind == "rainbow") d.rainbowReady = true;
        if (frenzyReady)
        {
            frenzyReady = false; fever = 0; frenzyShot = true; d.frenzy = true; BigText("DUCK FRENZY! x5 VỊT", new Color(1f, 0.5f, 0.75f)); Shake(0.3f); Sfx.Play("frenzy"); FxKit.StarBurst(W(d.p, 0.5f), 20);
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
        if (pushes >= 3 && state == "play")
        {   // a good carom gets its own little celebration
            PopText((pushes >= 5 ? "BI-A THẦN SẦU! x" : "BI-A ĐẸP! x") + pushes, new Vector2(270, 470), new Color(0.45f, 0.9f, 1f), 66 + Mathf.Min(pushes, 8) * 3);
            Sfx.Play("combo", 0.9f, 1.15f); FxKit.StarBurst(W(270, 470, 0.5f), 10 + pushes * 2);
        }
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
        if (n >= 6) { streak++; Sfx.Play("combo", 0.8f, 1f + streak * 0.06f); if (streak >= 1) PopText("Chuỗi " + (streak + 1) + " phát · Điểm x" + (streak + 1), new Vector2(270, 620), new Color(1f, 0.7f, 0.28f), 64); }
        else if (streak > 0) { PopText("Mất chuỗi…", new Vector2(270, 620), new Color(0.8f, 0.84f, 0.9f), 44); streak = 0; }
        if (!frenzyShot && !frenzyReady && (level > 2 || LevelLibrary.IsSandbox(level)))
        {
            float before = fever; fever = Mathf.Min(FEVER_MAX, fever + n);
            int gain = Mathf.RoundToInt(fever - before);
            if (gain > 0) { feverPop.text = "+" + gain + " FRENZY"; feverPopT = 1.1f; feverPop.gameObject.SetActive(true); }
            if (fever >= FEVER_MAX)
            {
                frenzyReady = true; BigText("FRENZY SẴN SÀNG!", new Color(1f, 0.5f, 0.75f));
                if (PlayerPrefs.GetInt("ds_frenzy_tut", 0) == 0 && state == "play")
                {   // explain it once, the first time it happens
                    PlayerPrefs.SetInt("ds_frenzy_tut", 1); PlayerPrefs.Save();
                    introTitle.text = "DUCK FRENZY!"; introTitle.color = new Color(1f, 0.45f, 0.72f);
                    introBody.text = "<size=40>Thanh FRENZY đầy rồi!</size>\n\n<size=36>Phát bắn <b>tiếp theo</b>:\n<b>5 vịt</b> cùng lao ra theo hình quạt\nvà <b>xuyên qua</b> đồ dễ vỡ</size>\n\n<size=30><color=#7a4dd8>Nạp lại: mỗi món phá được +1</color></size>";
                    introRoot.sizeDelta = new Vector2(820, 600); introMax = introTimer = 5f; introRoot.gameObject.SetActive(true); introRoot.SetAsLastSibling();
                }
            }
        }
    }

    // ---------- win / lose ----------
    void Win()
    {
        state = "win";
        int left = movesAtClear >= 0 ? movesAtClear : moves; leftAtWin = left;
        stars = left >= Mathf.CeilToInt(startMoves * 0.4f) ? 3 : left >= Mathf.CeilToInt(startMoves * 0.15f) ? 2 : 1;
        score += left * 150;
        if (!rewarded && LevelLibrary.IsSandbox(level)) { rewarded = true; reward = 0; }   // test levels: no progress saved
        newArt = false;
        if (!LevelLibrary.IsSandbox(level) && state == "win")
        {
            if (!string.IsNullOrEmpty(levelArt) && !SaveData.HasArt(levelArt)) { SaveData.SetArt(levelArt); newArt = true; }
        }
        if (!rewarded)
        {
            rewarded = true;
            bool first = SaveData.GetStars(level) == 0;
            reward = 20 + stars * 10 + left * 5 + rushCoins + (hardLevel ? 80 : 0) + (first ? 20 : 0);
            SaveData.Coins += reward; SaveData.SetStars(level, stars);
            SaveData.MaxLevel = Mathf.Min(LevelLibrary.Count, Mathf.Max(SaveData.MaxLevel, level + 1));
            if (hardLevel && first) foreach (var k in PRICE.Keys) SaveData.SetBoost(k, SaveData.GetBoost(k) + 1);   // hard level chest
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
        moving = true; shotClock = 0; shotSmash = 0; pushes = 0;
        Sfx.Play("shoot"); Sfx.Play("squeak", 0.5f, 1.4f); FxKit.Splash(W(d.p), 6);
        PopText("+" + (moves + 1), d.p, new Color(1f, 0.85f, 0.3f), 50);
        rushTimer = 0.12f;
    }

    // ---------- intro card / tutorial hand / coin flights / win anim (real-time) ----------
    void ShowIntro(LevelDef L)
    {
        var g = new List<string> { "<b>PHÁ SẠCH BỒN</b>", "<size=40>đập vỡ hết " + GoalsLeft() + " món</size>" };
        introTitle.text = LevelLibrary.Title(level);
        introTitle.color = hardLevel ? new Color(1f, 0.35f, 0.3f) : new Color(0.6f, 0.53f, 0.93f);
        // reminder of the combo ducks the player has already unlocked
        var known = LevelLibrary.Specials.Where(s => LevelLibrary.Unlocked(s.kind, level) && LevelLibrary.NewSpecialAt(level) != s).ToList();
        string combos = "";   // no combo recipe reminder on every level (players found it patronising); the COMBO ? guide has it
        introBody.text = string.Join("\n", g) + "\n\n" + moves + " lượt bắn\n<size=30><color=#c9a400>3 sao khi còn ≥ " + Mathf.CeilToInt(startMoves * 0.4f) + " lượt</color></size>" + combos;
        introMax = introTimer = 1.6f;
        introRoot.sizeDelta = new Vector2(760, 520);

        // onboarding: level 1 = how to play in 3 steps; levels 2-5 = the one new thing this level teaches
        if (level == 1)
        {
            introTitle.text = "CÁCH CHƠI";
            introBody.text = "<size=44><b>1.</b> Chạm vào vịt, <b>kéo ra sau</b>\n<b>2.</b> <b>Thả tay</b> để bắn\n<b>3.</b> <b>Phá sạch</b> mọi món trong bồn</size>\n\n<size=32>" + moves + " lượt bắn · chạm để bắt đầu</size>";
            introRoot.sizeDelta = new Vector2(820, 560); introMax = introTimer = 6f;
        }
        else if (!string.IsNullOrEmpty(L.hint) && !LevelLibrary.IsSandbox(level))
        {   // one short line about what's special here (shelves, rings, hard level...), then the goal
            introBody.text = "<size=38><color=#7a4dd8><b>" + (level <= LevelLibrary.Onboarding || level == 19 || level == 23 ? "MỚI:" : "MẸO:") + "</b></color> " + L.hint + "</size>\n\n<b>PHÁ SẠCH BỒN</b> · " + moves + " lượt";
            introRoot.sizeDelta = new Vector2(820, 520); introMax = introTimer = level <= LevelLibrary.Onboarding || level == 19 || level == 23 ? 4f : 3f;
        }

        // unlock level: teach how to earn the new duck and how it works, and give one free copy to try
        var nw = LevelLibrary.NewSpecialAt(level);
        if (nw != null)
        {
            introTitle.text = "VỊT MỚI!";
            introBody.text = "<size=58><color=#ff7a3d>" + nw.name + "</color></size>\n\n<size=38>" + nw.use + "</size>\n<size=32><color=#7a4dd8>Có khi: " + nw.how.ToLower() + "</color></size>";
            introRoot.sizeDelta = new Vector2(820, 520);
            introMax = introTimer = 6f;
            var pool = ducks.Where(x => x.kind == "normal").ToList(); if (pool.Count == 0) pool = ducks.Where(x => !x.special).ToList();
            if (pool.Count > 0) SetKind(pool[UnityEngine.Random.Range(0, pool.Count)], nw.kind, true);
            hint = "Thử ngay " + Cap(nw.name) + "!";
        }
        introBody.verticalOverflow = VerticalWrapMode.Overflow; introBody.horizontalOverflow = HorizontalWrapMode.Wrap;
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
        UpdateHint(rdt); UpdateHatchFliers(rdt);
        bool showHand = level <= 2 && state == "play" && !moving && drag == null && introTimer <= 0 && moves == startMoves && ducks.Count > 0 && !hintShown;
        handRt.gameObject.SetActive(showHand || hintShown);
        if (hintShown && !showHand)
        {   // hint hand: drag the suggested duck back, opposite to the suggested direction
            var d = ducks[Mathf.Clamp(hintBestDuck, 0, ducks.Count - 1)];
            float k = Mathf.Repeat(Time.unscaledTime, 1.6f) / 1.2f;
            var a = WorldToHud(W(d.p, 0.4f)); var b = WorldToHud(W(d.p - hintBestDir * 120f, 0.4f));
            handRt.anchoredPosition = Vector2.Lerp(a, b, Mathf.SmoothStep(0, 1, Mathf.Clamp01(k)));
            handRt.localScale = Vector3.one * (k < 0.1f ? 0.85f : 1f);
            handRt.GetComponent<CanvasGroup>().alpha = k > 1 ? 0 : 0.95f;
        }
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
        if (popupRoot.gameObject.activeSelf)
        {
            if (popCard == null) popCard = popupRoot.Find("Card");
            if (popCard) popCard.localScale = Vector3.one * BackOut((state == "win" ? popAnim : loseAnim) / 0.35f);
            if (state != "win") loseAnim += rdt;
        }
        if (sunburst != null)
        {
            bool sb = popupRoot.gameObject.activeSelf && state == "win";
            if (sunburst.gameObject.activeSelf != sb) sunburst.gameObject.SetActive(sb);
            if (sb) { sunburst.rectTransform.localEulerAngles = new Vector3(0, 0, -Time.unscaledTime * 18f); sunburst.rectTransform.localScale = Vector3.one * BackOut(popAnim / 0.5f); }
        }
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
        var img = UIKit.Dot(dynLayer, new Color(1f, 0.82f, 0.25f), "Coin");
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

    // boosters land with an animation first: the hammer swings down, the water bomb drops from the sky
    bool boosterBusy; Coroutine boosterCo; float lastClank = -9f; Vector3 camKick; float camTension;
    static Material hamHandle, hamHead, balloonMat;
    void ApplyTarget(Vector2 p)
    {
        if (boosterBusy) return;
        string k = target; target = null; SaveData.SetBoost(k, SaveData.GetBoost(k) - 1); shotSmash = 0;
        boosterCo = StartCoroutine(BoosterAnim(k, p));
    }

    System.Collections.IEnumerator BoosterAnim(string k, Vector2 p)
    {
        boosterBusy = true;
        if (k == "bomb")
        {
            if (balloonMat == null) balloonMat = Art.Mat(new Color(0.3f, 0.65f, 1f), 0.95f);
            var b = Art.Prim(PrimitiveType.Sphere, fx, W(p, 7f), Vector3.one * 0.7f, balloonMat);
            Sfx.Play("squeak", 0.5f, 1.6f);
            Vector3 from = W(p, 7f), to = W(p, 0.35f);
            for (float t0 = 0; t0 < 0.3f; t0 += Time.deltaTime)
            {
                float e = t0 / 0.3f; b.position = Vector3.Lerp(from, to, e * e);
                b.localScale = new Vector3(0.7f - e * 0.08f, 0.7f + e * 0.25f, 0.7f - e * 0.08f);   // stretches as it falls
                yield return null;
            }
            Destroy(b.gameObject);
            ApplyBooster(k, p);
        }
        else
        {
            if (hamHandle == null) { hamHandle = Art.Mat(new Color(0.62f, 0.42f, 0.25f), 0.4f); hamHead = Art.Mat(new Color(0.9f, 0.32f, 0.3f), 0.7f); }
            var root = new GameObject("Hammer").transform; root.SetParent(fx, false); root.position = W(p, 0.45f) + new Vector3(-1.35f * 1.25f, 0, 0.15f); root.localScale = Vector3.one * 1.25f;
            Art.Prim(PrimitiveType.Cylinder, root, new Vector3(0.65f, 0, 0), new Vector3(0.13f, 0.65f, 0.13f), hamHandle, new Vector3(0, 0, 90));
            Art.Prim(PrimitiveType.Cube, root, new Vector3(1.35f, 0, 0), new Vector3(0.42f, 0.7f, 0.46f), hamHead);
            Art.Prim(PrimitiveType.Cube, root, new Vector3(1.35f, -0.3f, 0), new Vector3(0.46f, 0.1f, 0.5f), hamHandle);   // striking face
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // wind-up, then a fast accelerating swing down
            for (float t0 = 0; t0 < 0.14f; t0 += Time.deltaTime) { root.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(40f, 80f, t0 / 0.14f)); yield return null; }
            Sfx.Play("squeak", 0.4f, 0.6f);
            for (float t0 = 0; t0 < 0.12f; t0 += Time.deltaTime) { float e = t0 / 0.12f; root.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(80f, -4f, e * e)); yield return null; }
            root.localEulerAngles = new Vector3(0, 0, -4f);
            ApplyBooster(k, p);
            yield return new WaitForSeconds(0.14f);
            for (float t0 = 0; t0 < 0.22f; t0 += Time.deltaTime)
            {
                float e = t0 / 0.22f; root.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(-4f, 50f, e)); root.localScale = Vector3.one * 1.25f * (1 - e * e);
                yield return null;
            }
            Destroy(root.gameObject);
        }
        boosterBusy = false;
    }

    void ApplyBooster(string k, Vector2 p)
    {
        if (k == "bomb") { Explode(p, 120, 3); PopText("ẦM!", p, new Color(0.3f, 0.75f, 1f), 70); Splash(p, 30); return; }
        Block best = null; float bd = 1e9f;
        foreach (var b in blocks)
        {
            if (!b.alive || Covered(b)) continue;
            float dd = Vector2.Distance(b.p, p) - (b.rect ? b.w / 2 : b.r);
            if (dd < bd) { bd = dd; best = b; }
        }
        if (best == null || bd > 30) { SaveData.SetBoost(k, SaveData.GetBoost(k) + 1); PopText("Trượt!", p, Color.white, 40); return; }
        HitStop(0.08f); Shake(0.5f); PopText("BỐP!", best.p, new Color(1f, 0.88f, 0.3f), 70); Sfx.Play("crash", 0.7f, 1.4f);
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
            else
            {   // a tap (or too-short pull) still answers: the duck quacks, and a short pull gets a nudge
                d.squash = 0.2f; Sfx.Play("quack", 0.6f, UnityEngine.Random.Range(1.1f, 1.3f));
                PopText(pv.z > 6 ? "Kéo xa hơn!" : "Quạc!", d.p + new Vector2(0, -46), Color.white, pv.z > 6 ? 38 : 32);
            }
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

    // ===================== idle hint: after a few idle seconds, suggest the shot that smashes the most =====================
    readonly List<Transform> hintDots = new List<Transform>();
    readonly List<Vector2> hintPath = new List<Vector2>();
    readonly HashSet<Block> simHits = new HashSet<Block>();
    float idleT; int hintSearch = -1, hintBestDuck = -1; float hintBestScore; Vector2 hintBestDir; bool hintReady, hintShown;
    const int HINT_ANGLES = 24;
    int predJump = -1;

    // cheap straight-line prediction (same rules as the aim preview); weights chain-reaction blocks higher
    float PredictShot(Duck d, Vector2 dir, List<Vector2> path)
    {
        Vector2 gp = d.p, gv = dir; float len = 260 + MAX_PULL * 4.5f, score = 0; simHits.Clear();
        path?.Clear(); path?.Add(gp);
        int dmg = d.kind == "rocket" ? 3 : d.Dmg; float r2 = 0; bool jumped = false; if (path != null) predJump = -1;
        for (float st = 0; st < len; st += 6)
        {
            gp += gv * 6;
            if (whirls.Count > 0 && d.kind != "rocket") { float om = WhirlTurn(gp, out _); if (om > 0) gv = Rot(gv, om * 6f / MAX_SPEED * 1.1f); }
            if (flows.Count > 0 && d.kind != "rocket") { var fdir = FlowAt(gp); if (fdir != Vector2.zero) gv = (gv * MAX_SPEED + fdir * FLOW_ACC * 6f / MAX_SPEED).normalized; }
            if (TubWalls(ref gp, ref gv, d.r, 1f)) path?.Add(gp);
            foreach (var m in bumpers) if (CircleRect(gp, d.r, m, out var bh)) { gp += bh.n * bh.pen; Reflect(ref gv, bh.n, 1f); path?.Add(gp); }
            gv.Normalize();
            if (!jumped && st > 10)
                foreach (var pt in portals)
                {
                    bool inA = (gp - pt.a).sqrMagnitude < PORTAL_R * PORTAL_R, inB = !inA && (gp - pt.b).sqrMagnitude < PORTAL_R * PORTAL_R;
                    if (!inA && !inB) continue;
                    path?.Add(gp); gp = (inA ? pt.b : pt.a) + gv * 4f; jumped = true; score += 30f;   // a pipe shot is the thing to show off (and the straight-line guess undersells its bounces)
                    if (path != null) { predJump = path.Count; path.Add(gp); }   // the hop itself is not drawn
                    break;
                }
            bool stop = false;
            for (int i = 0; i < ducks.Count; i++) { var o = ducks[i]; if (o == d) continue; r2 = o.r + d.r; if ((o.p - gp).sqrMagnitude < r2 * r2) { stop = true; score += 2f; break; } }
            if (!stop)
                for (int i = 0; i < blocks.Count; i++)
                {
                    var b = blocks[i];
                    if (!b.alive || Covered(b) || b.falling > 0 || simHits.Contains(b)) continue;
                    bool hitIt;
                    if (b.mvAmp > 0)
                    {   // moving target: where it will be when the duck gets there (same rule as the aim preview)
                        var saved = b.p; b.p.x = b.mvCx + Mathf.Sin(b.mvPhase + st / (MAX_SPEED * 0.85f) * b.mvSp) * b.mvAmp;
                        hitIt = Collide(gp, d.r, b, out _); b.p = saved;
                    }
                    else hitIt = Collide(gp, d.r, b, out _);
                    if (!hitIt) continue;
                    if (!b.Hard && b.hp <= dmg) { simHits.Add(b); len -= 10; score += b.type == "bathbomb" || b.type == "tnt" || b.type == "laser" || b.type == "domino" || b.type == "screw" ? 5f : b.type == "gold" ? 6f : 1f; }
                    else { if (b.type == "ring" && b.hp > 1) score += 4f; else if (b.type == "boss") score += 3f; stop = true; break; }   // knocking a ring sends it skating; boss hits count
                }
            if (stop) break;
        }
        path?.Add(gp);
        return score;
    }

    void HideHint() { hintShown = false; foreach (var h in hintDots) if (h.gameObject.activeSelf) h.gameObject.SetActive(false); }

    void UpdateHint(float rdt)
    {
        bool can = (level <= LevelLibrary.Onboarding || level == 19 || level == 23 || level == 24) && state == "play" && !moving && drag == null && introTimer <= 0 && target == null && ducks.Count > 0
                   && !popupRoot.gameObject.activeSelf && (guideRoot == null || !guideRoot.gameObject.activeSelf) && Time.timeScale > 0;
        if (!can || Input.GetMouseButton(0)) { idleT = 0; hintReady = false; hintSearch = -1; if (hintShown) HideHint(); return; }
        idleT += rdt;
        if (idleT > 5f && hintSearch < 0 && !hintReady) { hintSearch = 0; hintBestScore = 0; hintBestDuck = -1; }
        if (hintSearch >= 0)
        {   // a few candidates per frame so the search never causes a hitch
            int total = ducks.Count * HINT_ANGLES;
            for (int k = 0; k < 4 && hintSearch < total; k++, hintSearch++)
            {
                var d = ducks[hintSearch / HINT_ANGLES]; if (d.clone || d.spawnT > 0) continue;
                float a = (level <= LevelLibrary.Onboarding ? Mathf.Lerp(-165f, -15f, (hintSearch % HINT_ANGLES) / (HINT_ANGLES - 1f))   // upward fan (sim y points down)
                                                             : (hintSearch % HINT_ANGLES) * (360f / HINT_ANGLES) - 180f) * Mathf.Deg2Rad;        // ducks can sit anywhere now: all round
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float sc = PredictShot(d, dir, null);
                if (sc > hintBestScore) { hintBestScore = sc; hintBestDir = dir; hintBestDuck = hintSearch / HINT_ANGLES; }
            }
            if (hintSearch >= total) { hintSearch = -1; hintReady = hintBestDuck >= 0; if (hintReady) PredictShot(ducks[hintBestDuck], hintBestDir, hintPath); }
        }
        if (hintReady && idleT > 8f && hintBestDuck < ducks.Count)
        {
            hintShown = true;
            // marching soft dots along the suggested path
            float total = 0; for (int i = 1; i < hintPath.Count; i++) if (i != predJump) total += Vector2.Distance(hintPath[i - 1], hintPath[i]);
            const float gap = 26f; float at = 30 + Mathf.Repeat(Time.unscaledTime * 60f, gap), walked = 0; int used = 0;
            for (int i = 1; i < hintPath.Count && used < hintDots.Count; i++)
            {
                if (i == predJump) continue;
                Vector2 a = hintPath[i - 1], b = hintPath[i]; float seg = Vector2.Distance(a, b);
                while (at <= walked + seg && used < hintDots.Count)
                {
                    var q = Vector2.Lerp(a, b, seg > 0 ? (at - walked) / seg : 0);
                    var dot = hintDots[used++]; if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
                    dot.position = W(q, 0.3f); dot.localScale = Vector3.one * (0.16f + 0.04f * Mathf.Sin(Time.unscaledTime * 6 + at * 0.05f));
                    at += gap;
                }
                walked += seg;
            }
            for (int i = used; i < hintDots.Count; i++) if (hintDots[i].gameObject.activeSelf) hintDots[i].gameObject.SetActive(false);
        }
    }

    void BuildAim()
    {
        aimRoot = Art.Root("Aim", tubRoot); aimDots.Clear(); pushDots.Clear(); aimVisible = true;
        hintDots.Clear();
        var hm = Art.Mat(new Color(1f, 0.55f, 0.15f), 0.5f, 0, 0.6f);   // warm orange: reads clearly on the bright water
        for (int i = 0; i < 30; i++)
        {
            var hd = Art.Prim(PrimitiveType.Sphere, aimRoot, Vector3.zero, Vector3.one * 0.12f, hm);
            hd.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hd.gameObject.SetActive(false); hintDots.Add(hd);
        }
        if (mpb == null) mpb = new MaterialPropertyBlock();
        var m = Art.Mat(Color.white, 0.5f, 0, 0.6f, 0.0035f);   // outlined so the dots pop on the bright water
        for (int i = 0; i < 48; i++)
        {
            var dt = Art.Prim(PrimitiveType.Sphere, aimRoot, Vector3.zero, Vector3.one * 0.14f, m);
            dt.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dt.gameObject.SetActive(false); aimDots.Add(dt);
        }
        for (int i = 0; i < 14; i++)
        {
            var dt = Art.Prim(PrimitiveType.Sphere, aimRoot, Vector3.zero, Vector3.one * 0.1f, Art.Mat(new Color(0.55f, 0.9f, 1f), 0.5f, 0, 0.8f));
            dt.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dt.gameObject.SetActive(false); pushDots.Add(dt);
        }
        frenzyDots.Clear();
        var fm = Art.Mat(new Color(1f, 0.5f, 0.78f), 0.5f, 0, 0.9f);
        for (int i = 0; i < 4 * 7; i++)
        {
            var fd = Art.Prim(PrimitiveType.Sphere, aimRoot, Vector3.zero, Vector3.one * 0.1f, fm);
            fd.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fd.gameObject.SetActive(false); frenzyDots.Add(fd);
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
        if (target != null && state == "play" && !moving && ScreenToSim(Input.mousePosition, out var tp))
        {
            float rad = target == "bomb" ? 120f : 34f;
            aimTarget.gameObject.SetActive(true); aimTarget.position = W(tp, 0.1f);
            aimTarget.localScale = Vector3.one * rad * K * (1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.04f);
            var tlr = aimTarget.GetComponent<LineRenderer>(); var tc = target == "bomb" ? new Color(0.35f, 0.75f, 1f, 0.95f) : new Color(1f, 0.85f, 0.3f, 0.95f);
            tlr.startColor = tlr.endColor = tc; aimVisible = true; return;
        }
        if ((drag == null || moving) && !aimVisible) { powerStep = 0; return; }
        aimVisible = drag != null && !moving;
        foreach (var d in aimDots) d.gameObject.SetActive(false);
        foreach (var d in pushDots) d.gameObject.SetActive(false);
        foreach (var d in frenzyDots) d.gameObject.SetActive(false);
        aimX.gameObject.SetActive(false); aimTarget.gameObject.SetActive(false); aimGrab.gameObject.SetActive(false);
        band.enabled = band2.enabled = false; aimCount.gameObject.SetActive(false); powerTxt.gameObject.SetActive(false);
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
        if (step != powerStep) { Sfx.Play("click", 0.22f, 0.8f + step * 0.14f); if (step > powerStep) Sfx.Buzz(6 + step * 2); powerStep = step; }
        Color pc = PowerColor(pw);

        // slingshot: two rubber strands from the duck's sides to the finger knob
        var grab = drag.p - dir * (pv.z + 6);
        var side = new Vector2(-dir.y, dir.x) * drag.r * 0.85f;
        float bw = Mathf.Lerp(0.1f, 0.045f, pw);   // stretched band gets thinner
        var bc = Color.Lerp(new Color(1f, 0.55f, 0.6f), new Color(1f, 0.3f, 0.3f), pw);
        SetBand(band, drag.p + side - dir * 4, grab, bc, bw); SetBand(band2, drag.p - side - dir * 4, grab, bc, bw);
        aimGrab.gameObject.SetActive(true); aimGrab.position = W(grab, 0.32f);
        int pct = Mathf.RoundToInt(pw * 100f);
        powerTxt.gameObject.SetActive(level <= LevelLibrary.Onboarding);
        powerTxt.rectTransform.anchoredPosition = WorldToHud(W(drag.p, 0.4f)) + new Vector2(drag.p.x > 380 ? -150 : 150, 20);   // beside the duck, clear of the band + boosters
        if (pct != shownPower) { shownPower = pct; powerTxt.text = pct >= 99 ? "LỰC MAX" : "Lực " + pct + "%"; powerTxt.color = PowerColor(pw); }
        aimGrab.localScale = new Vector3(0.32f, 0.12f, 0.32f) * (1 + Mathf.Sin(Time.unscaledTime * 12) * 0.06f * pw);

        // trajectory: bounce off the walls, carve through what it will break, stop at the first thing it won't
        aimPath.Clear(); aimPath.Add(drag.p);
        float aimSpeed = Mathf.Max(300f, MAX_SPEED * Mathf.Pow(Mathf.Max(pv.z, 18) / MAX_PULL, 0.8f));
        Vector2 gp = drag.p, gv = dir; float len = (260 + pv.z * 4.5f) * (level <= 5 ? 1.45f : 1f), stepL = 5; Duck hitDuck = null; Vector2? firstContact = null; Block hitRing = null;
        aimHits.Clear(); int dmg = drag.kind == "rocket" ? 3 : drag.Dmg; bool pierce = frenzyReady; int jumpIdx = -1;
        bool rocketAim = drag.kind == "rocket" && !drag.spent;
        if (rocketAim) { len = ROCKET_SPEED * ROCKET_TIME * 0.85f; pc = new Color(1f, 0.55f, 0.2f); }   // rocket flies full thrust whatever the pull
        for (float s = 0; s < len; s += stepL)
        {
            gp += gv * stepL;
            if (whirls.Count > 0 && drag.kind != "rocket") { float om = WhirlTurn(gp, out _); if (om > 0) gv = Rot(gv, om * stepL / aimSpeed * 1.1f); }
            if (flows.Count > 0 && drag.kind != "rocket") { var fdir = FlowAt(gp); if (fdir != Vector2.zero) gv = (gv * aimSpeed + fdir * FLOW_ACC * stepL / aimSpeed).normalized; }
            if (TubWalls(ref gp, ref gv, drag.r, 1f)) aimPath.Add(gp);
            foreach (var m in bumpers)
                if (CircleRect(gp, drag.r, m, out var bh)) { gp += bh.n * bh.pen; Reflect(ref gv, bh.n, 1f); aimPath.Add(gp); }
            gv.Normalize();
            if (jumpIdx < 0 && s > 10)
                foreach (var pt in portals)
                {
                    bool inA = (gp - pt.a).sqrMagnitude < PORTAL_R * PORTAL_R, inB = !inA && (gp - pt.b).sqrMagnitude < PORTAL_R * PORTAL_R;
                    if (!inA && !inB) continue;
                    aimPath.Add(gp); gp = (inA ? pt.b : pt.a) + gv * 4f; jumpIdx = aimPath.Count; aimPath.Add(gp);
                    break;
                }
            foreach (var o in ducks) if (o != drag && Vector2.Distance(o.p, gp) < o.r + drag.r) { hitDuck = o; break; }
            bool stop = hitDuck != null;
            if (!stop)
                foreach (var b in blocks)
                {
                    if (!b.alive || Covered(b) || b.falling > 0 || aimHits.Contains(b)) continue;
                    bool hitIt;
                    if (b.mvAmp > 0)
                    {   // moving target: test where it WILL be when the duck gets this far (honest preview = timing shot)
                        var saved = b.p; float tAhead = s / Mathf.Max(200f, aimSpeed * 0.85f);
                        b.p.x = b.mvCx + Mathf.Sin(b.mvPhase + tAhead * b.mvSp) * b.mvAmp;
                        hitIt = Collide(gp, drag.r, b, out _); b.p = saved;
                    }
                    else hitIt = Collide(gp, drag.r, b, out _);
                    if (!hitIt) continue;
                    bool breaks = !b.Hard && b.type != "boss" && (b.type != "chest" || dmg >= 3) && (pierce ? b.hp <= 2 : b.hp <= dmg);
                    if (firstContact == null) firstContact = gp;
                    if (b.type == "ring" && b.hp > 1 && drag.kind != "rocket" && !pierce) { hitRing = b; stop = true; break; }
                    if (breaks) { aimHits.Add(b); if (drag.kind != "rocket") len -= 10; } else { stop = true; break; }
                }
            if (stop) break;
        }
        aimPath.Add(gp);

        // marching dots along the path (they flow toward the target)
        float total = 0; for (int i = 1; i < aimPath.Count; i++) if (i != jumpIdx) total += Vector2.Distance(aimPath[i - 1], aimPath[i]);
        const float gap = 22f; float at = drag.r + 8 + Mathf.Repeat(Time.unscaledTime * 80f, gap), walked = 0; int used = 0;
        for (int i = 1; i < aimPath.Count && used < aimDots.Count; i++)
        {
            if (i == jumpIdx) continue;   // the hop through the pipe is not drawn
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
        if (drag.kind == "bomb" && drag.charged && firstContact != null)
        {   // bomb duck: show the blast radius where it first touches something
            aimTarget.position = W(firstContact.Value, 0.08f);
            aimTarget.localScale = Vector3.one * (drag.special ? 120f : 100f) * K * (1f + Mathf.Sin(Time.unscaledTime * 9) * 0.03f);
        }
        var tl = aimTarget.GetComponent<LineRenderer>(); tl.startColor = tl.endColor = new Color(pc.r, pc.g, pc.b, 0.9f);
        // float ring preview: it skates off along the shot direction
        if (hitDuck == null && hitRing != null)
        {
            for (int i = 0; i < pushDots.Count; i++)
            {
                var q = hitRing.p + gv * (hitRing.r + 16 + i * 24);
                pushDots[i].gameObject.SetActive(true); pushDots[i].position = W(q, 0.32f);
                pushDots[i].localScale = Vector3.one * Mathf.Lerp(0.13f, 0.06f, i / (float)pushDots.Count);
            }
        }
        // FRENZY ready: show the fan of 4 extra ducks this shot will release (same angles as Launch)
        if (frenzyReady)
        {
            float baseA = Mathf.Atan2(dir.y, dir.x); int fi = 0;
            foreach (float off in new[] { -0.3f, -0.15f, 0.15f, 0.3f })
                for (int k = 0; k < 7; k++)
                {
                    var q = drag.p + new Vector2(Mathf.Cos(baseA + off), Mathf.Sin(baseA + off)) * (drag.r + 30 + k * 40 + Mathf.Repeat(Time.unscaledTime * 70f, 40f));
                    if (q.x < TX + 10 || q.x > TX + TW - 10 || q.y < TY + 10) { fi++; continue; }
                    var fd = frenzyDots[fi++]; fd.gameObject.SetActive(true); fd.position = W(q, 0.32f);
                    fd.localScale = Vector3.one * Mathf.Lerp(0.24f, 0.12f, k / 6f);
                }
        }
        // billiard preview: where the knocked duck will go
        if (hitDuck != null)
        {   // carom preview: where the knocked duck travels (walls included) and what it will break on the way
            Vector2 cp = hitDuck.p, cvv = (hitDuck.p - gp).normalized; float clen = 140 + pv.z * 2.4f; int used2 = 0;
            for (float s2 = 0; s2 < clen; s2 += 5)
            {
                cp += cvv * 5; TubWalls(ref cp, ref cvv, hitDuck.r, 1f); cvv.Normalize();
                bool stop2 = false;
                foreach (var b in blocks)
                {
                    if (!b.alive || Covered(b) || b.falling > 0 || aimHits.Contains(b) || !Collide(cp, hitDuck.r, b, out _)) continue;
                    if (!b.Hard && b.type != "boss" && b.type != "ring" && b.hp <= hitDuck.Dmg) { aimHits.Add(b); clen -= 8; }
                    else { stop2 = true; break; }
                }
                if (((int)s2) % 20 == 0 && used2 < pushDots.Count)
                {
                    var pd = pushDots[used2++]; pd.gameObject.SetActive(true); pd.position = W(cp, 0.32f);
                    pd.localScale = Vector3.one * Mathf.Lerp(0.14f, 0.07f, used2 / (float)pushDots.Count);
                }
                if (stop2) break;
            }
        }
        bool rbAim = drag.kind == "rainbow" && !drag.spent;
        aimCount.gameObject.SetActive(aimHits.Count > 0 || rbAim);
        if (aimHits.Count > 0 || rbAim)
        {
            // the combo payoff right on the aim: "x12 -> Vịt Bom" when this shot alone would hatch a special
            string tierName = null;
            if (state == "play" && level > LevelLibrary.Onboarding)
                foreach (var tr in TIERS) if (aimHits.Count >= tr.need && LevelLibrary.Unlocked(tr.kind, level)) { tierName = Cap(tr.name); break; }
            aimCount.text = "x" + aimHits.Count + (drag.kind == "rainbow" && !drag.spent ? " +16" : "") + (tierName != null ? "\n<size=36>→ " + tierName + "</size>" : "");
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
            else if (drag != null && d != drag && !moving)
            {   // bystanders turn to watch the duck being aimed (cute, and it reads "everyone's waiting on this shot")
                var lk = drag.p - d.p;
                if (lk.sqrMagnitude > 1) d.face = Mathf.LerpAngle(d.face * Mathf.Rad2Deg, Mathf.Atan2(lk.y, lk.x) * Mathf.Rad2Deg, 1 - Mathf.Exp(-6 * dt)) * Mathf.Deg2Rad;
            }
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
                float pulse = 1.8f + Mathf.Sin(t * 5 + d.p.x) * 0.08f + (dragged ? 0.25f + pw * 0.25f : 0);
                d.ring.localScale = new Vector3(pulse, 0.01f, pulse);
                Color rc = frenzyReady || d.frenzy ? new Color(1f, 0.5f, 0.75f, 0.6f)
                         : dragged ? (Color)(Color32)Color.Lerp(new Color(1, 1, 1, 0.7f), new Color(1f, 0.45f, 0.3f, 0.75f), powerStep / 4f)
                         : d.special ? new Color(1f, 0.8f, 0.3f, 0.6f) : new Color(0.45f, 0.9f, 0.55f, 0.42f);
                if (d.ringCol != rc) { d.ringCol = rc; d.ring.GetComponent<Renderer>().sharedMaterial = Art.RingShared(rc); }   // only when the colour changes (the cache key is a string)
            }
            var spark = d.body.Find("Spark"); if (spark) spark.gameObject.SetActive((d.charged || d.special) && Mathf.Sin(t * 14) > -0.3f);
            if (spd > 120 && UnityEngine.Random.value < 0.5f) FxKit.Wake(W(d.p - d.v.normalized * d.r));
            if (d.trail) d.trail.emitting = spd > 260 && d.spawnT <= 0;
            string want = d.clone ? null : KindTag(d.kind);
            if (want != d.tagKind) { if (d.tag) { tags.Remove(d.tag); Destroy(d.tag.gameObject); } d.tag = want != null ? MakeTag(want, KindColor(d.kind), false) : null; d.tagKind = want; }
            if (d.tag) d.tag.anchoredPosition = WorldToHud(W(d.p, 0.9f)) + new Vector2(0, 52);
            if (spd < 20 && d.spawnT <= 0 && UnityEngine.Random.value < dt * 0.5f) FxKit.Ripple(W(d.p), 0.75f);   // lazy idle ripples
            if (spd < 20 && !moving && drag == null && state == "play" && d.spawnT <= 0 && !d.clone && UnityEngine.Random.value < dt * 0.05f)
            {   // idle charm: now and then a duck gives a little quack and bounce
                d.squash = 0.18f; PopText("Quạc!", d.p + new Vector2(0, -44), Color.white, 30); Sfx.Play("quack", 0.25f, UnityEngine.Random.Range(1.05f, 1.35f));
            }
            if (d.rocketT > 0) { FxKit.Flame(W(d.p - d.v.normalized * d.r * 1.2f, 0.3f)); if (UnityEngine.Random.value < 0.3f) FxKit.Smoke(W(d.p - d.v.normalized * d.r * 1.6f), 0.3f, 1); }
            if (d.special && !moving && UnityEngine.Random.value < dt * 4) FxKit.Sparkle(W(d.p, 0.5f), d.kind == "rainbow" ? Color.HSVToRGB(UnityEngine.Random.value, 0.5f, 1f) : new Color(1f, 0.85f, 0.4f), 1);
        }
        bool fewLeft = state == "play" && !moving && GoalsLeft() <= 3;
        foreach (var b in blocks)
        {
            if (!b.alive || b.view == null) continue;
            b.hit = Mathf.Max(0, b.hit - dt);
            float k = 1 + b.hit * (b.type == "boss" ? 0.4f : 1.2f);
            if (b.hit > 0 || b.scaled) { b.view.localScale = b.baseScale * k; b.scaled = b.hit > 0; }
            if (b.wob > 0 && b.under == null && b.type != "boss")
            {
                b.wob = Mathf.Max(0, b.wob - dt * 1.6f);
                b.view.localEulerAngles = new Vector3(Mathf.Sin(t * 37f + b.p.x) * b.wob * 40f, 0, Mathf.Sin(t * 43f + b.p.y) * b.wob * 40f);
            }
            if (b.type == "boss") b.view.localEulerAngles = new Vector3(0, 0, b.hit > 0 ? Mathf.Sin(t * 60f) * b.hit * 60f : 0);   // the king wobbles when struck
            // floating goodies bob gently on the bath water
            // everything afloat bobs gently on the bath water (slow, small: calm motion, not visual noise)
            bool floats = b.type == "cookie" || b.type == "gold" || b.type == "ice" || b.type == "chest" || b.type == "tnt" || b.type == "laser" || b.type == "boss"
                       || b.Prop || b.type == "bathbomb" || (!App.Mobile && b.type == "pixel");
            float lift = 0;
            if (b.appear < 1)
            {   // level build-in: drop from above with a little overshoot squash
                if (b.delay > 0) { b.delay -= dt; b.view.localScale = Vector3.zero; b.scaled = true; continue; }
                b.appear = Mathf.Min(1, b.appear + dt / 0.28f);
                float e = b.appear; lift = (1 - e) * (1 - e) * 5f;
                b.view.localScale = b.baseScale * (e < 1 ? Mathf.Lerp(0.5f, 1f, e) : 1f); b.scaled = true;
                if (b.appear >= 1) { b.hit = 0.08f; if (UnityEngine.Random.value < 0.25f) FxKit.Ripple(W(b.p), 0.7f); }
            }
            if (b.under != null && b.under.shelf && b.under.view != null)
            {   // standing on a shelf (sinks with it while it collapses)
                float fall = b.under.falling > 0 ? Mathf.Min(1, b.under.falling / 0.35f) : 0;
                lift += 0.84f * (1 - fall); floats = false;
                if (fall > 0) b.view.localEulerAngles = new Vector3(fall * 40f, 0, fall * 25f);
            }
            if (b.type == "ring") { if (b.vel.sqrMagnitude > 1) b.view.Rotate(0, b.vel.magnitude * dt * 0.6f, 0); floats = !App.Mobile; }
            if (b.mvAmp > 0 || floats || lift > 0 || b.appear < 1 || b.type == "ring") b.view.position = W(b.p, lift + (floats ? Mathf.Sin(t * 2.2f + b.p.x * 0.031f + b.p.y * 0.017f) * 0.035f : 0));
            // white flash on hit (property block keeps the shared baked material untouched)
            // last few targets glow softly so the player can find them
            bool aimed = drag != null && !moving && aimHits.Contains(b);   // will break on this shot: light it up while aiming
            if (aimed && b.hit <= 0 && b.appear >= 1) { b.view.localScale = b.baseScale * (1f + 0.05f * Mathf.Sin(Time.unscaledTime * 7f + b.p.x * 0.05f)); b.scaled = true; }   // targeted props breathe: "these will go"
            bool enraged = b.type == "boss" && b.hp <= b.max / 2;
            bool glow = (fewLeft && b.Goal && !Covered(b)) || b.type == "gold" || aimed || enraged;
            if (b.type == "bathbomb" && UnityEngine.Random.value < dt * (App.Mobile ? 0.25f : 0.5f)) FxKit.Bubble(W(b.p + UnityEngine.Random.insideUnitCircle * 10f, 0.3f));   // fizzing: "I go boom"
            if (b.type == "chest" && UnityEngine.Random.value < dt * 0.6f) FxKit.Sparkle(W(b.p + new Vector2(UnityEngine.Random.Range(-18f, 18f), 0), 0.9f), new Color(1f, 0.92f, 0.55f), 1);
            if (b.type == "gold" && b.mvAmp > 0 && UnityEngine.Random.value < dt * 6f) FxKit.Sparkle(W(b.p, 0.5f), new Color(1f, 0.9f, 0.4f), 1);   // drifting gold leaves glints
            if (b.hit > 0 || b.flashing || glow)
            {
                b.flashing = b.hit > 0 || glow;
                var e = b.hit > 0 ? Color.white * Mathf.Min(1f, b.hit * 6f) * 0.55f
                      : aimed ? new Color(1f, 1f, 0.85f) * (0.26f + 0.07f * Mathf.Sin(Time.unscaledTime * 6f))
                      : enraged ? new Color(1f, 0.2f, 0.15f) * (0.25f + 0.2f * Mathf.Sin(t * 10f))
                      : glow ? new Color(1f, 0.85f, 0.35f) * (0.16f + 0.1f * Mathf.Sin(t * 3f)) : Color.clear;
                mpb.Clear(); mpb.SetColor("_Emission", e);
                foreach (var r in b.rends) if (r) r.SetPropertyBlock(b.flashing ? mpb : null);
            }
            if (b.type == "plank" && b.falling > 0) { float f = Mathf.Min(1, b.falling / 0.35f); b.view.position = W(b.p, -f * 0.6f); }
            if (b.type == "laser") b.view.Find("Arrow").localScale = Vector3.one * (1 + Mathf.Sin(t * 8) * 0.08f);
            if (b.tag) b.tag.anchoredPosition = WorldToHud(W(b.p, 1.2f)) + new Vector2(0, (b.type == "gold" ? -70 : 58) + Mathf.Sin(t * 3f + b.p.x) * 6f);   // gold sits under the HUD: label goes below it
            if (b.type == "screw") b.view.localEulerAngles = new Vector3(0, t * 30 + b.p.x, 0);
        }
        for (int i = dying.Count - 1; i >= 0; i--)
        {
            var dd = dying[i]; dd.life -= dt;
            if (dd.t == null || dd.life <= 0) { if (dd.t) Destroy(dd.t.gameObject); dying.RemoveAt(i); continue; }
            float f = 1 - dd.life / dd.max;
            if (dd.topple)
            {
                float e = 1 - (1 - f) * (1 - f);
                dd.t.rotation = Quaternion.AngleAxis(e * 95f, dd.axis) * dd.r0;
                dd.t.position = dd.p0 + dd.dir * e * 0.35f + Vector3.down * f * f * 0.6f;
                dd.t.localScale = dd.s0 * (f < 0.65f ? 1f : Mathf.Lerp(1f, 0f, (f - 0.65f) / 0.35f));
            }
            else dd.t.localScale = dd.s0 * (f < 0.4f ? Mathf.Lerp(1f, 1.35f, f / 0.4f) : Mathf.Lerp(1.35f, 0f, (f - 0.4f) / 0.6f));   // quick puff-up then vanish
        }
        foreach (var fl in flows) fl.Scroll(-Mathf.Repeat(t * 1.6f, 1000f));
        foreach (var pt in portals)
        {
            if (pt.sa) pt.sa.Rotate(0, 200f * dt, 0, Space.World);
            if (pt.sb) pt.sb.Rotate(0, 200f * dt, 0, Space.World);
            if (UnityEngine.Random.value < dt * (App.Mobile ? 0.8f : 1.8f)) FxKit.Bubble(W((UnityEngine.Random.value < 0.5f ? pt.a : pt.b) + UnityEngine.Random.insideUnitCircle * 10f));   // the pipes gurgle
        }
        foreach (var w in whirls)
            if (w.view)
            {
                w.view.Rotate(0, 140f * dt, 0, Space.World);   // clockwise on screen, same way it turns the ducks
                if (UnityEngine.Random.value < dt * 1.2f) FxKit.Ripple(W(w.p + UnityEngine.Random.insideUnitCircle * w.r * 0.5f), 0.5f);
                if (UnityEngine.Random.value < dt * (App.Mobile ? 0.8f : 2f)) FxKit.Bubble(W(w.p + UnityEngine.Random.insideUnitCircle * 8f));   // bubbles well up from the eye
            }
        foreach (var m in bumpers)
        {
            m.pulse = Mathf.Max(0, m.pulse - dt); m.view.localScale = m.h >= m.w ? new Vector3(1 + m.pulse * 2, 1, 1) : new Vector3(1, 1, 1 + m.pulse * 2);   // swell across the pad, not along it
            if (m.amp > 0) m.view.position = W(m.x + m.w / 2, m.y + m.h / 2);
        }
    }

    // a drain-pipe mouth on the water: dark swirling hole + a coloured glow ring (each pair shares a colour)
    Transform PortalView(Vector2 p, Color c)
    {   // dark hole + spinning foam swirl + solid coloured rim (each pair shares a colour) + soft glow
        float D = PORTAL_R * 2f * K;
        var hole = Art.Prim(PrimitiveType.Quad, world, W(p, 0.028f), new Vector3(D * 1.05f, D * 1.05f, 1f), PortalHoleMat(), new Vector3(90, 0, 0));
        var swirl = Art.Prim(PrimitiveType.Quad, world, W(p, 0.031f), new Vector3(D, D, 1f), Art.SwirlMat(), new Vector3(90, 0, 0));
        var rim = Art.Prim(PrimitiveType.Quad, world, W(p, 0.034f), new Vector3(D * 1.3f, D * 1.3f, 1f), Art.RingShared(new Color(c.r, c.g, c.b, 1f)), new Vector3(90, 0, 0));
        var glow = Art.Prim(PrimitiveType.Quad, world, W(p, 0.036f), new Vector3(D * 1.75f, D * 1.75f, 1f), Art.Glow(new Color(c.r, c.g, c.b, 0.55f)), new Vector3(90, 0, 0));
        foreach (var t in new[] { hole, swirl, rim, glow }) { var r = t.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        return swirl;
    }
    static Texture2D flowTex;
    static Texture2D FlowTex()
    {   // soft white chevrons ">" on a faint aqua band; tiles along u
        if (flowTex != null) return flowTex;
        const int S = 64; flowTex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                float chev = (0.62f - Mathf.Abs(v - 0.5f) * 0.6f) - u;                       // ">" shape: tip at the +u side
                float line = Mathf.Clamp01(1f - Mathf.Abs(chev) / 0.07f) * Mathf.Clamp01((0.42f - Mathf.Abs(v - 0.5f)) / 0.08f);
                float band = 0.22f;
                px[y * S + x] = Color.Lerp(new Color(0.55f, 0.95f, 1f, band), new Color(1f, 1f, 1f, 0.95f), line);
            }
        flowTex.SetPixels(px); flowTex.Apply();
        return flowTex;
    }
    static Material portalHole;
    static Material PortalHoleMat()
    {
        if (portalHole == null) { portalHole = Art.Fade(new Color(0.04f, 0.07f, 0.2f, 0.9f)); portalHole.mainTexture = BlobTex(); portalHole.renderQueue = 2985; }
        return portalHole;
    }

    void TeleportFx(Vector2 from, Vector2 to, Color c)
    {
        FxKit.Splash(W(from), 5, 0.8f); FxKit.Splash(W(to), 7, 1.1f);
        var cc = c; cc.a = 0.85f; FxKit.Shockwave(W(from), 1.6f, cc, 0.25f); FxKit.Shockwave(W(to), 2.2f, cc, 0.3f);
        FxKit.Sparkle(W(to, 0.3f), c, 6);
        Sfx.Play("pop", 0.7f, 0.6f); Sfx.Play("splash", 0.6f, 1.4f);
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
            tr = Art.Prim(type, bitRoot, pos, Vector3.one * size, Art.Mat(c, 0.4f));
            var rr = tr.GetComponent<Renderer>(); rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;
        }
        tr.GetComponent<Renderer>().sharedMaterial = Art.Mat(c, 0.4f);
        var dir = UnityEngine.Random.insideUnitCircle.normalized;
        bits.Add(new Bit { type = type, t = tr, v = new Vector3(dir.x, UnityEngine.Random.Range(0.6f, 1.6f), dir.y) * speed * UnityEngine.Random.Range(0.4f, 1f), spin = UnityEngine.Random.insideUnitSphere * 720, scale0 = tr.localScale, life = life, max = life, grav = true });
    }

    void AddRing(Vector2 p, float radius, Color c)
    {
        // pooled additive particle instead of a new cylinder + material per ring (chain explosions spawned dozens on phones)
        FxKit.Shockwave(W(p, 0.06f), radius * 2 * K * 1.15f, c, 0.35f);
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
            var c = r.c; c.a *= 1 - f; Art.SetGlow(r.m, c);
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
            if (p.life <= 0) { RecyclePop(p.t); pops.RemoveAt(i); continue; }
            float age = p.max - p.life;
            p.rise += dt * (age < 0.18f ? 25f : 95f);
            var sp = cam.WorldToScreenPoint(p.world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(hud, sp, null, out var lp);
            var rt = p.t.rectTransform; rt.anchoredPosition = lp + new Vector2(0, p.rise);
            rt.localScale = Vector3.one * BackOut(age / 0.22f);
            rt.localEulerAngles = new Vector3(0, 0, (1f - Mathf.Clamp01(age / 0.3f)) * ((p.t.GetInstanceID() & 1) == 0 ? 8f : -8f));
            var c = p.t.color; c.a = Mathf.Clamp01(p.life / p.max * 2.5f); p.t.color = c;
        }
        // big center text
        if (bigLife > 0)
        {
            bigLife -= dt;
            float s = BackOut((1.4f - bigLife) / 0.28f) * (1f + Mathf.Sin(Mathf.Clamp01((1.4f - bigLife) / 0.28f) * Mathf.PI) * 0.12f);
            bigTxt.rectTransform.localScale = Vector3.one * s;
            var c = bigTxt.color; c.a = Mathf.Clamp01(bigLife * 2.5f); bigTxt.color = c;
        }
    }

    void PopText(string s, Vector2 simPos, Color c, int size)
    {
        // too many floating labels at once is unreadable and each one is an outlined UI mesh: drop the oldest
        if (pops.Count >= (App.Mobile ? 6 : 10)) { RecyclePop(pops[0].t); pops.RemoveAt(0); }
        var wp = W(simPos, 0.6f);
        foreach (var o in pops)
            if (o.t && o.t.text == s && o.max - o.life < 0.6f && (o.world - wp).sqrMagnitude < 9f)
            { o.life = o.max - 0.02f; o.world = Vector3.Lerp(o.world, wp, 0.5f); return; }
        Text tx = null;
        while (popPool.Count > 0 && tx == null) tx = popPool.Pop();   // reuse a label: no new UI objects (and no GC hitch) on every smash
        if (tx != null) { tx.gameObject.SetActive(true); tx.text = s; tx.fontSize = size; tx.color = c; tx.rectTransform.SetAsLastSibling(); }
        else tx = UIKit.Outlined(UIKit.Label(dynLayer, s, size, c), new Color(0.23f, 0.18f, 0.42f), 4);
        var rt = tx.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(600, 120); rt.localScale = Vector3.zero;
        pops.Add(new Pop { t = tx, world = W(simPos, 0.6f), life = 0.9f, max = 0.9f });
        if (pops.Count > 30) { RecyclePop(pops[0].t); pops.RemoveAt(0); }
    }
    readonly Stack<Text> popPool = new Stack<Text>();
    void RecyclePop(Text t) { if (t == null) return; t.gameObject.SetActive(false); if (popPool.Count < 24) popPool.Push(t); else Destroy(t.gameObject); }

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
        if (App.Mobile) vig.gameObject.SetActive(false);   // full-screen alpha overlay = pure overdraw on phones
        flashImg = UIKit.Stretch(UIKit.Rect("Flash", hud)).gameObject.AddComponent<Image>();
        flashImg.color = new Color(1f, 0.97f, 0.9f, 0); flashImg.raycastTarget = false; flashImg.canvasRenderer.cullTransparentMesh = true;
        // top bar
        var top = UIKit.Panel(hud, PurpleDark, "TopBar").rectTransform; UIKit.Place(top, 0.5f, 1, 0, -150, 1040, 250);
        var topIn = UIKit.Panel(top, Purple, "Inner").rectTransform; UIKit.Stretch(topIn, 0, 10, 0, 0);
        UIKit.Gloss(topIn, 0.2f);
        var mframe = UIKit.Panel(topIn, Yellow, "MovesFrame").rectTransform; UIKit.Place(mframe, 0, 0.5f, 125, 18, 206, 196);
        var mb = UIKit.Panel(topIn, Color.white, "Moves").rectTransform; UIKit.Place(mb, 0, 0.5f, 125, 18, 186, 176);
        movesTxt = UIKit.Label(mb, "10", 100, Ink); movesTxt.rectTransform.offsetMin = new Vector2(0, 26);
        var ml = UIKit.Label(mb, "LƯỢT BẮN", 26, PurpleDark); ml.rectTransform.offsetMax = new Vector2(0, -122);
        var sp = UIKit.Rect("StarPreview", topIn); UIKit.Place(sp, 0, 0.5f, 125, -100, 170, 36);
        starPreview = UIKit.StarRow(sp, 3, 36, 6);
        aimCount = UIKit.Outlined(UIKit.Label(hud, "", 64, Color.white), Ink, 4); aimCount.verticalOverflow = VerticalWrapMode.Overflow; aimCount.horizontalOverflow = HorizontalWrapMode.Overflow; aimCount.lineSpacing = 0.85f;
        aimCount.rectTransform.anchorMin = aimCount.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); aimCount.rectTransform.sizeDelta = new Vector2(300, 100);
        aimCount.gameObject.SetActive(false);
        goalsTxt = UIKit.Outlined(UIKit.Label(topIn, "", 40, Color.white, TextAnchor.MiddleLeft), Ink, 3); UIKit.Place(goalsTxt.rectTransform, 0, 0.5f, 480, 22, 420, 150);
        goalsTxt.horizontalOverflow = HorizontalWrapMode.Overflow; goalsTxt.verticalOverflow = VerticalWrapMode.Overflow; goalsTxt.lineSpacing = 0.95f;
        goalsTxt.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        var ob = UIKit.Panel(topIn, new Color(0.23f, 0.18f, 0.42f, 0.55f), "ObjBar").rectTransform; UIKit.Place(ob, 0, 0.5f, 480, -78, 420, 26);
        objFill = UIKit.Panel(ob, new Color(0.45f, 0.95f, 0.5f), "Fill").rectTransform;
        objFill.anchorMin = Vector2.zero; objFill.anchorMax = new Vector2(0, 1); objFill.offsetMin = objFill.offsetMax = Vector2.zero; objFill.pivot = new Vector2(0, 0.5f);
        levelTxt = UIKit.Label(topIn, "", 34, Color.white, TextAnchor.MiddleRight); UIKit.Place(levelTxt.rectTransform, 1, 0.5f, -265, 50, 260, 60);
        scoreTxt = UIKit.Label(topIn, "", 40, Yellow, TextAnchor.MiddleRight); UIKit.Place(scoreTxt.rectTransform, 1, 0.5f, -265, -20, 260, 60);
        var gear = UIKit.Btn(topIn, "II", Yellow, Ink, 44, () => App.OpenSettings(true), "Settings");
        UIKit.Place((RectTransform)gear.transform, 1, 0.5f, -65, 10, 100, 100);
        // fever bar
        // FRENZY meter: how much is banked, what a full bar gives, and what this shot will add
        feverBar = UIKit.Panel(hud, new Color(0.33f, 0.27f, 0.53f, 1f), "Fever").rectTransform; UIKit.Place(feverBar, 0.5f, 1, 0, -312, 1010, 62);
        var ftrack = UIKit.Rect("Track", feverBar); UIKit.Stretch(ftrack, 6, 6, 6, 6);
        UIKit.Gloss(feverBar, 0.16f);
        feverGhost = UIKit.Panel(ftrack, new Color(1f, 1f, 1f, 0.4f), "Ghost").rectTransform;
        var ff = UIKit.Panel(ftrack, new Color(1f, 0.42f, 0.68f), "Fill"); feverFill = ff.rectTransform;
        foreach (var r in new[] { feverGhost, feverFill }) { r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(0, 1); r.offsetMin = r.offsetMax = Vector2.zero; r.pivot = new Vector2(0, 0.5f); }
        for (int i = 1; i < 7; i++)
        {   // a tick every 10 points
            var tk = UIKit.Panel(ftrack, new Color(1f, 1f, 1f, 0.35f), "Tick").rectTransform; tk.anchorMin = tk.anchorMax = new Vector2(i / 7f, 0.5f); tk.sizeDelta = new Vector2(3, 40);
        }
        var fl = UIKit.Outlined(UIKit.Label(feverBar, "FRENZY", 30, Color.white, TextAnchor.MiddleLeft), new Color(0.6f, 0.15f, 0.4f), 3); UIKit.Place(fl.rectTransform, 0, 0.5f, 110, 0, 200, 60);
        feverTxt = UIKit.Outlined(UIKit.Label(feverBar, "0 / 70", 36, Color.white), Ink, 3); UIKit.Place(feverTxt.rectTransform, 0.5f, 0.5f, 0, 0, 600, 60);
        feverHint = UIKit.Label(feverBar, "phá 1 món = +1", 24, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleRight); UIKit.Place(feverHint.rectTransform, 1, 0.5f, -120, 0, 220, 60);
        feverPop = UIKit.Outlined(UIKit.Label(hud, "", 46, new Color(1f, 0.55f, 0.8f)), Color.white, 3); UIKit.Place(feverPop.rectTransform, 0.5f, 1, 300, -262, 400, 70);
        feverPop.raycastTarget = false; feverPop.gameObject.SetActive(false);
        streakTxt = UIKit.Label(topIn, "", 26, new Color(1f, 0.8f, 0.35f), TextAnchor.MiddleRight); UIKit.Place(streakTxt.rectTransform, 1, 0.5f, -265, -70, 300, 40);
        // boss bar
        bossBar = UIKit.Panel(hud, new Color(0.23f, 0.18f, 0.42f, 0.7f), "BossBar").rectTransform; UIKit.Place(bossBar, 0.5f, 1, 0, -360, 700, 40);
        var bf = UIKit.Panel(bossBar, new Color(1f, 0.35f, 0.2f), "Fill"); bossFill = bf.rectTransform;
        bossFill.anchorMin = Vector2.zero; bossFill.anchorMax = Vector2.one; bossFill.offsetMin = new Vector2(4, 4); bossFill.offsetMax = new Vector2(-4, -4);
        bossTxt = UIKit.Label(bossBar, "BOSS", 26, Color.white);
        BuildComboBar();
        BuildComboGuide();
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
            var lbl = UIKit.BtnText(b); lbl.lineSpacing = 1.05f; lbl.fontSize = 32;
            lbl.rectTransform.offsetMin = new Vector2(84, lbl.rectTransform.offsetMin.y);   // room for the icon
            var bIcon = UIKit.Rect("Icon", b.transform).gameObject.AddComponent<RawImage>(); bIcon.raycastTarget = false;
            UIKit.Place(bIcon.rectTransform, 0, 0.5f, 54, 4, 76, 76); bIcon.texture = BoosterIcon(k);
            UIKit.Gloss(b.transform, 0.3f);
            boostTxt[k] = lbl; boostImg[k] = b.GetComponent<Image>();
        }
        // popup
        popupRoot = UIKit.Stretch(UIKit.Rect("Popup", hud));
        var dim = popupRoot.gameObject.AddComponent<Image>(); dim.color = new Color(0.11f, 0.08f, 0.25f, 0.75f);
        var card = UIKit.Panel(popupRoot, Purple, "Card").rectTransform; UIKit.Place(card, 0.5f, 0.5f, 0, 0, 900, 1000);
        popTitle = UIKit.Outlined(UIKit.Label(card, "", 90, Color.white), Ink, 5); UIKit.Place(popTitle.rectTransform, 0.5f, 1, 0, -120, 860, 140);
        popStars = UIKit.Label(card, "", 130, Yellow); UIKit.Place(popStars.rectTransform, 0.5f, 1, 0, -290, 860, 170);
        sunburst = UIKit.Rect("Sunburst", popStars.transform).gameObject.AddComponent<RawImage>(); sunburst.raycastTarget = false;
        sunburst.texture = SunburstTex(); sunburst.color = new Color(1f, 0.9f, 0.45f, 0.55f);
        UIKit.Place(sunburst.rectTransform, 0.5f, 0.5f, 0, 0, 620, 620);
        popStarImgs = UIKit.StarRow(popStars.transform, 3, 150, 24);
        popBody = UIKit.Label(card, "", 44, Color.white); UIKit.Place(popBody.rectTransform, 0.5f, 1, 0, -440, 860, 120);
        popReward = UIKit.Outlined(UIKit.Label(card, "", 64, Yellow), Ink, 4); UIKit.Place(popReward.rectTransform, 0.5f, 1, 0, -540, 860, 100);
        var paBg = UIKit.Dot(card, Color.white, "ArtBg"); UIKit.Place(paBg.rectTransform, 0.5f, 1, 335, -300, 150, 150); paBg.raycastTarget = false;
        popArt = UIKit.Rect("Art", paBg.transform).gameObject.AddComponent<RawImage>(); UIKit.Place(popArt.rectTransform, 0.5f, 0.5f, 0, 0, 104, 104); popArt.raycastTarget = false;
        popArtBg = paBg.gameObject; popArtBg.SetActive(false);
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
        hand.color = new Color(1f, 1f, 1f, 0.55f); handRt.sizeDelta = new Vector2(70, 70);
        // pixel pointing hand: the fingertip sits on the touch dot
        var hpx = UIKit.Rect("Finger", hand.transform).gameObject.AddComponent<RawImage>(); hpx.raycastTarget = false;
        hpx.texture = UIKit.PixelIcon(new[] {
            "....oo.......", "...owwo......", "...owwo......", "...owwo......", "...owwooo....", "...owwowwoo..",
            ".ooowwowwowo.", "owwowwwwwwwwo", "owwwwwwwwwwwo", ".owwwwwwwwwwo", ".owwwwwwwwwo.", "..owwwwwwwwo.",
            "..owwwwwwwo..", "...owwwwwwo..", "...owwwwwwo..", "...oooooooo.." },
            new Dictionary<char, Color> { { 'o', new Color(0.23f, 0.18f, 0.42f) }, { 'w', new Color(1f, 0.93f, 0.85f) } });
        hpx.rectTransform.anchorMin = hpx.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        hpx.rectTransform.sizeDelta = new Vector2(91, 112); hpx.rectTransform.anchoredPosition = new Vector2(14, -50);
        var hl = UIKit.Label(hand.transform, "KÉO", 26, Ink); hl.rectTransform.offsetMin = new Vector2(0, -150); hl.rectTransform.offsetMax = new Vector2(0, -120);
        handRt.gameObject.SetActive(false);
        // animated UI lives in a nested canvas: moving labels no longer force a rebuild of the whole HUD
        dynLayer = UIKit.Stretch(UIKit.Rect("DynamicUI", hud));
        dynLayer.gameObject.AddComponent<Canvas>();
        foreach (var g in new Graphic[] { comboTxt, bigTxt, aimCount }) g.transform.SetParent(dynLayer, false);
        powerTxt = UIKit.Outlined(UIKit.Label(dynLayer, "", 40, Color.white), Ink, 3);
        powerTxt.rectTransform.anchorMin = powerTxt.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); powerTxt.rectTransform.sizeDelta = new Vector2(300, 60);
        powerTxt.raycastTarget = false; powerTxt.gameObject.SetActive(false);
        handRt.SetParent(dynLayer, false);
        introRoot.SetAsLastSibling(); popupRoot.SetAsLastSibling();
        // level-start curtain: fades out over ~0.3 s so entering a level is a transition, not a cut
        curtain = UIKit.Stretch(UIKit.Rect("Curtain", hud)).gameObject.AddComponent<Image>();
        curtain.color = new Color(0.36f, 0.27f, 0.6f, 0); curtain.raycastTarget = false; curtain.canvasRenderer.cullTransparentMesh = true;
        curtain.transform.SetAsLastSibling();
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

    // pixel icons for the booster buttons (same chunky style as the album pictures)
    static readonly Dictionary<char, Color> ICON_PAL = new Dictionary<char, Color>
    {
        { 'K', new Color(0.16f, 0.12f, 0.3f) }, { 'S', new Color(0.8f, 0.84f, 0.9f) }, { 'D', new Color(0.55f, 0.6f, 0.7f) },
        { 'N', new Color(0.62f, 0.4f, 0.2f) }, { 'B', new Color(0.23f, 0.6f, 1f) }, { 'L', new Color(0.75f, 0.9f, 1f) },
        { 'G', new Color(0.3f, 0.78f, 0.3f) }, { 'W', Color.white },
    };
    static Texture2D BoosterIcon(string k)
    {
        string[] rows = k == "hammer" ? new[] {
            ".DDDDDDDD.", "DSSSSSSSSD", "DSSSSSSSSD", ".DDDNNDDD.", "....NN....", "....NN....", "....NN....", "....NN....", "....NN....", "...KNNK...", }
        : k == "bomb" ? new[] {
            "....KK....", "...KWWK...", "..KBBBBK..", ".KBLBBBBK.", ".KLLBBBBK.", ".KBBBBBBK.", ".KBBBBBBK.", "..KBBBBK..", "...KKKK...", }
        : new[] {
            "..KKKKKK..", ".KGGGGGGK.", "KGGGWWGGGK", "KGGGWWGGGK", "KGWWWWWWGK", "KGWWWWWWGK", "KGGGWWGGGK", "KGGGWWGGGK", ".KGGGGGGK.", "..KKKKKK..", };
        return UIKit.PixelIcon(rows, ICON_PAL);
    }

    // ===================== combo bar: how many smashes this shot, and which duck each milestone gives =====================
    RectTransform comboBar, comboFill, comboGhost; Text comboCount; readonly List<(int need, string kind, Image dot, Text label, RectTransform rt)> comboMarks = new List<(int, string, Image, Text, RectTransform)>();
    RectTransform guideRoot; Text guideBody; const float COMBO_MAX = 25f; float comboShownFill = -1, comboShownGhost = -1;

    void BuildComboBar()
    {
        comboBar = UIKit.Panel(hud, new Color(0.33f, 0.27f, 0.53f, 0.92f), "ComboBar").rectTransform; UIKit.Place(comboBar, 0.5f, 1, -70, -384, 820, 52);
        var track = UIKit.Rect("Track", comboBar); UIKit.Stretch(track, 96, 6, 12, 6);
        comboGhost = UIKit.Panel(track, new Color(1f, 1f, 1f, 0.35f), "Ghost").rectTransform;
        comboFill = UIKit.Panel(track, new Color(1f, 0.82f, 0.3f), "Fill").rectTransform;
        foreach (var r in new[] { comboGhost, comboFill }) { r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(0, 1); r.offsetMin = r.offsetMax = Vector2.zero; r.pivot = new Vector2(0, 0.5f); }
        comboCount = UIKit.Label(comboBar, "0", 34, Color.white, TextAnchor.MiddleLeft); UIKit.Place(comboCount.rectTransform, 0, 0.5f, 52, 0, 90, 50);
        // milestone markers, smallest first
        for (int i = TIERS.Length - 1; i >= 0; i--)
        {
            var tr = TIERS[i];
            var mk = UIKit.Rect("Mark" + tr.need, track); mk.anchorMin = mk.anchorMax = new Vector2(tr.need / COMBO_MAX, 0.5f); mk.sizeDelta = new Vector2(10, 64);
            var dot = UIKit.Dot(mk, tr.col, "Dot"); dot.rectTransform.sizeDelta = new Vector2(34, 34); dot.raycastTarget = false;
            var num = UIKit.Label(dot.transform, tr.need.ToString(), 20, Color.white); num.raycastTarget = false;
            var lbl = UIKit.Outlined(UIKit.Label(mk, "", 24, Ink), Color.white, 2); lbl.rectTransform.anchoredPosition = new Vector2(0, -46); lbl.rectTransform.sizeDelta = new Vector2(200, 30); lbl.raycastTarget = false;
            comboMarks.Add((tr.need, tr.kind, dot, lbl, mk));
        }
        var help = UIKit.Btn(hud, "COMBO ?", Yellow, Ink, 28, OpenComboGuide, "ComboHelp"); comboHelp = help.gameObject;
        UIKit.Place((RectTransform)help.transform, 1, 1, -88, -384, 150, 56);
    }

    GameObject comboHelp;
    void UpdateComboBar()
    {
        bool show = level > LevelLibrary.Onboarding && state == "play";
        if (comboBar.gameObject.activeSelf != show) comboBar.gameObject.SetActive(show);
        // first lessons: no combo help, no FRENZY bar (one idea at a time)
        bool help = level > LevelLibrary.Onboarding || LevelLibrary.IsSandbox(level);
        if (comboHelp != null && comboHelp.activeSelf != help) comboHelp.SetActive(help);
        bool fev = level > 2 || LevelLibrary.IsSandbox(level);
        if (feverBar.gameObject.activeSelf != fev) feverBar.gameObject.SetActive(fev);
        if (!show) return;
        int now = moving ? shotSmash : 0, ghost = !moving && drag != null && aimVisible ? aimHits.Count : 0;
        float f = Mathf.Clamp01(now / COMBO_MAX), g = Mathf.Clamp01(ghost / COMBO_MAX);
        if (f != comboShownFill) { comboShownFill = f; comboFill.anchorMax = new Vector2(f, 1); comboCount.text = (moving ? now : ghost).ToString(); }
        if (g != comboShownGhost) { comboShownGhost = g; comboGhost.anchorMax = new Vector2(g, 1); if (!moving) comboCount.text = ghost.ToString(); }
        foreach (var m in comboMarks)
        {
            bool unlocked = LevelLibrary.Unlocked(m.kind, level), hit = now >= m.need;
            m.rt.localScale = Vector3.one * (unlocked && hit ? 1.25f + Mathf.Sin(t * 12) * 0.08f : 1f);
        }
    }

    void RefreshComboLabels()
    {
        foreach (var m in comboMarks)
        {
            var info = LevelLibrary.Specials.FirstOrDefault(x => x.kind == m.kind);
            bool unlocked = LevelLibrary.Unlocked(m.kind, level);
            string nm = info != null ? Cap(info.name).Replace("Vịt ", "") : m.kind;
            m.label.text = unlocked ? nm : "Màn " + (info != null ? info.level : 0);
            m.dot.color = unlocked ? TIERS.First(x => x.kind == m.kind).col : new Color(0.55f, 0.52f, 0.64f);
        }
    }

    void BuildComboGuide()
    {
        guideRoot = UIKit.Stretch(UIKit.Rect("ComboGuide", hud));
        var dim = guideRoot.gameObject.AddComponent<Image>(); dim.color = new Color(0.11f, 0.08f, 0.25f, 0.82f);
        var card = UIKit.Panel(guideRoot, new Color(1f, 0.97f, 0.94f), "Card").rectTransform; UIKit.Place(card, 0.5f, 0.5f, 0, 0, 960, 1420);
        var title = UIKit.Outlined(UIKit.Label(card, "CÁCH TẠO VỊT COMBO", 60, Purple), Ink, 3); UIKit.Place(title.rectTransform, 0.5f, 1, 0, -80, 900, 100);
        guideBody = UIKit.Label(card, "", 31, Ink, TextAnchor.UpperLeft); UIKit.Place(guideBody.rectTransform, 0.5f, 1, 0, -620, 860, 960);
        guideBody.horizontalOverflow = HorizontalWrapMode.Wrap; guideBody.verticalOverflow = VerticalWrapMode.Overflow; guideBody.lineSpacing = 1.05f;
        var close = UIKit.Btn(card, "ĐÃ HIỂU", new Color(0.37f, 0.83f, 0.37f), Color.white, 44, CloseComboGuide, "Close");
        UIKit.Place((RectTransform)close.transform, 0.5f, 0, 0, 95, 440, 120);
        guideRoot.gameObject.SetActive(false);
    }

    void OpenComboGuide()
    {
        Sfx.Play("click");
        var sb = new System.Text.StringBuilder();
        sb.Append("Phá nhiều món trong <b>1 phát</b> → 1 vịt thường hoá <b>vịt đặc biệt</b> (dùng 1 lần):\n\n");
        foreach (var sp in LevelLibrary.Specials)
        {
            bool ok = LevelLibrary.Unlocked(sp.kind, level);
            string need = sp.kind == "bomb" ? "10 món" : sp.kind == "zap" ? "cốc 3 vịt" : sp.kind == "rocket" ? "16 món" : "25 món";
            string use = sp.kind == "bomb" ? "nổ tung" : sp.kind == "zap" ? "phóng sét" : sp.kind == "rocket" ? "xuyên thẳng" : "sét 16 món";
            sb.Append(ok ? "• <b>" + Cap(sp.name) + "</b> (" + need + "): " + use + "\n" : "• <color=#9a94ad>" + Cap(sp.name) + " – mở ở màn " + sp.level + "</color>\n");
        }
        sb.Append("\n• Nảy tường rồi phá: điểm x2\n• Đầy FRENZY: phát sau bắn 5 vịt");
        if (level >= 17 && !LevelLibrary.IsSandbox(level)) sb.Append("\n• Phao bơi: bắn trúng → trượt húc vỡ đồ");
        if (whirls.Count > 0) sb.Append("\n• Xoáy nước: bẻ cong đường bay (bắn nhẹ cong nhiều)");
        if (flows.Count > 0) sb.Append("\n• Dòng chảy: đẩy vịt trôi theo mũi tên");
        if (portals.Count > 0) sb.Append("\n• Cống nước: chui vào 1 đầu, trồi ra đầu cùng màu");
        if (blocks.Any(b => b.type == "ice" && b.alive)) sb.Append("\n• Đồ đóng băng: cú đầu vỡ băng, cú sau mới vỡ");
        if (blocks.Any(b => b.type == "gold" && b.mvAmp > 0 && b.alive)) sb.Append("\n• Bánh vàng trôi ở mép trên: trúng = +1 lượt");
        guideBody.text = sb.ToString();
        guideRoot.gameObject.SetActive(true); guideRoot.SetAsLastSibling(); Time.timeScale = 0f;
    }

    void CloseComboGuide() { Sfx.Play("click"); guideRoot.gameObject.SetActive(false); Time.timeScale = 1f; }

    void RefreshHud()
    {
        if (!Active) return;
        UpdateComboBar();
        goalPulse = Mathf.Max(0, goalPulse - Time.unscaledDeltaTime);
        goalsTxt.rectTransform.localScale = Vector3.one * (1 + goalPulse * 0.25f);
        flashA = Mathf.Max(0, flashA - Time.unscaledDeltaTime * 2.5f);
        flashImg.color = new Color(1f, 0.97f, 0.9f, flashA);
        if (curtainA > 0) { curtainA = Mathf.Max(0, curtainA - Mathf.Min(Time.unscaledDeltaTime, 0.034f) * 3.2f); curtain.color = new Color(0.36f, 0.27f, 0.6f, curtainA * curtainA); }
        if (shownMoves != moves) { shownMoves = moves; movesTxt.text = moves.ToString(); }
        movesPulse = Mathf.Max(0, movesPulse - Time.unscaledDeltaTime);
        movesTxt.rectTransform.localScale = Vector3.one * (1 + movesPulse * 1.2f);
        movesTxt.color = moves <= 2 && state == "play" ? new Color(1f, 0.35f, 0.3f) : Ink;
        int proj = moves >= Mathf.CeilToInt(startMoves * 0.4f) ? 3 : moves >= Mathf.CeilToInt(startMoves * 0.15f) ? 2 : 1;
        if (projShown < 0 || state != "play" || moves > shownMovesMax) { projShown = proj; shownMovesMax = moves; }
        if (proj < projShown) { starLossT = 1f; starLostIdx = proj; projShown = proj; Sfx.Play("squeak", 0.45f, 0.6f); PopText("-1 sao", new Vector2(110, 150), new Color(1f, 0.85f, 0.3f), 40); }
        if (proj > projShown) projShown = proj;
        starLossT = Mathf.Max(0, starLossT - Time.unscaledDeltaTime * 1.6f);
        for (int i = 0; i < 3; i++)
        {
            var st = starPreview[i].rectTransform;
            if (i == starLostIdx && starLossT > 0)
            {
                float k = 1 - starLossT;
                st.localScale = Vector3.one * (k < 0.2f ? 1 + k * 3f : Mathf.Lerp(1.6f, 1f, (k - 0.2f) / 0.8f));
                st.localEulerAngles = new Vector3(0, 0, k * 360f);
                starPreview[i].color = Color.Lerp(new Color(1f, 0.4f, 0.3f), new Color(1, 1, 1, 0.35f), k);
            }
            else { st.localScale = Vector3.one; st.localEulerAngles = Vector3.zero; starPreview[i].color = i < proj ? Yellow : new Color(1, 1, 1, 0.35f); }
        }
        // goal counts, boosters and titles only change on smashes: refresh them ~8x/s instead of every frame (GC + text rebuilds)
        hudTick -= Time.unscaledDeltaTime;
        bool slow = hudTick <= 0; if (slow) hudTick = 0.12f;
        var boss = bossBlock;
        if (slow) {
        int left = GoalsLeft();
        if (left != shownLeft)
        {
            shownLeft = left;
            goalsTxt.text = "<size=28>MỤC TIÊU: PHÁ SẠCH BỒN</size>\n<size=66><b>" + (left == 0 ? "XONG!" : "còn " + left) + "</b></size>";
            objFill.anchorMax = new Vector2(goalTotal > 0 ? 1f - left / (float)goalTotal : 1f, 1);
            goalPulse = 0.25f;
            if (!halfShown && goalTotal >= 10 && left > 0 && left <= goalTotal / 2)
            {
                halfShown = true; PopText("ĐÃ PHÁ 50%!", new Vector2(270, 470), new Color(0.45f, 0.95f, 0.5f), 62);
                Sfx.Play("star", 0.7f, 1.2f);
            }
        }
        levelTxt.text = LevelLibrary.Title(level);
        foreach (var k in PRICE.Keys)
        {
            int n = SaveData.GetBoost(k);
            boostTxt[k].text = BNAME[k] + "\n" + (n > 0 ? "còn " + n : PRICE[k] + " xu");
            boostImg[k].color = target == k ? new Color(0.37f, 0.83f, 0.37f) : Yellow;
        }
        }
        Sfx.MusicMood(state != "play" ? 1f : frenzyReady || frenzyShot ? 1.07f : moves <= 1 && shownLeft > 0 ? 1.05f : 1f);
        // last move and still a lot left: the useful booster nods at the player (they often forget they own some)
        string nudge = state == "play" && !moving && target == null && moves <= 1 && shownLeft > 3 && level > LevelLibrary.Onboarding
            ? (SaveData.GetBoost("moves") > 0 || shownLeft > 12 ? "moves" : "bomb") : null;
        foreach (var kv in boostImg) kv.Value.transform.localScale = Vector3.one * (kv.Key == nudge ? 1f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) * 0.09f : 1f);
        int ds = Mathf.RoundToInt(displayScore); if (ds != shownScore) { shownScore = ds; scoreTxt.text = ds.ToString("N0"); }
        scoreTxt.rectTransform.localScale = Vector3.one * (1 + scorePulse * 1.5f);
        float fdt = Time.unscaledDeltaTime;
        float fTarget = frenzyReady ? FEVER_MAX : fever;
        feverShown = Mathf.MoveTowards(feverShown, fTarget, Mathf.Max(20f, Mathf.Abs(fTarget - feverShown) * 4f) * fdt);   // fills smoothly
        feverFill.anchorMax = new Vector2(Mathf.Clamp01(feverShown / FEVER_MAX), 1);
        int ghost = !moving && drag != null && aimVisible && !frenzyReady ? aimHits.Count : 0;   // what this shot would add
        feverGhost.anchorMax = new Vector2(Mathf.Clamp01((feverShown + ghost) / FEVER_MAX), 1);
        feverFill.GetComponent<Image>().color = frenzyReady ? Color.HSVToRGB(t * 0.8f % 1, 0.6f, 1f) : new Color(1f, 0.42f, 0.68f);
        int shownF = Mathf.RoundToInt(feverShown);
        if (frenzyReady) { if (feverMode != 2) { feverMode = 2; feverTxt.text = "SẴN SÀNG: 5 VỊT!"; feverHint.text = "phát sau xuyên phá"; } }
        else if (shownF != feverShownInt || feverMode != 1) { feverMode = 1; feverShownInt = shownF; feverTxt.text = shownF + " / " + (int)FEVER_MAX; feverHint.text = "phá 1 món = +1"; }
        feverBar.localScale = Vector3.one * (frenzyReady ? 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.025f : 1f);
        if (feverPopT > 0)
        {
            feverPopT -= fdt; float a = 1.1f - feverPopT;
            feverPop.rectTransform.localScale = Vector3.one * BackOut(a / 0.25f);
            var pc = feverPop.color; pc.a = Mathf.Clamp01(feverPopT * 2.5f); feverPop.color = pc;
            if (feverPopT <= 0) feverPop.gameObject.SetActive(false);
        }
        if (shownStreak != streak) { shownStreak = streak; streakTxt.text = streak > 0 ? "Chuỗi: điểm x" + (streak + 1) : ""; }
        if (boss != null && boss.alive && boss.view != null)
        {   // the HP bar rides above the boss (the top of the HUD is already busy)
            bossBar.anchorMin = bossBar.anchorMax = new Vector2(0.5f, 0.5f); bossBar.sizeDelta = new Vector2(330, 40);
            bossBar.anchoredPosition = WorldToHud(W(boss.p, 0.2f)) + new Vector2(0, -95);   // just under the boss
        }
        if (boss != null) { bossFill.anchorMax = new Vector2(Mathf.Clamp01((float)boss.hp / boss.max), 1); bossTxt.text = boss.alive ? "VUA BÁNH QUY " + Mathf.Max(0, boss.hp) + "/" + boss.max : "HẠ GỤC!"; if (!boss.alive && bossBar.gameObject.activeSelf) bossBar.gameObject.SetActive(false); }
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
            string nextTier = "";
            if (level > LevelLibrary.Onboarding)
                for (int ti = TIERS.Length - 1; ti >= 0; ti--)   // TIERS run big -> small: find the smallest one not yet reached
                    if (shotSmash < TIERS[ti].need && LevelLibrary.Unlocked(TIERS[ti].kind, level)) { nextTier = "\n<size=40>còn " + (TIERS[ti].need - shotSmash) + " → " + Cap(TIERS[ti].name) + "</size>"; break; }
            comboTxt.text = "PHÁ " + shotSmash + nextTier + (shotBanks > 0 ? "\n<size=40>điểm x" + (shotBanks + 1) + "</size>" : "");
            }
            comboTxt.color = shotSmash >= 25 ? Color.HSVToRGB(t * 1.5f % 1f, 0.55f, 1f) : shotSmash >= 16 ? new Color(1f, 0.35f, 0.2f) : shotSmash >= 10 ? new Color(1f, 0.6f, 0.3f) : Yellow;
            comboTxt.rectTransform.localScale = Vector3.one * (1 + comboPulse * 2.5f + Mathf.Min(shotSmash, 30) * 0.006f);
            comboTxt.rectTransform.localEulerAngles = new Vector3(0, 0, Mathf.Sin(t * 30f) * comboPulse * 40f);   // wobble on every smash
        }
    }

    void ShowPopup(bool win)
    {
        popupRoot.gameObject.SetActive(true); popAnim = 0; loseAnim = 0;
        introTimer = 0; introRoot.gameObject.SetActive(false); popupRoot.SetAsLastSibling();
        if (win)
        {
            popTitle.text = level == LevelLibrary.Count ? "PHÁ ĐẢO!" : hardLevel ? "VƯỢT MÀN KHÓ!" : (stars >= 3 ? "Xuất sắc!" : level <= LevelLibrary.Onboarding ? "Giỏi lắm!" : "Hoàn thành!");
            popStars.text = "";
            popArtBg.SetActive(newArt);
            if (newArt) popArt.texture = LevelLibrary.ArtTexture(levelArt, false);
            for (int i = 0; i < 3; i++) { popStarImgs[i].gameObject.SetActive(true); popStarImgs[i].color = i < stars ? Yellow : new Color(0.49f, 0.42f, 0.84f); popStarImgs[i].rectTransform.localScale = Vector3.one * (i == 1 ? 1.2f : 1f); }
            // at most 3 short lines: score / coin bonuses / keepsakes (the old 5-line list ran into the reward counter)
            var bonus = new List<string>();
            if (leftAtWin > 0) bonus.Add("Lượt dư +" + leftAtWin * 5);
            if (hardLevel) bonus.Add("Màn khó +80");
            var keep = new List<string>();
            if (newArt) keep.Add("<color=#ffe066>Tranh mới vào Album!</color>");
            if (bossKilled) keep.Insert(0, "<color=#ffe066>Đã hạ Vua Bánh Quy!</color>");
            popBody.text = "Điểm " + score.ToString("N0") + (bonus.Count > 0 ? "\n<size=32>" + string.Join(" · ", bonus) + " xu</size>" : "")
                + (keep.Count > 0 ? "\n<size=32>" + string.Join(" · ", keep) + "</size>" : "");
            popBody.verticalOverflow = VerticalWrapMode.Overflow;
            popReward.text = "+0 xu";
            UIKit.BtnText(popA).text = HasNext ? "MÀN TIẾP" : "VỀ BẢN ĐỒ";
            UIKit.BtnText(popB).text = stars < 3 && !LevelLibrary.IsSandbox(level) ? "Chơi lại lấy 3 sao" : "Bản đồ";   // missing stars: offer the retry right here
        }
        else
        {
            popTitle.text = "Hết lượt!";
            foreach (var im in popStarImgs) im.gameObject.SetActive(false);
            popStars.text = "";
            int lft = GoalsLeft(); int pct = goalTotal > 0 ? Mathf.RoundToInt(100f * (goalTotal - lft) / goalTotal) : 0;
            popBody.text = "Đã phá <b>" + pct + "%</b> · còn <b>" + lft + "</b> món\n<size=34>" + (bossBlock != null && bossBlock.alive ? "Vua Bánh Quy còn " + bossBlock.hp + " máu: canh lúc vua trôi tới rồi bắn!" : pct >= 80 ? "Sắp xong rồi! Thêm lượt để phá nốt" : "Thử góc khác hoặc dùng booster nhé") + "</size>";
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
        if (state == "win" && (stars >= 3 || LevelLibrary.IsSandbox(level))) App.ShowMap();
        else Restart();
    }

    public int Level => level;
}
