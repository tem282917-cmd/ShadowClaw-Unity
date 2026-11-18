using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Shadow Claw/Encounters/Fire Thief Acolyte")]
public sealed class FireThiefAcolyte : ShadowEncounterActor
{
    public const string DefaultBodyResource="Acolyte/Character";
    [Header("Nearby ordinary lamp selection")]
    [Min(0)] public float LampSearchRadius=18f;
    [Min(0)] public float ShadowLightRadius=18f;
    [HideInInspector]
    public EncounterLantern LinkedLantern;
    [HideInInspector] public int LinkedLampIndex=-1;
    [HideInInspector]
    public EncounterLantern[] ProjectionLanterns;
    [HideInInspector]
    public int[] ProjectionLampIndices;
    [Header("Combat tuning")]
    [Min(1)] public int MaxHealth=30;
    [Min(0)] public float NoticeDistance=24f;
    [Tooltip("Distance that cancels unfinished attacks and ends combat; at least NoticeDistance + 4. NoticeDistance <= 0 disables distance disengagement.")]
    [Min(0)] public float DisengageDistance=38f;
    [Min(.1f)] public float AttackInterval=2.4f;
    [Min(.3f)] public float AttackWindup=1.15f;
    [Min(1.5f)] public float PrayerDuration=4.2f;
    [Min(.2f)] public float InterruptStagger=2.2f;
    [HideInInspector] public int VolleysBeforePrayer=3;
    [Min(.5f)] public float ProjectileSpeed=7f;
    [Min(.5f)] public float ProjectileLifetime=5f;
    [Min(1)] public int ProjectileDamage=6;
    [Range(1f,3f)] public float EmpoweredDamageMultiplier=1.7f;
    [Range(1f,2f)] public float EmpoweredSpeedMultiplier=1.15f;
    [Range(3f,35f)] public float FanAngle=12f;
    [Min(.2f)] public float ShadowSize=1f;
    [Header("Combat footwork")]
    [Min(0)] public float WalkSpeed=1.25f;
    [Min(0)] public float RoamRadius=5f;
    [Header("Imported art / VFX prefabs")]
    [Tooltip("Optional normalized character prefab: about 2 metres tall, feet at Y=0, facing +Z. Empty uses Resources/Acolyte/Character.")]
    public GameObject BodyPrefab;
    public GameObject ProjectilePrefab;
    public GameObject PrayerPrefab;
    public GameObject EmpoweredPrefab;
    public GameObject InterruptPrefab;
    public GameObject DeathPrefab;
    public bool UseImportedDefaultEffects=true;
    [Min(.01f)] public float ProjectileEffectScale=.45f;
    [Min(.01f)] public float PrayerEffectScale=.65f;
    [Min(.01f)] public float EmpoweredEffectScale=.5f;
    [Min(.01f)] public float InterruptEffectScale=.12f;
    [Min(.01f)] public float DeathEffectScale=.3f;
    public bool ShowDevelopmentPlaceholders=true;

    sealed class Projectile {
        public Vector3 Position,Direction;
        public float Remaining;
        public Transform Marker;
        public ImportedEnemyEffect Effect;
    }
    readonly FireThiefCycle cycle=new FireThiefCycle();
    readonly List<Projectile> projectiles=new List<Projectile>();
    readonly List<ImportedEnemyEffect> effects=new List<ImportedEnemyEffect>();
    Transform body,arm,windupMarker;
    LineRenderer tether,prayerProgress;
    ImportedEnemyEffect bodyArt,prayerEffect,empoweredEffect;
    FireThiefModelView modelView;
    MinorEnemyShadow shadow;
    Material bodyMaterial,headMaterial,shadowMaterial,tetherMaterial,warningMaterial;
    bool initialized,engaged,shadowHit;
    int health;
    float age;
    int claimedLamp=-1;
    bool tetherNarrated;
    float nextTetherSightCheck;
    public override bool Alive => !initialized || health>0;
    public int Health => health;
    public bool Engaged => engaged;
    public bool Connected => cycle.Connected && Link>=0;
    public bool ProtectedByLamp => Alive && Connected;
    public bool Empowered => Connected && !Encounter.IsLampLit(Link);
    public FireThiefCycle.Action Action => cycle.Current;
    public float PrayerProgress => Action==FireThiefCycle.Action.Prayer ? cycle.Progress : 0;
    public int Link => Encounter!=null && Activated && Alive && Encounter.IsOrdinaryLamp(claimedLamp) ? claimedLamp : -1;
    public int ShadowLampIndex {get;private set;}=-1;
    public Transform ImportedBodyRoot => bodyArt?.Transform;
    public Vector3 FootPosition => body!=null ? body.position : transform.position;

    void UpdateLampClaim() {
        int previous=claimedLamp;
        claimedLamp=Encounter.ClaimNearestOrdinaryLamp(this,FootPosition,Mathf.Max(0,LampSearchRadius),claimedLamp);
        if(previous!=claimedLamp)cycle.LoseLamp();
    }
    void ReleaseLampClaim() {Encounter?.ReleaseOrdinaryLamp(this);claimedLamp=-1;}

    bool Ensure() {
        if(initialized)return true;
        if(Encounter==null)return false;
        if(BodyPrefab==null)BodyPrefab=Resources.Load<GameObject>(DefaultBodyResource);
        ProjectilePrefab=MinorEnemyVisuals.DefaultEffect(ProjectilePrefab,"vfx_Projectile_02",UseImportedDefaultEffects);
        PrayerPrefab=MinorEnemyVisuals.DefaultEffect(PrayerPrefab,"vfx_Portal_01",UseImportedDefaultEffects);
        EmpoweredPrefab=MinorEnemyVisuals.DefaultEffect(EmpoweredPrefab,"vfx_Electricity_01",UseImportedDefaultEffects);
        InterruptPrefab=MinorEnemyVisuals.DefaultEffect(InterruptPrefab,"vfx_Impact_01",UseImportedDefaultEffects);
        DeathPrefab=MinorEnemyVisuals.DefaultEffect(DeathPrefab,"vfx_Implosion_01",UseImportedDefaultEffects);
        bodyMaterial=MinorEnemyVisuals.Material(new Color(.16f,.12f,.19f,1));
        headMaterial=MinorEnemyVisuals.Material(new Color(.53f,.46f,.58f,1));
        shadowMaterial=MinorEnemyVisuals.Material(new Color(.055f,.035f,.09f,1));
        tetherMaterial=MinorEnemyVisuals.Material(new Color(.035f,.018f,.052f,1));
        warningMaterial=MinorEnemyVisuals.Material(new Color(.75f,.39f,.94f,1));
        body=new GameObject("Acolyte body").transform;body.SetParent(transform,false);
        if(BodyPrefab!=null) {
            bodyArt=new ImportedEnemyEffect(BodyPrefab,body,Vector3.zero,true);bodyArt.DisableNativeShadows();
            modelView=new FireThiefModelView(bodyArt.Transform);
        }
        else if(ShowDevelopmentPlaceholders) {
            var placeholder=new GameObject("Development Placeholder - replace BodyPrefab").transform;
            placeholder.SetParent(body,false);
            MinorEnemyVisuals.Part("Robe",PrimitiveType.Capsule,placeholder,new Vector3(0,.87f,0),new Vector3(.57f,.76f,.42f),bodyMaterial);
            MinorEnemyVisuals.Part("Hood",PrimitiveType.Sphere,placeholder,new Vector3(0,1.65f,0),new Vector3(.52f,.58f,.44f),bodyMaterial);
            MinorEnemyVisuals.Part("Face",PrimitiveType.Sphere,placeholder,new Vector3(0,1.64f,.17f),new Vector3(.23f,.29f,.15f),headMaterial);
            arm=new GameObject("Casting arm").transform;arm.SetParent(placeholder,false);arm.localPosition=new Vector3(.29f,1.37f,0);
            MinorEnemyVisuals.Part("Sleeve",PrimitiveType.Capsule,arm,new Vector3(0,-.29f,0),new Vector3(.17f,.33f,.18f),bodyMaterial);
            windupMarker=MinorEnemyVisuals.Part("Development tell marker",PrimitiveType.Sphere,body,new Vector3(.29f,1.35f,.42f),Vector3.one*.14f,warningMaterial);
        }
        tether=MinorEnemyVisuals.Line("Permanent lamp tether",transform,tetherMaterial,.025f,17);
        prayerProgress=MinorEnemyVisuals.Line("Prayer progress mechanic",transform,warningMaterial,.035f,2);
        prayerProgress.enabled=false;
        if(PrayerPrefab!=null) {prayerEffect=new ImportedEnemyEffect(PrayerPrefab,body,new Vector3(0,1.15f,0),true);prayerEffect.SetScale(PrayerEffectScale);prayerEffect.SetVisible(false);}
        if(EmpoweredPrefab!=null) {empoweredEffect=new ImportedEnemyEffect(EmpoweredPrefab,body,new Vector3(0,1f,0),true);empoweredEffect.SetScale(EmpoweredEffectScale);empoweredEffect.SetVisible(false);}
        initialized=true;health=Mathf.Max(1,MaxHealth);cycle.Reset();return true;
    }
    public override void ResetEncounter() {
        if(!Ensure())return;
        health=Mathf.Max(1,MaxHealth);engaged=false;shadowHit=false;age=0;cycle.Reset();
        tetherNarrated=false;nextTetherSightCheck=0;
        body.localPosition=Vector3.zero;body.localRotation=Quaternion.identity;
        ReleaseLampClaim();UpdateLampClaim();
        ClearProjectiles();foreach(var effect in effects)effect.Dispose();effects.Clear();
        body.gameObject.SetActive(true);tether.enabled=true;
        bodyArt?.Restart();
        modelView?.Reset();
        prayerEffect?.SetVisible(false);empoweredEffect?.SetVisible(false);Draw(true);
    }
    public override void Tick(float dt) {
        if(!Ensure() || dt<=0)return;
        age+=dt;
        for(int i=effects.Count-1;i>=0;i--)if(!effects[i].Tick(dt)) {effects[i].Dispose();effects.RemoveAt(i);}
        if(!Alive) {
            if(body.gameObject.activeSelf && (modelView==null || !modelView.TickDeath(dt)))body.gameObject.SetActive(false);
            return;
        }
        UpdateLampClaim();
        Vector3 toHunter=Encounter.HunterGroundPosition-FootPosition;toHunter.y=0;
        float walked=0;
        float releaseDistance=Mathf.Max(NoticeDistance+4,DisengageDistance);
        if(engaged && NoticeDistance>0 && toHunter.sqrMagnitude>releaseDistance*releaseDistance) {
            engaged=false;
            // Leaving range stops the attack clock. An established connection
            // lasts until the lamp is shot/relit or becomes unavailable.
            if(!cycle.Connected)cycle.Reset();
            prayerEffect?.SetVisible(false);prayerProgress.enabled=false;
            if(windupMarker!=null)windupMarker.gameObject.SetActive(false);
        }
        bool newlyEngaged=!engaged && (NoticeDistance<=0 || toHunter.sqrMagnitude<=NoticeDistance*NoticeDistance);
        if(newlyEngaged)engaged=true;
        if(engaged) {
            if(toHunter.sqrMagnitude>.001f)body.rotation=Quaternion.LookRotation(toHunter.normalized,Vector3.up);
            cycle.AttackInterval=Mathf.Max(.1f,AttackInterval);cycle.Windup=Mathf.Max(.3f,AttackWindup);
            cycle.PrayerTime=Mathf.Max(1.5f,PrayerDuration);cycle.StaggerTime=Mathf.Max(.2f,InterruptStagger);
            bool lampLit=Link>=0 && Encounter.IsLampLit(Link);
            var previousAction=Action;
            var signal=newlyEngaged ? cycle.BeginEngagement(lampLit,Link>=0) : FireThiefCycle.Signal.None;
            // A new opening receives its full readable prayer duration, even
            // when the caller advances the encounter clock in a large step.
            if(signal==FireThiefCycle.Signal.None)signal=cycle.Step(dt,lampLit,Link>=0);
            if((signal&FireThiefCycle.Signal.PrayerStarted)!=0)Encounter.RequestNarration("03_fire_thief");
            if((signal&FireThiefCycle.Signal.ConnectionStarted)!=0){tetherNarrated=false;nextTetherSightCheck=age;}
            if((signal&FireThiefCycle.Signal.Extinguish)!=0 && Link>=0)Encounter.TryExtinguishLamp(Link);
            if((signal&FireThiefCycle.Signal.Volley)!=0)ReleaseVolley();
            else if(previousAction==FireThiefCycle.Action.Waiting && Action==previousAction && !(modelView?.FinishingAction ?? false))
                walked=WalkIntoRange(toHunter,dt);
        }
        Encounter.DisplayLampTheft(claimedLamp,this,cycle.Connected);
        TickProjectiles(dt);bodyArt?.Tick(dt);
        modelView?.Tick(dt,Action,cycle.Progress,engaged,ProtectedByLamp,Empowered,walked);
        prayerEffect?.SetVisible(engaged && Action==FireThiefCycle.Action.Prayer);prayerEffect?.Tick(dt);
        empoweredEffect?.SetVisible(Empowered);empoweredEffect?.Tick(dt);
        Draw();
    }
    // Move the combat body, leaving the level designer's encounter root as its home.
    // A ranged caster advances at long range and walks backwards when crowded.
    float WalkIntoRange(Vector3 toHunter,float dt) {
        float distance=toHunter.magnitude;
        if(distance<.01f || WalkSpeed<=0 || RoamRadius<=0)return 0;
        float stride=distance>7.5f ? distance-7.5f : distance<4 ? distance-4 : 0;
        if(stride==0)return 0;
        float sign=Mathf.Sign(stride);
        Vector3 from=body.position;
        Vector3 desired=from+toHunter/distance*(sign*Mathf.Min(Mathf.Abs(stride),WalkSpeed*dt));
        Vector3 fromHome=Vector3.ProjectOnPlane(desired-transform.position,Vector3.up);
        Vector3 goal=transform.position+Vector3.ClampMagnitude(fromHome,RoamRadius);
        Vector3 displacement=Vector3.ProjectOnPlane(goal-from,Vector3.up);
        float length=displacement.magnitude;
        if(length<.0001f)return 0;
        Vector3 direction=displacement/length;
        int link=Link;
        for(float covered=0;covered<length;) {
            float step=Mathf.Min(.22f,length-covered);
            // Advance in short sections so a slow frame cannot tunnel through a wall or cross a gap.
            Vector3 foot=body.position;
            if(!Encounter.TryMinorGround(foot+direction*step,foot.y,out var ground))break;
            if(link>=0 && (ground-Encounter.GetLampPosition(link)).sqrMagnitude>LampSearchRadius*LampSearchRadius)break;
            if(Physics.CapsuleCast(foot+Vector3.up*.85f,foot+Vector3.up*1.61f,.31f,direction,step,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) ||
                Physics.CheckCapsule(ground+Vector3.up*.85f,ground+Vector3.up*1.61f,.31f,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))break;
            body.position=ground;covered+=step;
        }
        return sign*Vector3.ProjectOnPlane(body.position-from,Vector3.up).magnitude;
    }
    void ReleaseVolley() {
        modelView?.OnVolley();
        Vector3 origin=modelView?.CastingHand!=null ? modelView.CastingHand.position : body.position+Vector3.up*1.37f+body.forward*.38f;
        Vector3 direction=(Encounter.HunterBodyPosition+Vector3.up*.86f-origin).normalized;
        // The volley direction is fixed on release. The continuing lamp state
        // controls every projectile's power, including already flying smoke arrows.
        int count=Empowered ? 3 : 1;
        for(int i=0;i<count;i++) {
            float yaw=count==1 ? 0 : (i-1)*FanAngle;
            var projectile=new Projectile {Position=origin,Direction=Quaternion.AngleAxis(yaw,Vector3.up)*direction,Remaining=ProjectileLifetime};
            if(ProjectilePrefab!=null) {
                projectile.Effect=new ImportedEnemyEffect(ProjectilePrefab,transform,origin,false);
                projectile.Effect.SetScale(ProjectileEffectScale);
                projectile.Effect.Transform.rotation=Quaternion.LookRotation(projectile.Direction);
            } else if(ShowDevelopmentPlaceholders) {
                projectile.Marker=MinorEnemyVisuals.Part("Development projectile marker",PrimitiveType.Sphere,transform,Vector3.zero,Vector3.one*.23f,warningMaterial);
                projectile.Marker.position=origin;
            }
            projectiles.Add(projectile);
        }
    }
    void TickProjectiles(float dt) {
        bool empowered=Empowered;
        for(int i=projectiles.Count-1;i>=0;i--) {
            var projectile=projectiles[i];Vector3 start=projectile.Position;
            float distance=ProjectileSpeed*(empowered ? EmpoweredSpeedMultiplier : 1)*Mathf.Min(dt,projectile.Remaining);
            Vector3 end=start+projectile.Direction*distance;
            float environmentDistance=float.PositiveInfinity;
            // Imported art colliders are disabled; only actual level geometry blocks smoke.
            if(Physics.Raycast(start,projectile.Direction,out RaycastHit obstruction,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                environmentDistance=obstruction.distance;
            Vector3 visibleEnd=environmentDistance<distance ? start+projectile.Direction*environmentDistance : end;
            bool hit=MinorEnemyRules.SegmentHitsHunter(start,visibleEnd,Encounter.HunterBodyPosition,.14f);
            if(hit)Encounter.DamageHunter(Mathf.Max(1,Mathf.RoundToInt(ProjectileDamage*(empowered ? EmpoweredDamageMultiplier : 1))));
            projectile.Position=visibleEnd;projectile.Remaining-=dt;
            if(projectile.Marker!=null)projectile.Marker.position=visibleEnd;
            if(projectile.Effect!=null) {projectile.Effect.Transform.position=visibleEnd;projectile.Effect.Tick(dt);}
            if(hit || environmentDistance<distance || projectile.Remaining<=0) {Release(projectile);projectiles.RemoveAt(i);}
        }
    }
    void Draw(bool force=false) {
        bool alive=Alive;int link=Link;
        tether.enabled=alive && Connected;
        bool praying=engaged && Action==FireThiefCycle.Action.Prayer;
        prayerProgress.enabled=alive && link>=0 && praying;
        if(tether.enabled) {
            Vector3 origin=body.position+Vector3.up*1.25f,target=Encounter.GetLampPosition(link);
            float pulse=Empowered ? .5f+.5f*Mathf.Sin(age*8f) : 0;
            tether.widthMultiplier=Empowered ? .046f+.022f*pulse : praying ? .04f : .025f;
            for(int i=0;i<tether.positionCount;i++) {
                float t=i/(float)(tether.positionCount-1);
                float sag=(praying ? .17f : .48f)*Mathf.Sin(t*Mathf.PI);
                tether.SetPosition(i,Vector3.Lerp(origin,target,t)-Vector3.up*sag);
            }
            if(engaged && !tetherNarrated && age>=nextTetherSightCheck) {
                nextTetherSightCheck=age+.2f;
                if(HunterCanSeeTether()) {
                    Encounter.RequestNarration("11_acolyte_shadow");
                    tetherNarrated=true;
                }
            }
            if(prayerProgress.enabled) {
                prayerProgress.SetPosition(0,target+Vector3.up*.42f-Vector3.right*.45f);
                prayerProgress.SetPosition(1,target+Vector3.up*.42f+Vector3.right*(-.45f+.9f*cycle.Progress));
            }
        }
        if(arm!=null)arm.localRotation=Quaternion.Euler(Action==FireThiefCycle.Action.Windup ? -95*cycle.Progress :
            Action==FireThiefCycle.Action.Prayer ? -115 : Action==FireThiefCycle.Action.Stagger ? 35 : -12,0,0);
        if(windupMarker!=null) {
            windupMarker.gameObject.SetActive(Action==FireThiefCycle.Action.Windup);
            windupMarker.localScale=Vector3.one*Mathf.Lerp(.08f,.22f,cycle.Progress);
        }
        Vector3 foot=FootPosition;
        if(!alive) {MinorEnemyShadow.Release(ref shadow);return;}
        if(!MinorEnemyShadow.Prepare(ref shadow,Encounter,transform,"Shootable acolyte shadow",shadowMaterial,foot,force,out bool full)) {
            if(shadow==null)ShadowLampIndex=-1;
            return;
        }
        if(Encounter.TryMinorGround(foot,foot.y,out var grounded))foot=grounded;
        var lighting=Encounter.GetMinorShadowLighting(foot,ShadowLightRadius);
        ShadowLampIndex=lighting.LampIndex;
        shadow.Clear();
        shadow.Volume(MinorEnemyVolumes.AcolyteRobe,foot,body.rotation,lighting,ShadowSize,Vector3.zero,Vector3.one);
        if(full) {
            shadow.Volume(MinorEnemyVolumes.AcolyteHood,foot,body.rotation,lighting,ShadowSize,Vector3.zero,Vector3.one);
            shadow.Volume(praying ? MinorEnemyVolumes.AcolytePrayerSleeve : MinorEnemyVolumes.AcolyteSleeve,
                foot,body.rotation,lighting,ShadowSize,Vector3.zero,Vector3.one);
        }
        shadow.Publish(alive);
    }
    bool HunterCanSeeTether() {
        var eye=Encounter.Eye;
        if(eye==null)return false;
        for(int i=1;i<tether.positionCount;i+=2) {
            Vector3 point=tether.GetPosition(i);
            Vector3 screen=eye.WorldToViewportPoint(point);
            if(screen.z<=eye.nearClipPlane || screen.x<0 || screen.x>1 || screen.y<0 || screen.y>1)continue;
            if(!Physics.Linecast(eye.transform.position,point,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))return true;
        }
        return false;
    }
    public override void OnLampShot(int index) {
        if(!initialized || !Alive || index!=Link)return;
        if(cycle.LampShot()) {
            Encounter.DisplayLampTheft(index,this,false);
            modelView?.OnInterrupted();
            if(InterruptPrefab!=null) {
                var effect=new ImportedEnemyEffect(InterruptPrefab,transform,body.position+Vector3.up,false,2f);
                effect.SetScale(InterruptEffectScale);effects.Add(effect);
            }
        }
        Draw();
    }
    public override bool TryHitShadow(Ray ray,float maxDistance,out float distance) {
        distance=initialized && Alive && shadow!=null ? shadow.Hit(ray) : float.PositiveInfinity;
        shadowHit=distance<=maxDistance;return shadowHit;
    }
    public override void ReceiveShadowHit(Bolt kind,Vector3 point) {
        if(!initialized || !Alive || !shadowHit || shadow==null || !shadow.Visible)return;
        shadowHit=false;
        if(ProtectedByLamp)return;
        // All bolt types have a direct hit; acolytes survive more than one shot.
        health=Mathf.Max(0,health-(kind==Bolt.Holy ? 10 : 8));
        if(health>0) {modelView?.OnHit();return;}
        ReleaseLampClaim();engaged=false;
        cycle.Kill();ClearProjectiles();
        prayerEffect?.SetVisible(false);empoweredEffect?.SetVisible(false);
        if(modelView==null || !modelView.StartDeath())body.gameObject.SetActive(false);
        tether.enabled=prayerProgress.enabled=false;MinorEnemyShadow.Release(ref shadow);
        if(DeathPrefab!=null) {
            var effect=new ImportedEnemyEffect(DeathPrefab,transform,FootPosition+Vector3.up,false,3f);
            effect.SetScale(DeathEffectScale);effects.Add(effect);
        }
    }
    static void Release(Projectile projectile) {projectile.Effect?.Dispose();if(projectile.Marker!=null)MinorEnemyVisuals.Release(projectile.Marker.gameObject);}
    void ClearProjectiles() {foreach(var projectile in projectiles)Release(projectile);projectiles.Clear();}
    public override void OnEncounterEnded() {
        ReleaseLampClaim();
        if(!initialized)return;
        ClearProjectiles();foreach(var effect in effects)effect.Dispose();effects.Clear();
        tether.enabled=prayerProgress.enabled=false;
        if(windupMarker!=null)windupMarker.gameObject.SetActive(false);
        prayerEffect?.SetVisible(false);empoweredEffect?.SetVisible(false);bodyArt?.StopAndClear();MinorEnemyShadow.Release(ref shadow);
        modelView?.Reset();
        if(!Alive)body.gameObject.SetActive(false);
    }
    protected override void OnDisable() {
        ReleaseLampClaim();
        base.OnDisable();if(!initialized)return;
        foreach(var effect in effects)effect.Dispose();effects.Clear();
        modelView?.Reset();
        body.gameObject.SetActive(false);tether.enabled=prayerProgress.enabled=false;MinorEnemyShadow.Release(ref shadow);ClearProjectiles();
    }
    protected override void OnEnable() {base.OnEnable();if(initialized)body.gameObject.SetActive(Alive);}
    protected override void OnActivationChanged(bool value) {
        if(value) {if(initialized && Alive)UpdateLampClaim();}
        else {
            ReleaseLampClaim();engaged=false;cycle.Reset();
            MinorEnemyShadow.Release(ref shadow);
            if(initialized) {tether.enabled=prayerProgress.enabled=false;prayerEffect?.SetVisible(false);empoweredEffect?.SetVisible(false);modelView?.Reset();}
        }
    }
    void OnDestroy() {
        ReleaseLampClaim();
        ClearProjectiles();foreach(var effect in effects)effect.Dispose();
        modelView?.Reset();bodyArt?.Dispose();prayerEffect?.Dispose();empoweredEffect?.Dispose();shadow?.Dispose();
        MinorEnemyVisuals.Release(bodyMaterial);MinorEnemyVisuals.Release(headMaterial);MinorEnemyVisuals.Release(shadowMaterial);
        MinorEnemyVisuals.Release(tetherMaterial);MinorEnemyVisuals.Release(warningMaterial);
    }
}
