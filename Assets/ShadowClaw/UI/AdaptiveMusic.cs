using UnityEngine;

public enum AdaptiveMusicCue { Exploration, MinorCombat, Guardian, FinalBoss, FinalBossBlackout }

/// <summary>
/// Five authored orchestral arrangements. Paired arrangements have identical sample counts
/// and start on the same DSP tick; changing a cue only changes gains, never playback position.
/// </summary>
[DisallowMultipleComponent]
public sealed class AdaptiveMusic : MonoBehaviour
{
    public const string ResourceFolder = "Music/DarkOrchestra/";
    static readonly string[] ClipNames = {
        "MisteryVillage_Explore", "MisteryVillage_Combat", "Cursed_Guardian",
        "UntilDeath_Normal", "UntilDeath_Blackout"
    };
    readonly AudioSource[] sources = new AudioSource[5];
    readonly float[] weights = { 1, 0, 0, 0, 0 };
    readonly float[] fadeFrom = new float[5];
    readonly int[] resumeSamples = new int[5];
    AudioSource titleSource;
    const float TitleGain = .4f;
    float titleFade, huntFade = 1;
    bool onTitle, titlePlaying;
    ShadowLab encounter;
    double scheduledStartDsp;
    float volume = .55f, duckGain = 1, fadeElapsed, fadeDuration, minorRelease;
    bool initialized, started, externallyPaused, transportPaused, speaking, applicationPaused;

    public bool FollowGameplay = true;
    public AdaptiveMusicCue CurrentCue { get; private set; } = AdaptiveMusicCue.Exploration;
    public bool IsPaused => transportPaused;
    public float NarrationGain => duckGain;
    public int TransportGeneration { get; private set; }
    public bool HasMusic {
        get { foreach (var source in sources) if (source != null && source.clip != null) return true; return false; }
    }
    public bool AllClipsAvailable {
        get { foreach (var source in sources) if (source == null || source.clip == null) return false; return true; }
    }

    void Awake() { Initialize(); }

    void Initialize()
    {
        if (initialized) return;
        initialized = true;
        for (int i = 0; i < sources.Length; i++) {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0;
            source.dopplerLevel = 0;
            source.pitch = 1;
            source.priority = 64;
            source.volume = 0;
            source.clip = Resources.Load<AudioClip>(ResourceFolder + ClipNames[i]);
            sources[i] = source;
        }
        ValidatePair(0, 1);
        ValidatePair(3, 4);
    }

    void ValidatePair(int first, int second)
    {
        var a = sources[first].clip;
        var b = sources[second].clip;
        if (a != null && b != null && (a.samples != b.samples || a.frequency != b.frequency))
            Debug.LogError("Adaptive music arrangements must have identical sample lengths/rates: " + a.name, this);
    }

    public void Bind(ShadowLab lab) { encounter = lab; }
    public void SetVolume(float value) { volume = ShiyinSettings.FiniteClamp(value, 0, 1, .55f); ApplyVolumes(); }
    public void SetNarrationDucking(bool value) { speaking = value; }
    public void SetPaused(bool value) { externallyPaused = value; RefreshPause(); }

    public void SetTitlePage(bool active)
    {
        if (active == onTitle) return;
        onTitle = active;
        if (active && titleSource == null) {
            titleSource = gameObject.AddComponent<AudioSource>();
            titleSource.playOnAwake = false;
            titleSource.loop = true;
            titleSource.spatialBlend = 0;
            titleSource.volume = 0;
            titleSource.clip = Resources.Load<AudioClip>(ResourceFolder + "Title_Veil");
        }
        if (titleSource != null && titleSource.clip != null) huntFade = 0;
        ApplyVolumes();
    }

    void UpdateTitle(float dt)
    {
        if (titleSource == null || titleSource.clip == null || applicationPaused) return;
        if (onTitle && !titlePlaying) {
            titleSource.Play();
            titlePlaying = true;
        }
        titleFade = Mathf.MoveTowards(titleFade, onTitle ? 1 : 0, dt / (onTitle ? 4.5f : 1.2f));
        if (!onTitle && titleFade == 0 && titlePlaying) {
            titleSource.Stop();
            titlePlaying = false;
        }
        if (!onTitle && titleFade == 0 && !transportPaused)
            huntFade = Mathf.MoveTowards(huntFade, 1, dt / 1.8f);
    }

    public static AdaptiveMusicCue ResolveCue(bool minorCombat, bool guardian, bool finalBoss, int litLamps)
    {
        if (finalBoss) return litLamps == 0 ? AdaptiveMusicCue.FinalBossBlackout : AdaptiveMusicCue.FinalBoss;
        if (guardian) return AdaptiveMusicCue.Guardian;
        return minorCombat ? AdaptiveMusicCue.MinorCombat : AdaptiveMusicCue.Exploration;
    }

    /// <summary>Also available to authored sequences with FollowGameplay disabled.</summary>
    public void SetCue(AdaptiveMusicCue cue)
    {
        if ((int)cue < 0 || (int)cue >= sources.Length || cue == CurrentCue) return;
        for (int i = 0; i < weights.Length; i++) fadeFrom[i] = weights[i];
        CurrentCue = cue;
        fadeElapsed = 0;
        fadeDuration = cue == AdaptiveMusicCue.FinalBossBlackout ? .65f
            : cue == AdaptiveMusicCue.Exploration ? 3f
            : cue == AdaptiveMusicCue.MinorCombat ? 1.2f : 1.6f;
    }

    void Update()
    {
        RefreshPause();
        UpdateTitle(Time.unscaledDeltaTime);
        if (transportPaused) { ApplyVolumes(); return; }
        float dt = Time.unscaledDeltaTime;
        if (FollowGameplay && encounter != null && encounter.GameplayReady) {
            minorRelease = encounter.MinorCombatActive ? 3f : Mathf.Max(0, minorRelease - dt);
            SetCue(ResolveCue(minorRelease > 0, encounter.ActiveGuardian != null,
                encounter.EncounterStarted, encounter.LitLamps));
        }
        StartTransport();
        if (fadeDuration > 0) {
            fadeElapsed = Mathf.Min(fadeDuration, fadeElapsed + dt);
            float blend = Mathf.SmoothStep(0, 1, fadeElapsed / fadeDuration);
            for (int i = 0; i < weights.Length; i++)
                weights[i] = Mathf.Lerp(fadeFrom[i], i == (int)CurrentCue ? 1 : 0, blend);
            if (fadeElapsed >= fadeDuration) fadeDuration = 0;
        }
        // The voice retains its own level; only music ducks, including during a cue transition.
        duckGain = Mathf.MoveTowards(duckGain, speaking ? .3f : 1,
            dt / (speaking ? .18f : .85f));
        ApplyVolumes();
    }

    void StartTransport()
    {
        if (started || !Application.isPlaying || !HasMusic) return;
        ScheduleFromBookmarks();
        started = true;
        TransportGeneration++;
    }

    void ScheduleFromBookmarks()
    {
        // Seek while stopped, then give every preloaded decoder the same future start.
        // A pause/resume does not start a new musical run or reset the phrase to zero.
        StopAtBookmarks();
        scheduledStartDsp = AudioSettings.dspTime + .15;
        foreach (var source in sources)
            if (source != null && source.clip != null) source.PlayScheduled(scheduledStartDsp);
    }

    void StopAtBookmarks()
    {
        for (int i = 0; i < sources.Length; i++) {
            var source = sources[i];
            if (source == null || source.clip == null) continue;
            source.Stop();
            source.timeSamples = Mathf.Clamp(resumeSamples[i], 0, source.clip.samples - 1);
        }
    }

    void CaptureBookmarks()
    {
        // A rapid re-pause before the scheduled start must keep its prior bookmark.
        // Some audio backends do not expose a pending decoder's seek position yet.
        if (AudioSettings.dspTime < scheduledStartDsp) return;
        // Read all references before stopping any source. Sequential Pause/UnPause
        // can straddle an audio buffer and leave correlated arrangements out of phase.
        resumeSamples[0] = resumeSamples[1] = SamplePosition(AdaptiveMusicCue.Exploration);
        resumeSamples[2] = SamplePosition(AdaptiveMusicCue.Guardian);
        resumeSamples[3] = resumeSamples[4] = SamplePosition(AdaptiveMusicCue.FinalBoss);
    }

    void RefreshPause()
    {
        bool pause = externallyPaused || applicationPaused || !isActiveAndEnabled
            || (FollowGameplay && encounter != null && encounter.GameplayPaused);
        if (transportPaused == pause) return;
        transportPaused = pause;
        if (!started) return;
        if (pause) { CaptureBookmarks(); StopAtBookmarks(); }
        else ScheduleFromBookmarks();
    }

    void ApplyVolumes()
    {
        for (int i = 0; i < sources.Length; i++)
            if (sources[i] != null) sources[i].volume = volume * duckGain * weights[i] * huntFade;
        if (titleSource != null) titleSource.volume = volume * TitleGain * Mathf.SmoothStep(0, 1, titleFade);
    }

    public int SamplePosition(AdaptiveMusicCue cue) => sources[(int)cue] != null ? sources[(int)cue].timeSamples : 0;
    public float TrackVolume(AdaptiveMusicCue cue) => sources[(int)cue] != null ? sources[(int)cue].volume : 0;
    public AudioClip Clip(AdaptiveMusicCue cue) => sources[(int)cue] != null ? sources[(int)cue].clip : null;

    /// <summary>Restart a new run without carrying battle intensity or narrator ducking over.</summary>
    public void ResetMusic()
    {
        System.Array.Clear(resumeSamples, 0, resumeSamples.Length);
        scheduledStartDsp = 0;
        StopAtBookmarks();
        started = false;
        minorRelease = fadeElapsed = fadeDuration = 0;
        CurrentCue = AdaptiveMusicCue.Exploration;
        speaking = false;
        duckGain = 1;
        for (int i = 0; i < weights.Length; i++) weights[i] = fadeFrom[i] = i == 0 ? 1 : 0;
        ApplyVolumes();
    }

    void OnApplicationPause(bool value) {
        applicationPaused = value;
        if (titleSource != null && titlePlaying) {
            if (value) titleSource.Pause(); else titleSource.UnPause();
        }
        RefreshPause();
    }
    void OnEnable() { RefreshPause(); }
    void OnDisable() {
        RefreshPause();
        if (titleSource != null) titleSource.Stop();
        titlePlaying = false;
        titleFade = 0;
    }
    void OnDestroy()
    {
        if (titleSource != null) {
            if (Application.isPlaying) Destroy(titleSource); else DestroyImmediate(titleSource);
        }
        foreach (var source in sources) {
            if (source == null) continue;
            source.Stop();
            if (Application.isPlaying) Destroy(source); else DestroyImmediate(source);
        }
    }
}
