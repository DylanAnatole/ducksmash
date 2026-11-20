using UnityEngine;

// Daily quest: smash N things today -> coins, paid automatically the moment it's done.
// Counts are buffered in memory and written at the end of each shot (no PlayerPrefs write per smash).
public static class DailyQuest
{
    public const int Target = 150, Reward = 80;
    static string Today => System.DateTime.Now.ToString("yyyyMMdd");
    static int pending;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => pending = 0;

    static void Roll()
    {
        if (PlayerPrefs.GetString("ds_dq_day", "") == Today) return;
        PlayerPrefs.SetString("ds_dq_day", Today); PlayerPrefs.SetInt("ds_dq_n", 0); PlayerPrefs.SetInt("ds_dq_done", 0);
    }
    public static int Progress { get { Roll(); return Mathf.Min(Target, PlayerPrefs.GetInt("ds_dq_n", 0) + pending); } }
    public static bool Done { get { Roll(); return PlayerPrefs.GetInt("ds_dq_done", 0) == 1; } }

    public static void Add(int n) => pending += n;

    // returns true exactly once, when today's quest gets completed (reward already paid)
    public static bool Flush()
    {
        if (pending == 0) return false;
        Roll();
        int now = PlayerPrefs.GetInt("ds_dq_n", 0) + pending; pending = 0;
        PlayerPrefs.SetInt("ds_dq_n", now);
        bool fin = now >= Target && PlayerPrefs.GetInt("ds_dq_done", 0) == 0;
        if (fin) { PlayerPrefs.SetInt("ds_dq_done", 1); SaveData.Coins += Reward; }
        PlayerPrefs.Save();
        return fin;
    }
}
