using System.Collections.Generic;
using UnityEngine;

[System.Flags]
public enum CombatGuidance { None=0, GhostShadows=1, AcolyteLight=2, HealWithRelic=4 }

public sealed partial class ShadowLab
{
    readonly List<ShadowEncounterActor> authoredActors=new List<ShadowEncounterActor>();
    readonly List<EncounterLantern> authoredLanterns=new List<EncounterLantern>();
    [Header("Contact pressure limits")]
    [Min(0)] public float MaxContactDamagePerSecond=3;
    [Range(.1f,1)] public float MinimumContactSpeedMultiplier=.65f;
    float contactDamage, contactSlow=1, contactFraction;
    FairyTaleNarrator narrator;
    bool wasRecovering;
    string currentNarrationPassage;

    public Vector3 HunterGroundPosition => state!=null ? state.Player : Vector3.zero;
    public Vector3 HunterBodyPosition => state!=null ? state.PlayerBody : Vector3.zero;
    public float HunterJumpHeight => state!=null ? state.JumpHeight : 0;
    public float GroundHeightAt(float x,float z) => hall!=null ? hall.SurfaceHeight(x,z) : 0;
    public int LampCount => (lamps?.Length ?? 0)+authoredLanterns.Count;
    public bool GameplayReady => state!=null && boss!=null;
    public bool AnyAcolytePraying {
        get {
            foreach(var actor in authoredActors)
                if(actor is FireThiefAcolyte acolyte && acolyte.isActiveAndEnabled && acolyte.Activated &&
                    acolyte.Alive && acolyte.Action==FireThiefCycle.Action.Prayer)return true;
            return false;
        }
    }
    public FixedLanternGuardian ActiveGuardian {
        get {
            foreach(var actor in authoredActors)
                if(actor is FixedLanternGuardian guardian && guardian.isActiveAndEnabled && guardian.Activated &&
                    guardian.Alive && guardian.State!=null && guardian.State.Action!=FixedGuardianState.Move.Dormant)
                    return guardian;
            return null;
        }
    }
    public bool ContactSlowed => contactSlow<.999f;
    CombatGuidance EngagedMinorKinds {
        get {
            if(FinalRoomIsolated)return CombatGuidance.None;
            var kinds=CombatGuidance.None;
            foreach(var actor in authoredActors) {
                if(actor==null || !actor.isActiveAndEnabled || !actor.Activated || !actor.Alive)continue;
                if(actor is GroundShadowSwarm swarm && swarm.Engaged)kinds|=CombatGuidance.GhostShadows;
                if(actor is FireThiefAcolyte acolyte && acolyte.Engaged)kinds|=CombatGuidance.AcolyteLight;
            }
            return kinds;
        }
    }
    public bool MinorCombatActive => EngagedMinorKinds!=CombatGuidance.None;
    CombatGuidance seenMinorKinds,firstEncounterGuides;
    public CombatGuidance ActiveCombatGuides {
        get {
            if(GameplayPaused || HunterHealth<=0)return CombatGuidance.None;
            return EncounterStarted && HunterHealth<HuntBoss.HunterMaxHealth
                ? firstEncounterGuides | CombatGuidance.HealWithRelic : firstEncounterGuides;
        }
    }
    public string CurrentBossTell => boss?.Tell ?? "";
    public bool GameplayPaused => !GameplayReady || Developer.Paused || finished || (UI!=null && UI.CurrentView!="battle");

    public void RegisterActor(ShadowEncounterActor actor) {
        if(actor==null || authoredActors.Contains(actor))return;
        if(actor.Encounter!=null && actor.Encounter!=this)return;
        actor.Encounter=this; authoredActors.Add(actor);
        if(GameplayReady)actor.ResetEncounter();
    }
    public void UnregisterActor(ShadowEncounterActor actor) { authoredActors.Remove(actor); }
    public void RegisterLantern(EncounterLantern lantern) {
        if(lantern==null || authoredLanterns.Contains(lantern))return;
        if(lantern.Encounter!=null && lantern.Encounter!=this)return;
        lantern.Encounter=this;authoredLanterns.Add(lantern);
        if(GameplayReady)lantern.ResetLantern();
    }
    public void UnregisterLantern(EncounterLantern lantern) {
        // Disabled objects keep their slot so numerical lamp links remain stable on re-enable.
    }
    public int GetLampIndex(EncounterLantern lantern) {
        if(lantern==null)return -1;
        RegisterLantern(lantern);
        return (lamps?.Length ?? 0)+authoredLanterns.IndexOf(lantern);
    }
    EncounterLantern AuthoredLamp(int index) {
        int local=index-(lamps?.Length ?? 0);
        return local>=0 && local<authoredLanterns.Count ? authoredLanterns[local] : null;
    }
    public bool IsLampLit(int index) => index>=0 && index<(lamps?.Length ?? 0)
        ? lamps[index].Lit : AuthoredLamp(index)!=null && AuthoredLamp(index).isActiveAndEnabled && AuthoredLamp(index).Lit;
    public Vector3 GetLampPosition(int index) => index>=0 && index<(lamps?.Length ?? 0)
        ? lamps[index].Fixture.position : AuthoredLamp(index)!=null ? AuthoredLamp(index).Position : Vector3.zero;
    public bool TryExtinguishLamp(int index) {
        if(!IsLampLit(index))return false;
        if(index<lamps.Length) {
            lamps[index].Remaining=0;lamps[index].Impact=.32f;ApplyLighting();
        } else AuthoredLamp(index).Extinguish();
        return true;
    }
    void NotifyLampShot(int index) {
        for(int i=0;i<authoredActors.Count;i++)
            if(authoredActors[i]!=null && authoredActors[i].isActiveAndEnabled)
                authoredActors[i].OnLampShot(index);
    }
    bool ShootAuthoredLamp(Collider hit) {
        for(int i=0;i<authoredLanterns.Count;i++) {
            var lamp=authoredLanterns[i];
            if(lamp==null || !lamp.OwnsInteractionCollider(hit))continue;
            bool wasLit=lamp.Lit;
            lamp.Ignite();NotifyLampShot(lamps.Length+i);
            if(!wasLit)OnPlayerRelitLamp();
            shot=wasLit ? "灯火已守护" : "灯火已复明";return true;
        }
        return false;
    }
    public bool TryIgniteLamp(Collider target) {
        if(target==null || !GameplayReady || GameplayPaused)return false;
        if(ShootAuthoredLamp(target)) {eye.GetComponent<ShiyinAudio>()?.PlayLampIgnition(target.bounds.center);return true;}
        foreach(var lamp in lamps)if(lamp.Target==target) {ShootLamp(lamp);eye.GetComponent<ShiyinAudio>()?.PlayLampIgnition(lamp.Fixture.position);return true;}
        return false;
    }
    public bool TryIgniteLamp(Ray ray,float maximumDistance,out Vector3 point) {
        point=ray.GetPoint(maximumDistance);
        if(!FirstShotSurface(ray,out var hit) || hit.distance>maximumDistance)return false;
        point=hit.point;return TryIgniteLamp(hit.collider);
    }
    public void DamageHunter(int amount) {
        if(!GameplayReady || Developer.Paused || finished || amount<=0)return;
        boss.HurtUnscaled(amount);
        CheckEnd();
    }
    public void ApplyContactPressure(float damagePerSecond,float slowMultiplier,float dt) {
        if(dt<=0)return;
        contactDamage=Mathf.Max(contactDamage,Mathf.Max(0,damagePerSecond));
        contactSlow=Mathf.Min(contactSlow,Mathf.Clamp(slowMultiplier,MinimumContactSpeedMultiplier,1));
    }
    void ResetAuthoredContent() {
        seenMinorKinds=firstEncounterGuides=CombatGuidance.None;
        contactFraction=contactDamage=0;contactSlow=1;wasRecovering=false;currentNarrationPassage=null;
        RefreshOrdinarySceneLamps();ordinaryLampClaims.Clear();localLightDiscoveryClock=1;
        foreach(var lamp in authoredLanterns)if(lamp!=null)lamp.ResetLantern();
        foreach(var actor in authoredActors)if(actor!=null)actor.ResetEncounter();
        foreach(var trigger in FindObjectsByType<AuthoredEncounterTrigger>(FindObjectsSortMode.None))
            if(trigger.Encounter==this || trigger.Encounter==null)trigger.ResetTrigger();
        narrator?.ResetNarration();
        Eye?.GetComponent<ShiyinAudio>()?.ResetAdaptiveMusic();
    }
    readonly List<MinorEnemyShadow> minorShadows=new List<MinorEnemyShadow>();
    const int DistantShadowsPerTick=2;
    int distantShadowStart;
    internal void RegisterMinorShadow(MinorEnemyShadow shadow)=>minorShadows.Add(shadow);
    internal void UnregisterMinorShadow(MinorEnemyShadow shadow)=>minorShadows.Remove(shadow);
    internal bool SimpleShadowDue(MinorEnemyShadow shadow) {
        int count=minorShadows.Count;
        if(count<=DistantShadowsPerTick)return true;
        int index=minorShadows.IndexOf(shadow);
        return index<0 || (index-distantShadowStart%count+count)%count<DistantShadowsPerTick;
    }
    void TickAuthoredContent(float dt) {
        if(FinalRoomIsolated) {
            if(boss.Recovering && !wasRecovering)RequestNarration("06_recovery");
            wasRecovering=boss.Recovering;
            return;
        }
        if(minorShadows.Count>0)distantShadowStart=(distantShadowStart+DistantShadowsPerTick)%minorShadows.Count;
        if(!EncounterStarted && hall!=null && hall.UseAuthoredLayout) {
            string passage=hall.PassageNarrationAt(HunterGroundPosition);
            if(passage!=null && passage!=currentNarrationPassage)RequestNarration(passage);
            currentNarrationPassage=passage;
        }
        contactDamage=0;contactSlow=1;
        TickOrdinaryLampDiscovery(dt);
        foreach(var lamp in authoredLanterns)if(lamp!=null && lamp.isActiveAndEnabled)lamp.Step(dt);
        for(int i=0;i<authoredActors.Count;i++) {
            var actor=authoredActors[i];
            if(actor!=null && actor.isActiveAndEnabled && actor.Activated)actor.Tick(dt);
        }
        if(contactDamage>0) {
            contactFraction+=Mathf.Min(contactDamage,MaxContactDamagePerSecond)*dt;
            int damage=Mathf.FloorToInt(contactFraction);
            if(damage>0) { contactFraction-=damage;DamageHunter(damage); }
        } else contactFraction=0;
        if(boss.Recovering && !wasRecovering)RequestNarration("06_recovery");
        wasRecovering=boss.Recovering;
    }
    bool ShootAuthoredShadow(Ray ray,float maximumDistance,Bolt? kind=null) {
        if(FinalRoomIsolated)return false;
        ShadowEncounterActor nearest=null;float distance=maximumDistance;
        foreach(var actor in authoredActors) {
            if(actor==null || !actor.isActiveAndEnabled || !actor.Activated || !actor.Alive)continue;
            if(actor.TryHitShadow(ray,maximumDistance,out float candidate) && candidate<distance) {
                nearest=actor;distance=candidate;
            }
        }
        if(nearest==null)return false;
        string protectionHint=AcolyteProtectionFeedback.For(nearest);
        nearest.ReceiveShadowHit(kind ?? crossbow.Loaded,ray.GetPoint(distance));
        shot=protectionHint ?? "射影命中";return true;
    }
    bool FirstShotSurface(Ray ray,out RaycastHit hit) {
        var hits=Physics.RaycastAll(ray,40,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide);
        System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach(var candidate in hits) {
            bool target=!candidate.collider.isTrigger;
            if(!target) {
                foreach(var lamp in lamps)if(lamp.Target==candidate.collider) {target=true;break;}
                if(!target)foreach(var lamp in authoredLanterns)if(lamp!=null && lamp.isActiveAndEnabled && lamp.Target==candidate.collider) {target=true;break;}
            }
            if(target) {hit=candidate;return true;}
        }
        hit=default;return false;
    }
    public void RequestNarration(string id) { narrator?.Request(id); }
}
