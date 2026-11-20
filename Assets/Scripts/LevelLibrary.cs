using System.Collections.Generic;
using UnityEngine;

public class BlockDef
{
    public string type; public float x, y, w, h, r; public bool rect;
    public string dir; public int pid = -1; public Color color = Color.white;
    public int hp = -1; public float mvAmp, mvSpeed;
    public bool shelf;   // plank drawn as a shelf: things it covers stand ON it and crash down when its legs go
}
public class DuckDef { public float x, y; public string kind; public DuckDef(float x, float y, string kind = "normal") { this.x = x; this.y = y; this.kind = kind; } }
public class BumperDef { public float x, y, w, h; }
public class LevelDef
{
    public int moves; public string hint = ""; public bool boss;
    public string art;   // pixel picture smashed in this level (goes into the album on a win)
    public bool hard;
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
    public const int SmashLevel = 101, SmashCount = 3;   // "phá đồ" destruction test levels 101..103
    public static bool IsSandbox(int n) => n >= TestLevel;
    public static string Title(int n) => n == TestLevel ? "Màn thử" : n >= SmashLevel ? "Phá đồ " + (n - SmashLevel + 1)
        : IsSuperHard(n) ? "Siêu khó " + n : IsHard(n) ? "Màn khó " + n : (n <= Onboarding ? "Hướng dẫn " : "Màn ") + n;

    // ---------- pixel-art album ----------
    public static readonly string[] ArtIds = { "star", "duck", "heart", "straw", "fish", "flower", "mush", "sun", "apple", "boat", "cat", "rainbow" };
    static readonly Dictionary<string, string> ArtNames = new Dictionary<string, string>
    {
        { "star", "Ngôi sao" }, { "duck", "Vịt vàng" }, { "heart", "Trái tim" }, { "straw", "Dâu tây" }, { "fish", "Cá xanh" }, { "flower", "Bông hoa" },
        { "mush", "Cây nấm" }, { "sun", "Mặt trời" }, { "apple", "Quả táo" }, { "boat", "Thuyền buồm" }, { "cat", "Mèo cam" }, { "rainbow", "Cầu vồng" },
    };
    public static string ArtName(string id) => ArtNames.TryGetValue(id, out var nm) ? nm : id;
    static string[] ArtRows(string id)
    {
        switch (id)
        {
            case "star": return ART_STAR; case "duck": return ART_DUCK; case "heart": return ART_HEART; case "straw": return ART_STRAW;
            case "fish": return ART_FISH; case "flower": return ART_FLOWER; case "mush": return ART_MUSH; case "sun": return ART_SUN;
            case "apple": return ART_APPLE; case "boat": return ART_BOAT; case "cat": return ART_CAT; default: return ART_RAINBOW;
        }
    }
    public static Texture2D ArtTexture(string id, bool locked)
    {
        var rows = ArtRows(id); int h = rows.Length, w = 0; foreach (var r in rows) w = Mathf.Max(w, r.Length);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = new Color(0, 0, 0, 0);
                if (x < rows[y].Length && PAL.TryGetValue(rows[y][x], out var pc)) c = locked ? new Color(0.62f, 0.58f, 0.72f, 0.55f) : pc;
                tex.SetPixel(x, h - 1 - y, c);
            }
        tex.Apply(); return tex;
    }

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
        if (IsSandbox(n)) return true;
        foreach (var s in Specials) if (s.kind == kind) return n >= s.level;
        return true;
    }
    public static SpecialInfo NewSpecialAt(int n) { foreach (var s in Specials) if (s.level == n) return s; return null; }
    // no bosses: milestones 10/15/20/25 are "hard levels" (denser layout, tight moves, bigger reward)
    public static bool IsHard(int n) => n == 10 || n == 15 || n == 20 || n == 25;
    public static bool IsSuperHard(int n) => n == 25;
    public static bool IsBoss(int n) => false;

    static System.Random rng; static int pidCounter;
    static float R() => (float)rng.NextDouble();
    static int RI(int a, int b) => rng.Next(a, b + 1);

    // ---------- builders ----------
    static BlockDef B(string t, float x, float y)
    {
        var b = new BlockDef { type = t, x = x, y = y };
        if (t == "crate" || t == "chest" || t == "stone") { b.rect = true; b.w = t == "chest" ? 50 : 36; b.h = t == "chest" ? 40 : 36; }
        else b.r = t == "tnt" || t == "bathbomb" || t == "vase" || t == "plates" ? 17 : 16;
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
        { 'P', Hex("ff9ec7") }, { 'G', Hex("5fd35f") }, { 'B', Hex("4fc3f7") }, { 'D', Hex("c7363f") }, { 'N', Hex("9a6233") },
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
    // original simple pixel pictures (album collection)
    static readonly string[] ART_STAR = { "....Y....", "...YYY...", "...YOY...", "YYYYYYYYY", ".YYYYYYY.", "..YYYYY..", ".YYYYYYY.", ".YY...YY.", "YY.....YY" };
    static readonly string[] ART_FLOWER = { "..PP.PP..", ".PPPPPPP.", "PPPYYYPPP", ".PPYOYPP.", "PPPYYYPPP", ".PPPPPPP.", "..PP.PP..", "....G....", "..GGG....", "....GGG..", "....G...." };
    static readonly string[] ART_MUSH = { "...RRRR...", ".RRWRRWRR.", "RRRRRRRRRR", "RWRRRRRRWR", "RRRRRRRRRR", "..WWWWWW..", "...WKWK...", "...WWWW...", "...WWWW..." };
    static readonly string[] ART_SUN = { "Y...Y...Y", ".Y.OOO.Y.", "..OOOOO..", ".OOYYYOO.", "YOOYYYOOY", ".OOYYYOO.", "..OOOOO..", ".Y.OOO.Y.", "Y...Y...Y" };
    static readonly string[] ART_APPLE = { "....N....", "....NGG..", "..RR.RR..", ".RRRRRRR.", "RRWRRRRRR", "RWRRRRRRR", "RRRRRRRRR", ".RRRRRRR.", "..RR.RR.." };
    static readonly string[] ART_BOAT = { ".....W.....", ".....WW....", ".....WWW...", ".....WWWW..", ".....N.....", "NNNNNNNNNNN", ".NNNNNNNNN.", "..BBBBBBB..", "BBB.BBB.BBB" };
    static readonly string[] ART_CAT = { "K.........K", "KK.......KK", "KOK.....KOK", "KOOOOOOOOOK", "KOGKOOOGKOK", "KOOOOOOOOOK", "KOOOOPOOOOK", ".KOOOOOOOK.", "..KKKKKKK.." };
    static readonly string[] ART_RAINBOW = { "...RRRRRR...", ".RROOOOOORR.", "ROOYYYYYYOOR", "ROYGGGGGGYOR", "OYGBBBBBBGYO", "YGB......BGY", "GB........BG" };
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
        if (n >= SmashLevel) return Smash(n - SmashLevel + 1);
        LevelDef d;
        switch (n)
        {
            // ======== ONBOARDING: one new kind of breakable per level ========
            case 1:
                d = L(8, "Kéo một con vịt ra sau rồi thả để bắn! Phá hết 1 hàng = CLEAR!", (200, 790, null), (340, 790, null), (270, 690, null));
                d.blocks.AddRange(PixelArt(ART_STAR, 270, 318, 34)); d.art = "star"; d.blocks.AddRange(Row("soap", 70, 500, 11, 40));
                d.bumpers.Add(BumperL(500)); d.bumpers.Add(BumperR(500));
                break;
            case 2:
                d = L(9, "Bắn vịt vào vịt → đẩy nhau bay đi phá tiếp!", (270, 810, null), (270, 650, null), (170, 570, null), (370, 570, null));
                d.blocks.AddRange(Cluster("soap", 270, 245, 3, 11, 40)); d.blocks.AddRange(Col("glass", 70, 380, 5, 40)); d.blocks.AddRange(Col("glass", 470, 380, 5, 40));
                d.blocks.AddRange(Row("glass", 150, 410, 7, 40));
                break;
            case 3:
                d = L(7, "Bắn trúng bom tắm → nổ tung cả vùng!", (200, 800, null), (340, 800, null), (270, 710, null));
                d.blocks.AddRange(Cluster("glass", 270, 230, 3, 11, 40));
                d.blocks.Add(B("bathbomb", 110, 340)); d.blocks.Add(B("bathbomb", 270, 340)); d.blocks.Add(B("bathbomb", 430, 340));
                d.blocks.AddRange(Row("soap", 150, 340, 2, 40)); d.blocks.AddRange(Row("soap", 310, 340, 2, 40));
                d.blocks.AddRange(Cluster("soap", 270, 430, 2, 11, 40)); d.blocks.Add(B("bathbomb", 190, 520)); d.blocks.Add(B("bathbomb", 350, 520));
                d.bumpers.Add(BumperL(600)); d.bumpers.Add(BumperR(600));
                break;
            case 4:
                d = L(11, "Chồng đĩa cần 3 cú · Bình gốm 2 cú · Phá rương được thêm 2 vịt!", (150, 800, null), (270, 800, null), (390, 800, null), (270, 690, null));
                for (int i = 0; i < 7; i++) d.blocks.Add(B("plates", 90 + i * 60, 240));
                d.blocks.AddRange(Row("vase", 70, 320, 11, 40)); d.blocks.Add(B("chest", 270, 400));
                d.blocks.AddRange(Cluster("glass", 270, 480, 2, 11, 40));
                break;
            case 5:
                d = L(7, "Đẩy đổ 1 quân domino → cả dây đổ theo!", (200, 800, null), (340, 800, null), (270, 710, null));
                d.blocks.AddRange(Cluster("glass", 270, 220, 2, 11, 40));
                for (int i = 0; i < 19; i++) d.blocks.Add(Dom(60 + i * 22, 310, true, 0));
                d.blocks.Add(B("bathbomb", 488, 310));
                d.blocks.AddRange(Row("vase", 70, 380, 11, 40)); d.blocks.AddRange(Row("soap", 70, 440, 11, 40));
                break;
            // ======== GAMEPLAY ========
            case 6:
                d = L(5, "Phá nhiều trong 1 phát → đầy thanh FRENZY!", (200, 800, null), (340, 800, null), (270, 710, null));
                d.blocks.AddRange(PixelArt(ART_DUCK, 270, 330, 26)); d.art = "duck";
                d.blocks.AddRange(Row("soap", 70, 520, 11, 40));
                d.bumpers.Add(BumperL(580, 160)); d.bumpers.Add(BumperR(580, 160));
                break;
            case 7:
                d = L(10, "Bắn gãy 2 chân kệ → cả kệ đổ vỡ tan tành!", (150, 810, null), (270, 810, null), (390, 810, null));
                d.blocks.AddRange(Shelf(270, 230, 420, "glass", "vase", "glass", "vase", "glass", "vase", "glass", "vase"));
                d.blocks.AddRange(Row("soap", 70, 300, 11, 40));
                d.blocks.AddRange(Shelf(270, 380, 420, "vase", "glass", "glass", "vase", "glass", "glass", "vase", "glass"));
                d.blocks.AddRange(Row("glass", 70, 450, 11, 40));
                d.blocks.AddRange(Shelf(270, 530, 360, "glass", "vase", "glass", "vase", "glass", "vase"));
                d.bumpers.Add(BumperL(620)); d.bumpers.Add(BumperR(620));
                break;
            case 8:
                d = L(7, "Vịt Sắt: hạ cả chồng đĩa chỉ 1 cú!", (150, 800, null), (270, 800, null), (390, 800, null), (210, 700, "heavy"), (330, 700, "heavy"));
                foreach (var p in new[] { (190, 260), (350, 260), (190, 340), (350, 340) }) d.blocks.Add(B("stone", p.Item1, p.Item2));
                d.blocks.Add(B("chest", 270, 250)); d.blocks.Add(B("chest", 270, 340));
                d.blocks.AddRange(Col("plates", 85, 220, 6, 40)); d.blocks.AddRange(Col("plates", 455, 220, 6, 40));
                d.blocks.AddRange(Row("vase", 70, 470, 11, 40)); d.blocks.AddRange(Cluster("glass", 270, 550, 2, 10, 40));
                d.blocks.Add(B("bathbomb", 130, 620)); d.blocks.Add(B("bathbomb", 410, 620));
                break;
            case 9:
                d = L(5, "Vịt Bom: nổ tung ở cú va chạm đầu tiên!", (150, 810, null), (270, 810, "bomb"), (390, 810, null));
                d.blocks.AddRange(PixelArt(ART_HEART, 270, 320, 34)); d.art = "heart"; d.blocks.Add(LZ(60, 320, "h")); d.blocks.Add(LZ(480, 252, "h"));
                d.blocks.AddRange(Shelf(270, 520, 340, "glass", "vase", "glass", "vase", "glass", "vase"));
                d.blocks.AddRange(Row("soap", 70, 590, 11, 40));
                break;
            case 10:
                d = L(13, "MÀN KHÓ! Đá chắn đường – ngắm qua khe, tận dụng domino và bom tắm!", (150, 810, "heavy"), (270, 810, null), (390, 810, null));
                d.blocks.AddRange(Shelf(270, 215, 440, "glass", "vase", "glass", "vase", "glass", "vase", "glass", "vase"));
                for (int i = 0; i < 11; i++) d.blocks.Add(B(i == 2 || i == 5 || i == 8 ? "stone" : "glass", 70 + i * 40, 290));   // lanes along the walls stay open for bank shots at the shelf legs
                for (int i = 0; i < 19; i++) d.blocks.Add(Dom(60 + i * 22, 350, true, 0));
                d.blocks.Add(B("bathbomb", 488, 350));
                for (int i = 0; i < 7; i++) d.blocks.Add(B("plates", 90 + i * 60, 415));
                d.blocks.AddRange(Row("vase", 70, 480, 11, 40));
                for (int i = 0; i < 11; i++) d.blocks.Add(B(i == 2 || i == 8 ? "stone" : "soap", 70 + i * 40, 540));
                d.blocks.AddRange(Shelf(270, 610, 360, "glass", "vase", "glass", "vase", "glass", "vase"));
                break;
            case 11:
                d = L(8, "Vịt Điện: giật điện 3 khối gần nhất!", (150, 810, null), (270, 810, "zap"), (390, 810, null), (270, 720, "zap"));
                for (int r = 0; r < 7; r++) d.blocks.AddRange(Row(r % 3 == 2 ? "plates" : (r == 1 || r == 4 ? "vase" : "glass"), 70, 200 + r * 42, 11, 40));
                d.blocks.Add(B("bathbomb", 270, 620)); d.blocks.Add(B("chest", 150, 620)); d.blocks.Add(B("chest", 390, 620));
                d.bumpers.Add(BumperL(660)); d.bumpers.Add(BumperR(660));
                break;
            case 12:
                d = L(6, "", (150, 810, null), (270, 810, "heavy"), (390, 810, null));
                d.blocks.AddRange(PixelArt(ART_STRAW, 270, 310, 32)); d.art = "straw"; d.blocks.Add(LZ(270, 470, "h"));
                for (int i = 0; i < 19; i++) d.blocks.Add(Dom(60 + i * 22, 530, true, 0));
                d.blocks.Add(B("bathbomb", 488, 530));
                d.blocks.AddRange(Shelf(270, 610, 380, "vase", "glass", "glass", "vase", "glass", "glass", "vase"));
                break;
            case 13:
                d = L(6, "", (150, 810, "zap"), (270, 810, null), (390, 810, "bomb"));
                d.blocks.AddRange(PixelArt(ART_FISH, 270, 290, 30)); d.art = "fish";
                d.blocks.AddRange(Row("vase", 70, 460, 11, 40)); d.blocks.Add(LZ(70, 520, "h")); d.blocks.AddRange(Row("soap", 110, 520, 10, 40));
                for (int i = 0; i < 7; i++) d.blocks.Add(B("plates", 90 + i * 60, 590));
                d.bumpers.Add(BumperL(640)); d.bumpers.Add(BumperR(640));
                break;
            default:
                d = IsHard(n) ? HardLevel(n) : Procedural(n);
                break;
        }
        if (n <= 13) d.bumpers.Clear();   // early game: no side bumpers until the player can judge power + angle
        d.boss = false; d.hard = IsHard(n);
        // unlock levels gift one special by transforming a plain duck: make sure there is one
        if (NewSpecialAt(n) != null && !d.ducks.Exists(x => x.kind == "normal") && d.ducks.Count > 0) d.ducks[d.ducks.Count - 1].kind = "normal";
        d.moves = Mathf.Max(3, d.moves);
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

    // ================= "PHÁ ĐỒ" destruction levels =================
    // breakables: glass (shatters), vase (2 hits), soap (bubbles), plates (stack, 3 hits), bathbomb (fizz blast),
    // domino (topples its whole chain), shelf (break both legs -> everything on it crashes down)
    static BlockDef Dom(float x, float y, bool vertical, int chain) =>
        new BlockDef { type = "domino", x = x, y = y, rect = true, w = vertical ? 10 : 32, h = vertical ? 32 : 10, pid = 1000 + chain };
    static IEnumerable<BlockDef> Shelf(float x, float y, float w, params string[] items)
    {
        int pid = pidCounter++;
        yield return new BlockDef { type = "plank", x = x, y = y, rect = true, w = w, h = 40, pid = pid, shelf = true };
        yield return new BlockDef { type = "screw", x = x - w / 2 + 16, y = y, r = 12, pid = pid };
        yield return new BlockDef { type = "screw", x = x + w / 2 - 16, y = y, r = 12, pid = pid };
        float x0 = x - w / 2 + 60, step = (w - 120) / Mathf.Max(1, items.Length - 1);
        for (int i = 0; i < items.Length; i++) yield return B(items[i], x0 + i * step, y);
    }

    static LevelDef Smash(int k)
    {
        LevelDef d;
        switch (k)
        {
            case 1:
                d = L(10, "Bắn gãy 2 chân kệ → cả kệ đổ vỡ tan tành! Phá hết 1 hàng = CLEAR!", (150, 810, null), (270, 810, null), (390, 810, null));
                d.blocks.AddRange(Shelf(270, 230, 420, "glass", "vase", "glass", "vase", "glass", "vase", "glass", "vase"));
                d.blocks.AddRange(Row("soap", 70, 320, 11, 40)); d.blocks.AddRange(Row("soap", 70, 360, 11, 40));
                foreach (var x in new[] { 110, 190, 270, 350, 430 }) d.blocks.Add(B("plates", x, 440));
                d.blocks.Add(B("bathbomb", 150, 520)); d.blocks.Add(B("bathbomb", 390, 520));
                d.blocks.AddRange(Row("glass", 190, 520, 4, 54));
                d.bumpers.Add(BumperL(600)); d.bumpers.Add(BumperR(600));
                break;
            case 2:
                d = L(8, "Đẩy đổ 1 quân domino → cả dây đổ theo!", (150, 810, null), (270, 810, null), (390, 810, null));
                d.blocks.AddRange(Cluster("glass", 270, 215, 2, 10, 40));
                for (int i = 0; i < 20; i++) d.blocks.Add(Dom(60 + i * 22, 300, true, 0));
                for (int i = 1; i <= 5; i++) d.blocks.Add(Dom(482, 300 + i * 22, false, 0));
                for (int i = 0; i < 19; i++) d.blocks.Add(Dom(460 - i * 22, 430, true, 0));
                d.blocks.AddRange(Row("vase", 70, 365, 10, 40));
                d.blocks.Add(B("bathbomb", 62, 478));
                d.blocks.AddRange(Row("soap", 110, 500, 10, 40)); d.blocks.AddRange(Row("soap", 110, 540, 10, 40));
                break;
            default:
                d = L(11, "Tổng vệ sinh! Kệ, domino, chồng đĩa, bom tắm — phá sạch!", (130, 815, null), (230, 815, "heavy"), (330, 815, null), (430, 815, null));
                d.blocks.AddRange(Shelf(270, 210, 440, "vase", "glass", "glass", "vase", "glass", "glass", "vase", "glass"));
                for (int i = 0; i < 7; i++) d.blocks.Add(B("plates", 90 + i * 60, 295));
                for (int i = 0; i < 20; i++) d.blocks.Add(Dom(60 + i * 22, 355, true, 1));
                d.blocks.AddRange(Cluster("soap", 270, 450, 3, 11, 40));
                foreach (var x in new[] { 110, 270, 430 }) d.blocks.Add(B("bathbomb", x, 545));
                d.blocks.AddRange(Row("glass", 150, 545, 2, 60)); d.blocks.AddRange(Row("glass", 330, 545, 2, 60));
                d.blocks.AddRange(Row("glass", 110, 600, 9, 40));
                d.bumpers.Add(BumperL(640)); d.bumpers.Add(BumperR(640));
                break;
        }
        return d;
    }

    // ---------- destruction "modules" stacked top-to-bottom for levels 14-25 ----------
    static string PropKind() { float p = R(); return p < 0.42f ? "glass" : p < 0.75f ? "soap" : "vase"; }
    static string[] ShelfItems(int w) { int k = Mathf.Min(RI(6, 8), (w - 120) / 36 + 1); var a = new string[k]; for (int i = 0; i < k; i++) a[i] = R() < 0.6f ? "glass" : "vase"; return a; }

    static float AddModule(LevelDef d, float y, bool allowArt)
    {
        float p = R();
        if (p < 0.28f)
        {   // 1-2 full rows of mixed props (line clears!)
            int rows = RI(1, 2);
            for (int r = 0; r < rows; r++, y += 40) { string k = PropKind(); for (int i = 0; i < 11; i++) d.blocks.Add(B(R() < 0.75f ? k : PropKind(), 70 + i * 40, y)); }
            return y + 6;
        }
        if (p < 0.46f) { int w = RI(10, 14) * 30; d.blocks.AddRange(Shelf(270, y + 8, w, ShelfItems(w))); return y + 72; }
        if (p < 0.62f && !d.blocks.Exists(x => x.type == "domino"))   // at most one domino run per level (more = everything chain-clears)
        {   // domino run ending in a bath bomb
            int ch = RI(1, 999); bool rev = R() < 0.5f;
            for (int i = 0; i < 19; i++) d.blocks.Add(Dom(rev ? 478 - i * 22 : 60 + i * 22, y + 6, true, ch));
            d.blocks.Add(B("bathbomb", rev ? 52 : 488, y + 6));
            return y + 56;
        }
        if (p < 0.76f) { for (int i = 0; i < 7; i++) d.blocks.Add(B("plates", 90 + i * 60, y)); return y + 48; }
        if (p < 0.86f && allowArt && d.art == null && y < 400)
        {   // small pixel picture (album)
            bool heart = R() < 0.5f;
            d.blocks.AddRange(PixelArt(heart ? ART_HEART : ART_STRAW, 270, y + 96, 22)); d.art = heart ? "heart" : "straw";
            return y + 214;
        }
        // hazard row: glass with stones, bath bombs, a laser
        for (int i = 0; i < 11; i++)
        {
            float q = R(), x = 70 + i * 40;
            d.blocks.Add(q < 0.12f ? B("stone", x, y) : q < 0.26f ? B("bathbomb", x, y) : q < 0.31f ? LZ(x, y, R() < 0.5f ? "h" : "v") : B("glass", x, y));
        }
        return y + 44;
    }

    static LevelDef Procedural(int n)
    {
        var d = new LevelDef { moves = 7 + n / 10 };
        float y = 205; int guard = 0;
        string pic = n == 14 ? "flower" : n == 16 ? "mush" : n == 17 ? "sun" : n == 18 ? "apple" : n == 19 ? "boat"
                   : n == 21 ? "cat" : n == 22 ? "rainbow" : n == 23 ? "heart" : n == 24 ? "duck" : null;
        if (pic != null)
        {   // picture on top (looks great + goes into the album), destruction modules underneath
            var rows = ArtRows(pic); int w = 0; foreach (var r in rows) w = Mathf.Max(w, r.Length);
            float cell = Mathf.Min(28f, Mathf.Min(250f / rows.Length, 430f / w));
            d.blocks.AddRange(PixelArt(rows, 270, y + rows.Length * cell / 2f, cell)); d.art = pic;
            y += rows.Length * cell + 24;
        }
        while (y < 590 && guard++ < 10) y = AddModule(d, y, pic == null);
        foreach (var b in d.blocks) if (b.type == "plank") d.moves++;   // each shelf needs extra shots for its legs
        if (R() < 0.6f) d.blocks.Add(B("chest", RI(130, 410), Mathf.Min(y + 30, 660)));
        if (R() < 0.5f) { d.bumpers.Add(BumperL(680)); d.bumpers.Add(BumperR(680)); }
        int nd = RI(3, 4);
        var spots = new[] { (150f, 800f), (270f, 800f), (390f, 800f), (270f, 710f) };
        for (int i = 0; i < nd; i++) d.ducks.Add(new DuckDef(spots[i].Item1, spots[i].Item2, RandKind()));
        return d;
    }

    // hard level: the whole tub packed with modules + a stone-studded hazard row, only 3 ducks, tight moves
    static LevelDef HardLevel(int n)
    {
        bool super = IsSuperHard(n);
        var d = new LevelDef { moves = (super ? 7 : 6) + n / 10, hint = super ? "SIÊU KHÓ! Mỗi phát đều phải tính!" : "MÀN KHÓ! Ngắm kỹ, tận dụng nổ dây chuyền!" };
        float y = 200; int guard = 0;
        while (y < 560 && guard++ < 10) y = AddModule(d, y, false);
        for (int i = 0; i < 11; i++) { float q = R(); d.blocks.Add(B(q < 0.25f ? "stone" : q < 0.4f ? "bathbomb" : PropKind(), 70 + i * 40, Mathf.Max(y + 10, 600))); }
        foreach (var b in d.blocks)
        {   // toughen: fragile props become 2-3 hit ones, half the bath bombs become stones (fewer free chain reactions)
            if ((b.type == "glass" || b.type == "soap") && R() < 0.35f) { bool pl = R() < 0.4f; b.type = pl ? "plates" : "vase"; b.r = 17; }
            else if (b.type == "bathbomb" && R() < 0.5f) { b.type = "stone"; b.rect = true; b.w = b.h = 36; }
            if (b.type == "plank") d.moves++;
        }
        var spots = new[] { (150f, 810f), (270f, 810f), (390f, 810f) };
        foreach (var sp in spots) d.ducks.Add(new DuckDef(sp.Item1, sp.Item2, RandKind()));
        return d;
    }
}
