using System.Collections.Generic;
using UnityEngine;

public class BlockDef
{
    public string type; public float x, y, w, h, r; public bool rect;
    public string dir; public int pid = -1; public Color color = Color.white;
    public int hp = -1; public float mvAmp, mvSpeed;
}
public class DuckDef { public float x, y; public string kind; public DuckDef(float x, float y, string kind = "normal") { this.x = x; this.y = y; this.kind = kind; } }
public class BumperDef { public float x, y, w, h; }
public class LevelDef
{
    public int moves; public string hint = ""; public bool boss;
    public List<DuckDef> ducks = new List<DuckDef>();
    public List<BlockDef> blocks = new List<BlockDef>();
    public List<BumperDef> bumpers = new List<BumperDef>();
}

// 25 levels: 1-5 onboarding (one new idea each), 6-25 gameplay (boss at 10/15/20/25).
// Coordinates are "sim pixels" on a 540x960 board; the tub spans x 28..512, y 150..870.
public static class LevelLibrary
{
    public const int Count = 25, Onboarding = 5;
    public const int TestLevel = 99;   // sandbox level for trying new mechanics (not on the 25-level path, saves nothing)

    // Combo special ducks are introduced one at a time, after the player has learned the basics.
    // On its unlock level the player gets a card explaining how to earn + use it, and one free copy to try.
    public class SpecialInfo { public string kind, name, how, use; public int level; }
    public static readonly SpecialInfo[] Specials =
    {
        new SpecialInfo { kind = "bomb",    level = 12, name = "VỊT BOM",      how = "Phá 10+ khối trong 1 phát",         use = "Nổ tung khi chạm khối đầu tiên" },
        new SpecialInfo { kind = "zap",     level = 14, name = "VỊT SÉT",      how = "Bắn vịt va đẩy 3 lần trong 1 phát", use = "Mỗi lần va chạm phóng sét sang khối gần" },
        new SpecialInfo { kind = "rocket",  level = 16, name = "VỊT TÊN LỬA",  how = "Phá 16+ khối trong 1 phát",         use = "Lao thẳng, xuyên qua mọi khối phá được" },
        new SpecialInfo { kind = "rainbow", level = 18, name = "VỊT CẦU VỒNG", how = "Phá 25+ khối trong 1 phát",         use = "Chạm khối đầu tiên → sét cầu vồng đánh 16 khối" },
    };
    public static bool Unlocked(string kind, int n)
    {
        if (n == TestLevel) return true;
        foreach (var s in Specials) if (s.kind == kind) return n >= s.level;
        return true;
    }
    public static SpecialInfo NewSpecialAt(int n) { foreach (var s in Specials) if (s.level == n) return s; return null; }
    public static bool IsBoss(int n) => n == 10 || n == 15 || n == 20 || n == 25;

    static System.Random rng; static int pidCounter;
    static float R() => (float)rng.NextDouble();
    static int RI(int a, int b) => rng.Next(a, b + 1);

    // ---------- builders ----------
    static BlockDef B(string t, float x, float y)
    {
        var b = new BlockDef { type = t, x = x, y = y };
        if (t == "crate" || t == "chest" || t == "stone") { b.rect = true; b.w = t == "chest" ? 50 : 36; b.h = t == "chest" ? 40 : 36; }
        else b.r = t == "tnt" ? 17 : 16;
        return b;
    }
    static IEnumerable<BlockDef> Row(string t, float x0, float y, int n, float dx) { for (int i = 0; i < n; i++) yield return B(t, x0 + i * dx, y); }
    static IEnumerable<BlockDef> Col(string t, float x, float y0, int n, float dy) { for (int i = 0; i < n; i++) yield return B(t, x, y0 + i * dy); }
    static IEnumerable<BlockDef> Cluster(string t, float cx, float cy, int rows, int cols, float s)
    { for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) yield return B(t, cx + (c - (cols - 1) / 2f) * s, cy + (r - (rows - 1) / 2f) * s); }
    static BlockDef LZ(float x, float y, string dir) => new BlockDef { type = "laser", x = x, y = y, r = 16, dir = dir };
    static IEnumerable<BlockDef> Plank(float x, float y, float w, float h = 34)
    {
        int pid = pidCounter++;
        yield return new BlockDef { type = "plank", x = x, y = y, rect = true, w = w, h = h, pid = pid };
        yield return new BlockDef { type = "screw", x = x - w / 2 + 16, y = y, r = 12, pid = pid };
        yield return new BlockDef { type = "screw", x = x + w / 2 - 16, y = y, r = 12, pid = pid };
    }
    static BlockDef Boss(float x, float y, int hp, float amp, float speed) => new BlockDef { type = "boss", x = x, y = y, r = 58, hp = hp, mvAmp = amp, mvSpeed = speed };

    static readonly Dictionary<char, Color> PAL = new Dictionary<char, Color>
    {
        { 'Y', Hex("ffd84a") }, { 'O', Hex("ff8a1f") }, { 'K', Hex("2b2b3b") }, { 'W', Color.white }, { 'R', Hex("ff4d6d") },
        { 'P', Hex("ff9ec7") }, { 'G', Hex("5fd35f") }, { 'B', Hex("4fc3f7") }, { 'D', Hex("c7363f") },
    };
    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

    static IEnumerable<BlockDef> PixelArt(string[] rows, float cx, float cy, float cell)
    {
        int h = rows.Length, w = 0; foreach (var r in rows) w = Mathf.Max(w, r.Length);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (PAL.TryGetValue(rows[y][x], out var c))
                    yield return new BlockDef { type = "pixel", rect = true, w = cell - 2, h = cell - 2, color = c, x = cx + (x - (w - 1) / 2f) * cell, y = cy + (y - (h - 1) / 2f) * cell };
    }

    static readonly string[] ART_DUCK = {
        ".....YYYY.....", "....YYYYYY....", "...YYYYWKYY...", "...YYYYKKYYOO.", "...YYYYYYYOOOO", "....YYYYYY.OO.",
        "..Y..YYYY.....", ".YYYYYYYYYYY..", "YYYYYYYYYYYYY.", "YYYYYYYYYYYYY.", ".YYYYYYYYYYY..", "..YYYYYYYYY..." };
    static readonly string[] ART_HEART = { ".DD...DD.", "DRRD.DRRD", "DRWRDRRRD", "DWRRRRRRD", "DRRRRRRRD", ".DRRRRRD.", "..DRRRD..", "...DRD...", "....D...." };
    static readonly string[] ART_STRAW = { "...GGGG...", "..GGGGGG..", ".RRRRRRRR.", "RRWRRRRWRR", "RRRRRWRRRR", "RWRRRRRRWR", ".RRRWRRRR.", "..RRRRRR..", "...RRRR..." };
    static readonly string[] ART_FISH = { "....BBB.....", "..BBBBBBB..B", ".BBWKBBBBBBB", "BBBKKBBBBBB.", ".BBBBBBBBBBB", "..BBBBBBB..B", "....BBB....." };

    static BumperDef BumperL(float y, float h = 140) => new BumperDef { x = 32, y = y, w = 12, h = h };
    static BumperDef BumperR(float y, float h = 140) => new BumperDef { x = 496, y = y, w = 12, h = h };

    static LevelDef L(int moves, string hint, params (float x, float y, string k)[] ducks)
    {
        var d = new LevelDef { moves = moves, hint = hint };
        foreach (var t in ducks) d.ducks.Add(new DuckDef(t.x, t.y, t.k ?? "normal"));
        return d;
    }

    static string RandKind() { float p = R(); return p < 0.55f ? "normal" : p < 0.7f ? "heavy" : p < 0.85f ? "bomb" : "zap"; }

    public static LevelDef Get(int n)
    {
        rng = new System.Random(n * 7919 + 3); pidCounter = 0;
        if (n == TestLevel) return Test();
        LevelDef d;
        switch (n)
        {
            // ======== ONBOARDING ========
            case 1:
                d = L(10, "Kéo một con vịt ra sau rồi thả để bắn!", (200, 780, null), (340, 780, null), (270, 680, null));
                d.blocks.AddRange(Cluster("cookie", 270, 290, 5, 11, 38)); d.blocks.Add(B("chest", 270, 420));
                d.bumpers.Add(BumperL(460)); d.bumpers.Add(BumperR(460));
                break;
            case 2:
                d = L(10, "Bắn vịt vào vịt → đẩy nhau bay đi phá tiếp!", (270, 810, null), (270, 640, null), (170, 560, null), (370, 560, null));
                d.blocks.AddRange(Cluster("cookie", 270, 250, 3, 11, 38)); d.blocks.AddRange(Col("cookie", 70, 380, 5, 38)); d.blocks.AddRange(Col("cookie", 470, 380, 5, 38));
                d.blocks.AddRange(Row("cookie", 150, 400, 7, 40));
                break;
            case 3:
                d = L(8, "Bắn trúng TNT để nổ dây chuyền!", (200, 800, null), (340, 800, null), (270, 710, null));
                d.blocks.AddRange(Cluster("cookie", 270, 230, 3, 12, 38)); d.blocks.Add(B("tnt", 120, 340)); d.blocks.Add(B("tnt", 270, 340)); d.blocks.Add(B("tnt", 420, 340));
                d.blocks.AddRange(Cluster("cookie", 270, 410, 2, 12, 38)); d.blocks.Add(B("tnt", 190, 500)); d.blocks.Add(B("tnt", 350, 500)); d.blocks.AddRange(Cluster("cookie", 270, 570, 2, 6, 38));
                d.bumpers.Add(BumperL(600)); d.bumpers.Add(BumperR(600));
                break;
            case 4:
                d = L(13, "Thùng gỗ cần 2 cú · Phá rương được thêm 2 vịt!", (150, 800, null), (270, 800, null), (390, 800, null), (270, 690, null));
                d.blocks.AddRange(Cluster("cookie", 150, 280, 4, 4, 36)); d.blocks.AddRange(Cluster("cookie", 390, 280, 4, 4, 36));
                d.blocks.AddRange(Row("crate", 72, 420, 10, 44)); d.blocks.Add(B("chest", 270, 250)); d.blocks.AddRange(Cluster("cookie", 270, 520, 2, 8, 38));
                break;
            case 5:
            {
                d = L(10, "Laser xoá sạch cả hàng / cột!", (200, 800, null), (340, 800, null), (270, 710, null));
                var lz = new Dictionary<string, string> { { "1,2", "h" }, { "3,8", "v" }, { "5,5", "h" }, { "6,1", "v" }, { "2,9", "h" } };
                for (int r = 0; r < 7; r++) for (int c = 0; c < 11; c++)
                    {
                        float x = 70 + c * 40, y = 210 + r * 40;
                        d.blocks.Add(lz.TryGetValue(r + "," + c, out var dir) ? LZ(x, y, dir) : B(r == 4 ? "crate" : "cookie", x, y));
                    }
                break;
            }
            // ======== GAMEPLAY ========
            case 6:
                d = L(8, "Phá nhiều trong 1 phát → đầy thanh FRENZY!", (200, 800, null), (340, 800, null), (270, 710, null));
                d.blocks.AddRange(PixelArt(ART_DUCK, 270, 345, 26)); d.bumpers.Add(BumperL(560, 160)); d.bumpers.Add(BumperR(560, 160));
                break;
            case 7:
                d = L(13, "Bắn rơi hết ốc → ván gỗ đè nát mọi thứ!", (150, 810, null), (270, 810, null), (390, 810, null));
                d.blocks.AddRange(Plank(270, 240, 340)); d.blocks.AddRange(Row("cookie", 110, 240, 9, 40)); d.blocks.AddRange(Row("cookie", 90, 300, 10, 40));
                d.blocks.AddRange(Plank(270, 370, 340)); d.blocks.AddRange(Row("cookie", 110, 370, 9, 40)); d.blocks.Add(B("tnt", 70, 430)); d.blocks.Add(B("tnt", 470, 430)); d.blocks.AddRange(Row("crate", 110, 430, 9, 40));
                d.blocks.AddRange(Plank(270, 500, 340)); d.blocks.AddRange(Row("cookie", 110, 500, 9, 40));
                d.bumpers.Add(BumperL(600)); d.bumpers.Add(BumperR(600));
                break;
            case 8:
                d = L(11, "Vịt Sắt: mỗi cú phá 3 máu – đập rương 1 phát!", (150, 800, null), (270, 800, null), (390, 800, null), (210, 700, "heavy"), (330, 700, "heavy"));
                foreach (var p in new[] { (190, 260), (350, 260), (190, 340), (350, 340) }) d.blocks.Add(B("stone", p.Item1, p.Item2));
                d.blocks.Add(B("chest", 270, 250)); d.blocks.Add(B("chest", 270, 340));
                d.blocks.AddRange(Cluster("cookie", 85, 300, 6, 2, 38)); d.blocks.AddRange(Cluster("cookie", 455, 300, 6, 2, 38));
                d.blocks.AddRange(Row("crate", 70, 450, 10, 44)); d.blocks.AddRange(Cluster("cookie", 270, 530, 2, 10, 40)); d.blocks.Add(B("tnt", 130, 600)); d.blocks.Add(B("tnt", 410, 600));
                break;
            case 9:
                d = L(8, "Vịt Bom: nổ tung ở cú va chạm đầu tiên!", (150, 810, null), (270, 810, "bomb"), (390, 810, null));
                d.blocks.AddRange(PixelArt(ART_HEART, 270, 330, 34)); d.blocks.Add(LZ(60, 330, "h")); d.blocks.Add(LZ(480, 262, "h")); d.blocks.Add(LZ(270, 520, "v"));
                d.blocks.AddRange(Plank(270, 600, 300)); d.blocks.AddRange(Row("cookie", 130, 600, 8, 40));
                break;
            case 10:
                d = L(12, "BOSS! Bánh khổng lồ – dùng Búa nếu bí!", (150, 810, "heavy"), (270, 810, "bomb"), (390, 810, null), (270, 720, null));
                d.blocks.Add(Boss(270, 300, 30, 150, 0.9f)); d.blocks.AddRange(Row("cookie", 72, 440, 11, 40)); d.blocks.AddRange(Row("cookie", 72, 480, 11, 40));
                d.blocks.Add(B("tnt", 120, 540)); d.blocks.Add(B("tnt", 420, 540)); d.blocks.Add(LZ(270, 540, "h"));
                d.bumpers.Add(BumperL(620)); d.bumpers.Add(BumperR(620));
                break;
            case 11:
                d = L(9, "Vịt Điện: giật điện 3 khối gần nhất! Bánh băng cần 2 cú", (150, 810, null), (270, 810, "zap"), (390, 810, null), (270, 720, "zap"));
                for (int r = 0; r < 7; r++) d.blocks.AddRange(Row(r % 3 == 2 ? "crate" : (r == 1 || r == 4 ? "ice" : "cookie"), 72, 200 + r * 42, 11, 40));
                d.blocks.Add(B("tnt", 270, 330)); d.blocks.Add(B("tnt", 110, 450)); d.blocks.Add(B("tnt", 430, 450));
                d.blocks.Add(B("chest", 270, 520)); d.blocks.Add(B("chest", 150, 560)); d.blocks.Add(B("chest", 390, 560));
                d.bumpers.Add(BumperL(640)); d.bumpers.Add(BumperR(640));
                break;
            case 12:
                d = L(10, "", (150, 810, null), (270, 810, "heavy"), (390, 810, null));
                d.blocks.AddRange(PixelArt(ART_STRAW, 270, 320, 32)); d.blocks.Add(LZ(270, 480, "h"));
                d.blocks.AddRange(Plank(270, 560, 320)); d.blocks.AddRange(Row("crate", 110, 560, 9, 40));
                d.blocks.AddRange(Plank(270, 640, 320)); d.blocks.AddRange(Row("cookie", 110, 640, 9, 40));
                break;
            case 13:
                d = L(9, "", (150, 810, "zap"), (270, 810, null), (390, 810, "bomb"));
                d.blocks.AddRange(PixelArt(ART_FISH, 270, 300, 30)); d.blocks.AddRange(Row("ice", 72, 470, 11, 40)); d.blocks.Add(LZ(70, 530, "h")); d.blocks.AddRange(Row("cookie", 110, 530, 10, 40));
                d.bumpers.Add(BumperL(620)); d.bumpers.Add(BumperR(620));
                break;
            default:
                d = IsBoss(n) ? BossLevel(n) : Procedural(n);
                break;
        }
        d.boss = IsBoss(n);
        // unlock levels gift one special by transforming a plain duck: make sure there is one
        if (NewSpecialAt(n) != null && !d.ducks.Exists(x => x.kind == "normal") && d.ducks.Count > 0) d.ducks[d.ducks.Count - 1].kind = "normal";
        // tighter move budget: every shot should matter (big combos refund moves in-game)
        d.moves = Mathf.Max(3, Mathf.RoundToInt(d.moves * (n <= Onboarding ? 0.9f : IsBoss(n) ? 0.85f : 0.6f)));
        return d;
    }

    // Test level: a dense cookie field so big combos (10 / 16 / 25) are easy to reach,
    // plus one rocket and one rainbow duck to try right away.
    static LevelDef Test()
    {
        var d = L(12, "Màn thử: phá 10+ / 16+ / 25+ trong 1 phát để nở vịt đặc biệt!",
            (150, 800, null), (270, 820, "rocket"), (390, 800, null), (200, 700, null), (340, 700, "rainbow"));
        d.blocks.AddRange(Cluster("cookie", 270, 250, 4, 11, 38));
        d.blocks.Add(B("tnt", 110, 345)); d.blocks.Add(B("tnt", 430, 345));
        d.blocks.AddRange(Row("cookie", 148, 345, 7, 40));
        d.blocks.Add(LZ(70, 395, "h")); d.blocks.AddRange(Row("crate", 110, 395, 10, 40));
        d.blocks.AddRange(Cluster("cookie", 270, 470, 2, 9, 38));
        d.blocks.Add(B("stone", 130, 560)); d.blocks.Add(B("stone", 410, 560)); d.blocks.Add(B("chest", 270, 560));
        d.bumpers.Add(BumperL(600)); d.bumpers.Add(BumperR(600));
        return d;
    }

    static LevelDef Procedural(int n)
    {
        var d = new LevelDef { moves = 7 + n / 10 };
        int rows = RI(5, 7);
        for (int r = 0; r < rows; r++)
        {
            float y = 200 + r * 40, t = R();
            for (int i = 0; i < 11; i++)
            {
                float x = 72 + i * 40, p = R();
                if (p > 0.12f && p < 0.17f) d.blocks.Add(LZ(x, y, R() < 0.5f ? "h" : "v"));
                else d.blocks.Add(B(p < 0.08f ? "tnt" : (p < 0.12f && r > 0 ? "stone" : (t < 0.3f ? "cookie" : (t < 0.55f ? "ice" : "crate"))), x, y));
            }
        }
        if (R() < 0.5f) d.blocks.AddRange(Plank(270, 240, 340));
        if (R() < 0.4f) { d.blocks.AddRange(PixelArt(R() < 0.5f ? ART_HEART : ART_STRAW, 270, 560, 22)); d.moves -= 1; }
        int chests = RI(1, 2); for (int i = 0; i < chests; i++) d.blocks.Add(B("chest", RI(120, 420), 200 + rows * 40 + 30));
        int tnts = RI(2, 4); for (int i = 0; i < tnts; i++) d.blocks.Add(B("tnt", RI(70, 470), RI(500, 600)));
        int nd = RI(3, 4);
        var spots = new[] { (150f, 800f), (270f, 800f), (390f, 800f), (270f, 700f) };
        for (int i = 0; i < nd; i++) d.ducks.Add(new DuckDef(spots[i].Item1, spots[i].Item2, RandKind()));
        return d;
    }

    static LevelDef BossLevel(int n)
    {
        bool final = n == 25;
        var d = new LevelDef { moves = final ? 14 : 12, hint = final ? "BOSS CUỐI! Hạ gục Vua Bánh!" : "BOSS! Bánh khổng lồ đang trượt qua lại" };
        d.blocks.Add(Boss(270, 290, final ? 45 : 30 + n / 4, 140 + n * 2, 0.8f + n * 0.02f));
        for (int r = 0; r < 3; r++)
        {
            float y = 430 + r * 42;
            for (int i = 0; i < 11; i++)
            {
                float p = R(), x = 72 + i * 40;
                d.blocks.Add(p < 0.08f ? B("tnt", x, y) : p < 0.14f ? LZ(x, y, R() < 0.5f ? "h" : "v") : B(r == 1 ? "ice" : (R() < 0.3f ? "crate" : "cookie"), x, y));
            }
        }
        if (final) { d.blocks.AddRange(Plank(270, 600, 300)); d.blocks.AddRange(Row("cookie", 130, 600, 8, 40)); }
        var spots = new[] { (150f, 810f), (270f, 810f), (390f, 810f), (270f, 720f) };
        foreach (var s in spots) d.ducks.Add(new DuckDef(s.Item1, s.Item2, RandKind()));
        return d;
    }
}
