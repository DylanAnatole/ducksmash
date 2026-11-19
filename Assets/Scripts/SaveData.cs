using UnityEngine;

// Player progress + settings, stored in PlayerPrefs.
public static class SaveData
{
    const string P = "ds_";

    public static int MaxLevel
    {
        get => PlayerPrefs.GetInt(P + "max", 1);
        set { PlayerPrefs.SetInt(P + "max", value); PlayerPrefs.Save(); }
    }

    public static int Coins
    {
        get => PlayerPrefs.GetInt(P + "coins", 150);
        set { PlayerPrefs.SetInt(P + "coins", value); PlayerPrefs.Save(); }
    }

    public static int GetStars(int level) => PlayerPrefs.GetInt(P + "star_" + level, 0);

    public static void SetStars(int level, int stars)
    {
        if (stars <= GetStars(level)) return;
        PlayerPrefs.SetInt(P + "star_" + level, stars);
        PlayerPrefs.Save();
    }

    public static int GetBoost(string key) => PlayerPrefs.GetInt(P + "b_" + key, key == "moves" ? 1 : 2);

    public static void SetBoost(string key, int value)
    {
        PlayerPrefs.SetInt(P + "b_" + key, Mathf.Max(0, value));
        PlayerPrefs.Save();
    }

    public static bool Sound { get => PlayerPrefs.GetInt(P + "snd", 1) == 1; set { PlayerPrefs.SetInt(P + "snd", value ? 1 : 0); PlayerPrefs.Save(); } }
    public static bool Music { get => PlayerPrefs.GetInt(P + "mus", 1) == 1; set { PlayerPrefs.SetInt(P + "mus", value ? 1 : 0); PlayerPrefs.Save(); } }
    public static bool Vibration { get => PlayerPrefs.GetInt(P + "vib", 1) == 1; set { PlayerPrefs.SetInt(P + "vib", value ? 1 : 0); PlayerPrefs.Save(); } }

    // Clears progress (levels, stars, coins, boosters) but keeps settings.
    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(P + "max");
        PlayerPrefs.DeleteKey(P + "coins");
        for (int i = 1; i <= LevelLibrary.Count; i++) PlayerPrefs.DeleteKey(P + "star_" + i);
        foreach (var k in new[] { "hammer", "bomb", "moves" }) PlayerPrefs.DeleteKey(P + "b_" + k);
        PlayerPrefs.Save();
    }
}
