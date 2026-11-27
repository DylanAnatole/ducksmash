using System;
using UnityEngine;
using UnityEngine.UI;

// Tiny runtime uGUI builder: rounded panels, labels, buttons. No sprite assets needed.
public static class UIKit
{
    static Font font;
    static Sprite round, circle, star;
    static bool customFont;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { font = null; round = circle = star = null; customFont = false; skins.Clear(); }

    // ---------------- skin: glossy sprites from the studio UI kit (Resources/UIKit, used with permission) ----------------
    static readonly System.Collections.Generic.Dictionary<string, Sprite> skins = new System.Collections.Generic.Dictionary<string, Sprite>();
    public static Sprite Skin(string name)
    {
        if (skins.TryGetValue(name, out var sp) && sp != null) return sp;
        sp = Resources.Load<Sprite>("UIKit/" + name); skins[name] = sp; return sp;
    }
    // pick the button sprite closest to the wanted colour (the kit has green / yellow / red / purple / pink / blue / white)
    public static string BtnSkinName(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v); h *= 360f;
        if (s < 0.22f) return "btn_white";
        if (h < 18 || h >= 345) return "btn_red";
        if (h < 65) return "btn_yellow";
        if (h < 165) return "btn_green";
        if (h < 245) return "btn_blue";
        if (h < 290) return "btn_purple";
        return "btn_pink";
    }
    // apply a skin to an image; white/grey buttons keep a tint so a "disabled" grey still reads
    public static void SkinButton(Image img, Color c)
    {
        var name = BtnSkinName(c); var sp = Skin(name);
        if (sp == null) { img.color = c; return; }
        img.sprite = sp; img.type = Image.Type.Sliced;
        Color.RGBToHSV(c, out _, out float s, out float v);
        img.color = name == "btn_white" ? new Color(Mathf.Min(1, c.r + 0.05f), Mathf.Min(1, c.g + 0.05f), Mathf.Min(1, c.b + 0.05f), c.a) : Color.white;
        img.pixelsPerUnitMultiplier = sp.rect.height / 110f;   // keep the rounded caps at button size
    }
    // a sliced skin panel (popup cards, rows, bars)
    public static Image SkinPanel(Transform parent, string skin, string name, float ppuMul = 1f)
    {
        var img = Panel(parent, Color.white, name); var sp = Skin(skin);
        if (sp != null) { img.sprite = sp; img.type = sp.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple; img.pixelsPerUnitMultiplier = ppuMul; }
        return img;
    }

    // Noto Sans Display Bold (OFL) has full Vietnamese coverage; WebGL has no OS font fallback.
    public static Font Font
    {
        get
        {
            if (font != null) return font;
            font = Resources.Load<Font>("Fonts/NotoSansDisplay-Bold");
            customFont = font != null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    public static Sprite Star
    {
        get
        {
            if (star != null) return star;
            var kit = Skin("star_on"); if (kit != null) return star = kit;
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    var v = new Vector2(x + 0.5f - s / 2f, y + 0.5f - s / 2f) / (s / 2f);
                    float a = Mathf.Atan2(v.y, v.x) - Mathf.PI / 2, r = v.magnitude;
                    // 5-point star: radius alternates between outer (0.95) and inner (0.42)
                    float seg = Mathf.Repeat(a, 2 * Mathf.PI / 5) / (2 * Mathf.PI / 5);
                    float edge = Mathf.Lerp(0.42f, 0.95f, Mathf.Abs(seg * 2 - 1));
                    px[y * s + x] = new Color(1, 1, 1, Mathf.Clamp01((edge - r) * s * 0.5f));
                }
            tex.SetPixels(px); tex.Apply();
            star = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            return star;
        }
    }

    // earned star = gold kit art, missing star = grey kit art (no tinting: the art has its own shading)
    public static void SetStar(Image img, bool on, Color fallbackOn, Color fallbackOff, float offAlpha = 1f)
    {
        if (Skin("star_on") == null) { img.color = on ? fallbackOn : fallbackOff; return; }
        img.sprite = Skin(on ? "star_on" : "star_off"); img.color = new Color(1, 1, 1, on ? 1f : offAlpha);
    }
    // A row of star images; returns them so callers can recolor.
    public static Image[] StarRow(Transform parent, int count, float size, float gap)
    {
        var imgs = new Image[count];
        for (int i = 0; i < count; i++)
        {
            var rt = Rect("Star" + i, parent);
            Place(rt, 0.5f, 0.5f, (i - (count - 1) / 2f) * (size + gap), 0, size, size);
            imgs[i] = rt.gameObject.AddComponent<Image>(); imgs[i].sprite = Star; imgs[i].raycastTarget = false; imgs[i].preserveAspect = true;
            if (Skin("star_on") == null) { var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.23f, 0.18f, 0.42f, 0.8f); o.effectDistance = new Vector2(3, -3); }
        }
        return imgs;
    }

    public static Sprite Round
    {
        get
        {
            if (round != null) return round;
            const int s = 64; const float rad = 24f;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x + 0.5f - s / 2f) - (s / 2f - rad));
                    float dy = Mathf.Max(0, Mathf.Abs(y + 0.5f - s / 2f) - (s / 2f - rad));
                    float a = Mathf.Clamp01(rad - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * s + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels(px); tex.Apply();
            round = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(26, 26, 26, 26));
            return round;
        }
    }

    public static Sprite Circle
    {
        get
        {
            if (circle != null) return circle;
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f));
                    px[y * s + x] = new Color(1, 1, 1, Mathf.Clamp01(s / 2f - d));
                }
            tex.SetPixels(px); tex.Apply();
            circle = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            return circle;
        }
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // Anchor at (ax, ay) of the parent, positioned/sized in reference pixels.
    public static RectTransform Place(RectTransform rt, float ax, float ay, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    public static RectTransform Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        return rt;
    }

    // soft glossy band over the top half of a panel/button (reads as a rounded, toy-like surface)
    public static void Gloss(Transform target, float alpha = 0.18f)
    {
        var g = Panel(target, new Color(1f, 1f, 1f, alpha), "Gloss");
        g.raycastTarget = false; g.transform.SetSiblingIndex(0);   // behind labels and icons, above the panel itself
        var rt = g.rectTransform; rt.anchorMin = new Vector2(0, 0.55f); rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(8, 0); rt.offsetMax = new Vector2(-8, -5);
    }

    // tiny pixel-art icon from rows of palette characters
    public static Texture2D PixelIcon(string[] rows, System.Collections.Generic.Dictionary<char, Color> pal)
    {
        int h = rows.Length, w = 0; foreach (var r in rows) w = Mathf.Max(w, r.Length);
        var src = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                src[(h - 1 - y) * w + x] = x < rows[y].Length && pal.TryGetValue(rows[y][x], out var c) ? c : new Color(0, 0, 0, 0);
        return SmoothPixels(src, w, h);
    }

    // Turns a tiny pixel grid into a smooth, rounded, anti-aliased picture (no blocky "retro" stair-steps):
    // every colour region gets a bilinear mask, each output pixel takes the strongest region, and the
    // margin to the runner-up gives a 1-pixel soft edge.
    public static Texture2D SmoothPixels(Color[] src, int w, int h, int scale = 8)
    {
        var cols = new System.Collections.Generic.List<Color> { new Color(0, 0, 0, 0) };
        var idx = new int[w * h];
        for (int i = 0; i < src.Length; i++)
        {
            var c = src[i]; if (c.a <= 0.01f) { idx[i] = 0; continue; }
            int k = cols.IndexOf(c); if (k < 0) { k = cols.Count; cols.Add(c); }
            idx[i] = k;
        }
        int W = w * scale, H = h * scale; var outPx = new Color[W * H]; var m = new float[cols.Count];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float sx = (x + 0.5f) / scale - 0.5f, sy = (y + 0.5f) / scale - 0.5f;
                int x0 = Mathf.FloorToInt(sx), y0 = Mathf.FloorToInt(sy); float fx = sx - x0, fy = sy - y0;
                System.Array.Clear(m, 0, m.Length);
                float tw = 0;
                for (int j = -1; j < 3; j++)
                    for (int i = -1; i < 3; i++)
                    {   // round kernel (radius 1.5 px): shapes come out as soft blobs, diagonals lose their stair-steps
                        int xx = x0 + i, yy = y0 + j;
                        float dx = xx - sx, dy = yy - sy, q = 1f - Mathf.Sqrt(dx * dx + dy * dy) / 1.5f;
                        if (q <= 0) continue; q *= q;
                        int k = xx < 0 || yy < 0 || xx >= w || yy >= h ? 0 : idx[yy * w + xx];
                        m[k] += q; tw += q;
                    }
                if (tw > 0) for (int k = 0; k < m.Length; k++) m[k] /= tw;
                int b1 = 0, b2 = -1;
                for (int k = 1; k < m.Length; k++) if (m[k] > m[b1]) b1 = k;
                for (int k = 0; k < m.Length; k++) if (k != b1 && (b2 < 0 || m[k] > m[b2])) b2 = k;
                Color c1 = cols[b1], c2 = b2 >= 0 ? cols[b2] : c1;
                float e = b2 >= 0 ? Mathf.Clamp01((m[b1] - m[b2]) * scale * 0.35f + 0.5f) : 1f;
                float a = Mathf.Lerp(c2.a, c1.a, e);
                Color rgb = a > 0.001f ? (c1 * (c1.a * e) + c2 * (c2.a * (1 - e))) / a : c1;
                rgb.a = a; outPx[y * W + x] = rgb;
            }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(outPx); tex.Apply(true, true); return tex;
    }

    public static Image Panel(Transform parent, Color c, string name = "Panel", bool rounded = true)
    {
        var rt = Rect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = rounded ? Round : null;
        img.type = rounded ? Image.Type.Sliced : Image.Type.Simple;
        img.color = c;
        return img;
    }

    public static Image Dot(Transform parent, Color c, string name = "Dot")
    {
        var rt = Rect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Circle; img.color = c;
        return img;
    }

    public static Text Label(Transform parent, string s, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = true)
    {
        var rt = Rect("Text", parent);
        Stretch(rt);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Font; t.text = s; t.fontSize = size; t.color = c; t.alignment = anchor;
        t.fontStyle = bold && !customFont ? FontStyle.Bold : FontStyle.Normal;   // bundled font is already bold
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    public static Text Outlined(Text t, Color outline, float dist = 3f)
    {
        // Outline copies the text mesh 4x; on phones a single drop shadow (1 copy) reads almost as well
        if (App.Mobile) { var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = outline; sh.effectDistance = new Vector2(dist * 0.8f, -dist * 1.4f); return t; }
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = outline; o.effectDistance = new Vector2(dist, -dist);
        return t;
    }

    public static Button Btn(Transform parent, string label, Color bg, Color fg, int size, Action onClick, string name = "Button")
    {
        var img = Panel(parent, bg, name);
        if (Skin("btn_green") != null) SkinButton(img, bg);
        else
        {
            var sh = img.gameObject.AddComponent<Shadow>();
            var lip = Color.Lerp(bg, new Color(0.15f, 0.2f, 0.32f), 0.3f); lip.a = 1f;
            sh.effectColor = lip; sh.effectDistance = new Vector2(0, -6);
            Gloss(img.transform, 0.1f);
        }
        var b = img.gameObject.AddComponent<Button>();
        var cb = b.colors; cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); b.colors = cb;
        var lt = Label(img.transform, label, size, fg); lt.rectTransform.offsetMin = new Vector2(0, 6);   // the kit buttons have a bottom lip: centre text on the face
        if (fg.r > 0.9f && fg.g > 0.9f && fg.b > 0.9f) { var to = lt.gameObject.AddComponent<Outline>(); to.effectColor = new Color(0, 0, 0, 0.25f); to.effectDistance = new Vector2(2, -2); }
        b.onClick.AddListener(() => { Sfx.Play("click"); onClick?.Invoke(); });
        return b;
    }

    public static Text BtnText(Button b) => b.GetComponentInChildren<Text>();
}
