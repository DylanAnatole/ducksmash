using UnityEngine;
using UnityEngine.UI;

// The only settings screen: sound / music / vibration, reset progress.
// When opened in-game it also offers Replay and Back-to-map.
public class SettingsPopup : MonoBehaviour
{
    RectTransform root; Button snd, mus, vib, reset, replay, toMap; bool confirmReset;
    static readonly Color Purple = new Color(0.6f, 0.53f, 0.93f), Ink = new Color(0.23f, 0.18f, 0.42f),
        On = new Color(0.37f, 0.83f, 0.37f), Off = new Color(0.72f, 0.7f, 0.78f), Yellow = new Color(1f, 0.8f, 0.2f);

    public void Build(Transform canvas)
    {
        root = UIKit.Stretch(UIKit.Rect("Settings", canvas));
        var dim = root.gameObject.AddComponent<Image>(); dim.color = new Color(0.11f, 0.08f, 0.25f, 0.75f);
        var card = UIKit.Panel(root, Purple, "Card").rectTransform; UIKit.Place(card, 0.5f, 0.5f, 0, 0, 860, 1200);
        UIKit.Outlined(UIKit.Label(card, "CÀI ĐẶT", 80, Color.white), Ink, 4).rectTransform.offsetMin = new Vector2(0, 1000);

        snd = Row(card, 0, () => { SaveData.Sound = !SaveData.Sound; Refresh(); });
        mus = Row(card, 1, () => { SaveData.Music = !SaveData.Music; Sfx.ApplySettings(); Refresh(); });
        vib = Row(card, 2, () => { SaveData.Vibration = !SaveData.Vibration; Refresh(); });

        replay = UIKit.Btn(card, "Chơi lại màn", Yellow, Ink, 44, () => { Close(); App.Game.Restart(); });
        UIKit.Place((RectTransform)replay.transform, 0.5f, 0, -170, 400, 315, 120);
        toMap = UIKit.Btn(card, "Về bản đồ", Yellow, Ink, 44, () => { Close(); App.ShowMap(); });
        UIKit.Place((RectTransform)toMap.transform, 0.5f, 0, 170, 400, 315, 120);

        reset = UIKit.Btn(card, "", new Color(1f, 0.45f, 0.4f), Color.white, 38, OnReset);
        UIKit.Place((RectTransform)reset.transform, 0.5f, 0, 0, 255, 680, 110);
        var close = UIKit.Btn(card, "ĐÓNG", On, Color.white, 50, Close);
        UIKit.Place((RectTransform)close.transform, 0.5f, 0, 0, 105, 680, 130);
        root.gameObject.SetActive(false);
    }

    Button Row(RectTransform card, int i, System.Action toggle)
    {
        float y = -260 - i * 150;
        var lbl = UIKit.Label(card, "", 50, Color.white, TextAnchor.MiddleLeft); UIKit.Place(lbl.rectTransform, 0.5f, 1, -120, y, 500, 120);
        lbl.name = "RowLabel";
        var b = UIKit.Btn(card, "", On, Color.white, 44, toggle); UIKit.Place((RectTransform)b.transform, 0.5f, 1, 240, y, 230, 110);
        b.gameObject.name = "Toggle" + i;
        lbl.text = i == 0 ? "Âm thanh" : i == 1 ? "Nhạc nền" : "Rung";
        return b;
    }

    void Set(Button b, bool on) { b.GetComponent<Image>().color = on ? On : Off; UIKit.BtnText(b).text = on ? "BẬT" : "TẮT"; }

    void Refresh()
    {
        Set(snd, SaveData.Sound); Set(mus, SaveData.Music); Set(vib, SaveData.Vibration);
        UIKit.BtnText(reset).text = confirmReset ? "Chạm lần nữa để xoá!" : "Xoá tiến trình";
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
        root.gameObject.SetActive(true); root.SetAsLastSibling(); Refresh(); Time.timeScale = 0f; // pause gameplay while open
    }

    public void Close() { root.gameObject.SetActive(false); confirmReset = false; Time.timeScale = 1f; }
    public bool IsOpen => root.gameObject.activeSelf;
}
