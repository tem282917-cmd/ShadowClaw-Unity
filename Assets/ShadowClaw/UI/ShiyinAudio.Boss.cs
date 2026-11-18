using System;
using System.Collections.Generic;
using UnityEngine;

public enum DemonSound
{
    Arrival, ClawTell, RiftTell, BurstTell, LungeTell, Sweep, Impact, BodyImpact,
    Ritual, Extinguish, DarkHunt, Stagger, Hurt, Death, MarkTell, Bell
}

public sealed partial class ShiyinAudio
{
    const int DemonVoiceLimit=6;
    readonly List<AudioSource> demonVoices=new List<AudioSource>(DemonVoiceLimit);
    readonly int[] demonClipCursors=new int[16],demonCueCounts=new int[16];
    AudioClip[][] demonClips;
    AudioSource demonDeathVoice;
    int demonVoiceCursor;
    bool demonDeathTail;

    public DemonSound LastDemonCue {get;private set;}
    public string LastDemonClip {get;private set;}
    public Vector3 LastDemonSoundPosition {get;private set;}
    public int DemonSoundCount(DemonSound cue) => demonCueCounts[(int)cue];
    public bool DemonDeathTailActive => demonDeathTail && demonDeathVoice!=null && demonDeathVoice.isPlaying;
    public bool DemonDeathTailReserved => demonDeathTail && demonDeathVoice!=null && demonDeathVoice.clip!=null;
    public bool DemonSoundsAvailable {
        get {LoadDemonClips();foreach(var clips in demonClips)if(clips.Length==0)return false;return true;}
    }

    void LoadDemonClips() {
        if(demonClips!=null)return;
        var clips=Resources.LoadAll<AudioClip>("DemonSfx");
        var cues=(DemonSound[])Enum.GetValues(typeof(DemonSound));
        demonClips=new AudioClip[cues.Length][];
        foreach(var cue in cues) {
            string prefix=cue+"_";
            var family=Array.FindAll(clips,c=>c!=null && c.name.StartsWith(prefix,StringComparison.Ordinal));
            Array.Sort(family,(a,b)=>string.CompareOrdinal(a.name,b.name));
            demonClips[(int)cue]=family;
        }
    }

    AudioSource CreateDemonVoice(string label) {
        var host=new GameObject(label);
        var lab=GetComponentInParent<ShadowLab>();
        host.transform.SetParent(lab!=null ? lab.transform : transform.root,false);
        var voice=host.AddComponent<AudioSource>();
        voice.playOnAwake=false;voice.loop=false;voice.spatialBlend=1;voice.dopplerLevel=0;
        var hall=lab!=null ? lab.GetComponent<IronHallEncounter>() : null;
        float diagonal=hall!=null ? hall.PlayArea.size.magnitude : 0;
        // Retain at least ~60% of the distance gain across an authored arena's
        // entire diagonal. The large room must not swallow important boss tells.
        voice.minDistance=5;voice.maxDistance=Mathf.Max(65,diagonal*2.5f);
        voice.rolloffMode=AudioRolloffMode.Linear;
        voice.priority=75;voice.volume=effects;
        return voice;
    }

    static float DemonGain(DemonSound cue) {
        switch(cue) {
            case DemonSound.Hurt:return .50f;
            case DemonSound.Ritual:return .58f;
            case DemonSound.ClawTell:case DemonSound.RiftTell:case DemonSound.MarkTell:return .72f;
            case DemonSound.Extinguish:return .73f;
            case DemonSound.Death:return .95f;
            case DemonSound.DarkHunt:return .88f;
            default:return .80f;
        }
    }

    public bool PlayDemonSound(DemonSound cue,Vector3 position) {
        if(paused || !isActiveAndEnabled || float.IsNaN(position.sqrMagnitude) || float.IsInfinity(position.sqrMagnitude))return false;
        LoadDemonClips();int kind=(int)cue;
        if(kind<0 || kind>=demonClips.Length || demonClips[kind].Length==0)return false;
        var clips=demonClips[kind];int choice=demonClipCursors[kind];
        demonClipCursors[kind]=(choice+1)%clips.Length;
        AudioClip clip=clips[choice];AudioSource source=null;
        if(cue==DemonSound.Death) {
            foreach(var voice in demonVoices)if(voice!=null)voice.Stop();
            if(demonDeathVoice==null)demonDeathVoice=CreateDemonVoice("Shadow demon death tail");
            source=demonDeathVoice;demonDeathTail=true;
            // The ending view pauses battle audio while the death animation continues.
            // This reserved source survives that view switch; menu/reset still stops it.
            source.ignoreListenerPause=true;
        } else {
            foreach(var voice in demonVoices)if(voice!=null && !voice.isPlaying) {source=voice;break;}
            if(source==null && demonVoices.Count<DemonVoiceLimit) {
                source=CreateDemonVoice("Shadow demon voice "+(demonVoices.Count+1));demonVoices.Add(source);
            }
            if(source==null)source=demonVoices[demonVoiceCursor++%demonVoices.Count];
        }
        source.Stop();source.transform.position=position;source.volume=effects;
        source.pitch=1;source.clip=clip;source.priority=cue==DemonSound.Hurt ? 110 : 70;
        source.PlayOneShot(clip,DemonGain(cue));
        demonCueCounts[kind]++;LastDemonCue=cue;LastDemonClip=clip.name;LastDemonSoundPosition=position;
        return true;
    }

    void UpdateDemonVolumes() {
        foreach(var voice in demonVoices)if(voice!=null)voice.volume=effects;
        if(demonDeathVoice!=null)demonDeathVoice.volume=effects;
    }
    void PauseDemonVoices(bool value) {
        foreach(var voice in demonVoices)if(voice!=null) {if(value)voice.Pause();else voice.UnPause();}
        if(demonDeathVoice!=null && !demonDeathTail) {if(value)demonDeathVoice.Pause();else demonDeathVoice.UnPause();}
    }
    public void ResetDemonSounds() {
        foreach(var voice in demonVoices)if(voice!=null) {voice.Stop();voice.clip=null;}
        if(demonDeathVoice!=null) {demonDeathVoice.Stop();demonDeathVoice.clip=null;}
        demonDeathTail=false;
    }
    void DisposeDemonVoices() {
        ResetDemonSounds();
        foreach(var voice in demonVoices)if(voice!=null)Release(voice.gameObject);
        if(demonDeathVoice!=null)Release(demonDeathVoice.gameObject);
        demonVoices.Clear();demonDeathVoice=null;
        // Resources owns these imported CC0 clips; never destroy the shared assets.
    }
}
