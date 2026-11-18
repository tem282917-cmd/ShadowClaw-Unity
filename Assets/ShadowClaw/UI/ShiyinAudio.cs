using System.Collections.Generic;
using UnityEngine;

/// <summary>Independent effect/music gain, authored adaptive music, and narration ducking.</summary>
[DisallowMultipleComponent]
public sealed partial class ShiyinAudio : MonoBehaviour
{
    static readonly List<ShiyinAudio> instances = new List<ShiyinAudio>();
    AudioSource uiSource, effectSource, musicSource,footstepSource;
    AudioClip[] footstepClips;
    AudioClip confirmClip;
    float effects = .9f, music = .55f;
    bool paused;
    bool narrationDucking;
    AdaptiveMusic adaptiveMusic;
    bool manualMusic;

    public bool MusicAvailable => manualMusic ? musicSource != null && musicSource.clip != null
        : adaptiveMusic != null && adaptiveMusic.HasMusic;
    public AdaptiveMusic MusicDirector => adaptiveMusic;
    public static bool HasMusic
    {
        get
        {
            foreach (var instance in instances)
                if (instance != null && instance.MusicAvailable) return true;
            return false;
        }
    }

    public static ShiyinAudio Ensure(Camera camera)
    {
        if (camera == null) return null;
        var audio = camera.GetComponent<ShiyinAudio>();
        if (audio == null) audio = camera.gameObject.AddComponent<ShiyinAudio>();
        if (camera.GetComponent<AudioListener>() == null
            && Object.FindFirstObjectByType<AudioListener>() == null)
            camera.gameObject.AddComponent<AudioListener>();
        audio.EnsureSources();
        return audio;
    }

    void Awake()
    {
        instances.Add(this);
        EnsureSources();
    }

    AudioSource CreateSource(bool ignorePause)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = ignorePause;
        return source;
    }

    void EnsureSources()
    {
        if (uiSource == null) uiSource = CreateSource(true);
        if (effectSource == null) effectSource = CreateSource(false);
        if (musicSource == null) musicSource = CreateSource(false);
        if (footstepSource == null) {footstepSource=CreateSource(false);footstepSource.priority=150;}
        uiSource.volume = effectSource.volume = effects;
        footstepSource.volume=effects;
        UpdateCombatVolumes();
        UpdateDemonVolumes();
        musicSource.volume = music*(narrationDucking ? .38f : 1);
        if (adaptiveMusic == null) {
            adaptiveMusic = GetComponent<AdaptiveMusic>();
            if (adaptiveMusic == null) adaptiveMusic = gameObject.AddComponent<AdaptiveMusic>();
            var lab = GetComponentInParent<ShadowLab>();
            if (lab == null) lab = Object.FindFirstObjectByType<ShadowLab>();
            adaptiveMusic.Bind(lab);
        }
        adaptiveMusic.enabled = isActiveAndEnabled && !manualMusic;
        adaptiveMusic.SetVolume(music);
        adaptiveMusic.SetNarrationDucking(narrationDucking);
        adaptiveMusic.SetPaused(paused);
    }

    public void SetLevels(float effectsVolume, float musicVolume)
    {
        effects = ShiyinSettings.FiniteClamp(effectsVolume, 0f, 1f, .9f);
        music = ShiyinSettings.FiniteClamp(musicVolume, 0f, 1f, .55f);
        EnsureSources();
    }

    public void SetNarrationDucking(bool speaking) {
        narrationDucking=speaking;EnsureSources();
    }

    public void PlayEffect(AudioClip clip, float volume = 1f)
    {
        if (clip == null || paused || !isActiveAndEnabled) return;
        EnsureSources();
        effectSource.PlayOneShot(clip, ShiyinSettings.FiniteClamp(volume, 0f, 1f, 1f));
    }

    public void PlayUiConfirm()
    {
        if (!isActiveAndEnabled) return;
        EnsureSources();
        if (confirmClip == null) confirmClip = MakeConfirmClip();
        uiSource.PlayOneShot(confirmClip, .24f);
    }

    public void PlayCrossbowShot()
    {
        if (paused || !isActiveAndEnabled) return;
        var clip=NextCombatClip(CombatCue.Crossbow);
        if(clip==null)return;
        PlayEffect(clip,.7f);CrossbowShotsPlayed++;LastCombatClip=clip.name;
    }

    public bool FootstepsAvailable {
        get {LoadFootsteps();return footstepClips.Length>0;}
    }
    void LoadFootsteps() {
        if(footstepClips!=null)return;
        footstepClips=Resources.LoadAll<AudioClip>("HunterItems/Footsteps");
        System.Array.Sort(footstepClips,(a,b)=>string.CompareOrdinal(a.name,b.name));
    }
    public void PlayFootstep(bool running,int stepNumber) {
        if(paused || !isActiveAndEnabled)return;
        EnsureSources();LoadFootsteps();
        if(footstepClips.Length==0)return;
        int index=Mathf.Abs(stepNumber-1)%footstepClips.Length;
        footstepSource.pitch=(running ? 1.06f : .97f)+(index%3-1)*.025f;
        footstepSource.PlayOneShot(footstepClips[index],running ? .78f : .52f);
    }

    public void SetMusicClip(AudioClip clip, bool play = false, bool loop = true)
    {
        manualMusic = true;
        EnsureSources();
        musicSource.Stop();
        musicSource.clip = clip;
        musicSource.loop = loop;
        if (play && clip != null)
        {
            musicSource.Play();
            if (paused) musicSource.Pause();
        }
    }

    public void StopMusic()
    {
        if (musicSource != null) musicSource.Stop();
        manualMusic = true;
        if (adaptiveMusic != null) adaptiveMusic.enabled = false;
    }

    public void ResetAdaptiveMusic()
    {
        manualMusic = false;
        narrationDucking = false;
        if (musicSource != null) { musicSource.Stop(); musicSource.clip = null; }
        EnsureSources();
        adaptiveMusic.ResetMusic();
    }

    public void SetPaused(bool value)
    {
        if (paused == value) return;
        paused = value;
        EnsureSources();
        if (paused)
        {
            effectSource.Pause();
            footstepSource.Pause();
            musicSource.Pause();
            PauseCombatVoices(true);
            PauseDemonVoices(true);
        }
        else
        {
            effectSource.UnPause();
            footstepSource.UnPause();
            musicSource.UnPause();
            PauseCombatVoices(false);
            PauseDemonVoices(false);
        }
    }

    static AudioClip MakeConfirmClip()
    {
        const int rate = 22050;
        const float duration = .075f;
        var data = new float[Mathf.CeilToInt(rate * duration)];
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)rate;
            float fade = Mathf.Clamp01(t / .003f) * Mathf.Clamp01((duration - t) / .012f);
            float envelope = Mathf.Exp(-t * 38f) * fade;
            data[i] = envelope * (.58f * Mathf.Sin(2f * Mathf.PI * 1080f * t)
                + .18f * Mathf.Sin(2f * Mathf.PI * 1620f * t));
        }
        return MakeClip("Shiyin UI confirmation", rate, data);
    }

    static AudioClip MakeClip(string name, int rate, float[] samples)
    {
        var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
        clip.hideFlags = HideFlags.HideAndDontSave;
        clip.SetData(samples, 0);
        return clip;
    }

    void OnDisable()
    {
        ResetCombatEffects();
        if (uiSource != null) uiSource.Stop();
        if (effectSource != null) effectSource.Stop();
        if (footstepSource != null) footstepSource.Stop();
        if (musicSource != null) musicSource.Stop();
        if (adaptiveMusic != null) adaptiveMusic.enabled = false;
    }

    void OnEnable() { EnsureSources(); }

    void OnDestroy()
    {
        instances.Remove(this);
        Release(uiSource);
        Release(effectSource);
        Release(footstepSource);
        Release(musicSource);
        Release(confirmClip);
        DisposeCombatVoices();
        DisposeDemonVoices();
        Release(adaptiveMusic);
    }

    static void Release(Object asset)
    {
        if (asset == null) return;
        if (Application.isPlaying) Destroy(asset);
        else DestroyImmediate(asset);
    }
}
