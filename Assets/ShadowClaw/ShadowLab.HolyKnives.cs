using System.Collections.Generic;
using UnityEngine;

public sealed partial class ShadowLab
{
    [Header("Holy fire knives")]
    [Range(0,5)] public int StartingHolyKnives=2;
    [Min(.1f)] public float HolyKnifeCooldown=.65f;
    readonly HolyKnifePouch holyKnives=new HolyKnifePouch();
    readonly HolyKnifeThrowMotion knifeMotion=new HolyKnifeThrowMotion();
    readonly List<HolyKnifeFlight> knifeFlights=new List<HolyKnifeFlight>(3);
    HolyKnifeFlight knifeFlight;
    HolyKnifeFlight.HitTest knifeHitTest;
    float knifeCooldown;
    public int HolyKnifeCount=>holyKnives.Count;
    public int HolyKnifeCapacity=>HolyKnifePouch.Capacity;
    public bool HolyKnifeReady=>GameplayReady && !GameplayPaused && !finished && knifeCooldown<=0 && !knifeMotion.Active && HolyKnifeCount>0;
    public bool HolyKnifeThrowing=>knifeMotion.Active;
    public float HolyKnifeThrowProgress=>knifeMotion.Progress;
    public Vector3 HolyKnifeLastReleasePosition {get;private set;}

    public void GrantHolyKnifeFromLamp() {
        if(holyKnives.Grant())RequestNarration("12_holy_knife");
    }
    void ResetHolyKnives() {
        CancelHolyKnifeThrow(false);holyKnives.Reset(StartingHolyKnives);HolyKnifeLastReleasePosition=Vector3.zero;
    }
    // A cancelled, unreleased preparation can be returned. A blade already in
    // the world is never refunded. The ending clears it without granting stock.
    public void CancelHolyKnifeThrow(bool refundUnreleased=true) {
        if(refundUnreleased && knifeMotion.Active && !knifeMotion.Released)holyKnives.Grant();
        knifeMotion.Reset();knifeCooldown=0;knifeFlight=null;
        foreach(var flight in knifeFlights)flight.Clear();
        weaponView?.CancelKnifeThrow();
    }
    void TickHolyKnives(float dt) {
        if(GameplayPaused || finished || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return;
        knifeCooldown=Mathf.Max(0,knifeCooldown-dt);
        if(knifeHitTest==null)knifeHitTest=ResolveHolyKnifeSegment;
        foreach(var flight in knifeFlights)if(flight.Active)flight.Step(dt,knifeHitTest);
        if(!knifeMotion.Active)return;
        float previous=knifeMotion.Age;
        bool release=knifeMotion.Advance(dt);
        if(!knifeMotion.Released || release)
            knifeFlight.Hold(eye.transform,Mathf.Min(knifeMotion.Age,HolyKnifeThrowMotion.ReleaseTime),dt);
        if(release) {
            ReleaseHolyKnife();
            float remainder=dt-Mathf.Max(0,HolyKnifeThrowMotion.ReleaseTime-previous);
            if(remainder>0 && knifeFlight!=null && knifeFlight.Active)knifeFlight.Step(remainder,knifeHitTest);
        }
    }
    void OnDestroy() {RestoreExterior();DisposeBossAudioBinding();foreach(var flight in knifeFlights)flight.Dispose();}
    public bool ThrowHolyKnife() {
        if(!GameplayReady || GameplayPaused || finished || knifeCooldown>0 || knifeMotion.Active)return false;
        if(!holyKnives.Spend()) {shot="圣火飞刀已用尽 · 点亮熄灭的灯补充";ShotResolved?.Invoke(shot);return false;}
        knifeMotion.Begin();knifeCooldown=Mathf.Max(HolyKnifeThrowMotion.Duration,HolyKnifeCooldown);
        knifeFlight=knifeFlights.Find(value=>!value.Visible);
        if(knifeFlight==null) {knifeFlight=new HolyKnifeFlight(transform);knifeFlights.Add(knifeFlight);}
        knifeFlight.Hold(eye.transform,0,0);weaponView?.NotifyKnifeThrow();
        shot="圣火飞刀 · 左侧蓄势";
        return true;
    }
    void ReleaseHolyKnife() {
        Physics.SyncTransforms();
        Vector3 from=knifeFlight.Position;
        HolyKnifeLastReleasePosition=from;
        eye.GetComponent<ShiyinAudio>()?.PlayKnifeThrow();
        // A nearby wall between the eye and left-side hand must not let the
        // blade spawn on the other side and bypass that wall.
        Vector3 clearance=from-eye.transform.position;
        if(clearance.sqrMagnitude>.00001f && FirstShotSurface(new Ray(eye.transform.position,clearance.normalized),out var blocked) &&
            blocked.distance<=clearance.magnitude) {
            ResolveKnifeWorldImpact(blocked);knifeFlight.Clear();return;
        }
        var aim=eye.ViewportPointToRay(new Vector3(.5f,.5f,0));
        Vector3 target=FirstShotSurface(aim,out var hit) ? hit.point : aim.GetPoint(40);
        Vector3 direction=(target-from).sqrMagnitude>.00001f ? (target-from).normalized : eye.transform.forward;
        // The reticle chooses a direction. Contact is exclusively along the
        // actual left-side path, including lateral obstructions.
        knifeFlight.Release(from,from+direction*40);
        shot="圣火飞刀 · 已掷出";
    }
    bool ResolveHolyKnifeSegment(Ray ray,float distance,out Vector3 contact) {
        bool surface=FirstShotSurface(ray,out var hit) && hit.distance<=distance;
        float limit=surface?hit.distance:distance;
        ShadowEncounterActor actorTarget=null;float actorDistance=limit;
        foreach(var actor in authoredActors) {
            if(actor==null || !actor.isActiveAndEnabled || !actor.Activated || !actor.Alive)continue;
            if(actor.TryHitShadow(ray,limit,out float candidate) && candidate<actorDistance) {actorTarget=actor;actorDistance=candidate;}
        }
        float enemy=float.PositiveInfinity,hunter=float.PositiveInfinity;ClawState targetShadow=null;
        if(EncounterStarted)foreach(var shadow in shadows) {
            float candidate=Mathf.Min(shadow.Witch.HitDistance(ray),shadow.Arm.HitDistance(ray));
            if(candidate<enemy) {enemy=candidate;targetShadow=shadow;}
            hunter=Mathf.Min(hunter,shadow.Hunter.HitDistance(ray));
        }
        if(targetShadow!=null && enemy<hunter && enemy<=limit && (actorTarget==null || enemy<actorDistance)) {
            contact=ray.GetPoint(enemy);
            Vector2 point=targetShadow.Ground ? new Vector2(contact.x,contact.z) : new Vector2(contact.x,contact.y);
            combat.Shoot(Bolt.Fire,point,true,targetShadow.Ground);
            if(targetShadow.Ground)shadowBurn?.Ignite(targetShadow.Witch.Body,point);
            combat.Step(0,shadows);SyncMarks();
            shot="圣火飞刀 · 敌影燃烧";ShotResolved?.Invoke(shot);CheckEnd();return true;
        }
        if(actorTarget!=null) {
            contact=ray.GetPoint(actorDistance);
            if(actorTarget is FixedLanternGuardian guardian)
                shot=guardian.TryIgniteShadow(contact) ? "圣火飞刀 · 守灯者的地影燃烧" : "圣火飞刀 · 地影已隐去";
            else {
                string protectionHint=AcolyteProtectionFeedback.For(actorTarget);
                actorTarget.ReceiveShadowHit(Bolt.Fire,contact);
                shot=protectionHint ?? "圣火飞刀 · 射影命中";
            }
            ShotResolved?.Invoke(shot);return true;
        }
        if(surface) {contact=hit.point;ResolveKnifeWorldImpact(hit);return true;}
        contact=default;return false;
    }
    void ResolveKnifeWorldImpact(RaycastHit hit) {
        if(!TryIgniteLamp(hit.collider))shot="圣火飞刀击中障碍";
        ShotResolved?.Invoke(shot);
    }
}
