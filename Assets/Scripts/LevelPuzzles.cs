using System.Collections.Generic;
using UnityEngine;

// Hand-designed puzzle levels 14-30. Each one is built around ONE idea the player has to read and exploit
// (a fortress gap, a domino snake, a bomb chain, a laser grid, a whirlpool curve...), instead of the
// random module stacks used before. Coordinates are sim pixels: tub x 28..512, y 150..870, ducks at y ~800.
public static partial class LevelLibrary
{
    // ---------- building blocks for designs ----------
    // pixel picture at the top of the tub (keeps the album going); leaves the far-wall lane free for a drifting gold
    static float Pic(LevelDef d, string id, float cell = 17f, float top = 215f)
    {
        var rows = ArtRows(id);
        d.blocks.AddRange(PixelArt(rows, 270, top + rows.Length * cell / 2f, cell)); d.art = id;
        return top + rows.Length * cell;
    }
    // stone ring (5x5 cells, step 40) around a 3x3 core with a bath bomb in the middle; one cell of the ring is open
    static void Fort(LevelDef d, float cx, float cy, string gap, string shell = "vase")
    {
        for (int r = -2; r <= 2; r++)
            for (int c = -2; c <= 2; c++)
            {
                float x = cx + c * 40, y = cy + r * 40;
                bool ring = Mathf.Abs(r) == 2 || Mathf.Abs(c) == 2;
                if (ring)
                {
                    bool open = (gap == "down" && r == 2 && c == 0) || (gap == "up" && r == -2 && c == 0)
                             || (gap == "left" && c == -2 && r == 0) || (gap == "right" && c == 2 && r == 0);
                    if (!open) d.blocks.Add(B("stone", x, y));
                }
                else d.blocks.Add(B(r == 0 && c == 0 ? "bathbomb" : ((r + c) % 2 == 0 ? shell : "glass"), x, y));
            }
    }
    static void StoneRow(LevelDef d, float x0, float x1, float y) { for (float x = x0; x <= x1 + 1; x += 40) d.blocks.Add(B("stone", x, y)); }
    static void StoneCol(LevelDef d, float x, float y0, float y1) { for (float y = y0; y <= y1 + 1; y += 40) d.blocks.Add(B("stone", x, y)); }
    static void Fill(LevelDef d, string t, float x0, float x1, float y0, float y1, float s = 40)
    { for (float y = y0; y <= y1 + 1; y += s) for (float x = x0; x <= x1 + 1; x += s) d.blocks.Add(B(t, x, y)); }
    static void Mix(LevelDef d, float x0, float x1, float y0, float y1, params string[] kinds)
    { int i = 0; for (float y = y0; y <= y1 + 1; y += 40) for (float x = x0; x <= x1 + 1; x += 40) d.blocks.Add(B(kinds[i++ % kinds.Length], x, y)); }
    static void Snake(LevelDef d, int chain, params Vector2[] pts)
    {   // dominoes every 22 px along a polyline: one knock anywhere topples the whole line
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var a = pts[i]; var b = pts[i + 1]; float len = Vector2.Distance(a, b); int n = Mathf.Max(1, Mathf.RoundToInt(len / 22f));
            bool vert = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y);   // tiles stand across the direction of travel
            for (int k = (i == 0 ? 0 : 1); k <= n; k++) { var p = Vector2.Lerp(a, b, k / (float)n); d.blocks.Add(Dom(p.x, p.y, vert, chain)); }
        }
    }
    static void Ring(LevelDef d, float x, float y) => d.blocks.Add(B("ring", x, y));
    static void Whirl(LevelDef d, float x, float y, float r) => d.whirls.Add(new Vector3(x, y, r));
    static LevelDef Ducks(LevelDef d, params string[] kinds)
    {
        var spots = kinds.Length <= 3 ? new[] { (150f, 805f), (270f, 805f), (390f, 805f) }
                                      : new[] { (120f, 805f), (220f, 805f), (320f, 805f), (420f, 805f) };
        for (int i = 0; i < kinds.Length; i++) d.ducks.Add(new DuckDef(spots[i].Item1, spots[i].Item2, kinds[i]));
        return d;
    }

    static LevelDef Puzzle(int n)
    {
        var d = new LevelDef();
        switch (n)
        {
            case 14:   // two little fortresses: aim through the gap, the core bomb clears the inside
                d.moves = 7; d.hint = "Bắn lọt khe pháo đài → bom bên trong nổ!";
                Pic(d, "flower", 15);
                Fort(d, 150, 485, "down"); Fort(d, 390, 485, "down");
                Fill(d, "soap", 110, 430, 625, 625);
                return Ducks(d, "normal", "heavy", "normal");
            case 15:   // HARD maze: stone bars with alternating gaps, a laser in every glass row
                d.moves = 12; d.hint = "MÀN KHÓ! Bắn trúng laser để xoá cả hàng";
                Fill(d, "plates", 90, 450, 215, 215, 60);
                for (int k = 0; k < 4; k++)
                {
                    float y = 280 + k * 90; bool leftGap = k % 2 == 0;
                    StoneRow(d, leftGap ? 190 : 70, leftGap ? 470 : 350, y);
                    float gy = y + 45;
                    for (int i = 0; i < 11; i++) d.blocks.Add(i == (leftGap ? 10 : 0) ? LZ(70 + i * 40, gy, "h") : B(i % 3 == 0 ? "vase" : "glass", 70 + i * 40, gy));
                }
                d.bumpers.Add(BumperL(660, 120)); d.bumpers.Add(BumperR(660, 120));
                return Ducks(d, "heavy", "normal", "bomb");
            case 16:   // domino snake winding through the pile, a bath bomb at the far end
                d.moves = 5; d.hint = "Chạm 1 quân domino: cả con rắn đổ!";
                Pic(d, "mush", 16);
                Snake(d, 1, new Vector2(70, 640), new Vector2(470, 640), new Vector2(470, 540), new Vector2(70, 540), new Vector2(70, 440), new Vector2(430, 440));
                d.blocks.Add(B("bathbomb", 470, 440));
                Mix(d, 130, 410, 590, 590, "vase", "glass", "soap");
                Mix(d, 130, 410, 490, 490, "glass", "plates", "glass");
                d.blocks.Add(B("chest", 270, 395));
                return Ducks(d, "normal", "normal", "heavy");
            case 17:   // float rings sit under two glass columns: knock them up the lanes
                d.moves = 6; d.hint = "Phao: bắn trúng thì trượt, húc vỡ cả cột!";
                Pic(d, "sun", 16);
                for (int k = 0; k < 2; k++) { float x = k == 0 ? 110 : 430; Fill(d, "glass", x, x, 400, 620); Ring(d, x, 690); }
                StoneRow(d, 190, 350, 600);
                Mix(d, 190, 350, 440, 520, "plates", "glass", "glass");
                return Ducks(d, "normal", "heavy", "normal");
            case 18:   // three stacked shelves: break the legs, the whole shelf crashes
                d.moves = 10; d.hint = "Gãy 2 chân kệ → cả kệ đổ!";
                Pic(d, "apple", 15);
                d.blocks.AddRange(Shelf(270, 400, 400, "vase", "glass", "vase", "glass", "vase", "glass"));
                d.blocks.AddRange(Shelf(270, 500, 440, "glass", "vase", "glass", "vase", "glass", "vase", "glass"));
                d.blocks.AddRange(Shelf(270, 600, 360, "soap", "vase", "soap", "vase", "soap"));
                d.blocks.Add(B("stone", 90, 650)); d.blocks.Add(B("stone", 450, 650));   // the bottom shelf's legs are shielded from straight shots
                return Ducks(d, "heavy", "normal", "zap");
            case 19:   // a wall straight ahead; the whirlpool on the left curls shots round it
                d.moves = 7; d.hint = "Xoáy nước: bẻ cong đường bay!";
                Pic(d, "boat", 15);
                StoneRow(d, 150, 430, 610);
                Mix(d, 150, 430, 430, 560, "soap", "glass", "vase", "glass");
                Whirl(d, 95, 620, 66);
                return Ducks(d, "normal", "normal", "heavy");
            case 20:   // HARD: two treasure chests locked in a stone cage - open it for more ducks
                d.moves = 9; d.hint = "MÀN KHÓ! Phá rương trong lồng để có thêm vịt";
                StoneRow(d, 150, 390, 330); StoneRow(d, 150, 390, 490);
                StoneCol(d, 150, 370, 450); StoneCol(d, 390, 370, 410);   // right wall has a gap at y 450
                d.blocks.Add(B("chest", 230, 410)); d.blocks.Add(B("chest", 310, 410)); d.blocks.Add(B("bathbomb", 270, 450));
                Mix(d, 70, 470, 230, 270, "vase", "glass", "plates");
                Mix(d, 70, 430, 560, 600, "glass", "soap", "vase");
                d.blocks.Add(LZ(70, 410, "v")); d.blocks.Add(LZ(470, 600, "h"));
                return Ducks(d, "heavy", "normal");
            case 21:   // laser grid: one laser knocks out a row, which sets off the next laser...
                d.moves = 5; d.hint = "Laser nối laser: tìm điểm mở màn!";
                Pic(d, "cat", 14);
                Fill(d, "soap", 110, 430, 420, 580);
                foreach (var b in d.blocks.ToArray())
                {
                    if (b.type != "soap") continue;
                    bool hz = (b.x == 110 && (b.y == 460 || b.y == 540)), vt = (b.y == 420 && (b.x == 190 || b.x == 350)) || (b.y == 580 && b.x == 270);
                    if (hz) { b.type = "laser"; b.dir = "h"; } else if (vt) { b.type = "laser"; b.dir = "v"; }
                }
                StoneRow(d, 110, 230, 640); StoneRow(d, 310, 430, 640);   // only the middle lane reaches the grid head-on
                return Ducks(d, "normal", "normal", "normal");
            case 22:   // two bath-bomb chains: each bomb is just close enough to set off the next one
                d.moves = 5; d.hint = "Bom tắm nổ lan: châm đúng quả đầu dây!";
                Pic(d, "rainbow", 16);
                foreach (var p in new[] { new Vector2(80, 620), new Vector2(150, 545), new Vector2(80, 470), new Vector2(150, 395) }) { d.blocks.Add(B("bathbomb", p.x, p.y)); }
                foreach (var p in new[] { new Vector2(460, 620), new Vector2(390, 545), new Vector2(460, 470), new Vector2(390, 395) }) { d.blocks.Add(B("bathbomb", p.x, p.y)); }
                Mix(d, 110, 190, 620, 620, "glass", "vase", "glass"); Mix(d, 350, 430, 620, 620, "glass", "vase", "glass");
                Mix(d, 230, 310, 400, 600, "plates", "glass", "soap");
                StoneCol(d, 230, 640, 640); StoneCol(d, 310, 640, 640);
                return Ducks(d, "normal", "bomb", "normal");
            case 23:   // a wall of ice: iron ducks crack it in one blow
                d.moves = 6; d.hint = "Đồ đóng băng: cú đầu vỡ băng, Vịt Sắt phá 1 cú!";
                Pic(d, "heart", 16);
                Fill(d, "ice", 70, 470, 560, 600);
                Mix(d, 110, 430, 430, 510, "glass", "soap", "vase", "glass");
                Ring(d, 270, 680);
                return Ducks(d, "heavy", "normal", "heavy");
            case 24:   // S-bend: two whirlpools steer shots round a central wall
                d.moves = 6; d.hint = "Hai xoáy nước: bắn vòng qua tường";
                Pic(d, "duck", 14);
                StoneRow(d, 190, 350, 590); StoneCol(d, 270, 470, 550);
                Mix(d, 110, 230, 440, 540, "vase", "glass", "soap"); Mix(d, 310, 430, 440, 540, "soap", "glass", "vase");
                Whirl(d, 110, 640, 60); Whirl(d, 430, 640, 60);
                return Ducks(d, "normal", "heavy", "normal");
            case 25:   // HARD: the fort's only door faces a whirlpool - aim off-centre and let it curl you in
                d.moves = 9; d.hint = "MÀN KHÓ! Xoáy trước cửa: bắn lệch để bị cuốn vào khe";
                Fort(d, 270, 420, "down", "plates");
                Mix(d, 70, 150, 300, 540, "vase", "glass"); Mix(d, 390, 470, 300, 540, "glass", "vase");
                Whirl(d, 270, 610, 62);
                Ring(d, 100, 690); Ring(d, 440, 690);
                return Ducks(d, "heavy", "normal", "bomb");
            case 26:   // billiards: a stone spine splits the tub, side bumpers bank shots round it
                d.moves = 7; d.hint = "Nảy băng vào thành để vòng qua vách đá";
                Pic(d, "star", 15);
                StoneCol(d, 270, 410, 650);
                Mix(d, 90, 230, 410, 570, "vase", "glass", "soap"); Mix(d, 310, 450, 410, 570, "plates", "glass", "glass");
                d.bumpers.Add(BumperL(430, 200)); d.bumpers.Add(BumperR(430, 200));
                return Ducks(d, "normal", "normal", "heavy");
            case 27:   // staircase: each step ends in a bath bomb that reaches the next step
                d.moves = 7; d.hint = "Bậc thang bom: nổ từ dưới lan lên trên";
                for (int s = 0; s < 5; s++)
                {
                    float y = 640 - s * 85, x0 = 70 + s * 50;
                    d.blocks.Add(B("stone", x0, y + 40)); d.blocks.Add(B("stone", x0 + 40, y + 40));
                    Mix(d, x0, x0 + 80, y, y, s % 2 == 0 ? "vase" : "glass", "glass");
                    d.blocks.Add(B("bathbomb", x0 + 120, y));
                }
                Mix(d, 430, 470, 220, 260, "plates", "glass"); d.blocks.Add(B("vase", 110, 260));
                return Ducks(d, "heavy", "normal", "normal");
            case 28:   // frozen river: ice lanes with float rings waiting at the bottom
                d.moves = 7; d.hint = "Đẩy phao trượt qua dòng băng";
                Pic(d, "fish", 15);
                Fill(d, "ice", 110, 110, 420, 620); Fill(d, "ice", 430, 430, 420, 620);
                Ring(d, 110, 690); Ring(d, 430, 690);
                Mix(d, 190, 350, 440, 600, "glass", "vase", "soap", "glass");
                StoneRow(d, 190, 350, 650);
                return Ducks(d, "normal", "heavy", "zap");
            case 29:   // everything at once: domino run into a laser, a fort, a whirlpool
                d.moves = 7; d.hint = "Domino → laser → bom: phát nào mở màn?";
                Pic(d, "straw", 14);
                Snake(d, 2, new Vector2(70, 640), new Vector2(250, 640));
                d.blocks.Add(LZ(272, 640, "v"));
                Fort(d, 390, 480, "left");
                Mix(d, 70, 190, 420, 560, "glass", "vase", "soap");
                Whirl(d, 430, 650, 52);
                return Ducks(d, "normal", "bomb", "heavy");
            default:   // 30 - SUPER HARD finale: three forts, each door facing a different way
                d.moves = 13; d.hint = "SIÊU KHÓ! 3 pháo đài, 3 cửa khác hướng";
                Fort(d, 130, 330, "right"); Fort(d, 410, 330, "left"); Fort(d, 270, 520, "down", "plates");
                Mix(d, 230, 310, 215, 255, "vase", "glass");
                for (int i = 0; i < 11; i++) d.blocks.Add(B(i == 0 || i == 10 ? "stone" : i % 4 == 2 ? "bathbomb" : "glass", 70 + i * 40, 650));
                Ring(d, 90, 720); Ring(d, 450, 720);
                return Ducks(d, "heavy", "bomb", "normal", "normal");
        }
    }
}
