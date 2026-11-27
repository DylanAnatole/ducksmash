using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Level map: 25 nodes on a winding path (1-5 onboarding, 6-25 gameplay, bosses at 10/15/20/25).
public class MapScreen : MonoBehaviour
{
    RectTransform root, content; ScrollRect scroll;
    Text coinsTxt, starsTxt, boostTxt;
    readonly List<(Image img, Text num, Text stars, Text tag, int n)> nodes = new List<(Image, Text, Text, Text, int)>();
    const float NodeGap = 230, Bottom = 260;
    readonly Dictionary<int, Image[]> nodeStars = new Dictionary<int, Image[]>();

    static readonly Color Bg = new Color(0.88f, 0.93f, 0.98f), Purple = new Color(0.27f, 0.56f, 0.93f), PurpleDark = new Color(0.2f, 0.42f, 0.78f),
        Ink = new Color(0.15f, 0.2f, 0.32f), Yellow = new Color(1f, 0.8f, 0.2f), Teal = new Color(0.25f, 0.78f, 0.75f),
        Red = new Color(1f, 0.38f, 0.3f), Green = new Color(0.37f, 0.83f, 0.37f), Locked = new Color(0.8f, 0.84f, 0.9f);

    public void Build(Transform canvas)
    {
        root = UIKit.Stretch(UIKit.Rect("MapScreen", canvas));
        var bg = root.gameObject.AddComponent<RawImage>();
        bg.texture = Art.TileTex(new Color(0.94f, 0.97f, 1f), new Color(0.86f, 0.92f, 0.98f)); bg.uvRect = new Rect(0, 0, 2, 3.6f);   // calm pale-blue bathroom tiles

        // soap bubbles drifting up behind the path
        var bub = UIKit.Stretch(UIKit.Rect("Bubbles", root)); bub.gameObject.AddComponent<Canvas>();
        for (int i = 0; i < 6; i++)
        {
            var b = UIKit.Dot(bub, new Color(1f, 1f, 1f, 0.32f), "Bubble"); b.raycastTarget = false;
            var o = b.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.75f, 0.9f, 1f, 0.6f); o.effectDistance = new Vector2(2, -2);
            var rt = b.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0); float sz = Random.Range(18f, 58f); rt.sizeDelta = new Vector2(sz, sz);
            rt.anchoredPosition = new Vector2(Random.Range(-520f, 520f), Random.Range(0f, 1900f));
            bubbles.Add((rt, Random.Range(30f, 75f), Random.Range(0f, 6.28f)));
        }
        // scroll view
        var view = UIKit.Stretch(UIKit.Rect("Scroll", root), 0, 230, 0, 250);
        scroll = view.gameObject.AddComponent<ScrollRect>();
        view.gameObject.AddComponent<RectMask2D>();
        var hit = view.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
        content = UIKit.Rect("Content", view);
        content.anchorMin = new Vector2(0, 0); content.anchorMax = new Vector2(1, 0); content.pivot = new Vector2(0.5f, 0);
        float height = Bottom + (LevelLibrary.Count - 1) * NodeGap + 400;
        content.sizeDelta = new Vector2(0, height); content.anchoredPosition = Vector2.zero;
        scroll.content = content; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = view; scroll.scrollSensitivity = 40;

        // path segments
        for (int n = 1; n < LevelLibrary.Count; n++)
        {
            Vector2 a = NodePos(n), b = NodePos(n + 1);
            var seg = UIKit.Panel(content, new Color(1, 1, 1, 0.95f), "Path").rectTransform;
            seg.anchorMin = seg.anchorMax = new Vector2(0.5f, 0); seg.pivot = new Vector2(0.5f, 0.5f);
            seg.anchoredPosition = (a + b) / 2; seg.sizeDelta = new Vector2(Vector2.Distance(a, b) + 20, 30);
            seg.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }
        // section banners
        Banner("HƯỚNG DẪN  ·  MÀN 1–5", 150, NodePos(1).y - 20, Teal);
        Banner("THỬ THÁCH  ·  MÀN 6–" + LevelLibrary.Count, -200, NodePos(6).y - 90, PurpleDark);
        Banner("ĐÍCH: MÀN " + LevelLibrary.Count + " SIÊU KHÓ", 0, NodePos(LevelLibrary.Count).y + 340, new Color(1f, 0.55f, 0.3f));   // end of the road

        for (int n = 1; n <= LevelLibrary.Count; n++)
        {
            int lvl = n, tier = LevelLibrary.Tier(n); bool boss = tier >= 2;
            float size = tier == 3 ? 200 : tier == 2 ? 175 : 150;   // harder = bigger node
            var img = UIKit.Dot(content, Color.white, "Node" + n);
            var rt = img.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0); rt.sizeDelta = new Vector2(size, size); rt.anchoredPosition = NodePos(n);
            var sh = img.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0.1f, 0.2f, 0.4f, 0.22f); sh.effectDistance = new Vector2(0, -8);
            var btn = img.gameObject.AddComponent<Button>(); btn.onClick.AddListener(() => { if (lvl <= SaveData.MaxLevel) { Sfx.Play("click"); App.StartLevel(lvl); } });
            var inner = UIKit.Dot(img.transform, Color.white, "Inner"); UIKit.Stretch(inner.rectTransform, 12, 12, 12, 12); inner.raycastTarget = false;
            {   // glossy cap: the node reads as a shiny round candy instead of a flat disc
                var gl = UIKit.Dot(inner.transform, new Color(1f, 1f, 1f, 0.18f), "Gloss"); gl.raycastTarget = false;
                var grt = gl.rectTransform; grt.anchorMin = new Vector2(0.17f, 0.52f); grt.anchorMax = new Vector2(0.83f, 0.92f); grt.offsetMin = grt.offsetMax = Vector2.zero;
                var sp = UIKit.Dot(inner.transform, new Color(1f, 1f, 1f, 0.55f), "Shine"); sp.raycastTarget = false;
                var srt = sp.rectTransform; srt.anchorMin = srt.anchorMax = new Vector2(0.3f, 0.76f); srt.sizeDelta = new Vector2(size * 0.13f, size * 0.09f); srt.localEulerAngles = new Vector3(0, 0, 35);
            }
            var num = UIKit.Outlined(UIKit.Label(img.transform, n.ToString(), boss ? 64 : 58, Color.white), Ink, 3);
            // every level after the tutorial shows its difficulty tier; tutorial levels show what they teach
            var tag = UIKit.Label(img.transform, n > LevelLibrary.Onboarding ? LevelLibrary.TierName(tier).ToUpper() : LevelLibrary.NewSpecialAt(n) != null ? "VỊT MỚI" : "", 28, Color.white); tag.fontSize = 24; tag.rectTransform.offsetMin = new Vector2(0, -size * 0.6f); tag.rectTransform.offsetMax = new Vector2(0, -size * 0.4f);   // pill sits on the node rim (no clash with the next node)
            if (tag.text.Length > 0)
            {   // pill behind the tag text
                var pill = UIKit.Panel(img.transform, n > LevelLibrary.Onboarding ? LevelLibrary.TierColor(tier) : Ink, "TagPill"); pill.raycastTarget = false;
                var prt = pill.rectTransform; prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.sizeDelta = new Vector2(tag.text.Length * 15 + 36, 40); prt.anchoredPosition = new Vector2(0, -size * 0.5f);
                tag.transform.SetAsLastSibling();
            }
            var st = UIKit.Outlined(UIKit.Label(img.transform, "", 44, Yellow), Ink, 2); st.rectTransform.offsetMin = new Vector2(0, size * 0.62f); st.rectTransform.offsetMax = new Vector2(0, size * 0.62f);
            nodes.Add((inner, num, st, tag, n));
            // picture levels show their pixel picture as a badge (silhouette until collected)
            var art = LevelLibrary.Get(n).art;
            if (!string.IsNullOrEmpty(art))
            {
                var bdg = UIKit.Dot(img.transform, Color.white, "BadgeBg"); bdg.raycastTarget = false;
                var bgrt = bdg.rectTransform; bgrt.anchorMin = bgrt.anchorMax = new Vector2(0.5f, 0.5f); bgrt.sizeDelta = new Vector2(86, 86);
                bgrt.anchoredPosition = new Vector2(size * 0.42f, size * 0.36f);
                var badge = UIKit.Rect("ArtBadge", img.transform).gameObject.AddComponent<RawImage>(); badge.raycastTarget = false;
                var brt = badge.rectTransform; brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f); brt.sizeDelta = new Vector2(60, 60);
                brt.anchoredPosition = bgrt.anchoredPosition;
                artBadges[n] = (badge, art);
            }
            nodeStars[n] = UIKit.StarRow(st.transform, 3, 40, 2);
        }

        // the player's duck stands on the current level node
        duckMark = UIKit.Rect("YouAreHere", content).gameObject.AddComponent<RawImage>(); duckMark.raycastTarget = false;
        duckMark.texture = LevelLibrary.ArtTexture("duck", false);
        var dm = duckMark.rectTransform; dm.anchorMin = dm.anchorMax = new Vector2(0.5f, 0); dm.sizeDelta = new Vector2(104, 88);
        var shadowDot = UIKit.Dot(dm, new Color(0.23f, 0.18f, 0.42f, 0.25f), "Shadow"); shadowDot.raycastTarget = false;
        shadowDot.rectTransform.anchorMin = shadowDot.rectTransform.anchorMax = new Vector2(0.5f, 0); shadowDot.rectTransform.sizeDelta = new Vector2(70, 16);
        shadowDot.rectTransform.anchoredPosition = new Vector2(0, -6); shadowDot.transform.SetAsFirstSibling();
        // header: title on the left, coin + star pills, round settings button
        var head = UIKit.Panel(root, PurpleDark, "Header").rectTransform; UIKit.Place(head, 0.5f, 1, 0, -128, 1040, 220);
        var headIn = UIKit.Panel(head, Purple, "Inner").rectTransform; UIKit.Stretch(headIn, 0, 8, 0, 0);
        var title = UIKit.Outlined(UIKit.Label(headIn, "Duck Smash", 66, Color.white, TextAnchor.MiddleLeft), new Color(0, 0, 0, 0.18f), 3); UIKit.Place(title.rectTransform, 0, 0.5f, 270, 52, 480, 80);
        var sub = UIKit.Label(headIn, "Bắn vịt · Phá tung bồn tắm", 28, new Color(1, 1, 1, 0.85f), TextAnchor.MiddleLeft, false); UIKit.Place(sub.rectTransform, 0, 0.5f, 270, 4, 480, 40);
        coinsTxt = Pill(headIn, -75, -62, Yellow, out _);
        starsTxt = Pill(headIn, 155, -62, Yellow, out var starIcon); starIcon.sprite = UIKit.Star; starIcon.color = Yellow;
        boostTxt = UIKit.Label(headIn, "", 1, Color.clear); boostTxt.gameObject.SetActive(false);   // booster counts live in-game now
        var gear = UIKit.Btn(headIn, "", Color.white, Ink, 30, () => App.OpenSettings(false), "Settings");
        UIKit.Place((RectTransform)gear.transform, 1, 0.5f, -78, 0, 116, 116);
        for (int i = 0; i < 3; i++)
        {   // "menu" icon: three bars
            var bar = UIKit.Panel(gear.transform, Ink, "Bar"); bar.raycastTarget = false; UIKit.Place(bar.rectTransform, 0.5f, 0.5f, 0, 22 - i * 22, 52, 11);
        }

        // bottom dock: album · PLAY · smash levels · sandbox
        var dock = UIKit.Panel(root, Color.white, "Dock").rectTransform; UIKit.Place(dock, 0.5f, 0, 0, 105, 1040, 190);
        var dsh = dock.gameObject.AddComponent<Shadow>(); dsh.effectColor = new Color(0.1f, 0.2f, 0.4f, 0.18f); dsh.effectDistance = new Vector2(0, 8);
        var album = UIKit.Btn(dock, "ALBUM", Teal, Color.white, 32, OpenAlbum, "Album");
        UIKit.Place((RectTransform)album.transform, 0.5f, 0.5f, -390, 4, 170, 130);
        playBtn = UIKit.Btn(dock, "", new Color(0.3f, 0.75f, 0.4f), Color.white, 46, () => { Sfx.Play("click"); App.StartLevel(Mathf.Min(SaveData.MaxLevel, LevelLibrary.Count)); }, "Play");
        UIKit.Place((RectTransform)playBtn.transform, 0.5f, 0.5f, -95, 4, 380, 140);
        var smash = UIKit.Btn(dock, "PHÁ\nĐỒ", new Color(0.55f, 0.4f, 0.95f), Color.white, 32, () => { Sfx.Play("click"); App.StartLevel(LevelLibrary.SmashLevel); }, "SmashLevels");
        UIKit.Place((RectTransform)smash.transform, 0.5f, 0.5f, 200, 4, 170, 130);
        var test = UIKit.Btn(dock, "MÀN\nTHỬ", Red, Color.white, 32, () => { Sfx.Play("click"); App.StartLevel(LevelLibrary.TestLevel); }, "TestLevel");
        UIKit.Place((RectTransform)test.transform, 0.5f, 0.5f, 390, 4, 170, 130);
        foreach (var b in new[] { album, smash, test }) UIKit.BtnText(b).lineSpacing = 0.9f;

        BuildAlbum();
    }


    readonly Dictionary<int, (RawImage img, string id)> artBadges = new Dictionary<int, (RawImage, string)>();
    static readonly Dictionary<string, Texture2D> badgeTex = new Dictionary<string, Texture2D>();
    static Texture2D BadgeTex(string id, bool locked)
    {
        string k = id + (locked ? "_l" : "");
        if (!badgeTex.TryGetValue(k, out var t) || t == null) badgeTex[k] = t = LevelLibrary.ArtTexture(id, locked);
        return t;
    }
    Button playBtn;
    // white pill with an icon on the left and a number
    Text Pill(RectTransform parent, float x, float y, Color iconCol, out Image icon)
    {
        var p = UIKit.Panel(parent, new Color(0.1f, 0.2f, 0.45f, 0.35f), "Pill").rectTransform; UIKit.Place(p, 0, 0.5f, x + 210, y, 210, 58);
        icon = UIKit.Dot(p, iconCol, "Icon"); UIKit.Place(icon.rectTransform, 0, 0.5f, 30, 0, 46, 46); icon.raycastTarget = false;
        var t = UIKit.Label(p, "", 32, Color.white); UIKit.Place(t.rectTransform, 0.5f, 0.5f, 22, 0, 160, 58);
        return t;
    }
    RawImage duckMark; readonly List<(RectTransform rt, float speed, float phase)> bubbles = new List<(RectTransform, float, float)>();
    RectTransform albumRoot; readonly List<RawImage> albumImgs = new List<RawImage>(); readonly List<Text> albumNames = new List<Text>();
    Text albumTitle;


    void BuildAlbum()
    {
        albumRoot = UIKit.Stretch(UIKit.Rect("AlbumPopup", root));
        var dim = albumRoot.gameObject.AddComponent<Image>(); dim.color = new Color(0.08f, 0.12f, 0.22f, 0.6f);
        var card = UIKit.Panel(albumRoot, Color.white, "Card").rectTransform; UIKit.Place(card, 0.5f, 0.5f, 0, 0, 980, 1500);
        var ahead = UIKit.Panel(card, Teal, "Header"); UIKit.Place(ahead.rectTransform, 0.5f, 1, 0, -85, 980, 170);
        albumTitle = UIKit.Outlined(UIKit.Label(ahead.transform, "", 64, Color.white), new Color(0, 0, 0, 0.18f), 3);
        var ids = LevelLibrary.ArtIds;
        for (int i = 0; i < ids.Length; i++)
        {
            int col = i % 3, row = i / 3;   // 3 x 4 grid (12 pictures)
            var cell = UIKit.Panel(card, new Color(0.94f, 0.95f, 0.98f), "Art" + i).rectTransform; UIKit.Place(cell, 0.5f, 1, (col - 1) * 305, -330 - row * 290, 285, 275);
            var img = UIKit.Rect("Pic", cell).gameObject.AddComponent<RawImage>(); UIKit.Place(img.rectTransform, 0.5f, 0.5f, 0, 22, 190, 180);
            albumImgs.Add(img);
            var nm = UIKit.Label(cell, "", 28, Ink); UIKit.Place(nm.rectTransform, 0.5f, 0, 0, 26, 280, 40); albumNames.Add(nm);
        }
        var close = UIKit.Btn(card, "ĐÓNG", new Color(0.3f, 0.75f, 0.4f), Color.white, 44, () => { Sfx.Play("click"); albumRoot.gameObject.SetActive(false); }, "Close");
        UIKit.Place((RectTransform)close.transform, 0.5f, 0, 0, 90, 400, 120);
        albumRoot.gameObject.SetActive(false);
    }

    void OpenAlbum()
    {
        Sfx.Play("click");
        var ids = LevelLibrary.ArtIds; int have = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            bool got = SaveData.HasArt(ids[i]); if (got) have++;
            albumImgs[i].texture = LevelLibrary.ArtTexture(ids[i], !got);
            albumNames[i].text = got ? LevelLibrary.ArtName(ids[i]) : "???";
        }
        albumTitle.text = "ALBUM TRANH  " + have + "/" + ids.Length;
        albumRoot.gameObject.SetActive(true); albumRoot.SetAsLastSibling();
    }

    void Banner(string text, float x, float y, Color c)
    {
        var b = UIKit.Panel(content, c, "Banner").rectTransform;
        b.anchorMin = b.anchorMax = new Vector2(0.5f, 0); b.sizeDelta = new Vector2(500, 64); b.anchoredPosition = new Vector2(x, y);
        var bs = b.gameObject.AddComponent<Shadow>(); bs.effectColor = new Color(0.1f, 0.2f, 0.4f, 0.2f); bs.effectDistance = new Vector2(0, -6);
        UIKit.Label(b, text, 28, Color.white);
    }

    // zig-zag: 4 columns going up
    static Vector2 NodePos(int n)
    {
        int i = n - 1; float[] xs = { -270, -90, 90, 270 };   // kept clear of the side buttons
        int row = i / 4, col = i % 4; if (row % 2 == 1) col = 3 - col;
        float y = Bottom + i * NodeGap * 0.62f + row * 90;
        return new Vector2(xs[col], y);
    }

    int lastShownMax = -1; float unlockT;
    public void Show()
    {
        // a level unlocked since the map was last shown pops in
        if (lastShownMax > 0 && SaveData.MaxLevel > lastShownMax) { unlockT = 0.01f; Sfx.Play("star", 0.8f, 1.2f); }
        // saves from the 25-level version: a cleared last level unlocks the new ones
        if (SaveData.MaxLevel < LevelLibrary.Count && SaveData.GetStars(SaveData.MaxLevel) > 0) SaveData.MaxLevel = SaveData.MaxLevel + 1;
        lastShownMax = SaveData.MaxLevel;
        root.gameObject.SetActive(true);
        Refresh();
        Canvas.ForceUpdateCanvases();
        // scroll so the current level is visible
        float y = NodePos(Mathf.Min(SaveData.MaxLevel, LevelLibrary.Count)).y;
        float viewH = ((RectTransform)scroll.viewport).rect.height, total = content.sizeDelta.y;
        scroll.verticalNormalizedPosition = Mathf.Clamp01((y - viewH * 0.4f) / Mathf.Max(1, total - viewH));
    }

    public void Hide() => root.gameObject.SetActive(false);

    public void Refresh()
    {
        foreach (var kv in artBadges) kv.Value.img.texture = BadgeTex(kv.Value.id, !SaveData.HasArt(kv.Value.id));
        int max = SaveData.MaxLevel, totalStars = 0;
        foreach (var nd in nodes)
        {
            int n = nd.n, s = SaveData.GetStars(n); totalStars += s;
            int tier = LevelLibrary.Tier(n); bool open = n <= max;
            // open nodes: hard = orange, extreme = red; otherwise done = green, current = yellow, tutorial = teal. Locked = pale tint of that colour.
            Color c = tier == 3 ? new Color(0.9f, 0.25f, 0.25f) : tier == 2 ? new Color(1f, 0.55f, 0.15f) : n <= LevelLibrary.Onboarding ? Teal : s > 0 ? Green : Yellow;
            nd.img.color = open ? c : Color.Lerp(tier >= 2 ? c : Locked, Color.white, tier >= 2 ? 0.55f : 0f);
            nd.num.text = n.ToString();
            nd.num.color = open ? Color.white : new Color(0.6f, 0.65f, 0.74f);
            nd.stars.text = "";
            for (int i = 0; i < 3; i++) { nodeStars[n][i].gameObject.SetActive(s > 0); nodeStars[n][i].color = i < s ? Yellow : new Color(1, 1, 1, 0.55f); }
            nd.img.transform.parent.localScale = Vector3.one * (n == max ? 1.12f : 1f);
        }
        coinsTxt.text = SaveData.Coins >= 100000 ? (SaveData.Coins / 1000) + "k" : SaveData.Coins.ToString();
        starsTxt.text = totalStars + "/" + LevelLibrary.Count * 3;
        if (playBtn) UIKit.BtnText(playBtn).text = "CHƠI MÀN " + Mathf.Min(max, LevelLibrary.Count);
        boostTxt.text = "Búa " + SaveData.GetBoost("hammer") + " · Bom " + SaveData.GetBoost("bomb") + " · +3 lượt " + SaveData.GetBoost("moves");
    }

    void Update()
    {
        if (!root.gameObject.activeSelf) return;
        Sfx.MusicMood(1f);
        // duck hops on the current node
        if (duckMark != null)
        {
            var np = NodePos(Mathf.Min(SaveData.MaxLevel, LevelLibrary.Count));
            float hop = Mathf.Abs(Mathf.Sin(Time.time * 3.2f)) * 26f;
            duckMark.rectTransform.anchoredPosition = np + new Vector2(-6, 212 + hop);   // above the star row
            duckMark.rectTransform.localScale = new Vector3(1f + (hop < 3f ? 0.12f : 0f), 1f - (hop < 3f ? 0.1f : 0f), 1f);   // squash on landing
        }
        // bubbles rise and sway, then wrap to the bottom
        for (int i = 0; i < bubbles.Count; i++)
        {
            var (rt, sp, ph) = bubbles[i];
            var p = rt.anchoredPosition; p.y += sp * Time.deltaTime; p.x += Mathf.Sin(Time.time * 1.3f + ph) * 12f * Time.deltaTime;
            if (p.y > 2000f) p = new Vector2(Random.Range(-520f, 520f), -60f);
            rt.anchoredPosition = p;
        }
        // bob the current node
        foreach (var nd in nodes)
            if (nd.n == SaveData.MaxLevel)
            {
                float pop = 1f;
                if (unlockT > 0)
                {   // back-out pop for the freshly unlocked node
                    unlockT += Time.deltaTime; float x = Mathf.Clamp01((unlockT - 0.25f) / 0.45f), m = x - 1f;
                    pop = x <= 0 ? 0.01f : 1f + 3.2f * m * m * m + 2.2f * m * m;
                    if (unlockT > 0.75f) unlockT = 0;
                }
                nd.img.transform.parent.localScale = Vector3.one * (1.12f + Mathf.Sin(Time.time * 4) * 0.04f) * pop;
            }
    }
}
