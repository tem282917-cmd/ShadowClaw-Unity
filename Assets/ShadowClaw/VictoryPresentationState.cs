using System;

// Presentation time is independent of the finished combat simulation.
public sealed class VictoryPresentationState
{
    public enum Stage { Idle, Death, FadeOut, Story, MenuReveal, Complete }
    float death, fadeOut, reveal, elapsed;
    float[] pageStarts = Array.Empty<float>();
    bool begun;
    public float Elapsed => elapsed;
    public float DeathDuration => death;
    public float StoryStart => pageStarts.Length == 0 ? 0 : pageStarts[0];
    float RevealStart => pageStarts.Length == 0 ? 0 : pageStarts[pageStarts.Length - 1];
    public float TotalDuration => RevealStart + reveal;
    public Stage Phase => !begun ? Stage.Idle : elapsed < death ? Stage.Death :
        elapsed < StoryStart ? Stage.FadeOut : elapsed < RevealStart ? Stage.Story :
        elapsed < TotalDuration ? Stage.MenuReveal : Stage.Complete;
    public bool Active => begun && Phase != Stage.Complete;
    public bool MenuReady => begun && elapsed >= RevealStart;
    public float DeathProgress => begun ? Clamp(elapsed / death) : 0;
    public float BlackOpacity => !begun || Phase == Stage.Death ? 0 :
        Phase == Stage.FadeOut ? Smooth((elapsed - death) / fadeOut) :
        Phase == Stage.Story ? 1 : 1 - Smooth((elapsed - RevealStart) / reveal);
    public int StoryIndex {
        get {
            if (Phase != Stage.Story) return -1;
            int index = Array.BinarySearch(pageStarts, elapsed);
            return index >= 0 ? index : ~index - 1;
        }
    }
    public float PageDuration(int index) => pageStarts[index + 1] - pageStarts[index];
    public float NextVoiceBoundary => StoryIndex < 0 ? StoryStart : pageStarts[StoryIndex + 1];
    public float StoryOpacity {
        get {
            if (StoryIndex < 0) return 0;
            float age = elapsed - pageStarts[StoryIndex];
            float storyLine = PageDuration(StoryIndex);
            float edge = Math.Min(.25f, storyLine * .2f);
            return Math.Min(Smooth(age / edge), Smooth((storyLine - age) / edge));
        }
    }
    static float Clamp(float value) => Math.Max(0, Math.Min(1, value));
    static float Smooth(float value) { float t = Clamp(value); return t * t * (3 - 2 * t); }
    static float Duration(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(.1f, value);
    public void Begin(float deathSeconds, float fadeSeconds, float lineSeconds, int paragraphs, float revealSeconds) {
        float[] durations = new float[Math.Max(1, paragraphs)];
        for (int i = 0; i < durations.Length; i++) durations[i] = lineSeconds;
        Begin(deathSeconds, fadeSeconds, 0, durations, revealSeconds);
    }
    public void Begin(float deathSeconds, float fadeSeconds, float deathSpeechSeconds, float[] pageSeconds, float revealSeconds) {
        death = Duration(deathSeconds, 3.2f); fadeOut = Duration(fadeSeconds, 1.4f);
        reveal = Duration(revealSeconds, 1.4f);
        pageStarts = new float[pageSeconds.Length + 1];
        pageStarts[0] = Math.Max(death + fadeOut, Duration(deathSpeechSeconds, 0));
        for (int i = 0; i < pageSeconds.Length; i++)
            pageStarts[i + 1] = pageStarts[i] + Duration(pageSeconds[i], 4.8f);
        elapsed = 0; begun = true;
    }
    public void Advance(float seconds) {
        if (!Active || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        elapsed = Math.Min(TotalDuration, elapsed + seconds);
    }
    public void Reset() { elapsed = 0; begun = false; }
}
