using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Plays the recorded English narration and its matching approved Chinese subtitle.</summary>
[DisallowMultipleComponent]
public sealed class FairyTaleNarrator : MonoBehaviour
{
    public const string GuardianRelicPickupId = "20_guardian_relic_pickup";
    public const string OpeningId = "01_adventure";
    public const string DispersalId = "24_demon_dispersal";
    public const string CastleEndingId = "08_boss_defeated";
    public const string EpilogueId = "09_epilogue";

    [Serializable]
    public sealed class Line {
        public string id, zh, en;
        public Part[] parts;
    }
    [Serializable]
    public sealed class Part { public string zh, en, clip; }
    [Serializable]
    sealed class Script { public Line[] lines; }

    readonly Dictionary<string, Line> lines = new Dictionary<string, Line>();
    readonly Dictionary<string, AudioClip> recordings = new Dictionary<string, AudioClip>();
    readonly List<Line> queue = new List<Line>();
    readonly HashSet<string> heard = new HashSet<string>();
    ShadowLab lab;
    AudioSource voice;
    ShiyinAudio mix;
    Font font;
    Text subtitle;
    Vector2 subtitleViewport;
    float subtitleScale;
    Line current;
    int partIndex;
    float remaining, gap;
    bool paused, opening, ending, endingSpeechComplete;
    public string CurrentId => current?.id;
    public string CurrentSubtitle => current == null ? "" : current.parts[partIndex].zh;
    public bool Speaking => current != null;
    public bool EndingSpeechPending => ending && !endingSpeechComplete;
    public ICollection<Line> ApprovedLines => lines.Values;
    public bool IsPending(string id) => queue.Exists(line => line.id == id);

    public void Initialize(ShadowLab encounter) {
        lab = encounter;
        var asset = Resources.Load<TextAsset>("Narration/approved");
        if (asset == null) throw new InvalidOperationException("Missing approved narration script.");
        foreach (var line in JsonUtility.FromJson<Script>(asset.text).lines)
            lines.Add(line.id, line);
        voice = lab.Eye.gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0;
        voice.dopplerLevel = 0;
        voice.priority = 16;
        mix = lab.Eye.GetComponent<ShiyinAudio>();
        font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei",22);
        var host = new GameObject("Narration subtitles", typeof(RectTransform), typeof(Text), typeof(Outline));
        host.transform.SetParent(lab.UI.UiCanvas.transform, false);
        subtitle = host.GetComponent<Text>();
        subtitle.font = font;
        subtitle.alignment = TextAnchor.MiddleCenter;
        subtitle.supportRichText = false;
        subtitle.raycastTarget = false;
        subtitle.color = new Color(.96f, .94f, .88f);
        subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
        subtitle.resizeTextForBestFit = true;
        var outline = host.GetComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, .92f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        subtitle.rectTransform.anchorMin = subtitle.rectTransform.anchorMax = new Vector2(.5f, 0);
        host.SetActive(false);
        var glyphs = new HashSet<char>();
        foreach (var line in lines.Values)
            foreach (var part in line.parts)
                foreach (char glyph in part.zh) glyphs.Add(glyph);
        var characters = new char[glyphs.Count];
        glyphs.CopyTo(characters);
        font.RequestCharactersInTexture(new string(characters),
            Mathf.Max(1, Mathf.RoundToInt(22 * (lab.UI?.Settings.TextScale ?? 1) * Screen.height / 1080f)));
    }
    public string Subtitle(string id) => lines.TryGetValue(id, out var line) ? line.zh : "";
    AudioClip Recording(Part part) {
        if (!recordings.TryGetValue(part.clip, out var clip)) {
            clip = Resources.Load<AudioClip>("Narration/en/" + part.clip);
            recordings.Add(part.clip, clip);
        }
        return clip;
    }
    static float PartDuration(Part part, AudioClip clip) =>
        clip != null ? clip.length : Mathf.Max(2, part.zh.Length * .21f);
    public float LineDuration(string id) {
        float seconds = 0;
        foreach (var part in lines[id].parts) seconds += PartDuration(part, Recording(part));
        return seconds;
    }
    public void ResetNarration() {
        EndEnding();
        queue.Clear();
        gap = 0;
        opening = false;
        // A retry can replay interrupted teaching, but never repeats a completed line.
    }
    internal void BeginOpening() {
        paused=false;
        opening=true;
        Play(lines[OpeningId]);
    }
    internal float SecondsLeftInPart => Mathf.Max(0,remaining);
    static int Priority(string id) {
        switch (id) {
            case "03_fire_thief": case "14_guardian_trial": case "17_final_blackout": case "18_final_relight":
                return 3;
            case "13_guardian_entrance": case "15_guardian_defeated": case GuardianRelicPickupId:
            case "16_final_fog_gate": case "21_first_boss_wound": return 2;
            default: return 1;
        }
    }
    public void Request(string id) {
        if (lab == null || lab.Finished || ending || id == null || id == OpeningId ||
            id == DispersalId || id == CastleEndingId || id == EpilogueId || !lines.TryGetValue(id, out var entry)) return;
        // Opposing cues can arrive even after an earlier tutorial has been heard.
        // Only withdraw unsaid, obsolete instructions. Let spoken sentences resolve.
        if (id == "18_final_relight") queue.RemoveAll(line => line.id == "17_final_blackout");
        if (id == "17_final_blackout") queue.RemoveAll(line => line.id == "18_final_relight");
        if (id == "15_guardian_defeated") queue.RemoveAll(line => line.id == "14_guardian_trial");
        if (heard.Contains(id) || CurrentId == id || IsPending(id)) return;
        int priority = Priority(id);
        int index = queue.FindIndex(line => Priority(line.id) < priority);
        if (index < 0) queue.Add(entry);
        else queue.Insert(index, entry);
    }
    void Play(Line line) {
        current = line;
        partIndex = 0;
        PlayPart();
    }
    void PlayPart() {
        var part = current.parts[partIndex];
        voice.clip = Recording(part);
        voice.volume = lab.UI?.Settings.Narration ?? .85f;
        remaining = PartDuration(part, voice.clip);
        // Keep muted speech in sync if the player raises the slider mid-sentence.
        if (voice.clip != null) voice.Play();
        mix?.SetNarrationDucking(voice.clip != null && voice.volume > 0);
    }
    void StopLine() {
        if (voice != null) {
            voice.Stop();
            voice.clip = null;
        }
        current = null;
        remaining = 0;
        mix?.SetNarrationDucking(false);
    }
    public void PlayEnding(string id) {
        if (ending && CurrentId == id) return;
        StopLine();
        queue.Clear();
        ending = true;
        endingSpeechComplete = false;
        paused = false;
        Play(lines[id]);
    }
    public void EndEnding() {
        StopLine();
        ending = false;
        endingSpeechComplete = false;
        paused = false;
    }
    void Update() => Tick(Time.unscaledDeltaTime, true);
    // The chapter's deterministic Player checks can advance this same queue without waiting on the audio device.
    internal void Advance(float dt) => Tick(dt, false);
    void Tick(float dt, bool useAudioClock) {
        if (lab == null || voice == null) return;
        voice.volume = lab.UI?.Settings.Narration ?? .85f;
        mix?.SetNarrationDucking(voice.isPlaying && voice.volume > 0);
        if (opening) {
            if (AdvanceSpeech(dt,useAudioClock)) { opening=false; StopLine(); }
            return;
        }
        // Ending pages own the same voice channel while the combat simulation is frozen.
        if (ending) {
            if (!endingSpeechComplete) endingSpeechComplete = AdvanceSpeech(dt, useAudioClock);
            return;
        }
        if (lab.Finished) {
            if (current != null) StopLine();
            queue.Clear();
            return;
        }
        if (paused != lab.GameplayPaused) {
            paused = lab.GameplayPaused;
            if (paused) voice.Pause();
            else voice.UnPause();
        }
        if (paused) return;
        gap = Mathf.Max(0, gap - dt);
        if (current != null && AdvanceSpeech(dt, useAudioClock)) {
            heard.Add(current.id);
            StopLine();
            gap = .35f;
        }
        if (current != null || queue.Count == 0 || gap > 0) return;
        var next = queue[0];
        queue.RemoveAt(0);
        Play(next);
    }
    bool AdvanceSpeech(float dt, bool useAudioClock) {
        remaining -= dt;
        if (useAudioClock && voice.clip != null && voice.isPlaying)
            remaining = Mathf.Max(.001f, (voice.clip.samples - voice.timeSamples) / (float)voice.clip.frequency);
        if (remaining > 0) return false;
        if (partIndex + 1 == current.parts.Length) return true;
        partIndex++;
        PlayPart();
        return false;
    }
    void LateUpdate() {
        if (subtitle == null) return;
        bool visible = !ending && current != null && !lab.GameplayPaused && !lab.Developer.HudHidden &&
                       lab.UI.Settings.NarrationSubtitles;
        if (subtitle.gameObject.activeSelf != visible) subtitle.gameObject.SetActive(visible);
        if (!visible) return;
        var canvas = lab.UI.UiCanvas;
        var viewport = ((RectTransform)canvas.transform).rect.size;
        float scale = lab.UI.Settings.TextScale;
        if (subtitleViewport != viewport || subtitleScale != scale) {
            subtitleViewport = viewport;
            subtitleScale = scale;
            var safe = SubtitleSafeRect(viewport.x, viewport.y);
            subtitle.rectTransform.sizeDelta = new Vector2(Mathf.Max(1, safe.width - 8), Mathf.Max(1, safe.height - 8));
            subtitle.rectTransform.anchoredPosition = new Vector2(0, viewport.y - safe.center.y);
            subtitle.resizeTextMinSize = 12;
            subtitle.resizeTextMaxSize = subtitle.fontSize = Mathf.Max(12, Mathf.RoundToInt(22 * scale));
        }
        if (subtitle.text != CurrentSubtitle) subtitle.text = CurrentSubtitle;
    }
    public static Rect SubtitleSafeRect(float screenWidth,float screenHeight) {
        float scale=screenHeight/1080f;
        float width=Mathf.Min(1000*scale,Mathf.Max(240*scale,screenWidth-1000*scale));
        // At the 1080px reference height, the toast ends at y=863. Text starts
        // at 876, leaving 13px (11.5px including its outline), and ends at 1012
        // above the developer label at 1024. Health and ammo sit to either side.
        return new Rect((screenWidth-width)*.5f,screenHeight-204*scale,width,136*scale);
    }
    void OnDisable() {
        ResetNarration();
        if (subtitle != null) subtitle.gameObject.SetActive(false);
    }
    void OnDestroy() { if(subtitle!=null)Destroy(subtitle.gameObject);if(voice!=null)Destroy(voice);if(font!=null)Destroy(font); }
}
