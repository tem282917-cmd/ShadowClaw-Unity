using System.Collections.Generic;
using UnityEngine;
using Move = FixedGuardianState.Move;

// Armour, the chest bell and floor impacts originate at the same points as their effects.
internal sealed class FixedGuardianAudio
{
    readonly FixedLanternGuardian guardian;
    readonly FixedGuardianView view;
    readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
    ShiyinAudio mixer;
    AudioSource mechanism,impact,armour;
    float hurtCooldown;

    internal FixedGuardianAudio(FixedLanternGuardian guardian,FixedGuardianView view) {
        this.guardian=guardian;this.view=view;
        foreach(var clip in Resources.LoadAll<AudioClip>("GuardianSfx"))clips.Add(clip.name,clip);
    }

    void Play(ref AudioSource voice,string channel,string cue,Vector3 point,float gain) {
        if(mixer==null && guardian.Encounter.Eye!=null)mixer=guardian.Encounter.Eye.GetComponent<ShiyinAudio>();
        if(mixer==null || !mixer.CanPlayWorldSound || !clips.TryGetValue(cue,out var clip))return;
        if(voice==null)voice=mixer.CreateWorldVoice("Guardian "+channel,guardian.transform,5,
            Mathf.Max(65,guardian.ArenaSize.magnitude*2.5f),channel=="armour" ? 100 : 72);
        voice.Stop();
        voice.transform.position=point;voice.clip=clip;
        voice.PlayOneShot(clip,gain);
    }

    void Tell(string cue,Vector3 point,float gain) => Play(ref mechanism,"mechanism",cue,point,gain);
    void Strike(string cue,Vector3 point,float gain) => Play(ref impact,"impact",cue,point,gain);
    internal void Wake() => Tell("Awaken",view.ChestPosition,.70f);
    internal void Tick(float dt) => hurtCooldown=Mathf.Max(0,hurtCooldown-dt);

    internal void MoveChanged(Move action) {
        var state=guardian.State;
        switch(action) {
            case Move.SlamTrack: Tell("RaiseHammer",view.SweepOrigin(1),.63f);break;
            case Move.SlamLocked: Tell("Lock",state.LockedPoint,.70f);break;
            case Move.SlamImpact: Strike("Slam",state.LockedPoint,.95f);break;
            case Move.SweepTell: Tell("DrawArm",view.SweepOrigin(state.SweepSide),.68f);break;
            case Move.SweepImpact: Strike("Sweep",view.SweepOrigin(state.SweepSide),.88f);break;
            case Move.WaveTell: Tell("ChestChime",view.ChestPosition,.68f);break;
            case Move.WaveTravel: Strike("Wave",state.WaveOrigin,.86f);break;
            case Move.Ritual: Tell("Prayer",view.ChestPosition,.58f);break;
            case Move.Dead:
                Stop();Strike("Death",view.ChestPosition,.95f);break;
        }
    }

    internal void LampExtinguished(int index) => Strike("Snuff",guardian.Encounter.GetLampPosition(index),.76f);
    internal void Stagger() {
        if(mechanism!=null)mechanism.Stop();
        Strike("Stagger",view.ChestPosition,.80f);
    }
    internal void Hurt() {
        if(hurtCooldown>0)return;
        Play(ref armour,"armour","Hurt",view.ChestPosition,.54f);
        hurtCooldown=.38f;
    }
    internal void Stop() {
        if(mechanism!=null)mechanism.Stop();
        if(impact!=null)impact.Stop();
        if(armour!=null)armour.Stop();
        hurtCooldown=0;
    }
    internal void Dispose() {
        if(mixer==null)return;
        mixer.ReleaseWorldVoice(mechanism);
        mixer.ReleaseWorldVoice(impact);
        mixer.ReleaseWorldVoice(armour);
    }
}
