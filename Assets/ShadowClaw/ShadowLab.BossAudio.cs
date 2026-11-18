using System.Collections.Generic;
using UnityEngine;

public sealed partial class ShadowLab
{
    HuntBoss demonAudioBoss;
    HuntBoss.Move demonAudioAction;
    readonly HashSet<ClawState> demonAudioClosedClaws=new HashSet<ClawState>();
    bool demonAudioStarted,demonAudioEnded;
    int demonAudioLights;
    float demonAudioDamage,demonAudioScar,demonAudioHurtCooldown;

    ShiyinAudio DemonAudio => eye!=null ? eye.GetComponent<ShiyinAudio>() : null;
    Vector3 DemonSoundOrigin => state!=null ? state.Enemy+Vector3.up*1.4f : transform.position;

    void ResetBossAudio() {
        DisposeBossAudioBinding();
        demonAudioBoss=boss;
        if(demonAudioBoss!=null)demonAudioBoss.StrikeReleased+=OnDemonStrikeSound;
        demonAudioStarted=demonAudioEnded=false;
        demonAudioAction=boss!=null ? boss.Action : HuntBoss.Move.Stalk;
        demonAudioLights=DemonLampMask();demonAudioDamage=combat!=null ? combat.Damage : 0;
        demonAudioScar=boss!=null ? boss.ScarLife : 0;demonAudioHurtCooldown=0;
        demonAudioClosedClaws.Clear();DemonAudio?.ResetDemonSounds();
    }

    int DemonLampMask() {
        int mask=0;
        if(lamps!=null)for(int i=0;i<lamps.Length && i<31;i++)if(lamps[i].Lit)mask|=1<<i;
        return mask;
    }

    // Called from the encounter clock and once with dt=0 when damage is resolved.
    // Phase entries and damage deltas are observed, never a per-frame sound loop.
    void TickBossAudio(float dt) {
        if(GameplayPaused || !EncounterStarted || demonAudioEnded || boss==null || combat==null)return;
        var audio=DemonAudio;if(audio==null)return;
        if(demonAudioBoss!=boss)ResetBossAudio();
        if(dt>0 && !float.IsInfinity(dt) && !float.IsNaN(dt))
            demonAudioHurtCooldown=Mathf.Max(0,demonAudioHurtCooldown-dt);
        int mask=DemonLampMask();
        if(!demonAudioStarted) {
            if(!audio.PlayDemonSound(DemonSound.Arrival,DemonSoundOrigin))return;
            demonAudioStarted=true;demonAudioLights=mask;
        }
        int lost=demonAudioLights & ~mask;
        if(lost!=0) {
            for(int i=0;i<lamps.Length && i<31;i++)if((lost & (1<<i))!=0) {
                audio.PlayDemonSound(DemonSound.Extinguish,lamps[i].Fixture.position);break;
            }
        }
        if(mask==0 && demonAudioLights!=0)audio.PlayDemonSound(DemonSound.DarkHunt,DemonSoundOrigin);
        demonAudioLights=mask;

        var action=boss.Action;
        if(action!=demonAudioAction) {
            // Completed rituals also return to Recover; only an actual interruption cries out.
            if(demonAudioAction==HuntBoss.Move.Ritual && !boss.RitualComplete && action==HuntBoss.Move.Recover)
                audio.PlayDemonSound(DemonSound.Stagger,DemonSoundOrigin);
            switch(action) {
                case HuntBoss.Move.Claw:case HuntBoss.Move.SleeveTell:
                    audio.PlayDemonSound(DemonSound.ClawTell,DemonSoundOrigin);break;
                case HuntBoss.Move.MarkFloor:audio.PlayDemonSound(DemonSound.MarkTell,DemonSoundOrigin);break;
                case HuntBoss.Move.Rift:audio.PlayDemonSound(DemonSound.RiftTell,DemonSoundOrigin);break;
                case HuntBoss.Move.Burst:case HuntBoss.Move.BellTell:
                    audio.PlayDemonSound(DemonSound.BurstTell,DemonSoundOrigin);break;
                case HuntBoss.Move.LungeTell:audio.PlayDemonSound(DemonSound.LungeTell,DemonSoundOrigin);break;
                case HuntBoss.Move.Lunge:case HuntBoss.Move.Pounce:
                    audio.PlayDemonSound(DemonSound.Sweep,DemonSoundOrigin);break;
                case HuntBoss.Move.Ritual:audio.PlayDemonSound(DemonSound.Ritual,DemonSoundOrigin);break;
                case HuntBoss.Move.LightStagger:audio.PlayDemonSound(DemonSound.Stagger,DemonSoundOrigin);break;
            }
            demonAudioAction=action;
        }
        foreach(var shadow in shadows) {
            if(shadow==null)continue;
            if(!shadow.Active || !shadow.Resolved) {demonAudioClosedClaws.Remove(shadow);continue;}
            if(demonAudioClosedClaws.Add(shadow))audio.PlayDemonSound(DemonSound.Impact,shadow.BodyLockPoint);
        }
        if(boss.ScarLife>0 && demonAudioScar<=0)
            audio.PlayDemonSound(DemonSound.Impact,new Vector3(boss.Scar.x,GroundHeightAt(boss.Scar.x,boss.Scar.y),boss.Scar.y));
        demonAudioScar=boss.ScarLife;
        if(combat.Damage>demonAudioDamage && combat.Damage<HuntBoss.DemonHealth && demonAudioHurtCooldown<=0) {
            audio.PlayDemonSound(DemonSound.Hurt,DemonSoundOrigin);demonAudioHurtCooldown=.42f;
        }
        demonAudioDamage=combat.Damage;
    }

    void OnDemonStrikeSound(HuntBoss.Move move,Vector3 from,Vector3 to) {
        if(GameplayPaused || !EncounterStarted || demonAudioEnded)return;
        if(move==HuntBoss.Move.Burst || move==HuntBoss.Move.BellWaves || move==HuntBoss.Move.SleeveSweep || move==HuntBoss.Move.Rift)
            ShakeOnImpact();
        var audio=DemonAudio;if(audio==null)return;
        switch(move) {
            case HuntBoss.Move.Rift:
                audio.PlayDemonSound(DemonSound.Sweep,from+Vector3.up);break;
            case HuntBoss.Move.Burst:
                audio.PlayDemonSound(DemonSound.Impact,to);break;
            case HuntBoss.Move.Lunge:case HuntBoss.Move.Pounce:
                audio.PlayDemonSound(DemonSound.BodyImpact,to);break;
            case HuntBoss.Move.SleeveSweep:
                audio.PlayDemonSound(DemonSound.Sweep,from+Vector3.up);break;
            case HuntBoss.Move.BellWaves:
                audio.PlayDemonSound(DemonSound.Bell,from+Vector3.up);break;
        }
    }

    // Must precede BeginVictoryPresentation: its UI transition pauses battle audio.
    void BossAudioEnding(bool won) {
        if(demonAudioEnded)return;
        demonAudioEnded=true;
        if(won)DemonAudio?.PlayDemonSound(DemonSound.Death,DemonSoundOrigin);
        else DemonAudio?.ResetDemonSounds();
    }
    void DisposeBossAudioBinding() {
        if(demonAudioBoss!=null)demonAudioBoss.StrikeReleased-=OnDemonStrikeSound;
        demonAudioBoss=null;
    }
}
