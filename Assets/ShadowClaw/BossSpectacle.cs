using UnityEngine;
using UnityEngine.Rendering;

// All emitters follow the encounter clock. Pausing, resetting or interrupting a tell
// cannot advance a separate VFX timer into an attack that the encounter cancelled.
public sealed class BossSpectacle : MonoBehaviour {
    static readonly Color Silk=new Color(.84f,.33f,.92f,.81f);
    static readonly Color Gold=new Color(1,.71f,.34f,.98f);
    static readonly Color Blood=new Color(1,.22f,.31f,.92f);
    static readonly Color Rose=new Color(1,.43f,.74f,.95f);
    readonly System.Random random=new System.Random(743);
    ParticleSystem wisps, motes, tells, strikes, dust, clawPrint, rupture;
    ParticleSystem[] pools;
    Material edgeMat;
    Transform head,leftHand,rightHand,actor;
    IronHallEncounter hall;
    LineRenderer danger, scar, wavefront;
    readonly LineRenderer[] bellFronts=new LineRenderer[3];
    BossImportedEffects imported;
    BossApparition apparition;
    float bodyCarry,tellCarry,scarCarry,clock;
    bool seeded,clawClosed;
    HuntBoss.Move lastAction;
    Vector3 lastFoot;

    public int BodyParticleCount => wisps.particleCount+motes.particleCount;
    public int AttackParticleCount => tells.particleCount+strikes.particleCount+dust.particleCount+clawPrint.particleCount+rupture.particleCount;
    public int ImpactCount {get;private set;}
    public LineRenderer DangerOutline => danger;
    public GameObject ApparitionRoot => apparition?.Root;
    public bool ApparitionVisible => apparition!=null && apparition.Visible;
    public float ApparitionHeight => apparition?.Height ?? 0;
    public float BellWaveReferenceRadius(int index) => imported?.WaveReferenceRadius(index) ?? 0;
    public float BellWaveVisualRadius(int index) => imported?.WaveVisualRadius(index) ?? -1;

    struct Footprint {
        public Vector3 A,B;
        public float Radius;
        public Footprint(Vector3 a,Vector3 b,float r){ A=a; B=b; Radius=r; }
        // Circle or capsule, including the rounded ends tested by the combat sweep.
        public Vector3 Edge(float t) {
            Vector3 axis=B-A;axis.y=0;
            float len=axis.magnitude;
            if(len<.001f) {
                float a=t*Mathf.PI*2;
                return A+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*Radius;
            }
            axis/=len;
            Vector3 side=Vector3.Cross(Vector3.up,axis);
            float arc=Mathf.PI*Radius, s=t*(len*2+arc*2);
            if(s<len) return A+axis*s+side*Radius;
            s-=len;
            if(s<arc) {float a=s/Radius; return B+(side*Mathf.Cos(a)+axis*Mathf.Sin(a))*Radius;}
            s-=arc;
            if(s<len) return B-axis*s-side*Radius;
            float angle=(s-len)/Radius;
            return A-(side*Mathf.Cos(angle)+axis*Mathf.Sin(angle))*Radius;
        }
    }

    public void Initialize(Transform caster,ShadowDemonView model,IronHallEncounter room) {
        hall=room;
        actor=model ? model.transform : caster;
        if(model) {
            var bones=model.GetComponentInChildren<SkinnedMeshRenderer>().bones;
            head=System.Array.Find(bones,b=>b.name=="Head");
            leftHand=System.Array.Find(bones,b=>b.name=="Hand.L");
            rightHand=System.Array.Find(bones,b=>b.name=="Hand.R");
        } else {
            head=caster.Find("Head");
            leftHand=rightHand=caster.Find("Torso");
        }
        edgeMat=new Material(Resources.Load<Shader>("IncantationEdge"));
        wisps=Pool("Bell-sleeve veil","flame_03",130);
        motes=Pool("Gilded ash","trace_02",160);
        tells=Pool("Gathering shadow","spark_06",350);
        strikes=Pool("Discharged shadow","spark_06",360);
        dust=Pool("Spell drift","smoke_05",80);
        clawPrint=Pool("Closing talons","scratch_01",8);
        rupture=Pool("Spell impact","scorch_03",16);
        clawPrint.GetComponent<ParticleSystemRenderer>().renderMode=ParticleSystemRenderMode.HorizontalBillboard;
        rupture.GetComponent<ParticleSystemRenderer>().renderMode=ParticleSystemRenderMode.HorizontalBillboard;
        pools=new[]{wisps,motes,tells,strikes,dust,clawPrint,rupture};
        danger=Outline("Spell perimeter");
        scar=Outline("Lingering claw seal");
        wavefront=Outline("Travelling spell front");
        for(int i=0;i<bellFronts.Length;i++)bellFronts[i]=Outline("Bell wave collision boundary "+(i+1));
        imported=new BossImportedEffects(transform,caster.GetComponentInParent<BossEffectLibrary>());
        apparition=new BossApparition(transform,caster.GetComponentInParent<BossEffectLibrary>());
    }
    ParticleSystem Pool(string label,string textureName,int capacity) {
        var go=new GameObject(label);go.transform.SetParent(transform,false);
        var ps=go.AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.playOnAwake=false;main.loop=false;main.startSize3D=true;
        main.maxParticles=capacity;main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
        var emission=ps.emission;emission.enabled=false;
        var shape=ps.shape;shape.enabled=false;
        var fade=ps.colorOverLifetime;fade.enabled=true;
        var g=new Gradient();
        g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(.5f,0),new GradientAlphaKey(1,.16f),new GradientAlphaKey(.68f,.55f),new GradientAlphaKey(0,1)});
        fade.color=g;
        var size=ps.sizeOverLifetime;size.enabled=true;
        size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,1,1,.40f));
        var renderer=go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial=new Material(Resources.Load<Shader>("SpiritParticles")) {
            mainTexture=Resources.Load<Texture2D>("KenneyParticles/"+textureName)
        };
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        return ps;
    }
    LineRenderer Outline(string label) {
        var go=new GameObject(label);go.transform.SetParent(transform,false);
        var line=go.AddComponent<LineRenderer>();
        line.sharedMaterial=edgeMat;line.loop=true;line.useWorldSpace=true;
        line.textureMode=LineTextureMode.Tile;line.positionCount=96;
        line.startWidth=line.endWidth=.09f;
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;
        return line;
    }
    Vector3 Ground(Vector3 p) {
        p.y=(hall!=null ? hall.SurfaceHeight(p.x,p.z) : 0)+.051f;
        return p;
    }
    float R(float lo,float hi)=>Mathf.Lerp(lo,hi,(float)random.NextDouble());
    void Puff(ParticleSystem ps,Vector3 p,Vector3 v,Color color,float width,float height,float life,float roll=0) {
        ps.Emit(new ParticleSystem.EmitParams{position=p,velocity=v,startColor=color,
            startSize3D=new Vector3(width,height,width),startLifetime=life,rotation=roll},1);
    }
    void Trace(LineRenderer line,Footprint shape,Color color,float width) {
        line.enabled=true;
        line.startColor=line.endColor=color;
        line.startWidth=line.endWidth=width;
        for(int i=0;i<line.positionCount;i++) line.SetPosition(i,Ground(shape.Edge((float)i/line.positionCount)));
    }
    void EdgeEmbers(Footprint shape,ref float carry,float dt,float rate,Color tint) {
        carry+=dt*rate;
        while(carry>=1) {
            carry--;
            Vector3 p=Ground(shape.Edge(R(0,1)));
            Puff(tells,p,Vector3.up*R(.14f,.53f),tint,R(.028f,.058f),R(.06f,.16f),R(.18f,.43f),R(-24,24));
        }
    }
    void Body(HuntBoss boss,bool charging) {
        bool horn=random.Next(3)==0;
        Transform anchor=horn?head:(random.Next(2)==0 ? rightHand : leftHand);
        float side=anchor==leftHand ? -1 : 1;
        Vector3 pos=anchor.position+
            actor.right*(horn ? R(-.24f,.24f) : side*R(.0f,.09f))+
            actor.up*(horn ? R(.10f,.29f) : R(-.04f,.15f))-
            actor.forward*R(.025f,.11f);
        Vector3 drift=actor.right*R(-.19f,.19f)+Vector3.up*R(.23f,.59f);
        if(horn) {
            // Shed luminous strands from each horn, just outside the character silhouette.
            float handSide=random.Next(2)==0 ? -1 : 1;
            float height=R(0,1);
            float arc=(clock*.9f+height*1.7f)*handSide;
            pos=actor.position+Vector3.up*(1.79f+height*.32f)+
                actor.right*handSide*(.21f+.17f*height)+actor.forward*(.13f*Mathf.Sin(arc));
            drift=actor.right*handSide*.14f+Vector3.up*R(.22f,.49f)+actor.forward*Mathf.Cos(arc)*.12f;
        }
        var silk=boss.Enraged ? Blood : Silk;
        Puff(wisps,pos,drift,silk,R(.17f,.28f),R(.30f,.52f)*(charging?1.35f:1),R(.48f,.87f),R(-28,28));
        Puff(motes,pos+drift*.18f,drift*1.28f,Gold,R(.06f,.10f),R(.08f,.15f),R(.48f,.91f),R(-30,30));
    }
    public void ResetVfx() {
        foreach(var p in pools) p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        seeded=false;clawClosed=false;clock=bodyCarry=tellCarry=scarCarry=0;
        ImpactCount=0;danger.enabled=scar.enabled=wavefront.enabled=false;
        foreach(var line in bellFronts)if(line)line.enabled=false;
        imported?.Reset();
        apparition?.Hide();
        lastAction=HuntBoss.Move.Stalk;
    }

    public void Tick(HuntBoss boss,ClawState world,Transform ritualTarget,float dt) {
        if(!seeded) {
            lastFoot=world.Enemy;seeded=true;
            for(int i=0;i<27;i++)Body(boss,false);
            foreach(var system in pools) system.Simulate(.075f,false,false,false);
        }
        if(boss.GrandAttacks) {
            imported?.Tick(boss,world,ritualTarget,dt);
            apparition?.Tick(boss,world,dt);
        } else {
            // A ritual in the restored fight uses the original caster only.
            // Do not instantiate a second character or mix the rejected large
            // attack treatment into the original body-linked particles.
            imported?.Reset();
            apparition?.Hide();
        }
        if(dt<=0) return;
        clock+=dt;
        foreach(var system in pools) system.Simulate(dt,false,false,false);
        var action=boss.Action;
        bool charging=action==HuntBoss.Move.Ritual || action==HuntBoss.Move.Claw ||
                      action==HuntBoss.Move.Burst || action==HuntBoss.Move.Rift || action==HuntBoss.Move.LungeTell;
        bodyCarry+=dt*(charging?42:21);
        while(bodyCarry>=1) {bodyCarry--;Body(boss,charging);}
        if(action!=lastAction) {
            tells.Clear(false);tellCarry=0;
        }
        danger.enabled=wavefront.enabled=false;
        foreach(var line in bellFronts)line.enabled=false;
        if(action==HuntBoss.Move.SleeveTell || action==HuntBoss.Move.SleeveSweep) {
            // This border is the actual rectangular damage footprint. The imported
            // spectacle may extend above it, but cannot change the hit region.
            Rect area=boss.SweepBounds;
            Vector3[] corners={new Vector3(area.xMin,0,area.yMin),new Vector3(area.xMax,0,area.yMin),
                new Vector3(area.xMax,0,area.yMax),new Vector3(area.xMin,0,area.yMax)};
            danger.enabled=true;danger.startColor=danger.endColor=action==HuntBoss.Move.SleeveTell ? Rose : Gold;
            danger.startWidth=danger.endWidth=.12f;
            for(int i=0;i<danger.positionCount;i++) {
                float segment=(float)i/danger.positionCount*4;
                int edge=Mathf.FloorToInt(segment);
                danger.SetPosition(i,Ground(Vector3.Lerp(corners[edge],corners[(edge+1)%4],segment-edge)));
            }
        }
        if(action==HuntBoss.Move.BellTell)
            Trace(danger,new Footprint(boss.BellCenter,boss.BellCenter,1.1f),Gold,.11f);
        if(action==HuntBoss.Move.BellWaves) {
            for(int i=0;i<bellFronts.Length;i++) {
                float radius=boss.GetBellRadius(i);
                if(radius>=0)Trace(bellFronts[i],new Footprint(boss.BellCenter,boss.BellCenter,radius),Gold,.08f);
            }
        }
        if(action==HuntBoss.Move.Rift || action==HuntBoss.Move.Burst || action==HuntBoss.Move.LungeTell ||
           action==HuntBoss.Move.Lunge || boss.Pursuing) {
            var a=action==HuntBoss.Move.Burst?boss.Destination:world.Enemy;
            float width=action==HuntBoss.Move.Burst?HuntBoss.BurstRadius:boss.Pursuing?HuntBoss.PounceWidth:
                action==HuntBoss.Move.Rift?HuntBoss.RiftWidth:HuntBoss.LungeWidth;
            var region=new Footprint(a,boss.Destination,width);
            Color hue=boss.Pursuing ? (boss.DarkHuntLocked ? Gold : Blood) : Rose;
            Trace(danger,region,hue,.086f+.026f*(1+Mathf.Sin(clock*16)));
            EdgeEmbers(region,ref tellCarry,dt,action==HuntBoss.Move.Rift?140:88,hue);
            if(action==HuntBoss.Move.Burst) {
                // The leading ring reaches the marked perimeter when the damage checks run.
                float r=Mathf.Lerp(.10f,width,boss.MoveProgress);
                Trace(wavefront,new Footprint(a,a,r),Gold,.064f);
            } else if(action==HuntBoss.Move.Rift) {
                Vector3 front=Vector3.Lerp(a,boss.Destination,boss.MoveProgress);
                Vector3 side=Vector3.Cross(Vector3.up,(boss.Destination-a).normalized)*width;
                Trace(wavefront,new Footprint(front-side*.86f,front+side*.86f,.035f),Gold,.052f);
            }
        }
        if(action==HuntBoss.Move.Claw && !world.Active) {
            tells.Clear(false);tellCarry=0;
        }
        if(action==HuntBoss.Move.Claw && world.Active) {
            Vector3 lockPoint=world.BodyLockPoint;
            if(!world.Resolved) {
                var ring=new Footprint(lockPoint,lockPoint,ClawState.ClawRadius);
                Trace(danger,ring,Rose,.055f);
                float r=Mathf.Lerp(ClawState.ClawRadius,.07f,Mathf.Clamp01(world.Age/ClawState.CloseTime));
                Trace(wavefront,new Footprint(lockPoint,lockPoint,r),Gold,.038f);
                EdgeEmbers(ring,ref tellCarry,dt,50,Gold);
            } else if(!clawClosed) Release(action,world.Enemy,lockPoint);
        }
        if(action==HuntBoss.Move.Ritual && ritualTarget) {
            tellCarry+=dt*70;
            while(tellCarry>=1) {
                tellCarry--;
                Vector3 start=rightHand.position, end=ritualTarget.position;
                float t=R(0,1),wave=Mathf.Sin(t*Mathf.PI);
                Vector3 cross=Vector3.Cross(Vector3.up,(end-start).normalized);
                Vector3 p=Vector3.Lerp(start,end,t)+wave*(Vector3.up*.35f+cross*Mathf.Sin(11*t-clock*7)*.13f);
                Puff(tells,p,(end-start).normalized*.85f,Rose,.05f,.095f,.28f);
            }
        }
        if(action==HuntBoss.Move.Lunge || action==HuntBoss.Move.Pounce) {
            for(int i=0;i<3;i++) {
                Vector3 p=Ground(Vector3.Lerp(lastFoot,world.Enemy,R(0,1)))+Vector3.up*R(.06f,.9f);
                Puff(strikes,p,-actor.forward*.55f+Vector3.up*.13f,boss.Enraged?Blood:Silk,.18f,.35f,.32f,R(-65,65));
            }
        }
        scar.enabled=action==HuntBoss.Move.MarkFloor || boss.ScarLife>0;
        if(scar.enabled) {
            var point=new Vector3(boss.Scar.x,0,boss.Scar.y);
            var ring=new Footprint(point,point,HuntBoss.ScarRadius);
            Trace(scar,ring,Gold,.057f);
            EdgeEmbers(ring,ref scarCarry,dt,40,Gold);
        }
        clawClosed=world.Active && world.Resolved;
        lastFoot=world.Enemy;lastAction=action;
    }



    // Called by the damage/area release sites. Interrupts do not pass through them.
    public void Release(HuntBoss.Move attack,Vector3 from,Vector3 to) {
        ImpactCount++;
        Vector3 impact=Ground(attack==HuntBoss.Move.Burst ? from : to);
        if(attack==HuntBoss.Move.Claw) {
            float heading=Vector3.SignedAngle(Vector3.forward,to-from,Vector3.up);
            Puff(clawPrint,impact,Vector3.zero,Gold,ClawState.ClawRadius*1.32f,ClawState.ClawRadius*1.32f,.39f,heading-45);
        } else {
            float diameter=attack==HuntBoss.Move.Burst ? HuntBoss.BurstRadius*1.6f : .86f;
            Puff(rupture,impact,Vector3.zero,Rose,diameter,diameter,.42f,R(0,360));
        }
        for(int i=0;i<9;i++) {
            float turn=(i+R(0,.5f))*Mathf.PI*2/9;
            Vector3 outward=new Vector3(Mathf.Cos(turn),0,Mathf.Sin(turn));
            var soot=new Color(.55f,.29f,.63f,.56f);
            Puff(dust,impact+outward*.10f,outward*.72f+Vector3.up*.27f,soot,.35f,.46f,R(.42f,.7f),R(-65,65));
        }
        if(attack==HuntBoss.Move.Burst || attack==HuntBoss.Move.Rift) {
            var area=attack==HuntBoss.Move.Burst ? new Footprint(from,from,HuntBoss.BurstRadius) :
                                                  new Footprint(from,to,HuntBoss.RiftWidth);
            for(int i=0;i<92;i++) {
                Vector3 p=Ground(area.Edge((i+R(0,.68f))/92));
                Vector3 drift=attack==HuntBoss.Move.Rift ? (to-from).normalized*.90f : (p-from).normalized*.45f;
                Puff(strikes,p,drift+Vector3.up*R(.45f,1.4f),i%4==0?Gold:Rose,R(.12f,.22f),R(.24f,.56f),R(.23f,.51f),R(-19,19));
            }
        } else {
            Vector3 p=Ground(to);
            for(int i=0;i<27;i++) {
                float a=R(0,Mathf.PI*2);
                Vector3 drift=new Vector3(Mathf.Cos(a)*R(.3f,1.25f),R(.3f,1.4f),Mathf.Sin(a)*R(.3f,1.25f));
                Puff(strikes,p,drift,i%3==0?Gold:Rose,.09f,.24f,R(.25f,.5f),R(-35,35));
            }
        }
    }
    void OnDestroy() {
        imported?.Dispose();
        apparition?.Dispose();
        if(pools!=null)
            foreach(var pool in pools) if(pool) Destroy(pool.GetComponent<ParticleSystemRenderer>().sharedMaterial);
        Destroy(edgeMat);
    }
}
