using UnityEngine;
using UnityEngine.UI;

// The only settings screen: sound / music / vibration, reset progress.
// When opened in-game it also offers Replay and Back-to-map.
// Layout: white card + blue ribbon header (same family as the result popup), one light row per setting
// with an iOS-style switch, a big "continue" button and a quiet reset link at the bottom.
public class SettingsPopup : MonoBehaviour
{
    RectTransform root, card; Button snd, mus, vib, reset, replay, toMap, close; bool confirmReset; Text resumeTxt;
    static readonly Color Blue = new Color(0.27f, 0.56f, 0.93f), BlueDark = new Color(0.2f, 0.42f, 0.78f), Ink = new Color(0.15f, 0.2f, 0.32f),
        On = new Color(0.3f, 0.75f, 0.4f), Off = new Color(0.78f, 0.8f, 0.86f), Yellow = new Color(1f, 0.8f, 0.2f), Row = new Color(0.94f, 0.95f, 0.98f),
        Danger = new Color(0.92f, 0.36f, 0.32f);

    public void Build(Transform canvas)
    {
        root = UIKit.Stretch(UIKit.Rect("Settings", canvas));
        var dim = root.gameObject.AddComponent<Image>(); dim.color = new Color(0.08f, 0.12f, 0.22f, 0.6f);
        card = UIKit.Panel(root, Color.white, "Card").rectTransform; UIKit.Place(card, 0.5f, 0.5f, 0, 0, 860, 1080);
        var csh = card.gameObject.AddComponent<Shadow>(); csh.effectColor = new Color(0.05f, 0.1f, 0.2f, 0.35f); csh.effectDistance = new Vector2(0, -16);
        var head = UIKit.Panel(card, Blue, "Header"); UIKit.Place(head.rectTransform, 0.5f, 1, 0, -85, 860, 170);
        var lip = UIKit.Panel(head.transform, new Color(0, 0, 0, 0.1f), "Lip"); var lrt = lip.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = new Vector2(1, 0); lrt.sizeDelta = new Vector2(0, 12); lrt.anchoredPosition = new Vector2(0, 6);
        UIKit.Outlined(UIKit.Label(head.transform, "CÀI ĐẶT", 64, Color.white), new Color(0, 0, 0, 0.18f), 3);
        // round close (X) button on the header
        close = UIKit.Btn(head.transform, "", Color.white, Ink, 40, Close, "CloseX"); UIKit.Place((RectTransform)close.transform, 1, 0.5f, -70, 4, 92, 92);
        foreach (float a in new[] { 45f, -45f })
        {
            var bar = UIKit.Panel(close.transform, Ink, "X"); bar.raycastTarget = false;
            UIKit.Place(bar.rectTransform, 0.5f, 0.5f, 0, 0, 46, 10); bar.rectTransform.localEulerAngles = new Vector3(0, 0, a);
        }

        snd = RowToggle(0, "Âm thanh", () => { SaveData.Sound = !SaveData.Sound; Refresh(); });
        mus = RowToggle(1, "Nhạc nền", () => { SaveData.Music = !SaveData.Music; Sfx.ApplySettings(); Refresh(); });
        vib = RowToggle(2, "Rung", () => { SaveData.Vibration = !SaveData.Vibration; Refresh(); });

        replay = UIKit.Btn(card, "Chơi lại", Yellow, Ink, 40, () => { Close(); App.Game.Restart(); }, "Replay");
        UIKit.Place((RectTransform)replay.transform, 0.5f, 0, -170, 330, 320, 112);
        toMap = UIKit.Btn(card, "Bản đồ", Blue, Color.white, 40, () => { Close(); App.ShowMap(); }, "ToMap");
        UIKit.Place((RectTransform)toMap.transform, 0.5f, 0, 170, 330, 320, 112);
        var resume = UIKit.Btn(card, "TIẾP TỤC", On, Color.white, 50, Close, "Resume");
        UIKit.Place((RectTransform)resume.transform, 0.5f, 0, 0, 190, 660, 130); resumeTxt = UIKit.BtnText(resume);

        // reset: a quiet text link, red only once armed (two taps)
        reset = UIKit.Btn(card, "", Color.white, Danger, 30, OnReset, "Reset"); UIKit.Place((RectTransform)reset.transform, 0.5f, 0, 0, 68, 460, 70);
        var rs = reset.GetComponent<Shadow>(); if (rs) rs.enabled = false;
        root.gameObject.SetActive(false);
    }

    Button RowToggle(int i, string name, System.Action toggle)
    {
        float y = -260 - i * 140;
        var row = UIKit.Panel(card, Row, "Row" + i).rectTransform; UIKit.Place(row, 0.5f, 1, 0, y, 720, 116);
        var lbl = UIKit.Label(row, name, 44, Ink, TextAnchor.MiddleLeft); UIKit.Place(lbl.rectTransform, 0, 0.5f, 260, 0, 420, 100);
        lbl.name = "RowLabel";
        // switch: pill track + white knob
        var b = UIKit.Btn(row, "", On, Color.white, 1, toggle, "Toggle" + i); UIKit.Place((RectTransform)b.transform, 1, 0.5f, -110, 0, 156, 80);
        var sh = b.GetComponent<Shadow>(); if (sh) sh.enabled = false;
        var gl = b.transform.Find("Gloss"); if (gl) gl.gameObject.SetActive(false);
        var knob = UIKit.Dot(b.transform, Color.white, "Knob"); knob.raycastTarget = false; UIKit.Place(knob.rectTransform, 0.5f, 0.5f, 38, 0, 66, 66);
        var ks = knob.gameObject.AddComponent<Shadow>(); ks.effectColor = new Color(0, 0, 0, 0.18f); ks.effectDistance = new Vector2(0, -3);
        return b;
    }

    void Set(Button b, bool on)
    {
        b.GetComponent<Image>().color = on ? On : Off;
        var k = b.transform.Find("Knob"); if (k) ((RectTransform)k).anchoredPosition = new Vector2(on ? 38 : -38, 0);
    }

    void Refresh()
    {
        Set(snd, SaveData.Sound); Set(mus, SaveData.Music); Set(vib, SaveData.Vibration);
        UIKit.BtnText(reset).text = confirmReset ? "Chạm lần nữa để XOÁ hết tiến trình" : "Xoá tiến trình chơi";
        reset.GetComponent<Image>().color = confirmReset ? new Color(1f, 0.9f, 0.88f) : Color.white;
    }

    void OnReset()
    {
        if (!confirmReset) { confirmReset = true; Refresh(); return; }
        confirmReset = false; SaveData.ResetProgress(); Refresh(); Close(); App.ShowMap();
    }

    public void Open(bool inGame)
    {
        confirmReset = false;
        replay.gameObject.SetActive(inGame); toMap.gameObject.SetActive(inGame);
        resumeTxt.text = inGame ? "TIẾP TỤC" : "XONG";
        card.sizeDelta = new Vector2(860, inGame ? 1080 : 940);   // no replay/map row on the map screen: a shorter card
        root.gameObject.SetActive(true); root.SetAsLastSibling(); Refresh(); Time.timeScale = 0f; // pause gameplay while open
    }

    public void Close() { root.gameObject.SetActive(false); confirmReset = false; Time.timeScale = 1f; }
    public bool IsOpen => root.gameObject.activeSelf;
}
