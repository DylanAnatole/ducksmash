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
    static void ResetStatics() { font = null; round = circle = star = null; customFont = false; }

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

    // A row of star images; returns them so callers can recolor.
    public static Image[] StarRow(Transform parent, int count, float size, float gap)
    {
        var imgs = new Image[count];
        for (int i = 0; i < count; i++)
        {
            var rt = Rect("Star" + i, parent);
            Place(rt, 0.5f, 0.5f, (i - (count - 1) / 2f) * (size + gap), 0, size, size);
            imgs[i] = rt.gameObject.AddComponent<Image>(); imgs[i].sprite = Star; imgs[i].raycastTarget = false;
            var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.23f, 0.18f, 0.42f, 0.8f); o.effectDistance = new Vector2(3, -3);
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
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = outline; o.effectDistance = new Vector2(dist, -dist);
        return t;
    }

    public static Button Btn(Transform parent, string label, Color bg, Color fg, int size, Action onClick, string name = "Button")
    {
        var img = Panel(parent, bg, name);
        var sh = img.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.25f); sh.effectDistance = new Vector2(0, -8);
        var b = img.gameObject.AddComponent<Button>();
        var cb = b.colors; cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); b.colors = cb;
        Label(img.transform, label, size, fg);
        b.onClick.AddListener(() => { Sfx.Play("click"); onClick?.Invoke(); });
        return b;
    }

    public static Text BtnText(Button b) => b.GetComponentInChildren<Text>();
}
