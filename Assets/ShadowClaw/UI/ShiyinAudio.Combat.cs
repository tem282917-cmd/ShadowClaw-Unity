using System;
using System.Collections.Generic;
using UnityEngine;

public enum ArrowImpactKind { Solid, Shadow, Blocked }

public sealed partial class ShiyinAudio
{
    enum CombatCue { Crossbow, Lamp, Arrow, Knife }
    struct PendingImpact {
        public CombatCue Cue;
        public Vector3 Point;
        public float Remaining,Gain;
    }
    const int ImpactVoices=8;
    readonly List<PendingImpact> pendingImpacts=new List<PendingImpact>(12);
    readonly List<AudioSource> impactVoices=new List<AudioSource>(ImpactVoices);
    readonly List<AudioSource> worldVoices=new List<AudioSource>(ImpactVoices+3);
    AudioClip[][] combatClips;
    readonly int[] clipCursors=new int[4];
    int voiceCursor;
    public int CrossbowShotsPlayed {get;private set;}
    public int LampIgnitionsPlayed {get;private set;}
    public int ArrowImpactsPlayed {get;private set;}
    public int KnifeThrowsPlayed {get;private set;}
    public string LastCombatClip {get;private set;}
    public int PendingCombatSounds=>pendingImpacts.Count;
    public bool CombatSoundsAvailable {
        get {LoadCombatClips();foreach(var clips in combatClips)if(clips.Length==0)return false;return true;}
    }

    void LoadCombatClips() {
        if(combatClips!=null)return;
        string[] prefixes={"Crossbow_","LampIgnite_","ArrowImpact_","KnifeThrow_"};
        var clips=Resources.LoadAll<AudioClip>("GameplaySfx");
        combatClips=new AudioClip[prefixes.Length][];
        for(int i=0;i<prefixes.Length;i++) {
            string prefix=prefixes[i];
            combatClips[i]=Array.FindAll(clips,c=>c!=null && c.name.StartsWith(prefix,StringComparison.Ordinal));
            Array.Sort(combatClips[i],(a,b)=>string.CompareOrdinal(a.name,b.name));
        }
    }
    AudioClip NextCombatClip(CombatCue cue) {
        LoadCombatClips();int kind=(int)cue;var clips=combatClips[kind];
        if(clips.Length==0)return null;
        int choice=clipCursors[kind];clipCursors[kind]=(choice+1)%clips.Length;return clips[choice];
    }
    public void PlayKnifeThrow() {
        if(paused || !isActiveAndEnabled)return;
        var clip=NextCombatClip(CombatCue.Knife);if(clip==null)return;
        PlayEffect(clip,.58f);KnifeThrowsPlayed++;LastCombatClip=clip.name;
    }
    public void PlayLampIgnition(Vector3 point,float delay=0) => QueueImpact(CombatCue.Lamp,point,.62f,delay);
    public void PlayArrowImpact(Vector3 point,ArrowImpactKind kind=ArrowImpactKind.Solid,float delay=0) =>
        QueueImpact(CombatCue.Arrow,point,kind==ArrowImpactKind.Shadow ? .30f : .46f,delay);

    void QueueImpact(CombatCue cue,Vector3 point,float gain,float delay) {
        if(paused || !isActiveAndEnabled)return;
        if(delay<=0) {PlayWorldImpact(cue,point,gain);return;}
        // Never keep an unbounded queue after an unusual scripted firing burst.
        if(pendingImpacts.Count>=24)pendingImpacts.RemoveAt(0);
        pendingImpacts.Add(new PendingImpact {Cue=cue,Point=point,Gain=gain,Remaining=delay});
    }
    // Owned by the same gameplay clock as the travelling visual bolt. Pausing
    // freezes both flight and pending contacts; a new hunt clears old contacts.
    public void StepCombatEffects(float dt) {
        if(paused || !isActiveAndEnabled || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return;
        for(int i=pendingImpacts.Count-1;i>=0;i--) {
            var impact=pendingImpacts[i];impact.Remaining-=dt;
            if(impact.Remaining>0) {pendingImpacts[i]=impact;continue;}
            pendingImpacts.RemoveAt(i);PlayWorldImpact(impact.Cue,impact.Point,impact.Gain);
        }
    }
    void PlayWorldImpact(CombatCue cue,Vector3 point,float gain) {
        var clip=NextCombatClip(cue);if(clip==null)return;
        AudioSource source=null;
        foreach(var voice in impactVoices)if(voice!=null && !voice.isPlaying) {source=voice;break;}
        if(source==null && impactVoices.Count<ImpactVoices) {
            var lab=GetComponentInParent<ShadowLab>();
            source=CreateWorldVoice("Recorded impact voice "+(impactVoices.Count+1),
                lab!=null ? lab.transform : transform.root,2.5f,48,96);
            impactVoices.Add(source);
        }
        if(source==null) {source=impactVoices[voiceCursor++%impactVoices.Count];source.Stop();}
        source.transform.position=point;source.volume=effects;source.pitch=1;
        source.PlayOneShot(clip,gain);LastCombatClip=clip.name;
        if(cue==CombatCue.Lamp)LampIgnitionsPlayed++;else ArrowImpactsPlayed++;
    }
    internal bool CanPlayWorldSound => !paused && isActiveAndEnabled;

    internal AudioSource CreateWorldVoice(string label,Transform parent,float near,float far,int priority) {
        var host=new GameObject(label);
        host.transform.SetParent(parent,false);
        var source=host.AddComponent<AudioSource>();
        source.playOnAwake=false;source.spatialBlend=1;source.dopplerLevel=0;
        source.minDistance=near;source.maxDistance=far;source.rolloffMode=AudioRolloffMode.Linear;
        source.priority=priority;source.volume=effects;
        worldVoices.Add(source);
        return source;
    }
    internal void ReleaseWorldVoice(AudioSource voice) {
        if(voice==null)return;
        worldVoices.Remove(voice);
        Release(voice.gameObject);
    }
    void UpdateCombatVolumes() {foreach(var voice in worldVoices)if(voice!=null)voice.volume=effects;}
    void PauseCombatVoices(bool value) {
        foreach(var voice in worldVoices)if(voice!=null) {if(value)voice.Pause();else voice.UnPause();}
    }
    public void ResetCombatEffects() {
        ResetDemonSounds();
        pendingImpacts.Clear();
        foreach(var voice in worldVoices)if(voice!=null)voice.Stop();
        if(effectSource!=null)effectSource.Stop();
    }
    void DisposeCombatVoices() {
        foreach(var voice in worldVoices)if(voice!=null)Release(voice.gameObject);
        worldVoices.Clear();impactVoices.Clear();pendingImpacts.Clear();
        // Imported clips belong to Resources, not this component: do not Destroy them.
    }
}
