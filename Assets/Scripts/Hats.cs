using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Cosmetic hats for the ducks: a coin sink with no gameplay effect.
// Built from primitives at runtime (no imported art), bought on the map, worn by every duck in the tub.
public static class Hats
{
    public class HatDef { public string id, name; public int price; }
    public static readonly HatDef[] All =
    {
        new HatDef { id = "",      name = "Không mũ",      price = 0 },
        new HatDef { id = "cap",   name = "Mũ lưỡi trai",  price = 300 },
        new HatDef { id = "bow",   name = "Nơ hồng",       price = 500 },
        new HatDef { id = "party", name = "Mũ tiệc",       price = 800 },
        new HatDef { id = "crown", name = "Vương miện",    price = 1500 },
    };

    public static string Current { get => PlayerPrefs.GetString("ds_hat", ""); set { PlayerPrefs.SetString("ds_hat", value); PlayerPrefs.Save(); } }
    public static bool Owned(string id) => id == "" || PlayerPrefs.GetInt("ds_hat_" + id, 0) == 1;
    public static void Own(string id) { PlayerPrefs.SetInt("ds_hat_" + id, 1); PlayerPrefs.Save(); }

    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => mats.Clear();
    static Material M(Color c)
    {
        if (!mats.TryGetValue(c, out var m) || m == null) mats[c] = m = Art.Mat(c, 0.6f);
        return m;
    }
    static void P(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Color c, Vector3 euler = default)
    {
        var tr = Art.Prim(t, parent, pos, scale, M(c), euler);
        var r = tr.GetComponent<Renderer>();
        if (r && App.Mobile) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    }

    // attach the chosen hat to a duck body (local space: head top is around y 1.17, z 0.24)
    public static void Attach(Transform body, string id = null)
    {
        if (body == null) return;
        id = id ?? Current;
        var old = body.Find("Hat"); if (old) Object.Destroy(old.gameObject);
        if (string.IsNullOrEmpty(id)) return;
        var h = new GameObject("Hat").transform; h.SetParent(body, false); h.localPosition = new Vector3(0, 1.12f, 0.22f);
        switch (id)
        {
            case "cap":
                var blue = new Color(0.25f, 0.55f, 1f);
                P(PrimitiveType.Sphere, h, new Vector3(0, 0.04f, -0.02f), new Vector3(0.6f, 0.34f, 0.6f), blue);
                P(PrimitiveType.Cylinder, h, new Vector3(0, -0.04f, 0.26f), new Vector3(0.46f, 0.02f, 0.36f), blue);
                P(PrimitiveType.Sphere, h, new Vector3(0, 0.2f, -0.02f), Vector3.one * 0.1f, Color.white);
                break;
            case "bow":
                var pink = new Color(1f, 0.45f, 0.7f);
                P(PrimitiveType.Sphere, h, new Vector3(-0.17f, 0.02f, -0.04f), new Vector3(0.28f, 0.2f, 0.14f), pink, new Vector3(0, 0, 20));
                P(PrimitiveType.Sphere, h, new Vector3(0.17f, 0.02f, -0.04f), new Vector3(0.28f, 0.2f, 0.14f), pink, new Vector3(0, 0, -20));
                P(PrimitiveType.Sphere, h, new Vector3(0, 0.02f, -0.04f), Vector3.one * 0.13f, new Color(1f, 0.3f, 0.55f));
                break;
            case "party":
                var cols = new[] { new Color(0.55f, 0.4f, 0.95f), new Color(1f, 0.85f, 0.3f), new Color(0.55f, 0.4f, 0.95f) };
                for (int i = 0; i < 3; i++)
                    P(PrimitiveType.Cylinder, h, new Vector3(0, 0.03f + i * 0.13f, -0.02f), new Vector3(0.36f - i * 0.1f, 0.07f, 0.36f - i * 0.1f), cols[i]);
                P(PrimitiveType.Sphere, h, new Vector3(0, 0.44f, -0.02f), Vector3.one * 0.12f, new Color(1f, 0.4f, 0.4f));
                break;
            case "crown":
                var gold = new Color(1f, 0.66f, 0.12f);   // deeper than the duck's yellow so it reads
                P(PrimitiveType.Cylinder, h, new Vector3(0, 0.06f, -0.02f), new Vector3(0.42f, 0.08f, 0.42f), gold);
                for (int i = 0; i < 5; i++)
                {
                    float a = i / 5f * Mathf.PI * 2;
                    P(PrimitiveType.Sphere, h, new Vector3(Mathf.Cos(a) * 0.17f, 0.2f, -0.02f + Mathf.Sin(a) * 0.17f), new Vector3(0.1f, 0.18f, 0.1f), gold);
                }
                P(PrimitiveType.Sphere, h, new Vector3(0, 0.1f, 0.19f), Vector3.one * 0.08f, new Color(1f, 0.3f, 0.4f));
                break;
        }
        Art.Bake(h, "duck_hat_" + id, 0.6f, 0.002f);   // one mesh per hat: no extra draw calls per duck on phones
    }

    // pixel version for the map's duck marker (same look as the album art: 1 cell = 1 pixel)
    public static string[] PixelRows(string id)
    {
        switch (id)
        {
            case "cap": return new[] { "..BBBB...", ".BBBBBB..", ".BBBBBBBB" };
            case "bow": return new[] { "PP.PP", "PPRPP", "PP.PP" };
            case "party": return new[] { "...R...", "...U...", "..UYU..", ".UYUYU.", "UYUYUYU" };
            case "crown": return new[] { "C..C..C", "CC.C.CC", "CCCRCCC" };
            default: return null;
        }
    }
    public static readonly Dictionary<char, Color> PixelPal = new Dictionary<char, Color>
    {
        { 'B', new Color(0.25f, 0.55f, 1f) }, { 'P', new Color(1f, 0.45f, 0.7f) }, { 'R', new Color(1f, 0.3f, 0.4f) },
        { 'U', new Color(0.55f, 0.4f, 0.95f) }, { 'Y', new Color(1f, 0.85f, 0.3f) }, { 'C', new Color(1f, 0.66f, 0.12f) },
    };

    // ---------- shop popup (lives on the map screen) ----------
    static RectTransform shopRoot; static readonly List<(HatDef def, Text btn, Image bg)> rows = new List<(HatDef, Text, Image)>();
    static System.Action onChange; static Text shopMsg; static string shopMsgDefault;
    static void Say(string m, Color c) { if (shopMsg == null) return; shopMsg.text = m; shopMsg.color = c; }
    public static void BuildShop(Transform parent, System.Action changed)
    {
        rows.Clear(); onChange = changed;
        shopRoot = UIKit.Stretch(UIKit.Rect("HatShop", parent));
        var dim = shopRoot.gameObject.AddComponent<Image>(); dim.color = new Color(0.11f, 0.08f, 0.25f, 0.8f);
        var card = UIKit.Panel(shopRoot, new Color(0.6f, 0.53f, 0.93f), "Card").rectTransform; UIKit.Place(card, 0.5f, 0.5f, 0, 0, 900, 1180);
        var title = UIKit.Outlined(UIKit.Label(card, "MŨ CHO VỊT", 70, Color.white), new Color(0.23f, 0.18f, 0.42f), 4); UIKit.Place(title.rectTransform, 0.5f, 1, 0, -90, 800, 110);
        var sub = UIKit.Label(card, "Chỉ để đẹp · cả đàn vịt cùng đội", 30, Color.white, TextAnchor.MiddleCenter, false); UIKit.Place(sub.rectTransform, 0.5f, 1, 0, -160, 800, 50);
        shopMsg = sub; shopMsgDefault = sub.text;
        for (int i = 0; i < All.Length; i++)
        {
            var def = All[i];
            var row = UIKit.Panel(card, new Color(1f, 0.97f, 0.94f), "Hat" + i); UIKit.Place(row.rectTransform, 0.5f, 1, 0, -270 - i * 160, 800, 140);
            Icon(row.rectTransform, def.id);
            var nm = UIKit.Label(row.rectTransform, def.name, 42, new Color(0.23f, 0.18f, 0.42f), TextAnchor.MiddleLeft); UIKit.Place(nm.rectTransform, 0, 0.5f, 315, 0, 330, 100);
            var b = UIKit.Btn(row.rectTransform, "", new Color(1f, 0.82f, 0.25f), new Color(0.23f, 0.18f, 0.42f), 34, () => Pick(def), "Pick");
            UIKit.Place((RectTransform)b.transform, 1, 0.5f, -150, 0, 260, 100);
            rows.Add((def, UIKit.BtnText(b), b.GetComponent<Image>()));
        }
        var close = UIKit.Btn(card, "ĐÓNG", new Color(1f, 0.82f, 0.25f), new Color(0.23f, 0.18f, 0.42f), 44, () => shopRoot.gameObject.SetActive(false), "Close");
        UIKit.Place((RectTransform)close.transform, 0.5f, 0, 0, 90, 400, 120);
        shopRoot.gameObject.SetActive(false);
    }

    // flat 2D preview: a duck head with the hat drawn from circles and bars
    static void Icon(RectTransform row, string id)
    {
        var box = UIKit.Rect("Icon", row); UIKit.Place(box, 0, 0.5f, 78, 4, 120, 120);
        void D(Color c, float x, float y, float w, float h) { var i = UIKit.Dot(box, c); i.raycastTarget = false; UIKit.Place(i.rectTransform, 0.5f, 0.5f, x, y, w, h); }
        void B(Color c, float x, float y, float w, float h) { var i = UIKit.Panel(box, c, "Bar"); i.raycastTarget = false; UIKit.Place(i.rectTransform, 0.5f, 0.5f, x, y, w, h); }
        D(new Color(1f, 0.85f, 0.25f), 0, -18, 78, 74);                      // head
        D(new Color(1f, 0.55f, 0.15f), 30, -26, 30, 16);                     // beak
        D(new Color(0.2f, 0.16f, 0.3f), 10, -8, 12, 14);                     // eye
        switch (id)
        {
            case "cap": D(new Color(0.25f, 0.55f, 1f), -2, 20, 70, 44); B(new Color(0.2f, 0.45f, 0.9f), 26, 10, 50, 12); break;
            case "bow": D(new Color(1f, 0.45f, 0.7f), -18, 26, 34, 26); D(new Color(1f, 0.45f, 0.7f), 18, 26, 34, 26); D(new Color(1f, 0.3f, 0.55f), 0, 26, 16, 16); break;
            case "party":
                B(new Color(0.55f, 0.4f, 0.95f), 0, 22, 44, 14); B(new Color(1f, 0.85f, 0.3f), 0, 34, 30, 12); B(new Color(0.55f, 0.4f, 0.95f), 0, 45, 18, 10);
                D(new Color(1f, 0.4f, 0.4f), 0, 54, 14, 14); break;
            case "crown":
                B(new Color(1f, 0.66f, 0.12f), 0, 22, 56, 18);
                for (int k = -1; k <= 1; k++) D(new Color(1f, 0.66f, 0.12f), k * 20, 36, 14, 18);
                D(new Color(1f, 0.3f, 0.4f), 0, 22, 10, 10); break;
            default: D(new Color(0.6f, 0.58f, 0.68f, 0.6f), 0, 30, 30, 8); break;
        }
    }

    public static void OpenShop() { if (shopRoot == null) return; Say(shopMsgDefault, Color.white); RefreshShop(); shopRoot.gameObject.SetActive(true); shopRoot.SetAsLastSibling(); }

    static void RefreshShop()
    {
        foreach (var (def, btn, bg) in rows)
        {
            bool own = Owned(def.id), on = Current == def.id;
            btn.text = def.id == "" ? (on ? "ĐANG CHỌN" : "BỎ MŨ") : on ? "ĐANG ĐỘI" : own ? "ĐỘI" : def.price + " xu";
            bg.color = on ? new Color(0.37f, 0.83f, 0.37f) : own || SaveData.Coins >= def.price ? new Color(1f, 0.82f, 0.25f) : new Color(0.8f, 0.78f, 0.85f);
        }
    }

    static void Pick(HatDef def)
    {
        if (!Owned(def.id))
        {
            if (SaveData.Coins < def.price) { Sfx.Play("click", 0.6f, 0.7f); Say("Chưa đủ xu! Cần " + def.price + " xu", new Color(1f, 0.8f, 0.8f)); return; }
            SaveData.Coins -= def.price; Own(def.id); Sfx.Play("coin"); Sfx.Play("win", 0.5f, 1.4f);
            Say("Đã mua " + def.name + "!", new Color(1f, 0.95f, 0.5f));
        }
        Current = def.id; Sfx.Play("quack", 0.8f, 1.2f);
        RefreshShop(); onChange?.Invoke();
    }
}
