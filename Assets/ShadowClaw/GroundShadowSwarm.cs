using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Shadow Claw/Encounters/Ground Shadow Swarm")]
public sealed class GroundShadowSwarm : ShadowEncounterActor
{
    [Header("Authored placement (this component never places itself in a scene)")]
    [Range(4,5)] public int Count=4;
    public Transform[] MemberAnchors;
    [Min(.2f)] public float FormationRadius=1.25f;
    [Min(0)] public float NoticeDistance=18f;
    [Tooltip("Distance from the nearest living member that ends pursuit; at least NoticeDistance + 4. NoticeDistance <= 0 disables distance disengagement.")]
    [Min(0)] public float DisengageDistance=32f;
    [Header("Associated effective lamps")]
    [Min(0)] public float ShadowLightRadius=18f;
    [HideInInspector]
    public EncounterLantern[] LinkedLanterns;
    [HideInInspector]
    public int[] LampIndices={0,1,2,3,4,5};
    [Header("Pursuit and capped contact pressure")]
    [Min(.1f)] public float PursuitSpeed=3.6f;
    [Min(.4f)] public float ContactRadius=1.12f;
    [Range(.1f,1f)] public float ContactSlowMultiplier=.65f;
    [Min(0)] public float GroupDamagePerSecond=3f;
    [Tooltip("Height of each ghost's centre above its current floor, before the gentle bob.")]
    [Min(.15f)] public float FloatHeight=1.75f;
    [Min(.2f)] public float ShadowSize=1f;
    [Header("Body and imported effects")]
    public GameObject BodyPrefab;
    public GameObject MistPrefab;
    public GameObject DissolvePrefab;
    public bool UseImportedDefaultEffects=true;
    [Min(.01f)] public float MistScale=.4f;
    [Min(.01f)] public float DissolveScale=.22f;
    [Tooltip("Display the default wisp model when no body prefab is assigned.")]
    public bool ShowDevelopmentPlaceholders=true;
    [Min(.1f)] public float DissolveDuration=2f;

    sealed class Member {
        public Vector3 Foot,Spawn;
        public Transform Visual;
        public MinorEnemyShadow Shadow;
        public string ShadowName;
        public ImportedEnemyEffect Art;
        public ImportedEnemyEffect Mist;
        public bool Alive=true;
    }
    readonly List<Member> members=new List<Member>();
    readonly List<ImportedEnemyEffect> effects=new List<ImportedEnemyEffect>();
    Material shadowMaterial;
    Vector3[] projectionShape,simpleShape;
    float projectionTop;
    bool initialized,engaged;
    int pendingHit=-1;
    float age;
    public int LivingCount {get;private set;}
    public override bool Alive => !initialized || LivingCount>0;
    public bool Engaged => engaged;

    bool Ensure() {
        if(initialized)return true;
        if(Encounter==null)return false;
        MistPrefab=MinorEnemyVisuals.DefaultEffect(MistPrefab,"vfx_Smoke_01",UseImportedDefaultEffects);
        DissolvePrefab=MinorEnemyVisuals.DefaultEffect(DissolvePrefab,"vfx_Implosion_01",UseImportedDefaultEffects);
        if(BodyPrefab==null && ShowDevelopmentPlaceholders)BodyPrefab=Resources.Load<GameObject>("HollowWisp/HollowWisp");
        var bodyMesh=BodyPrefab!=null ? BodyPrefab.GetComponent<MeshFilter>() : null;
        projectionShape=bodyMesh!=null && bodyMesh.sharedMesh!=null && bodyMesh.sharedMesh.isReadable
            ? MinorEnemyVolumes.GhostVertices(bodyMesh.sharedMesh) : MinorEnemyVolumes.GhostBody;
        simpleShape=MinorEnemyVolumes.SimpleBody(projectionShape);
        foreach(var vertex in projectionShape)projectionTop=Mathf.Max(projectionTop,vertex.y);
        shadowMaterial=MinorEnemyVisuals.Material(new Color(.055f,.035f,.09f,1));
        int count=Mathf.Clamp(Count,4,5);
        for(int i=0;i<count;i++) {
            Vector3 local=new Vector3(Mathf.Cos(i*Mathf.PI*2/count),0,Mathf.Sin(i*Mathf.PI*2/count))*FormationRadius;
            Vector3 spawn=MemberAnchors!=null && i<MemberAnchors.Length && MemberAnchors[i]!=null ?
                MemberAnchors[i].position : transform.TransformPoint(local);
            if(Encounter.TryMinorGround(spawn,spawn.y,out var groundedSpawn))spawn=groundedSpawn;
            var member=new Member {Spawn=spawn,Foot=spawn};
            member.Visual=new GameObject("Ground shadow "+(i+1)).transform;
            member.Visual.SetParent(transform,true);member.Visual.position=spawn;
            if(BodyPrefab!=null) {member.Art=new ImportedEnemyEffect(BodyPrefab,member.Visual,Vector3.zero,true);member.Art.DisableNativeShadows();}
            if(MistPrefab!=null) {member.Mist=new ImportedEnemyEffect(MistPrefab,member.Visual,Vector3.zero,true);member.Mist.SetScale(MistScale);}
            member.ShadowName="Shootable ground shadow "+(i+1);
            members.Add(member);
        }
        initialized=true;LivingCount=members.Count;
        return true;
    }
    public override void ResetEncounter() {
        if(!Ensure())return;
        age=0;engaged=false;pendingHit=-1;LivingCount=members.Count;
        foreach(var effect in effects)effect.Dispose();effects.Clear();
        for(int i=0;i<members.Count;i++) {
            var member=members[i];
            if(MemberAnchors!=null && i<MemberAnchors.Length && MemberAnchors[i]!=null)member.Spawn=MemberAnchors[i].position;
            member.Foot=member.Spawn;
            if(Encounter.TryMinorGround(member.Foot,member.Foot.y,out var grounded))member.Foot=grounded;
            member.Alive=true;member.Visual.gameObject.SetActive(true);
            member.Art?.Restart();member.Mist?.SetVisible(true);member.Mist?.Restart();
            member.Visual.position=member.Foot+Vector3.up*FloatHeight;
        }
        DrawShadows(true);
    }
    public override void Tick(float dt) {
        if(!Ensure() || dt<=0)return;
        age+=dt;
        for(int i=effects.Count-1;i>=0;i--)if(!effects[i].Tick(dt)) {effects[i].Dispose();effects.RemoveAt(i);}
        if(!Alive)return;
        Vector3 hunter=Encounter.HunterGroundPosition;
        float nearest=float.PositiveInfinity;
        foreach(var member in members)if(member.Alive)
            nearest=Mathf.Min(nearest,Horizontal(hunter-member.Foot).sqrMagnitude);
        float releaseDistance=Mathf.Max(NoticeDistance+4,DisengageDistance);
        if(engaged && NoticeDistance>0 && nearest>releaseDistance*releaseDistance)engaged=false;
        // A pursuing group can travel far from its authored root. Perception
        // follows surviving members so returning near them wakes the group.
        if(!engaged && (NoticeDistance<=0 || nearest<=NoticeDistance*NoticeDistance)) {
            engaged=true;Encounter.RequestNarration("02_ground_shadows");
        }
        bool contact=false;
        for(int i=0;i<members.Count;i++) {
            var member=members[i];if(!member.Alive)continue;
            Vector3 delta=Horizontal(hunter-member.Foot);
            if(engaged) {
                float distance=delta.magnitude;
                // A small ring prevents every target disappearing under the first-person camera.
                Vector3 direction=distance>.001f ? delta/distance : transform.forward;
                Vector3 separation=Vector3.zero;
                foreach(var other in members)if(other!=member && other.Alive) {
                    Vector3 away=Horizontal(member.Foot-other.Foot);float sq=away.sqrMagnitude;
                    if(sq>.0001f && sq<.85f*.85f)separation+=away.normalized*(.85f-Mathf.Sqrt(sq))*2f;
                }
                float advance=Mathf.Min(PursuitSpeed*dt,Mathf.Max(0,distance-.93f));
                Vector3 movement=direction*advance+Vector3.ClampMagnitude(separation,PursuitSpeed*.35f)*dt;
                // Follow consecutive treads from this member's current storey.
                // A frame may cross several risers; it may not jump to a roof or
                // fabricate a floor across a gap from the global highest sample.
                int steps=Mathf.Max(1,Mathf.CeilToInt(movement.magnitude/.15f));
                for(int step=0;step<steps;step++) {
                    Vector3 candidate=member.Foot+movement/steps;
                    if(!Encounter.TryMinorGround(candidate,member.Foot.y,out var next))break;
                    member.Foot=next;
                }
                if(direction.sqrMagnitude>.01f)member.Visual.rotation=Quaternion.LookRotation(direction,Vector3.up);
            }
            float bob=Mathf.Sin(age*2.7f+i*1.6f)*.08f;
            member.Visual.position=member.Foot+Vector3.up*(FloatHeight+bob);
            member.Art?.Tick(dt);
            member.Mist?.Tick(dt);
            // The trailing body hangs below the head and can still brush a grounded hunter.
            Vector3 tail=member.Visual.position-Vector3.up*.52f;
            if(engaged && MinorEnemyRules.SegmentHitsHunter(tail,member.Visual.position,Encounter.HunterBodyPosition,Mathf.Max(.1f,ContactRadius-.35f)))contact=true;
        }
        if(contact)Encounter.ApplyContactPressure(GroupDamagePerSecond,ContactSlowMultiplier,dt);
        DrawShadows();
    }
    void DrawShadows(bool force=false) {
        for(int i=0;i<members.Count;i++) {
            var member=members[i];
            if(!member.Alive || !MinorEnemyShadow.Prepare(ref member.Shadow,Encounter,transform,member.ShadowName,
                shadowMaterial,member.Foot,force,out bool full))continue;
            member.Shadow.Clear();
            var body=member.Art?.Transform ?? member.Visual;
            Vector3 caster=body.position;
            if(!full)caster.y=member.Foot.y+FloatHeight+(body.position.y-member.Visual.position.y);
            Vector3 offset=Quaternion.Inverse(body.rotation)*(caster-member.Foot);
            var lighting=Encounter.GetMinorShadowLightingAt(caster,ShadowLightRadius);
            lighting.ProjectionHeight=Mathf.Max(2.1f,caster.y-member.Foot.y+projectionTop*body.lossyScale.y);
            member.Shadow.Volume(full?projectionShape:simpleShape,member.Foot,body.rotation,lighting,ShadowSize,offset,body.lossyScale,1.3f);
            member.Shadow.Publish(true);
        }
    }
    static Vector3 Horizontal(Vector3 point) => new Vector3(point.x,0,point.z);
    public override bool TryHitShadow(Ray ray,float maxDistance,out float distance) {
        distance=float.PositiveInfinity;pendingHit=-1;
        if(!initialized || !Alive)return false;
        for(int i=0;i<members.Count;i++)if(members[i].Alive) {
            float hit=members[i].Shadow?.Hit(ray) ?? float.PositiveInfinity;
            if(hit<=maxDistance && hit<distance) {distance=hit;pendingHit=i;}
        }
        return pendingHit>=0;
    }
    public override void ReceiveShadowHit(Bolt kind,Vector3 point) {
        if(pendingHit<0 || pendingHit>=members.Count)return;
        var member=members[pendingHit];pendingHit=-1;
        if(!member.Alive || member.Shadow==null || !member.Shadow.Visible)return;
        member.Alive=false;LivingCount--;member.Visual.gameObject.SetActive(false);
        MinorEnemyShadow.Release(ref member.Shadow);
        if(DissolvePrefab!=null) {
            var effect=new ImportedEnemyEffect(DissolvePrefab,transform,member.Visual.position,false,DissolveDuration);
            effect.SetScale(DissolveScale);effects.Add(effect);
        }
    }
    public override void OnEncounterEnded() {
        foreach(var effect in effects)effect.Dispose();effects.Clear();
        foreach(var member in members) {
            member.Mist?.SetVisible(false);member.Art?.StopAndClear();MinorEnemyShadow.Release(ref member.Shadow);
        }
    }
    protected override void OnDisable() {
        base.OnDisable();
        foreach(var effect in effects)effect.Dispose();effects.Clear();
        foreach(var member in members) {member.Visual.gameObject.SetActive(false);MinorEnemyShadow.Release(ref member.Shadow);}
    }
    protected override void OnEnable() {
        base.OnEnable();
        if(initialized)foreach(var member in members)member.Visual.gameObject.SetActive(member.Alive);
    }
    protected override void OnActivationChanged(bool value) {
        if(!value)foreach(var member in members)MinorEnemyShadow.Release(ref member.Shadow);
    }
    void OnDestroy() {
        foreach(var member in members) {
            MinorEnemyShadow.Release(ref member.Shadow);member.Art?.Dispose();member.Mist?.Dispose();
        }
        foreach(var effect in effects)effect.Dispose();
        MinorEnemyVisuals.Release(shadowMaterial);
    }
}
