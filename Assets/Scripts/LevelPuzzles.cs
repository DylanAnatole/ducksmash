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
                    bool open = (gap == "down" && r == 2 && (c == 0 || c == 1)) || (gap == "up" && r == -2 && (c == 0 || c == 1))
                             || (gap == "left" && c == -2 && (r == 0 || r == 1)) || (gap == "right" && c == 2 && (r == 0 || r == 1));
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
    static void Duck(LevelDef d, float x, float y, string k = "normal") => d.ducks.Add(new DuckDef(x, y, k));
    // rubber pad (bouncy bumper) given by its centre and size
    static void Pad(LevelDef d, float cx, float cy, float w, float h) => d.bumpers.Add(new BumperDef { x = cx - w / 2, y = cy - h / 2, w = w, h = h });
    static void Ring(LevelDef d, float x, float y) => d.blocks.Add(B("ring", x, y));
    // "bait" ducks parked out on the water: shoot a duck into them and they fly off and smash too (billiards)
    static void Bait(LevelDef d, params float[] xy) { for (int i = 0; i + 1 < xy.Length; i += 2) d.ducks.Add(new DuckDef(xy[i], xy[i + 1], "normal")); }
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
                Fill(d, "soap", 110, 430, 680, 680); Bait(d, 165, 620, 405, 620);   // push a bait duck up through the door
                return Ducks(d, "normal", "heavy", "normal");
            case 15:   // HARD maze: stone bars with alternating gaps, a laser in every glass row
                d.moves = 13; d.hint = "MÀN KHÓ! Bắn trúng laser để xoá cả hàng";
                Fill(d, "plates", 90, 450, 215, 215, 60);
                for (int k = 0; k < 4; k++)
                {
                    float y = 280 + k * 90; bool leftGap = k % 2 == 0;
                    StoneRow(d, leftGap ? 190 : 70, leftGap ? 470 : 350, y);
                    float gy = y + 45;
                    for (int i = 0; i < 11; i++) d.blocks.Add(i == (leftGap ? 10 : 0) ? LZ(70 + i * 40, gy, "h") : B(i % 3 == 0 ? "vase" : "glass", 70 + i * 40, gy));
                }
                d.bumpers.Add(BumperL(660, 120)); d.bumpers.Add(BumperR(660, 120));
                Bait(d, 230, 690, 270, 655, 310, 690);   // a little rack to break
                return Ducks(d, "heavy", "normal", "bomb");
            case 16:   // domino snake winding through the pile, a bath bomb at the far end
                d.moves = 5; d.hint = "Chạm 1 quân domino: cả con rắn đổ!";
                Pic(d, "mush", 16);
                Snake(d, 1, new Vector2(70, 640), new Vector2(470, 640), new Vector2(470, 540), new Vector2(70, 540), new Vector2(70, 440), new Vector2(430, 440));
                d.blocks.Add(B("bathbomb", 470, 440));
                Mix(d, 130, 410, 590, 590, "vase", "glass", "soap");
                Mix(d, 130, 410, 490, 490, "glass", "plates", "glass");
                d.blocks.Add(B("chest", 270, 395));
                Bait(d, 200, 700, 340, 700);
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
                d.blocks.Add(B("stone", 90, 650)); d.blocks.Add(B("stone", 450, 650));
                Bait(d, 200, 690, 340, 690);   // the bottom shelf's legs are shielded from straight shots
                return Ducks(d, "heavy", "normal", "zap");
            case 19:   // a wall straight ahead; the whirlpool on the left curls shots round it
                d.moves = 7; d.hint = "Xoáy nước: bẻ cong đường bay!";
                Pic(d, "boat", 15);
                StoneRow(d, 150, 430, 610);
                Mix(d, 150, 430, 430, 560, "soap", "glass", "vase", "glass");
                Whirl(d, 95, 620, 66);
                Bait(d, 300, 690, 360, 690);
                return Ducks(d, "normal", "normal", "heavy");
            case 20:   // HARD: the ducks start in the MIDDLE; the mess is all around the edges - shoot every direction
                d.moves = 10; d.hint = "MÀN KHÓ! Vịt ở giữa: bắn tỏa ra 4 phía";
                Mix(d, 70, 470, 200, 240, "vase", "glass", "soap");
                Mix(d, 70, 470, 780, 820, "glass", "soap", "vase");
                Mix(d, 70, 110, 300, 740, "glass", "vase", "soap", "glass"); Mix(d, 430, 470, 300, 740, "soap", "glass", "vase", "glass");
                d.blocks.Add(B("chest", 190, 300)); d.blocks.Add(B("chest", 350, 720));
                d.blocks.Add(B("bathbomb", 190, 740)); d.blocks.Add(B("bathbomb", 350, 300));
                d.blocks.Add(B("stone", 190, 520)); d.blocks.Add(B("stone", 350, 520));
                Duck(d, 230, 470, "heavy"); Duck(d, 310, 470); Duck(d, 270, 560, "bomb");
                return d;
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
                Bait(d, 270, 690);   // knock it up the lane into the laser
                return Ducks(d, "normal", "normal", "normal");
            case 22:   // two bath-bomb chains: each bomb is just close enough to set off the next one
                d.moves = 5; d.hint = "Bom tắm nổ lan: châm đúng quả đầu dây!";
                Pic(d, "rainbow", 16);
                foreach (var p in new[] { new Vector2(80, 620), new Vector2(150, 545), new Vector2(80, 470), new Vector2(150, 395) }) { d.blocks.Add(B("bathbomb", p.x, p.y)); }
                foreach (var p in new[] { new Vector2(460, 620), new Vector2(390, 545), new Vector2(460, 470), new Vector2(390, 395) }) { d.blocks.Add(B("bathbomb", p.x, p.y)); }
                Mix(d, 110, 190, 620, 620, "glass", "vase", "glass"); Mix(d, 350, 430, 620, 620, "glass", "vase", "glass");
                Mix(d, 230, 310, 400, 600, "plates", "glass", "soap");
                StoneCol(d, 230, 640, 640); StoneCol(d, 310, 640, 640);
                Bait(d, 270, 690, 150, 690, 390, 690);
                return Ducks(d, "normal", "bomb", "normal");
            case 23:   // a wall of ice: iron ducks crack it in one blow
                d.moves = 6; d.hint = "Đồ đóng băng: cú đầu vỡ băng, Vịt Sắt phá 1 cú!";
                Pic(d, "heart", 16);
                Fill(d, "ice", 70, 470, 560, 600);
                Mix(d, 110, 430, 430, 510, "glass", "soap", "vase", "glass");
                Ring(d, 270, 680);
                Bait(d, 170, 680, 370, 680);
                return Ducks(d, "heavy", "normal", "heavy");
            case 24:   // the ducks sit at the TOP; everything is below them - shoot down into the bath
                d.moves = 7; d.hint = "Vịt ở trên cao: kéo lên, bắn xuống!";
                Fort(d, 270, 600, "up");
                Mix(d, 70, 150, 420, 780, "glass", "vase", "soap"); Mix(d, 390, 470, 420, 780, "soap", "glass", "vase");
                Fill(d, "soap", 190, 350, 790, 790);
                Duck(d, 150, 260); Duck(d, 270, 240, "heavy"); Duck(d, 390, 260);
                return d;
            case 25:   // HARD: the fort's only door faces a whirlpool - aim off-centre and let it curl you in
                d.moves = 9; d.hint = "MÀN KHÓ! Xoáy trước cửa: bắn lệch để bị cuốn vào khe";
                Fort(d, 270, 420, "down", "plates");
                Mix(d, 70, 150, 300, 540, "vase", "glass"); Mix(d, 390, 470, 300, 540, "glass", "vase");
                Whirl(d, 270, 610, 62);
                Ring(d, 100, 690); Ring(d, 440, 690);
                Bait(d, 200, 700, 340, 700);
                return Ducks(d, "heavy", "normal", "bomb");
            case 26:   // ducks on BOTH SIDE WALLS, a tall pile in the middle, rubber pads top and bottom for bank shots
                d.moves = 8; d.hint = "Vịt ở hai bên: bắn ngang, nảy đệm cao su";
                Pic(d, "star", 13, 200f);
                Mix(d, 190, 350, 380, 740, "vase", "glass", "soap", "glass", "plates");
                d.blocks.Add(B("stone", 270, 460)); d.blocks.Add(B("stone", 270, 660));
                d.blocks.Add(B("bathbomb", 190, 560)); d.blocks.Add(B("bathbomb", 350, 560));
                Pad(d, 270, 820, 200, 14);
                Duck(d, 70, 440); Duck(d, 70, 660, "heavy"); Duck(d, 470, 440, "heavy"); Duck(d, 470, 660);
                return d;
            case 27:   // staircase: each step ends in a bath bomb that reaches the next step
                d.moves = 9; d.hint = "Bậc thang bom: nổ từ dưới lan lên trên";
                for (int s = 0; s < 5; s++)
                {
                    float y = 640 - s * 85, x0 = 70 + s * 50;
                    d.blocks.Add(B("stone", x0, y + 40)); d.blocks.Add(B("stone", x0 + 40, y + 40));
                    Mix(d, x0, x0 + 80, y, y, s % 2 == 0 ? "vase" : "glass", "glass");
                    d.blocks.Add(B("bathbomb", x0 + 120, y));
                }
                Mix(d, 430, 470, 220, 260, "plates", "glass"); d.blocks.Add(B("vase", 110, 260));
                Bait(d, 330, 690, 390, 690, 450, 690);
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
                Bait(d, 170, 700);
                return Ducks(d, "normal", "bomb", "heavy");
            default:   // 30 - SUPER HARD: the ducks are boxed in by rubber pads; get out through the corners or bounce out
                d.moves = 14; d.hint = "SIÊU KHÓ! Vịt trong lồng cao su: bắn chéo qua góc";
                Pad(d, 270, 405, 100, 14); Pad(d, 270, 615, 100, 14); Pad(d, 175, 510, 14, 100); Pad(d, 365, 510, 14, 100);
                Mix(d, 70, 470, 200, 280, "glass", "vase", "soap");
                Mix(d, 70, 470, 740, 820, "soap", "glass", "vase");
                Mix(d, 70, 110, 340, 680, "glass", "soap", "vase"); Mix(d, 430, 470, 340, 680, "vase", "glass", "soap");
                d.blocks.Add(B("bathbomb", 270, 240)); d.blocks.Add(B("bathbomb", 270, 780));
                d.blocks.Add(LZ(90, 510, "v")); d.blocks.Add(LZ(450, 510, "v"));
                Duck(d, 235, 510, "heavy"); Duck(d, 305, 510, "bomb"); Duck(d, 270, 460); Duck(d, 270, 560);
                return d;
        }
    }
}
