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
    // sliding rubber pad: moves +-amp px along x (or y) at `speed` rad/s
    static void MovingPad(LevelDef d, float cx, float cy, float w, float h, float amp, float speed = 1.6f, bool vertical = false)
        => d.bumpers.Add(new BumperDef { x = cx - w / 2, y = cy - h / 2, w = w, h = h, amp = amp, speed = speed, vertical = vertical });
    static void Ring(LevelDef d, float x, float y) => d.blocks.Add(B("ring", x, y));
    // "bait" ducks parked out on the water: shoot a duck into them and they fly off and smash too (billiards)
    static void Bait(LevelDef d, params float[] xy) { for (int i = 0; i + 1 < xy.Length; i += 2) d.ducks.Add(new DuckDef(xy[i], xy[i + 1], "normal")); }
    static void Whirl(LevelDef d, float x, float y, float r) => d.whirls.Add(new Vector3(x, y, r));
    // water current given by its centre + size; dir "left" / "right" / "up" / "down"
    static void Flow(LevelDef d, float cx, float cy, float w, float h, string dir)
    {
        float dx = dir == "left" ? -1 : dir == "right" ? 1 : 0, dy = dir == "up" ? -1 : dir == "down" ? 1 : 0;
        d.flows.Add(new FlowDef { x = cx - w / 2, y = cy - h / 2, w = w, h = h, dx = dx, dy = dy });
    }
    static void Pipe(LevelDef d, float ax, float ay, float bx, float by) => d.portals.Add(new Vector4(ax, ay, bx, by));
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
            case 14:   // the ducks stand BETWEEN two fortresses whose doors face them: shoot sideways into the doors
                d.moves = 7; d.hint = "Vịt đứng giữa 2 pháo đài: bắn ngang vào cửa!";
                Pic(d, "flower", 13);
                Fort(d, 130, 500, "right"); Fort(d, 410, 500, "left");
                Fill(d, "soap", 110, 430, 700, 700);
                Duck(d, 270, 440); Duck(d, 270, 540, "heavy"); Duck(d, 270, 800);
                return d;
            case 15:   // HARD: the ducks start at the TOP of a stone maze and have to find the gaps on the way down
                d.moves = 14; d.hint = "MÀN KHÓ! Vịt trên đỉnh mê cung: lách khe, bắn laser";
                for (int k = 0; k < 4; k++)
                {
                    float y = 310 + k * 90; bool leftGap = k % 2 == 0;
                    StoneRow(d, leftGap ? 190 : 70, leftGap ? 470 : 350, y);
                    float gy = y + 45;
                    for (int i = 0; i < 11; i++) d.blocks.Add(i == (leftGap ? 10 : 0) ? LZ(70 + i * 40, gy, "h") : B(i % 3 == 0 ? "vase" : "glass", 70 + i * 40, gy));
                }
                Fill(d, "soap", 110, 430, 720, 720);
                d.bumpers.Add(BumperL(680, 120)); d.bumpers.Add(BumperR(680, 120));
                Duck(d, 150, 225); Duck(d, 390, 225, "bomb"); Duck(d, 270, 810, "heavy");   // two on top of the maze, one below it
                return d;
            case 16:   // the ducks stand INSIDE a ring of dominoes: knock one tile and the whole ring falls
                d.moves = 6; d.hint = "Vịt trong vòng domino: chạm 1 quân, cả vòng đổ!";
                Pic(d, "mush", 13);
                Snake(d, 1, new Vector2(150, 640), new Vector2(390, 640), new Vector2(390, 410), new Vector2(150, 410), new Vector2(150, 590));
                Mix(d, 70, 70, 420, 620, "vase", "glass", "soap"); Mix(d, 470, 470, 420, 620, "glass", "soap", "vase");
                Mix(d, 110, 430, 730, 770, "glass", "vase", "soap", "plates");
                d.blocks.Add(B("bathbomb", 70, 740)); d.blocks.Add(B("bathbomb", 470, 740));
                Duck(d, 230, 500); Duck(d, 310, 500, "heavy"); Duck(d, 270, 570);
                return d;
            case 17:   // ducks up TOP, float rings sit on top of two glass columns: shoot down, the rings plough through
                d.moves = 8; d.hint = "Vịt trên cao: bắn phao trượt xuống, phá cả cột!";
                Duck(d, 150, 230); Duck(d, 270, 220, "heavy"); Duck(d, 390, 230);
                for (int k = 0; k < 2; k++) { float x = k == 0 ? 90 : 450; Ring(d, x, 320); Fill(d, "glass", x, x, 390, 630); }
                StoneRow(d, 230, 310, 330);
                Mix(d, 170, 370, 420, 580, "plates", "glass", "soap", "vase");
                Fill(d, "soap", 150, 390, 760, 760);
                Pad(d, 270, 845, 220, 14);   // bounce off the bottom pad back up into the pile
                Flow(d, 270, 690, 200, 70, "up");   // ...and the current lifts it back into the pile
                return d;
            case 18:   // three narrow shelves, ducks on BOTH SIDE WALLS level with the legs: snap them sideways
                d.moves = 10; d.hint = "Vịt hai bên: bắn ngang gãy chân kệ!";
                Pic(d, "apple", 14);
                d.blocks.AddRange(Shelf(270, 400, 300, "vase", "glass", "vase", "glass", "vase"));
                d.blocks.AddRange(Shelf(270, 510, 300, "glass", "vase", "glass", "vase", "glass"));
                d.blocks.AddRange(Shelf(270, 620, 300, "soap", "vase", "soap", "vase", "soap"));
                Fill(d, "soap", 150, 390, 760, 760);
                Duck(d, 62, 455, "heavy"); Duck(d, 478, 455); Duck(d, 62, 570, "zap"); Duck(d, 478, 570);
                return d;
            case 19:   // ducks on the LEFT behind a long wall; the whirlpool at its foot curls shots round to the right
                d.moves = 6; d.hint = "Cống nước: chui vào 1 đầu, trồi ra đầu kia!";
                Pic(d, "boat", 13);
                StoneCol(d, 210, 400, 640);
                Mix(d, 290, 470, 400, 640, "soap", "glass", "vase", "glass", "plates");
                Pipe(d, 110, 760, 380, 340);   // the pipe under the ducks comes out right above the pile
                Duck(d, 110, 440); Duck(d, 110, 530, "heavy"); Duck(d, 110, 620);
                return d;
            case 20:   // HARD: the ducks sit in the MIDDLE of a ring of currents that runs round the bath; the mess is all around the edges
                d.moves = 10; d.hint = "MÀN KHÓ! Dòng nước chạy vòng: thả vịt vào cho nước cuốn đi!";
                Flow(d, 270, 360, 300, 56, "right"); Flow(d, 420, 505, 56, 234, "down");
                Flow(d, 270, 650, 300, 56, "left"); Flow(d, 120, 505, 56, 234, "up");
                Mix(d, 70, 470, 200, 260, "vase", "glass", "soap");
                Mix(d, 70, 470, 740, 820, "glass", "soap", "vase");
                Mix(d, 70, 70, 320, 680, "glass", "vase", "soap"); Mix(d, 470, 470, 320, 680, "soap", "glass", "vase");
                d.blocks.Add(B("chest", 170, 300)); d.blocks.Add(B("chest", 370, 700));
                d.blocks.Add(B("bathbomb", 370, 300)); d.blocks.Add(B("bathbomb", 170, 700));
                Duck(d, 230, 490, "heavy"); Duck(d, 310, 490); Duck(d, 270, 570, "bomb");
                return d;
            case 21:   // a duck sits INSIDE the laser grid, two more wait on the side walls
                d.moves = 5; d.hint = "Vịt nằm giữa lưới laser: chạm laser, cả hàng nổ!";
                Pic(d, "cat", 14);
                Fill(d, "soap", 110, 430, 420, 580);
                d.blocks.RemoveAll(b => b.type == "soap" && b.x >= 229 && b.x <= 311 && b.y >= 459 && b.y <= 541);   // a 3x3 room in the middle
                foreach (var b in d.blocks.ToArray())
                {
                    if (b.type != "soap") continue;
                    bool hz = (b.x == 110 && (b.y == 460 || b.y == 540)), vt = (b.y == 420 && (b.x == 190 || b.x == 350)) || (b.y == 580 && b.x == 270);
                    if (hz) { b.type = "laser"; b.dir = "h"; } else if (vt) { b.type = "laser"; b.dir = "v"; }
                }
                Mix(d, 110, 430, 700, 700, "glass", "vase", "glass");
                Pipe(d, 70, 810, 470, 340);   // bottom-left pipe pops out beside the grid's top corner
                Duck(d, 270, 500, "heavy"); Duck(d, 60, 500); Duck(d, 480, 500);
                return d;
            case 22:   // the ducks stand in the MIDDLE between two bath-bomb chains: shoot sideways to light them
                d.moves = 5; d.hint = "Vịt giữa 2 dây bom: bắn ngang châm ngòi!";
                Pic(d, "rainbow", 14);
                foreach (var p in new[] { new Vector2(80, 650), new Vector2(150, 575), new Vector2(80, 500), new Vector2(150, 425) }) { d.blocks.Add(B("bathbomb", p.x, p.y)); }
                foreach (var p in new[] { new Vector2(460, 650), new Vector2(390, 575), new Vector2(460, 500), new Vector2(390, 425) }) { d.blocks.Add(B("bathbomb", p.x, p.y)); }
                Mix(d, 80, 150, 720, 760, "glass", "vase"); Mix(d, 390, 460, 720, 760, "vase", "glass");
                Mix(d, 230, 310, 760, 800, "plates", "glass", "soap");
                Duck(d, 270, 440); Duck(d, 270, 530, "bomb"); Duck(d, 270, 620);
                return d;
            case 23:   // a river runs across the middle: drop a duck in and the current carries it into the pile on the far side
                d.moves = 7; d.hint = "Dòng chảy: thả vịt vào, nước cuốn đi!";
                Pic(d, "heart", 14);
                Flow(d, 255, 520, 390, 76, "right");
                Flow(d, 285, 745, 390, 64, "left");
                Mix(d, 430, 470, 400, 640, "glass", "vase", "soap", "plates");
                Mix(d, 70, 110, 700, 790, "soap", "glass", "vase");
                Mix(d, 150, 350, 410, 410, "vase", "glass", "soap"); Mix(d, 150, 350, 630, 630, "soap", "glass", "vase");
                Duck(d, 90, 520, "heavy"); Duck(d, 90, 430); Duck(d, 270, 830);
                return d;
            case 24:   // the ducks sit at the TOP; everything is below them - shoot down into the bath
                d.moves = 8; d.hint = "Vịt trên cao: canh đệm trượt rồi bắn xuống cửa!";
                Fort(d, 270, 600, "up");
                MovingPad(d, 270, 390, 110, 14, 150, 1.2f);   // a sliding pad guards the door: wait for the gap
                Mix(d, 70, 150, 420, 780, "glass", "vase", "soap"); Mix(d, 390, 470, 420, 780, "soap", "glass", "vase");
                Fill(d, "soap", 190, 350, 790, 790);
                Duck(d, 150, 260); Duck(d, 270, 240, "heavy"); Duck(d, 390, 260);
                return d;
            case 25:   // HARD: the fort's door faces a whirlpool; ducks come at it from the side walls and from below
                d.moves = 9; d.hint = "MÀN KHÓ! Xoáy trước cửa: bắn lệch để bị cuốn vào khe";
                Fort(d, 270, 420, "down", "plates");
                Mix(d, 70, 150, 300, 540, "vase", "glass"); Mix(d, 390, 470, 300, 540, "glass", "vase");
                Whirl(d, 270, 610, 62);
                Ring(d, 130, 720); Ring(d, 410, 720);
                Duck(d, 62, 630, "heavy"); Duck(d, 478, 630); Duck(d, 270, 800, "bomb");
                return d;
            case 26:   // ducks on BOTH SIDE WALLS, a tall pile in the middle, rubber pads top and bottom for bank shots
                d.moves = 8; d.hint = "Vịt hai bên: canh đệm cao su trượt để bắn nảy!";
                Pic(d, "star", 13, 200f);
                Mix(d, 190, 350, 380, 740, "vase", "glass", "soap", "glass", "plates");
                d.blocks.Add(B("stone", 270, 460)); d.blocks.Add(B("stone", 270, 660));
                d.blocks.Add(B("bathbomb", 190, 560)); d.blocks.Add(B("bathbomb", 350, 560));
                MovingPad(d, 270, 820, 150, 14, 110, 1.4f);   // sliding pad: time the bank shot
                Duck(d, 70, 440); Duck(d, 70, 660, "heavy"); Duck(d, 470, 440, "heavy"); Duck(d, 470, 660);
                return d;
            case 27:   // staircase of bath bombs; the ducks are perched in the TOP-LEFT corner above the bottom step
                d.moves = 10; d.hint = "Vịt trên góc: châm bom bậc nào trước?";
                for (int s = 0; s < 5; s++)
                {
                    float y = 680 - s * 85, x0 = 70 + s * 50;
                    d.blocks.Add(B("stone", x0, y + 40)); d.blocks.Add(B("stone", x0 + 40, y + 40));
                    Mix(d, x0, x0 + 80, y, y, s % 2 == 0 ? "vase" : "glass", "glass");
                    d.blocks.Add(B("bathbomb", x0 + 120, y));
                }
                Mix(d, 430, 470, 220, 260, "plates", "glass");
                Pipe(d, 465, 822, 300, 230);   // bottom-right corner pipe pops out at the top step
                Duck(d, 80, 240); Duck(d, 160, 240, "heavy"); Duck(d, 80, 330);
                Bait(d, 370, 800);
                return d;
            case 28:   // ducks on the SIDE WALLS and at the bottom, two ice lanes across the middle
                d.moves = 8; d.hint = "Vịt khắp nơi: bắn ngang phá băng, đẩy phao lên";
                Mix(d, 110, 430, 220, 260, "glass", "vase", "soap", "glass");
                Ring(d, 190, 330); Ring(d, 350, 330);
                Fill(d, "ice", 150, 390, 410, 410); Fill(d, "ice", 150, 390, 630, 630);
                Mix(d, 150, 390, 470, 570, "glass", "vase", "soap", "plates");
                Pipe(d, 270, 700, 270, 330);   // straight up through the ice lanes
                Duck(d, 70, 520); Duck(d, 470, 520, "zap"); Duck(d, 270, 790, "heavy");
                return d;
            case 29:   // everything at once; two ducks stand in the alley between the pile and the fort
                d.moves = 7; d.hint = "Đủ cả: domino, laser, xoáy, cống nước. Phát nào mở màn?";
                Pic(d, "straw", 13);
                Snake(d, 2, new Vector2(70, 640), new Vector2(250, 640));
                d.blocks.Add(LZ(272, 640, "v"));
                Fort(d, 400, 480, "left");
                Mix(d, 70, 190, 420, 560, "glass", "vase", "soap");
                Whirl(d, 430, 650, 52);
                Pipe(d, 110, 790, 460, 330);   // bottom-left pipe pops out above the fort
                Duck(d, 255, 430); Duck(d, 255, 530, "bomb"); Duck(d, 270, 800, "heavy");
                return d;
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
