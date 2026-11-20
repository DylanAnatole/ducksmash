using System;
using System.Collections.Generic;
using UnityEngine;

// Procedurally generated sound effects + a soft music loop (no audio assets needed).
public class Sfx : MonoBehaviour
{
    static Sfx I;
    AudioSource sfx, music;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
    const int SR = 44100;
    int frame, playedThisFrame;

    public static void Init(GameObject host)
    {
        I = host.AddComponent<Sfx>();
        I.sfx = host.AddComponent<AudioSource>();
        I.music = host.AddComponent<AudioSource>();
        I.music.loop = true; I.music.volume = 0.16f;
        I.Build();
        I.music.clip = I.clips["music"];
        I.music.Play();
        ApplySettings();
    }

    public static void ApplySettings()
    {
        if (I == null) return;
        I.music.mute = !SaveData.Music;
    }

    public static void Play(string name, float volume = 1f, float pitch = 1f)
    {
        if (I == null || !SaveData.Sound || !I.clips.TryGetValue(name, out var clip)) return;
        // every PlayOneShot is a new WebAudio node: throttle each sound and cap sounds per frame
        if (Time.frameCount != I.frame) { I.frame = Time.frameCount; I.playedThisFrame = 0; }
        if (I.playedThisFrame >= 3) return;
        I.lastPlayed.TryGetValue(name, out float last);
        if (Time.unscaledTime - last < (name == "pop" || name == "crunch" || name == "wood" || name == "splash" || name == "ice" ? 0.05f : 0.03f)) return;
        I.lastPlayed[name] = Time.unscaledTime; I.playedThisFrame++;
        I.sfx.pitch = pitch;
        I.sfx.PlayOneShot(clip, volume);
    }

    public static void Vibrate()
    {
#if UNITY_ANDROID || UNITY_IOS
        if (SaveData.Vibration) Handheld.Vibrate();
#endif
    }

    // ---------- synthesis ----------
    void Build()
    {
        var rnd = new System.Random(7);
        float Noise() => (float)rnd.NextDouble() * 2f - 1f;

        // Phase-accumulating oscillator so pitch sweeps sound clean (no chirp artifacts).
        Func<float, float> Osc(Func<float, float> freq, Func<float, float> env, int shape = 0)
        {
            float ph = 0;
            return t =>
            {
                ph += 2 * Mathf.PI * freq(t) / SR;
                float w = shape == 0 ? Mathf.Sin(ph) : shape == 1 ? Mathf.Sign(Mathf.Sin(ph)) * 0.6f : (Mathf.Repeat(ph / (2 * Mathf.PI), 1) * 2 - 1) * 0.7f;
                return w * env(t);
            };
        }
        Func<float, float> Filtered(float k, Func<float, float> env) { float lp = 0; return t => { lp = Mathf.Lerp(lp, Noise(), k); return lp * env(t); }; }

        // bubbly pop
        clips["pop"] = Make("pop", 0.1f, Osc(t => Mathf.Lerp(1200, 420, Mathf.Sqrt(t / 0.1f)), t => Mathf.Exp(-t * 38) * Mathf.Min(1, t * 900)));
        // cookie crunch: short band-limited noise grains
        var crunchN = Filtered(0.55f, t => 1);
        clips["crunch"] = Make("crunch", 0.16f, t => crunchN(t) * (Mathf.Exp(-t * 30) + 0.5f * Mathf.Exp(-Mathf.Abs(t - 0.05f) * 120) + 0.35f * Mathf.Exp(-Mathf.Abs(t - 0.09f) * 140)) * 1.6f);
        // wood knock
        var woodO = Osc(t => Mathf.Lerp(340, 220, t / 0.15f), t => Mathf.Exp(-t * 32));
        var woodN = Filtered(0.3f, t => Mathf.Exp(-t * 60));
        clips["wood"] = Make("wood", 0.15f, t => woodO(t) * 0.9f + woodN(t) * 1.2f);
        // rubber-duck squeak: rising then falling nasal tone with vibrato
        var sq = Osc(t => 900 + 700 * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.22f)) + 40 * Mathf.Sin(t * 120), t => Mathf.Min(1, t * 60) * Mathf.Exp(-t * 7), 2);
        clips["squeak"] = Make("squeak", 0.26f, t => sq(t) * 0.7f);
        // water splash
        var spN = Filtered(0.35f, t => Mathf.Min(1, t * 200) * Mathf.Exp(-t * 14));
        var spB = Osc(t => Mathf.Lerp(500, 1400, t / 0.25f), t => Mathf.Exp(-t * 25) * 0.25f);
        clips["splash"] = Make("splash", 0.3f, t => spN(t) * 1.4f + spB(t));
        clips["click"] = Make("click", 0.05f, Osc(t => 1800, t => Mathf.Exp(-t * 110)));
        clips["push"] = Make("push", 0.12f, Osc(t => Mathf.Lerp(260, 110, t / 0.12f), t => Mathf.Exp(-t * 30)));
        var whoosh = Filtered(0.12f, t => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.22f)));
        clips["shoot"] = Make("shoot", 0.22f, t => whoosh(t) * 2.2f);
        var boomN = Filtered(0.1f, t => Mathf.Exp(-t * 5.5f)); var boomS = Osc(t => Mathf.Lerp(90, 40, t / 0.7f), t => Mathf.Exp(-t * 6));
        clips["boom"] = Make("boom", 0.7f, t => boomN(t) * 2.4f + boomS(t) * 0.9f);
        var crashN = Filtered(0.22f, t => Mathf.Exp(-t * 4.5f)); var crashS = Osc(t => Mathf.Lerp(140, 60, t / 0.6f), t => Mathf.Exp(-t * 8));
        clips["crash"] = Make("crash", 0.7f, t => crashN(t) * 1.8f + crashS(t) * 0.7f);
        clips["zap"] = Make("zap", 0.22f, Osc(t => 700 + 500 * Mathf.Sin(t * 260), t => Mathf.Exp(-t * 12) * 0.5f, 1));
        var lz1 = Osc(t => Mathf.Lerp(400, 2200, t / 0.35f), t => Mathf.Exp(-t * 5) * 0.5f, 2);
        var lz2 = Osc(t => Mathf.Lerp(404, 2210, t / 0.35f), t => Mathf.Exp(-t * 5) * 0.5f, 2);
        clips["laser"] = Make("laser", 0.35f, t => lz1(t) + lz2(t));
        var iceO = Osc(t => 2600, t => Mathf.Exp(-t * 30) * 0.5f); var iceN = Filtered(0.8f, t => Mathf.Exp(-t * 45));
        clips["ice"] = Make("ice", 0.18f, t => iceO(t) + iceN(t) * 0.8f);
        clips["win"] = Make("win", 1.1f, t => Arp(t, new[] { 523f, 659f, 784f, 1047f, 1319f }, 0.12f));
        clips["lose"] = Make("lose", 0.9f, t => Arp(t, new[] { 392f, 349f, 294f, 262f }, 0.2f));
        clips["coin"] = Make("coin", 0.3f, t => Mathf.Sin(2 * Mathf.PI * (t < 0.07f ? 988 : 1319) * t) * Mathf.Exp(-t * 10) * 0.6f);
        clips["star"] = Make("star", 0.5f, t => (Mathf.Sin(2 * Mathf.PI * 1568 * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * 2349 * t)) * Mathf.Exp(-t * 7) * 0.45f);
        clips["combo"] = Make("combo", 0.35f, t => Arp(t, new[] { 784f, 988f, 1175f }, 0.07f));
        var frz = Osc(t => Mathf.Lerp(200, 1200, t / 0.6f), t => Mathf.Sin(Mathf.PI * t / 0.6f) * 0.4f, 2);
        var frzN = Filtered(0.2f, t => Mathf.Sin(Mathf.PI * t / 0.6f));
        clips["frenzy"] = Make("frenzy", 0.6f, t => frz(t) + frzN(t) * 1.2f);
        // rubber duck "quack": nasal saw with a falling pitch and quick vibrato, low-passed
        var qk = Osc(t => 420 - 160 * (t / 0.2f) + 25 * Mathf.Sin(t * 180), t => Mathf.Min(1, t * 80) * Mathf.Exp(-t * 9), 2);
        float qlp = 0;
        clips["quack"] = Make("quack", 0.22f, t => { qlp = Mathf.Lerp(qlp, qk(t), 0.35f); return qlp * 1.3f; });
        clips["music"] = Music();
    }

    static float Arp(float t, float[] notes, float step)
    {
        int i = Mathf.Min(notes.Length - 1, (int)(t / step));
        float lt = t - i * step;
        return Mathf.Sin(2 * Mathf.PI * notes[i] * t) * Mathf.Exp(-lt * 6) * 0.6f;
    }

    static AudioClip Make(string name, float dur, Func<float, float> f)
    {
        int n = Mathf.CeilToInt(dur * SR);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)SR), -1f, 1f) * 0.8f;
        var clip = AudioClip.Create(name, n, 1, SR, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Bouncy 8-bar loop (~17 s @ 112 BPM): soft kick, plucked bass, ukulele-like arpeggio, pad, shaker.
    static AudioClip Music()
    {
        float[][] chords = {
            new[] { 261.6f, 329.6f, 392f }, new[] { 220f, 261.6f, 329.6f }, new[] { 174.6f, 220f, 261.6f }, new[] { 196f, 246.9f, 293.7f },
            new[] { 261.6f, 329.6f, 392f }, new[] { 164.8f, 196f, 246.9f }, new[] { 174.6f, 220f, 261.6f }, new[] { 196f, 246.9f, 293.7f } };
        const float bpm = 112f; float beat = 60f / bpm, bar = beat * 4, eighth = beat / 2;
        int n = (int)(bar * 8 * SR);
        var data = new float[n];
        var rnd = new System.Random(11); float hp = 0, prevN = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR; int b = (int)(t / bar) % 8; float lt = t - (int)(t / bar) * bar;
            var ch = chords[b];
            float s = 0;
            // pad (quiet, slow swell)
            foreach (var f in ch) s += Mathf.Sin(2 * Mathf.PI * f * t) * 0.05f;
            // kick on beats 1 & 3
            float kb = lt % (beat * 2); s += Mathf.Sin(2 * Mathf.PI * (55 + 90 * Mathf.Exp(-kb * 30)) * kb) * Mathf.Exp(-kb * 9) * 0.35f;
            // plucked bass: root on beat 1/3, octave on the "and" of 2/4
            float bt = lt % beat; int bi = (int)(lt / beat);
            float bf = ch[0] / 2 * (bi % 2 == 1 && bt > eighth ? 2 : 1); float bt2 = bt > eighth && bi % 2 == 1 ? bt - eighth : bt;
            s += (Mathf.Sin(2 * Mathf.PI * bf * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * bf * t)) * Mathf.Exp(-bt2 * 6) * 0.22f;
            // ukulele arpeggio in 8ths
            int ei = (int)(lt / eighth); float et = lt - ei * eighth; int[] pat = { 0, 1, 2, 1, 0, 2, 1, 2 };
            float af = ch[pat[ei % 8]] * 2;
            s += (Mathf.Sin(2 * Mathf.PI * af * t) + 0.5f * Mathf.Sin(4 * Mathf.PI * af * t) + 0.25f * Mathf.Sin(6 * Mathf.PI * af * t)) * Mathf.Exp(-et * 14) * 0.11f;
            // shaker on off-beats (high-passed noise)
            float nz = (float)rnd.NextDouble() * 2 - 1; hp = 0.85f * (hp + nz - prevN); prevN = nz;
            float st = lt % eighth; bool off = ((int)(lt / eighth)) % 2 == 1;
            s += hp * Mathf.Exp(-st * 60) * (off ? 0.07f : 0.03f);
            data[i] = Mathf.Clamp(s, -1, 1) * 0.8f;
        }
        var clip = AudioClip.Create("music", n, 1, SR, false);
        clip.SetData(data, 0);
        return clip;
    }
}
