using UnityEngine;
using System.Collections.Generic;

public sealed partial class ShadowLab : MonoBehaviour
{
    public string EncounterDisplayName="铁冠厅";
    public string EncounterDisplayNameEnglish="THE IRON CROWN HALL";
    [Header("Hunter movement")]
    [Min(.1f)] public float WalkSpeed=5f;
    [Min(.1f)] public float SprintSpeed=8f;
    [Range(.05f,1f)] public float SlowSpeedMultiplier=.4f;
    [Header("Final boss shadow")]
    [Tooltip("Linear size of the visible and shootable ground shadow. 2 doubles width and length.")]
    [Min(.1f)] public float DemonShadowScale=2f;
    bool sprinting;
    public bool Sprinting => sprinting && !GameplayPaused && state.Pin<=0;
    public bool Recovering => !Developer.Paused && !finished && boss.Recovering;
    public float RecoveryRemaining => boss.RecoveryRemaining;
    IronHallEncounter hall;
    CharacterController playerBody, enemyBody;
    System.Func<Vector3,float,Vector3> moveDemon;
    ClawState state;
    HuntBoss boss;
    internal DeveloperControls Developer {get;private set;}
    public ShiyinUI UI {get;private set;}
    public Camera Eye => eye;
    public bool EncounterStarted { get; private set; }
    public float PinRemaining => state!=null ? state.Pin : 0;
    public float SlowRemaining => boss!=null ? boss.Slow : 0;
    public float StunRemaining => combat.Stun;
    public float BossActionRemaining => boss!=null ? boss.ActionRemaining : 0;
    public event System.Action<string> ShotResolved;

    sealed class RoomSource {
        public Transform Fixture;
        public Collider Target;

        public float Remaining, Impact;
        public LanternFire Flame;
        public LineRenderer Soot;
        public bool Lit => Remaining>0;
        public float Protected;
        public string Name;
    }
    const float LampLife=24f;
    RoomSource ritual;
    readonly Vector4[] lampVectors=new Vector4[HuntBoss.LampCapacity];
    RoomSource[] lamps;
    float lightDecision, hurtFlash;
    BossSpectacle bossEffects;
    public int ShadowCount => shadows.Count;
    public Transform RitualTarget => ritual?.Fixture;
    public int LitLamps => boss.LitLamps;
    public HuntBoss.Move BossAction => boss.Action;
    public Vector2 DemonShadowCenter => state.Witch.Body?.Aim ?? state.At(state.Enemy,0,1.2f);
    public Vector3 GameplayLight => state.Lamp;
    internal ClawState ShadowState => state;
    ShadowBurnFx shadowBurn;
    readonly List<ClawState> shadows=new List<ClawState>(1);
    bool finished;
    HunterCrossbow crossbow;
    HunterWeaponView weaponView;
    public HunterCrossbow Weapon => crossbow;
    Material skin;
    Font hudFont;
    static readonly string[] bolts={"圣水","火焰","雷电","光亮"};
    WallCombat combat = new WallCombat();
    Transform receiver, floor;
    float yaw, pitch;

    readonly System.Collections.Generic.Dictionary<WallCombat.Mark,GameObject> marks =
        new System.Collections.Generic.Dictionary<WallCombat.Mark,GameObject>();
    Material fireColor, stormColor;
    Camera eye;
    Transform witch, hand;
    ShadowDemonView modelView;
    string shot="";

    Material Make(string color) {
        var mat=new Material(Resources.Load<Shader>("Ink"));
        ColorUtility.TryParseHtmlString("#"+color,out Color c); mat.color=c; return mat;
    }
    Transform Solid(string name, PrimitiveType shape, Transform parent, Vector3 p, Vector3 size, Material mat) {
        var obj=GameObject.CreatePrimitive(shape);
        obj.name=name; obj.transform.SetParent(parent,false);
        obj.transform.localPosition=p; obj.transform.localScale=size;
        obj.GetComponent<Renderer>().sharedMaterial=mat;
        if(name=="Masonry joint" || parent==eye?.transform) obj.GetComponent<Collider>().enabled=false;
        return obj.transform;
    }
    Transform Actor(string name) {
        var root=new GameObject(name).transform;
        root.SetParent(transform,false);
        var coat=Make("653F50"); skin=Make("D9AC9B");
        Solid("Torso",PrimitiveType.Capsule,root,new Vector3(0,1.05f,0),new Vector3(.44f,.47f,.32f),coat);
        Solid("Head",PrimitiveType.Sphere,root,new Vector3(0,1.67f,0),new Vector3(.29f,.34f,.28f),skin);
        for(int s=-1;s<=1;s+=2) {
            Solid("Leg",PrimitiveType.Capsule,root,new Vector3(s*.115f,.42f,0),new Vector3(.17f,.42f,.19f),coat);
            var pivot=new GameObject("Shoulder").transform;
            pivot.SetParent(root,false); pivot.localPosition=new Vector3(s*.21f,1.36f,0);
            Solid("Arm",PrimitiveType.Capsule,pivot,new Vector3(s*.04f,-.25f,0),new Vector3(.13f,.28f,.14f),coat);
            if(s==1) hand=pivot;
        }
        var mantle=Make("412238"); var horn=Make("C1A278");
        Solid("Bell mantle",PrimitiveType.Capsule,root,new Vector3(0,.82f,-.04f),new Vector3(.59f,.64f,.39f),mantle);
        for(int side=-1;side<=1;side+=2) {
            var tine=Solid("Crown horn",PrimitiveType.Capsule,root,new Vector3(.15f*side,1.91f,-.015f),new Vector3(.07f,.21f,.075f),horn);
            tine.localRotation=Quaternion.Euler(-14,0,-side*30);
            Solid("Eye",PrimitiveType.Sphere,root,new Vector3(.065f*side,1.70f,.134f),new Vector3(.045f,.029f,.036f),Make("FF9B59"));
        }
        return root;
    }
    void Awake() {
        Developer=new DeveloperControls(value=>boss.HunterHealthLocked=value);
        hudFont=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",16);
        hall=GetComponent<IronHallEncounter>();
        eye=hall!=null && hall.UseAuthoredLayout ? hall.GameplayCamera : null;
        if(eye==null) {
            eye=new GameObject("Hunter eyes").AddComponent<Camera>();
            eye.clearFlags=CameraClearFlags.SolidColor; eye.backgroundColor=new Color(.08f,.10f,.14f);
            eye.fieldOfView=72; eye.nearClipPlane=.04f;
        }
        eye.transform.SetParent(transform,true);
        eye.gameObject.SetActive(true); eye.enabled=true;
        if(hall!=null && hall.UseAuthoredLayout) eye.clearStencilAfterLightingPass=true;
        weaponView=HunterWeaponView.Equip(eye,transform);
        fireColor=Make("FF6437"); stormColor=Make("6DD6EF");
        if(hall!=null) {
            hall.Prepare();
            floor=hall.Floor!=null ? hall.Floor.transform : null;
            playerBody=hall.CreateBody("Hunter capsule",transform);
            enemyBody=hall.CreateBody("Demon capsule",transform);
            moveDemon=MoveDemonBody;
        } else BuildTestRoom();
        WallInk FloorInk(string name,string color,float height) {
            Material material=hall!=null && (name=="Floor hunter" || name=="Floor claw")
                ? new Material(Resources.Load<Shader>("ReceiverShadow")) : Make(color);
            if(name=="Floor claw" && hall!=null)material.SetFloat("_InkBit",32);
            return new WallInk(transform,name,material,-height,true,hall!=null ? hall.InkBounds : (Rect?)null,
                hall!=null ? hall.SurfaceHeight : (System.Func<float,float,float>)null);
        }
        state=new ClawState(FloorInk("Floor demon","1C1121",.015f),FloorInk("Floor hunter","273447",.012f),
            FloorInk("Floor claw","241129",.018f),FloorInk("Floor mark","FF9349",.020f),true);
        if(hall!=null) state.GroundHeight=hall.SurfaceHeight;
        lamps=new RoomSource[HuntBoss.LampCapacity];
        string[] bays={"前","中","后"};
        for(int i=0;i<lamps.Length;i++) {
            bool left=i<3;
            int bay=i%3;
            var position=new Vector3(left ? -4.5f : 4.5f,2.7f,2.6f-3.25f*bay);
            if(hall!=null) position=hall.WickPosition(i);
            lamps[i]=CreateSource((left ? "左" : "右")+bays[bay]+"灯",position);
            if(hall!=null && hall.UseAuthoredLayout && hall.WickAnchors!=null && i<hall.WickAnchors.Length && hall.WickAnchors[i]!=null) {
                lamps[i].Fixture.SetParent(hall.WickAnchors[i],true);
                lamps[i].Fixture.localPosition=Vector3.zero;
                hall.WickAnchors[i].GetComponentInParent<GrandCombatLantern>()?.ConfigureRuntimeWick(
                    lamps[i].Fixture,lamps[i].Target as SphereCollider,lamps[i].Flame);
            }
        }
        witch=Actor("Caster");
        modelView=ShadowDemonView.Attach(witch);
        hall?.Readability?.Bind(modelView);
        var effectRoot=new GameObject("Bell-sleeve effects");
        effectRoot.transform.SetParent(transform,false);
        bossEffects=effectRoot.AddComponent<BossSpectacle>();
        bossEffects.Initialize(witch,modelView,hall);
        if(hall!=null && modelView!=null) {
            eye.depthTextureMode|=DepthTextureMode.Depth;
            state.Witch.BindCharacter(modelView.GetComponentInChildren<SkinnedMeshRenderer>());
            state.Witch.Body.LinearScale=DemonShadowScale;
            state.Arm.MergeVisualIntoBody();
            var fire=new GameObject("Shadow edge fire");
            fire.transform.SetParent(transform,false);shadowBurn=fire.AddComponent<ShadowBurnFx>();
        }
        if(enemyBody!=null) foreach(var collider in witch.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(enemyBody,collider);
        ResetLab();
        UI=gameObject.AddComponent<ShiyinUI>();
        UI.Initialize(this);
        narrator=gameObject.AddComponent<FairyTaleNarrator>();
        narrator.Initialize(this);
    }
    RoomSource CreateSource(string name,Vector3 position) {
        var fixture=new GameObject(name).transform;
        fixture.SetParent(transform,false);
        fixture.position=position;
        var target=fixture.gameObject.AddComponent<SphereCollider>();
        target.isTrigger=true;
        target.radius=hall!=null ? .49f : .45f;
        var flame=LanternFire.Create(fixture);
        var soot=Warning(name+" extinguish",19);
        soot.sharedMaterial.color=new Color(.09f,.035f,.085f);
        var taper=new AnimationCurve();
        for(int i=0;i<=6;i++) taper.AddKey(i/6f,i%2==0 ? .052f : .008f);
        soot.widthCurve=taper; soot.widthMultiplier=1;
        return new RoomSource { Name=name,Fixture=fixture,Target=target,Flame=flame,Soot=soot };
    }
    static float LampStrength(float remaining) {
        if(remaining<=0) return 0;
        float Blend(float from,float to,float t) => Mathf.Lerp(from,to,Mathf.SmoothStep(0,1,t));
        float fraction=remaining/LampLife;
        if(fraction<1f/60) return Blend(0,.25f,fraction*60);
        if(fraction<=1f/6) return .25f;
        if(fraction<1f/6+1f/16) return Blend(.25f,.55f,(fraction-1f/6)*16);
        if(fraction<=.5f) return .55f;
        return Blend(.55f,1,(fraction-.5f)*16);
    }
    void DrawLamps() {
        float total=0;
        for(int i=0;i<lamps.Length;i++) {
            var lamp=lamps[i];
            float strength=LampStrength(lamp.Remaining);
            total+=strength;
            Vector3 p=lamp.Fixture.position;
            lampVectors[i]=new Vector4(p.x,p.y,p.z,strength);
            lamp.Flame.SetStrength(strength);
            if(hall!=null) hall.SetLamp(i,strength);
            bool threatened=ritual==lamp && boss.Action==HuntBoss.Move.Ritual;
            lamp.Soot.enabled=threatened || lamp.Impact>0;
            if(lamp.Soot.enabled) {
                float reach=threatened ? Mathf.Lerp(.78f,.075f,boss.RitualProgress) : .14f*lamp.Impact/.32f;
                Vector3 center=p+Vector3.left*Mathf.Sign(p.x)*.17f;
                lamp.Soot.SetPosition(0,center);
                for(int arm=0;arm<3;arm++) {
                    float angle=.52f+arm*2.12f;
                    Vector3 along=new Vector3(0,Mathf.Sin(angle),Mathf.Cos(angle));
                    Vector3 bend=new Vector3(0,along.z,-along.y);
                    Vector3 inner=center+(along*.32f+bend*.09f)*reach;
                    Vector3 outer=center+(along*.68f-bend*.08f)*reach;
                    int first=1+arm*6;
                    lamp.Soot.SetPosition(first,inner);
                    lamp.Soot.SetPosition(first+1,outer);
                    lamp.Soot.SetPosition(first+2,center+along*reach);
                    lamp.Soot.SetPosition(first+3,outer);
                    lamp.Soot.SetPosition(first+4,inner);
                    lamp.Soot.SetPosition(first+5,center);
                }
            }
        }
        Shader.SetGlobalVectorArray("_HallLamps",lampVectors);
        Shader.SetGlobalFloat("_HallBrightness",Mathf.Lerp(.19f,1f,total/lamps.Length));
        if(hall!=null) hall.SetRoomLight(total);
    }
    void AgeLamps(float dt) {
        bool changed=false;
        foreach(var lamp in lamps) {
            lamp.Impact=Mathf.Max(0,lamp.Impact-dt);
            if(!lamp.Lit) continue;
            lamp.Remaining=Mathf.Max(0,lamp.Remaining-dt);
            if(lamp.Lit) continue;
            changed=true;
            if(ritual==lamp) { boss.BreakRitual(); ritual=null; lightDecision=1.8f; }
        }
        if(changed) ApplyLighting();
    }
    void ApplyLighting() {
        if(!EncounterStarted) {
            DrawLamps();
            boss.SetLitLamps(HuntBoss.LampCapacity);
            shadows.Clear(); state.Show(false); shadowBurn?.Clear();
            return;
        }
        int count=0, mask=0;
        Vector3 sum=Vector3.zero;
        for(int i=0;i<lamps.Length;i++) {
            var lamp=lamps[i];
            var p=lamp.Fixture.position;
            if(!lamp.Lit) continue;
            count++; mask|=1<<i; sum+=p;
        }
        DrawLamps();
        int previousLit=boss.LitLamps;
        boss.SetLitLamps(count);
        if(previousLit>0 && count==0)RequestNarration("17_final_blackout");
        if(previousLit==0 && count>0 && boss.Action==HuntBoss.Move.LightStagger)RequestNarration("18_final_relight");
        shadows.Clear();
        if(count>0) {
            shadows.Add(state);
            Vector3 center=sum/count;
            state.SilhouetteScale=Mathf.Lerp(1.35f,.65f,(count-1f)/(lamps.Length-1));
            // Let side and bay affect direction without folding the shadow underneath its caster.
            state.SetLamp(mask,hall!=null ? hall.ProjectionLight(center) : new Vector3(center.x*.65f,4.3f,2.6f+center.z*.25f));
        }
        state.Show(count>0);
        if(count>0) state.Witch.Body?.Update(state);
        else shadowBurn?.Clear();
    }
    void ShootLamp(RoomSource lamp) {
        bool wasLit=lamp.Lit;
        int previousCount=LitLamps;
        lamp.Protected=3;
        if(ritual==lamp) {
            boss.BreakRitual(); ritual=null; lightDecision=1.8f;
            shot=lamp.Name+"已守住";
        } else shot=lamp.Name+(lamp.Lit ? "已加固" : "已点燃");
        bool interrupted=boss.Pursuing;
        lamp.Remaining=LampLife;
        if(!wasLit)OnPlayerRelitLamp();
        ApplyLighting();
        NotifyLampShot(System.Array.IndexOf(lamps,lamp));
        if(LitLamps>previousCount && LitLamps>=3)RequestNarration("05_more_lamps");
        if(interrupted) shot="暗猎已打断 · 射击地影";
    }
    void ConsiderLights(float dt) {
        lightDecision-=dt;
        foreach(var lamp in lamps) lamp.Protected-=dt;
        if(lightDecision>0 || combat.Stun>0 || boss.Action!=HuntBoss.Move.Stalk || !boss.WantsRitual) return;
        RoomSource target=null;
        foreach(var lamp in lamps) {
            if(!lamp.Lit || lamp.Protected>0) continue;
            if(target==null || (lamp.Fixture.position-state.Enemy).sqrMagnitude <
                (target.Fixture.position-state.Enemy).sqrMagnitude) target=lamp;
        }
        if(target!=null && boss.BeginRitual()) ritual=target;
    }
    LineRenderer Warning(string name,int count) {
        var obj=new GameObject(name);
        obj.transform.SetParent(transform,false);
        var line=obj.AddComponent<LineRenderer>();
        line.positionCount=count; line.startWidth=line.endWidth=.045f;
        line.sharedMaterial=Make("EF8745"); line.sharedMaterial.SetFloat("_SelfLit",1); line.enabled=false;
        return line;
    }
    void BuildTestRoom() {
        receiver=Solid("Receiver",PrimitiveType.Cube,transform,new Vector3(0,2.79f,3.34f),new Vector3(17,5.60f,.28f),Make("928575"));
        floor=Solid("Floor",PrimitiveType.Cube,transform,new Vector3(0,-.12f,0),new Vector3(22,.24f,15),Make("414951"));
        var joint=Make("796F65");
        for(int y=1;y<9;y++){
            Solid("Masonry joint",PrimitiveType.Cube,transform,new Vector3(0,y*.60f,3.196f),new Vector3(17,.012f,.004f),joint);
            for(int x=-4;x<=4;x++)
                Solid("Masonry joint",PrimitiveType.Cube,transform,new Vector3(x*1.9f+(y%2)*.95f,(y-.5f)*.6f,3.196f),new Vector3(.012f,.60f,.004f),joint);
        }
        DressHall();
    }
    void DressHall() {
        var iron=Make("37313E"); var brass=Make("AB8455"); var velvet=Make("542C42");
        for(int side=-1;side<=1;side+=2) {
            for(int bay=0;bay<4;bay++) {
                float z=2.5f-bay*2.9f;
                Solid("Gallery pier",PrimitiveType.Cube,transform,new Vector3(side*7.6f,2.8f,z),new Vector3(.6f,5.6f,.65f),iron);
                Solid("Gilt capital",PrimitiveType.Cube,transform,new Vector3(side*7.6f,4.8f,z),new Vector3(.85f,.14f,.85f),brass);
                var arch=Solid("Vault rib",PrimitiveType.Cube,transform,new Vector3(side*3.85f,6.05f,z),new Vector3(8.1f,.22f,.28f),iron);
                arch.Rotate(0,0,-side*16);
                Solid("Hanging banner",PrimitiveType.Cube,transform,new Vector3(side*7.5f,3.5f,z),new Vector3(.14f,1.8f,1.2f),velvet);
            }
        }
    }
    float retryDelay;
    int entryKnives,entryCharges,entryRelights;
    Vector3 OutsideGate() {
        var gate=hall.FogGate;
        Vector3 feet=gate.FloorCenter-gate.Inward*(gate.EntryClearance+gate.Thickness*.5f+playerBody.radius+1.25f);
        if(TryMinorGround(feet,gate.FloorCenter.y,out var ground))return ground;
        feet.y=hall.SurfaceHeight(feet.x,feet.z);return feet;
    }
    void ResetLab() => ResetHunt(false);
    void ResetHunt(bool atGate) {
        RestoreExterior();
        retryDelay=0;impactShake=0;hurtSoundCooldown=0;
        ResetVictoryPresentation();
        Developer.SetFrozen(false);
        shadowBurn?.Clear();
        weaponView.ResetView();
        eye.GetComponent<ShiyinAudio>()?.ResetCombatEffects();
        state.Reset(); boss=new HuntBoss(); finished=false; crossbow=new HunterCrossbow();
        boss.HunterDamaged+=ShowFirstBossWoundHint;
        boss.HunterDamaged+=HunterHitFeedback;
        boss.HunterSprintSpeed=Mathf.Max(WalkSpeed,SprintSpeed);
        EncounterStarted=hall==null || !hall.UseAuthoredLayout || !hall.StartWithExploration;
        boss.HunterHealthLocked=Developer.HealthLocked;
        if(hall!=null) {
            boss.MoveBounds=hall.PlayArea;
            state.WalkBounds=EncounterStarted && hall.FogGate==null ? hall.PlayArea : hall.ExplorationArea;
            hall.FogGate?.ResetGate();
            hall.FogGate?.SetEncounterState(EncounterStarted,false,false);
            state.Player=atGate ? OutsideGate() : hall.HunterStart;
            state.Enemy=hall.DemonStart;
            state.Player=PlaceBody(playerBody,state.Player);
            state.Enemy=PlaceBody(enemyBody,state.Enemy);
        }
        boss.StrikeReleased+=bossEffects.Release;
        bossEffects.ResetVfx();
        combat=new WallCombat(); SyncMarks();
        shot=""; ritual=null; lightDecision=2; hurtFlash=0; sprinting=false;
        for(int i=0;i<lamps.Length;i++) {
            lamps[i].Remaining=!EncounterStarted || i%3==0 ? LampLife : 0;
            lamps[i].Impact=0;
            lamps[i].Protected=0;
        }
        ApplyLighting();
        yaw=!EncounterStarted && hall.PlayerSpawn!=null ? hall.PlayerSpawn.eulerAngles.y :
            hall!=null ? Quaternion.LookRotation(state.Enemy-state.Player).eulerAngles.y : -6;
        if(atGate)yaw=hall.FogGate.transform.eulerAngles.y;
        pitch=4;
        modelView?.ResetPose(state.Enemy);
        Sync();
        if(atGate) {
            CancelHolyKnifeThrow(false);holyKnives.Reset(entryKnives);
            GuardianRelicCharges=entryCharges;GuardianRelicRelights=entryRelights;
            ClearHealingEffect();firstBossWoundHintRemaining=0;
            narrator?.ResetNarration();UI?.ShowGateRetry();
            wasRecovering=false;contactFraction=contactDamage=0;contactSlow=1;
        } else {
            ResetAuthoredContent();ResetHolyKnives();ResetGuardianRelic();
        }
        ResetSprintStamina();ResetPlayerMotion();
        ResetBossAudio();
    }
    void TryBeginEncounter() {
        if(EncounterStarted || hall==null || playerBody==null || !hall.ShouldStartEncounter(state.Player)) return;
        // A jump through the visible gate must commit the fight before the player
        // can land beside the doorway. Legacy halls retain their grounded entry.
        if(hall.FogGate==null && (!playerBody.isGrounded || state.JumpHeight>.001f))return;
        entryKnives=HolyKnifeCount;entryCharges=GuardianRelicCharges;entryRelights=GuardianRelicRelights;
        EncounterStarted=true;
        IsolateFinalRoom();
        RequestNarration("16_final_fog_gate");
        boss.MoveBounds=hall.PlayArea;
        state.WalkBounds=hall.FogGate!=null ? hall.ExplorationArea : hall.PlayArea;
        hall.FogGate?.SetEncounterState(true,false,false);
        lightDecision=2; ritual=null;
        for(int i=0;i<lamps.Length;i++) {
            lamps[i].Remaining=i%3==0 ? LampLife : 0;
            lamps[i].Impact=lamps[i].Protected=0;
        }
        bossEffects.ResetVfx();
        ApplyLighting();
    }
    static Vector3 PlaceBody(CharacterController body,Vector3 position) {
        body.enabled=false;
        body.transform.position=position+Vector3.up*.08f;
        body.enabled=true;
        body.Move(Vector3.down*.15f);
        return body.transform.position;
    }
    static void MoveAcrossHall(CharacterController body,Vector3 destination,float vertical) {
        Vector3 delta=destination-body.transform.position;
        delta.y=vertical;
        body.Move(delta);
    }
    Vector3 MoveDemonBody(Vector3 destination,float dt) {
        MoveAcrossHall(enemyBody,destination,-4*dt);
        return enemyBody.transform.position;
    }
    void Sync(float modelDt = 0) {
        witch.position=state.Enemy;
        Vector3 facing=Vector3.ProjectOnPlane(state.Player-state.Enemy,Vector3.up);
        witch.rotation=!EncounterStarted && hall!=null && hall.DemonSpawn!=null ? hall.DemonSpawn.rotation :
            Quaternion.LookRotation(facing.sqrMagnitude>.001f ? facing : Vector3.forward);
        witch.localScale=new Vector3(1,boss.Pursuing || boss.Action==HuntBoss.Move.LightStagger || boss.Action==HuntBoss.Move.LungeTell ? .76f : 1,1);
        skin.color=boss.Action==HuntBoss.Move.LightStagger ? new Color(1,1,.65f) : boss.Enraged ? new Color(1,.08f,.04f) : boss.Action==HuntBoss.Move.LungeTell ? new Color(1,.3f,.08f) : combat.Stun>0 ? Color.cyan : new Color(.82f,.62f,.57f);
        var caster=state;
        foreach(var shadow in shadows) if(shadow.Active) { caster=shadow; break; }
        float pose=caster.Active ? Mathf.Clamp01(caster.Age/.32f)*Mathf.Clamp01((ClawState.EndTime-caster.Age)/.4f):0;
        hand.localRotation=Quaternion.Euler(0,0,pose*80);
        modelView?.SyncPose(witch,boss,caster,combat.Stun,EncounterStarted ? modelDt : 0);
        if(EncounterStarted && !finished) {
            bossEffects.Tick(boss,state,RitualTarget,modelDt);
            if(LitLamps>0 && state.Witch.Body!=null)state.Witch.Body.Update(state);
            shadowBurn?.Tick(state.Witch.Body,modelDt,LitLamps>0);
        }
        eye.transform.SetPositionAndRotation(state.PlayerBody+Vector3.up*1.65f,Quaternion.Euler(pitch,yaw,CameraImpactRoll));
        weaponView.SyncPose(crossbow,modelDt);
    }
    void Tick(float dt) {
        if(GameplayPaused || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return;
        impactShake=Mathf.Max(0,impactShake-dt);
        hurtFlash=Mathf.Max(0,hurtFlash-dt);
        hurtSoundCooldown=Mathf.Max(0,hurtSoundCooldown-dt);
        if(retryDelay>0) {
            retryDelay=Mathf.Max(0,retryDelay-dt);
            if(retryDelay<=0)ResetHunt(true);
            return;
        }
        eye.GetComponent<ShiyinAudio>()?.StepCombatEffects(dt);
        TickHolyKnives(dt);
        TickGuardianRelic(dt);
        boss.StepRecovery(dt);
        crossbow.Step(dt);
        if(!EncounterStarted) {
            boss.StepExternalPlayerStatus(dt);
            float previousJump=state.JumpHeight;
            state.StepMovement(dt);
            if(playerBody!=null) {
                float lift=state.JumpHeight-previousJump;
                MoveAcrossHall(playerBody,state.Player,state.JumpHeight>0 ? lift : lift-4*dt);
                state.Player=playerBody.transform.position-Vector3.up*state.JumpHeight;
            }
            TickAuthoredContent(dt);
            if(finished) { Sync();return; }
            TryBeginEncounter();
            DrawLamps();TickBossAudio(dt);CheckEnd();
            TickStamina(dt);
            TickPlayerMotion(dt);
            Sync(modelDt:dt);
            return;
        }
        AgeLamps(dt);
        int previousHits=state.Hits;
        float jumpBefore=state.JumpHeight;
        state.HunterYaw=yaw;
        state.Step(dt);
        if(playerBody!=null) {
            float lift=state.JumpHeight-jumpBefore;
            MoveAcrossHall(playerBody,state.Player,state.JumpHeight>0 ? lift : lift-4*dt);
            state.Player=playerBody.transform.position-Vector3.up*state.JumpHeight;
        }
        TickAuthoredContent(dt);
        if(finished) { Sync();return; }
        if(state.Hits>previousHits) boss.Hurt(16);
        combat.Step(dt,shadows);
        boss.SetDemonDamage(combat.Damage);
        ConsiderLights(dt);
        boss.StepWithLights(dt,state,shadows,LitLamps>0 ? state : null,combat.Stun,moveDemon);
        if(ritual!=null && boss.Action!=HuntBoss.Move.Ritual) {
            if(boss.RitualComplete) {
                ritual.Remaining=0;
                ritual.Impact=.32f;
                ApplyLighting();
            }
            ritual=null; lightDecision=LitLamps<=2 ? 2.0f : LitLamps<=4 ? 3.4f : 4.2f;
        }
        if(LitLamps>0) state.Draw();
        DrawLamps();TickBossAudio(dt);CheckEnd();
        TickStamina(dt);
        TickPlayerMotion(dt);
        Sync(modelDt: dt); SyncMarks();
    }
    public void Walk(Vector3 direction,float dt,bool sprint=false) {
        sprinting=false;
        if(GameplayPaused || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return;
        SyncStaminaCombat(0);
        walkedSinceStaminaTick=true;
        direction.y=0; direction=Vector3.ClampMagnitude(direction,1);
        float sprintTime=sprint && direction.sqrMagnitude>.000001f && state.Pin<=0
            ? sprintStamina.AvailableSprintTime(dt) : 0;
        float slow=Mathf.Min(contactSlow,boss.Slow>0 ? SlowSpeedMultiplier : 1);
        bool Move(float duration,float speed) {
            if(duration<=0)return false;
            Vector3 previous=state.Player;
            state.Walk(direction*speed*slow*duration);
            if(playerBody!=null) {
                MoveAcrossHall(playerBody,state.Player,state.JumpHeight>0 ? 0 : -4*duration);
                state.Player=playerBody.transform.position-Vector3.up*state.JumpHeight;
            }
            RecordPlayerMotion(previous,state.Player);
            return Vector3.ProjectOnPlane(state.Player-previous,Vector3.up).sqrMagnitude>.000001f;
        }
        if(sprintTime>0 && Move(sprintTime,Mathf.Max(WalkSpeed,SprintSpeed))) {
            sprintStamina.SpendSprint(sprintTime);
            sprinting=true;
        }
        // If stamina runs out within this step, only the affordable portion
        // receives sprint speed; the rest still moves at normal walk speed.
        if(dt>sprintTime) { Move(dt-sprintTime,WalkSpeed); sprinting=false; }
    }
    public void Jump() { if(!Developer.Paused)state.Jump(); }
    void Update() {
        AdvanceVictory(Time.unscaledDeltaTime, true);
        if(VictoryPresentationActive)return;
        if(UI!=null) { if(UI.PollInput())return; }
        else {
        if(Developer.Poll()) return;
        if(Input.GetKeyDown(KeyCode.F5) || (finished && Input.GetKeyDown(KeyCode.R))) { ResetLab(); return; }
        if(Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState=CursorLockMode.None; Cursor.visible=true; return; }
        }
        if(finished || Developer.Paused) return;
        if(retryDelay>0){Tick(Time.deltaTime);return;}
        if(UI!=null && UI.AutomatedCheck) { Tick(Time.deltaTime);return; }
        if(Cursor.lockState!=CursorLockMode.Locked) {
            if(Input.GetMouseButtonDown(0)) { Cursor.lockState=CursorLockMode.Locked; Cursor.visible=false; }
            return;
        }
        float dt=Time.deltaTime;
        if(Input.GetKeyDown(KeyCode.Space)) Jump();
        float sensitivity=UI!=null ? UI.Settings.Sensitivity : 2f;
        bool invert=UI!=null && UI.Settings.InvertY;
        yaw+=Input.GetAxisRaw("Mouse X")*sensitivity;
        pitch=Mathf.Clamp(pitch+Input.GetAxisRaw("Mouse Y")*sensitivity*(invert ? 1 : -1),-85,85);
        float x=(Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0);
        float z=(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0);
        Walk(Quaternion.Euler(0,yaw,0)*new Vector3(x,0,z),dt,
            Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift));
        if(Input.GetKeyDown(KeyCode.R)) crossbow.Request(Bolt.Holy);
        if(Input.GetKeyDown(KeyCode.E)) {Sync();ThrowHolyKnife();}
        if(Input.GetKeyDown(KeyCode.Q))UseGuardianRelic();
        if(Input.GetMouseButtonDown(0)) { Sync(); Shoot(); }
        if(!finished) Tick(dt);
    }
    void CheckEnd() {
        TickBossAudio(0);
        if(boss.HunterHealth==0 && EncounterStarted && hall!=null && hall.FogGate!=null && hall.UseAuthoredLayout) {
            if(retryDelay<=0)retryDelay=.35f;
            return;
        }
        bool wasFinished=finished;
        finished=combat.Damage>=HuntBoss.DemonHealth || boss.HunterHealth==0;
        if(finished && !wasFinished) {
            CancelHolyKnifeThrow(false);
            ClearHealingEffect();
            ResetSprintStamina();
            hall?.FogGate?.SetEncounterState(EncounterStarted,true,combat.Damage>=HuntBoss.DemonHealth);
            bossEffects.ResetVfx();
            foreach(var actor in authoredActors)if(actor!=null)actor.OnEncounterEnded();
            BossAudioEnding(combat.Damage>=HuntBoss.DemonHealth && boss.HunterHealth>0);
            if(combat.Damage>=HuntBoss.DemonHealth && boss.HunterHealth>0)BeginVictoryPresentation();
        }
        if(finished) { Cursor.lockState=CursorLockMode.None; Cursor.visible=true; }
    }
    public string LastShot => shot;
    public bool Finished => finished;
    public int HunterHealth => boss.HunterHealth;
    public float Damage => combat.Damage;
    public void Shoot() {
        if(Developer.Paused || finished || !crossbow.Fire()) return;
        var audio=eye.GetComponent<ShiyinAudio>();
        audio?.PlayCrossbowShot();
        Bolt kind=crossbow.Loaded;
        Physics.SyncTransforms();
        Ray ray=eye.ViewportPointToRay(new Vector3(.5f,.5f,0));
        bool struck=FirstShotSurface(ray,out RaycastHit hit);
        weaponView.Release(struck ? hit.point : ray.GetPoint(40),kind);
        float impactDelay=weaponView.ShotImpactDelay;
        weaponView.SyncPose(crossbow,0);
        if(!struck) { shot="MISS"; ShotResolved?.Invoke(shot);return; }
        if(ShootAuthoredLamp(hit.collider)) { audio?.PlayLampIgnition(hit.point,impactDelay);ShotResolved?.Invoke(shot);return; }
        foreach(var lamp in lamps) if(hit.collider==lamp.Target) {
            ShootLamp(lamp);
            audio?.PlayLampIgnition(hit.point,impactDelay);
            ShotResolved?.Invoke(shot);
            return;
        }
        if(ShootAuthoredShadow(ray,hit.distance+.10f)) { audio?.PlayArrowImpact(hit.point,ArrowImpactKind.Shadow,impactDelay);ShotResolved?.Invoke(shot);return; }
        bool onFloor=hall!=null ? hall.IsGround(hit.collider) && hit.normal.y>.5f : hit.transform==floor;
        bool wall=hall!=null ? (hall.UseAuthoredLayout ? !hit.collider.isTrigger && !hit.transform.IsChildOf(transform) :
            floor!=null && hit.transform.parent==floor.parent) : hit.transform==receiver && hit.normal.z<-.9f;
        if(!onFloor && !wall) { audio?.PlayArrowImpact(hit.point,ArrowImpactKind.Blocked,impactDelay);shot="BODY BLOCKED";ShotResolved?.Invoke(shot); return; }
        if(kind==Bolt.Light) {
            EncounterLantern localNearest=null;float localDistance=float.PositiveInfinity;
            foreach(var local in authoredLanterns)if(local!=null && local.isActiveAndEnabled) {
                float d=(hit.point-local.Position).sqrMagnitude;
                if(d<localDistance) { localDistance=d;localNearest=local; }
            }
            var nearest=lamps[0];
            foreach(var lamp in lamps)
                if((hit.point-lamp.Fixture.position).sqrMagnitude < (hit.point-nearest.Fixture.position).sqrMagnitude) nearest=lamp;
            if(localNearest!=null && localDistance<(hit.point-nearest.Fixture.position).sqrMagnitude) {
                localNearest.Ignite();NotifyLampShot(GetLampIndex(localNearest));shot="灯火已复明";
            } else ShootLamp(nearest);
            audio?.PlayLampIgnition(localNearest!=null && localDistance<(hit.point-nearest.Fixture.position).sqrMagnitude ? localNearest.Position : nearest.Fixture.position,impactDelay);
            ShotResolved?.Invoke(shot);
            return;
        }
        if(!EncounterStarted) { audio?.PlayArrowImpact(hit.point,ArrowImpactKind.Solid,impactDelay);shot="射击练习";ShotResolved?.Invoke(shot);return; }
        if(hall!=null && !onFloor) { audio?.PlayArrowImpact(hit.point,ArrowImpactKind.Solid,impactDelay);shot="MISS";ShotResolved?.Invoke(shot); return; }
        Vector2 point=onFloor ? new Vector2(hit.point.x,hit.point.z) : new Vector2(hit.point.x,hit.point.y);
        float enemy=float.PositiveInfinity, hunter=float.PositiveInfinity;

        foreach(var shadow in shadows) {
            if(shadow.Ground!=onFloor) continue;
            float distance=Mathf.Min(shadow.Witch.HitDistance(ray),shadow.Arm.HitDistance(ray));
            enemy=Mathf.Min(enemy,distance);
            hunter=Mathf.Min(hunter,shadow.Hunter.HitDistance(ray));
        }
        bool struckShadow=enemy<hunter;
        audio?.PlayArrowImpact(hit.point,struckShadow ? ArrowImpactKind.Shadow : ArrowImpactKind.Solid,impactDelay);
        shot=combat.Shoot(kind,point,struckShadow,onFloor);
        if(struckShadow && kind==Bolt.Fire && shadowBurn!=null)
            shadowBurn.Ignite(state.Witch.Body,point);
        combat.Step(0,shadows); SyncMarks(); CheckEnd();
        ShotResolved?.Invoke(shot);
    }
    void SyncMarks() {
        foreach(var key in new System.Collections.Generic.List<WallCombat.Mark>(marks.Keys)) {
            if(combat.Marks.Contains(key)) continue;
            Destroy(marks[key]); marks.Remove(key);
        }
        foreach(var mark in combat.Marks) {
            if(marks.ContainsKey(mark)) continue;
            var obj=new GameObject(mark.Kind.ToString());
            obj.transform.SetParent(transform,false);
            if(mark.Ground && mark.Kind==Bolt.Fire && shadowBurn!=null) {
                shadowBurn.PlaceCinders(obj,mark.Point,hall.SurfaceHeight);
                marks.Add(mark,obj);continue;
            }
            var ring=obj.AddComponent<LineRenderer>();
            ring.sharedMaterial=mark.Kind==Bolt.Fire ? fireColor : stormColor;
            ring.useWorldSpace=true; ring.loop=true; ring.positionCount=32;
            ring.startWidth=ring.endWidth=.022f;
            float radius=mark.Kind==Bolt.Fire ? WallCombat.FireRadius : .06f;
            for(int i=0;i<32;i++) {
                float angle=i*2*Mathf.PI/32;
                float x=mark.Point.x+radius*Mathf.Cos(angle), y=mark.Point.y+radius*Mathf.Sin(angle);
                ring.SetPosition(i,mark.Ground ? new Vector3(x,.028f+(hall!=null ? hall.SurfaceHeight(x,y) : 0),y) : new Vector3(x,y,3.12f));
            }
            marks.Add(mark,obj);
        }
    }
    void OnGUI() {
        if(UI!=null)return;
        if(state==null || Developer.HudHidden) return;
        float scale=Mathf.Clamp(Screen.width/1100f,.72f,1.45f);
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        float w=Screen.width/scale,h=Screen.height/scale;
        if(hudFont!=null) GUI.skin.font=hudFont;
        if(hurtFlash>0) {
            GUI.color=new Color(.85f,.12f,.18f,hurtFlash*1.35f);
            GUI.DrawTexture(new Rect(0,0,w,6),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0,h-6,w,6),Texture2D.whiteTexture);
            GUI.color=Color.white;
        }
        float bar=Mathf.Min(430,w*.48f), left=(w-bar)*.5f;
        GUI.Box(new Rect(left-12,10,bar+24,59),"");
        GUI.Label(new Rect(left,13,bar,24),"影魔  /  古堡之主");
        DrawBar(new Rect(left,40,bar,9),1-Damage/HuntBoss.DemonHealth,new Color(.82f,.31f,.39f));
        GUI.Box(new Rect(12,h-84,238,68),"");
        GUI.Label(new Rect(22,h-80,224,25),"猎人   "+boss.HunterHealth+" / 100   "+(Developer.HealthLocked ? "锁血  " : "")+(state.Pin>0 ? "定身" : boss.Slow>0 ? "减速" : ""));
        DrawBar(new Rect(22,h-49,208,7),boss.HunterHealth/100f,new Color(.36f,.76f,.67f));
        
        string action;
        switch(crossbow.Phase) {
            case HunterCrossbow.Motion.Cycle: action="上弦"; break;
            case HunterCrossbow.Motion.Lower: action="压弩"; break;
            case HunterCrossbow.Motion.Load: action="装入"+bolts[(int)crossbow.Target]; break;
            case HunterCrossbow.Motion.Raise: action="举弩"; break;
            default: action=crossbow.Rounds==0 ? "空匣 · R 换弹" : "就绪"; break;
        }
        GUI.Label(new Rect(w-310,h-54,300,32),bolts[(int)crossbow.Loaded]+"    "+crossbow.Rounds+" / "+HunterCrossbow.Capacity+"    "+action);
        var centered=new GUIStyle(GUI.skin.label) { alignment=TextAnchor.MiddleCenter,fontSize=18 };
        GUI.Label(new Rect(left-70,72,bar+140,36),combat.Stun>0 ? "麻痹 · 射击投影" : ritual!=null ? "影魔正在熄灯 · 射击灯火可打断" : boss.Tell,centered);
        GUI.Label(new Rect(left-70,h-55,bar+140,30),shot,centered);
        if(finished) GUI.Box(new Rect(w/2-160,h/2-24,320,48),boss.HunterHealth==0 ? "猎人倒下了    ·    R 再战" : "影魔已被驱逐    ·    R 再战");
        else if(Cursor.lockState!=CursorLockMode.Locked) {
            GUI.Box(new Rect(w/2-245,h/2-75,490,150),Application.productName);
            GUI.Label(new Rect(w/2-238,h/2-61,476,130),"六灯齐明最安全 · 射击地面敌影以驱逐影魔\nWASD 移动   空格跳跃   左键圣水弩   R 装填   E 圣火飞刀\n"+Developer.Help+"\n全暗时影魔无敌 · 射灯复明    ·    单击进入猎场",centered);
        }
        else {
            GUI.DrawTexture(new Rect(w/2-5,h/2-.75f,10,1.5f),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(w/2-.75f,h/2-5,1.5f,10),Texture2D.whiteTexture);
        }
    }
    static void DrawBar(Rect rect,float amount,Color color) {
        GUI.color=new Color(.18f,.16f,.20f); GUI.DrawTexture(rect,Texture2D.whiteTexture);
        rect.width*=Mathf.Clamp01(amount); GUI.color=color; GUI.DrawTexture(rect,Texture2D.whiteTexture);
        GUI.color=Color.white;
    }
    void OnDisable() {
        Developer?.ClearPauseReasons();
        Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
    }
    public void RestartEncounter() { ResetLab(); }
    public void RefreshView() { Sync(); }
    public void SetMenuPresentation(bool menu) {
        if(weaponView!=null)weaponView.gameObject.SetActive(!menu);
        if(menu && hall!=null) {
            if(hall.UseAuthoredLayout && hall.MenuView!=null)
                eye.transform.SetPositionAndRotation(hall.MenuView.position,hall.MenuView.rotation);
            else if(hall.UseAuthoredLayout) Sync();
            else {
                Vector3 from=new Vector3(-1.35f,2.34f,8.48f);
                eye.transform.SetPositionAndRotation(from,Quaternion.LookRotation(new Vector3(.3f,1.8f,-8.2f)-from));
            }
        } else if(state!=null)Sync();
    }
}
 