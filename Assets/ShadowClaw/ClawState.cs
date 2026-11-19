using UnityEngine;

public sealed class ClawState
{
    public const float WallZ = 3.2f, CloseTime = .74f, EndTime = 1.45f, MaxReach = 11.4f;
    public const float ClawRadius=1f, GroundAttackHeight=.45f;
    public readonly bool Ground;
    public System.Func<float,float,float> GroundHeight;
    public readonly WallInk Witch, Hunter, Arm, Glyph;
    public Vector3 Player, Enemy, Lamp;
    const float JumpSpeed=5.8f, Gravity=19;
    float jumpVelocity;
    public float JumpHeight { get; private set; }
    public Vector3 PlayerBody => Player+Vector3.up*JumpHeight;
    internal void LandAt(Vector3 feet) {
        Player=feet;
        JumpHeight=jumpVelocity=stride=Pin=0;
    }
    public void Jump() {
        if(Pin<=0 && JumpHeight==0 && jumpVelocity==0) jumpVelocity=JumpSpeed;
    }
    public Vector2 LockPoint, Shoulder, Palm;
    public Vector3 BodyLockPoint { get; private set; }
    public int LightId, CastLight, Hits, Misses, Cancels;
    public float Age, Pin;
    public float SilhouetteScale=1;
    public bool Active, Resolved;
    public string Result;
    public ClawState(WallInk witch, WallInk hunter, WallInk arm, WallInk glyph, bool ground=false)
    { Ground=ground; Witch=witch; Hunter=hunter; Arm=arm; Glyph=glyph; Reset(); }
    public void Reset()
    {
        Player=new Vector3(.65f,0,.05f); Enemy=new Vector3(-1.38f,0,1.25f);
        BodyLockPoint=Player;
        JumpHeight=jumpVelocity=stride=0;
        Active=false; LightId=0; SelectLight(0); Resolved=false; Age=0; Pin=0;
        Hits=Misses=Cancels=0; Result="READY"; Draw();
    }
    public void SelectLight(int id)
    {
        SetLamp(id, Ground ? new Vector3(id==0 ? 2.8f : -2.8f,4.3f,3f) : new Vector3(id==0 ? -1.55f : 2.2f,1.15f,-3.5f));
    }
    public void SetLamp(int id, Vector3 position)
    {
        LightId=id;
        Lamp=position;
        if(Active && !Resolved && id!=CastLight)
        { Active=false; Cancels++; Result="CANCEL / LIGHT REPLACED"; }
        else if(Active && id!=CastLight) Active=false;
        Draw();
    }
    public Vector2 Project(Vector3 p)
    {
        if(Ground) {
            float elevation=GroundHeight?.Invoke(p.x,p.z) ?? 0;
            float scale=(Lamp.y-elevation)/(Lamp.y-p.y);
            return new Vector2(Lamp.x+(p.x-Lamp.x)*scale,Lamp.z+(p.z-Lamp.z)*scale);
        }
        float t=(WallZ-Lamp.z)/(p.z-Lamp.z);
        return new Vector2(Lamp.x+(p.x-Lamp.x)*t, Lamp.y+(p.y-Lamp.y)*t);
    }
    bool Projects(Vector3 body) => Ground ? Lamp.y-(GroundHeight?.Invoke(body.x,body.z) ?? 0)>2.2f : Lamp.z < body.z - .12f && body.z < WallZ;
    public float HunterYaw;
    float stride;
    public Rect WalkBounds = Rect.MinMaxRect(-6.5f,-4.5f,6.5f,2.5f);
    public void Walk(Vector3 delta)
    {
        if(Pin > 0) return;
        var candidate=Player+delta;
        var offset=candidate-Enemy;
        if(offset.sqrMagnitude<.60f*.60f) {
            if(offset.sqrMagnitude<.000001f) offset=Player-Enemy;
            candidate=Enemy+offset.normalized*.60f;
        }
        Vector3 last=Player;
        Player.x=Mathf.Clamp(candidate.x,WalkBounds.xMin,WalkBounds.xMax);
        Player.z=Mathf.Clamp(candidate.z,WalkBounds.yMin,WalkBounds.yMax);
        stride+=Vector3.Distance(Player,last)*4.8f;
    }
    public void Interrupt()
    {
        if(!Active) return;
        if(!Resolved) Cancels++;
        Active=false;
        Result="INTERRUPTED";
        Draw();
    }
    public void Show(bool visible) {
        Witch.Show(visible); Hunter.Show(visible); Arm.Show(visible); Glyph.Show(visible);
        if(!visible && Active) Interrupt();
    }
    public void MovePlayer(float dx)
    {
        if(Pin<=0) Player.x=Mathf.Clamp(Player.x+dx,-2.2f,2.2f);
    }
    public bool StartCast()
    {
        if(Active) return false;
        Draw();
        if(!Projects(Enemy)) return false;
        if(Vector3.Distance(Enemy,Player)>MaxReach)
        { Result="OUT OF REACH"; return false; }
        BodyLockPoint=Player;
        Vector2 target=Ground ? new Vector2(Player.x,Player.z) : At(Player,0,1.20f);
        LockPoint=target; CastLight=LightId; Age=0; Active=true; Resolved=false;
        Result="LOCKED"; Draw(); return true;
    }
    public void StepMovement(float dt)
    {
        if(jumpVelocity!=0 || JumpHeight>0) {
            JumpHeight=Mathf.Max(0,JumpHeight+jumpVelocity*dt-.5f*Gravity*dt*dt);
            jumpVelocity=JumpHeight>0 ? jumpVelocity-Gravity*dt : 0;
        }
        Pin=Mathf.Max(0,Pin-dt);
    }
    public void Step(float dt)
    {
        StepMovement(dt);
        if(Active) Age+=dt;
        Draw();
        if(Active && !Resolved && Age>=CloseTime)
        {
            Resolved=true;
            Vector2 body=new Vector2(Player.x,Player.z), locked=new Vector2(BodyLockPoint.x,BodyLockPoint.z);
            if(JumpHeight<=GroundAttackHeight && Vector2.Distance(body,locked)<=ClawRadius)
            { Hits++; Pin=.8f; Result="HIT / PINNED"; }
            else { Misses++; Result="MISS / EMPTY STONE"; }
        }
        if(Active && Age>=EndTime) { Active=false; Draw(); }
    }
    public Vector2 At(Vector3 body,float x,float y) {
        Vector2 foot=Project(body);
        return foot+(Project(body+new Vector3(x,y,0))-foot)*SilhouetteScale;
    }
    void Figure(WallInk ink, Vector3 body, bool demon)
    {
        float scale=Ground ? (Lamp.y-(GroundHeight?.Invoke(body.x,body.z) ?? 0))/(Lamp.y-body.y-1.2f) : (WallZ-Lamp.z)/(body.z-Lamp.z);
        scale*=SilhouetteScale;
        if(!demon && Witch.Body!=null) {DrawHunter(body,scale);return;}
        ink.Oval(At(body,0,1.68f),scale*.14f,scale*.18f);
        ink.Polygon(At(body,-.10f,1.51f),At(body,.10f,1.51f),At(body,.23f,1.37f),
            At(body,.15f,.91f),At(body,-.16f,.91f),At(body,-.22f,1.37f));
        if(demon) {
            ink.Polygon(At(body,-.14f,1f),At(body,.14f,1f),At(body,.44f,.12f),At(body,-.37f,.12f));
            for(int side=-1;side<=1;side+=2) {
                ink.Polygon(At(body,.11f*side,1.73f),At(body,.24f*side,2.04f),At(body,.04f*side,1.84f));
            }
        } else {
            for(int s=-1;s<=1;s+=2) {
                ink.Strip(At(body,s*.095f,.98f),At(body,s*.16f,.06f),scale*.09f,scale*.058f);
            }
        }
        ink.Strip(At(body,-.18f,1.38f),At(body,-.34f,.84f),scale*.075f,scale*.045f);
        if(!demon) ink.Strip(At(body,.18f,1.38f),At(body,.34f,.98f),scale*.075f,scale*.045f);
    }
    void DrawHunter(Vector3 body,float scale) {
        Quaternion turn=Quaternion.Euler(0,HunterYaw,0);
        Vector2 foot=Project(body);
        Vector2 P(float x,float y,float z) => foot+(Project(body+turn*new Vector3(x,y,z))-foot)*SilhouetteScale;
        // Each section tapers between posture landmarks; adjacent sections share rounded joints.
        // The hunter holds a crossbow in front of the chest and shifts weight from boot to boot.
        void Sleeve(Vector2 a,Vector2 b,float ra,float rb) {
            Hunter.Strip(a,b,ra*scale,rb*scale);
            Hunter.Oval(b,rb*scale,rb*scale);
        }
        Vector2 waist=P(0,.88f,0),ribs=P(0,1.18f,.015f),neck=P(0,1.49f,.028f);
        Sleeve(waist,ribs,.145f,.198f);Sleeve(ribs,P(0,1.38f,.035f),.198f,.19f);
        Sleeve(P(0,1.38f,.035f),neck,.18f,.086f);
        Sleeve(neck,P(0,1.62f,.06f),.082f,.138f);
        Sleeve(P(0,1.62f,.06f),P(0,1.72f,.038f),.138f,.115f);
        for(int side=-1;side<=1;side+=2) {
            float step=Mathf.Sin(stride+.38f)*side;
            Vector2 hip=P(side*.103f,.91f,0),knee=P(side*.128f,.48f,step*.18f);
            Vector2 ankle=P(side*.14f,.10f,step*.35f-.035f);
            Sleeve(hip,knee,.114f,.083f);Sleeve(knee,P(side*.14f,.32f,step*.24f-.024f),.083f,.082f);
            Sleeve(P(side*.14f,.32f,step*.24f-.024f),ankle,.082f,.052f);
            Sleeve(ankle,P(side*.14f,.055f,step*.35f+.095f),.063f,.067f);
            Vector2 shoulder=P(side*.16f,1.40f,.025f);
            Vector2 elbow=P(side*.29f,1.14f,.13f),grip=P(side*.088f,1.29f,side<0?.44f:.21f);
            Sleeve(shoulder,elbow,.102f,.067f);Sleeve(elbow,grip,.067f,.050f);
        }
        Sleeve(P(.045f,1.29f,.13f),P(0,1.31f,.55f),.050f,.032f);
        Hunter.Strip(P(-.24f,1.30f,.43f),P(0,1.31f,.55f),.021f*scale,.028f*scale);
        Hunter.Strip(P(0,1.31f,.55f),P(.24f,1.30f,.43f),.028f*scale,.021f*scale);
    }
    public void Draw()
    {
        Witch.Clear(); Hunter.Clear(); Arm.Clear(); Glyph.Clear();
        if(Projects(PlayerBody)) Figure(Hunter,PlayerBody,false);
        if(Projects(Enemy)) DrawEnemy();
        Witch.Publish(); Hunter.Publish(); Arm.Publish(); Glyph.Publish();
    }
    void DrawEnemy()
    {
        if(Witch.Body!=null) {
            Shoulder=Witch.Body.Shoulder;
            Palm=Witch.Body.Hand;
            if(Active) DrawGlyph();
            return;
        }
        Figure(Witch,Enemy,true);
        Shoulder=At(Enemy,.19f,1.38f);
        Vector2 rest=At(Enemy,.36f,.84f);
        Vector2 aim=Active ? LockPoint : rest;
        float reach=0, close=0;
        if(Active) {
            reach=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.21f,CloseTime-.04f,Age));
            close=Mathf.SmoothStep(0,1,Mathf.InverseLerp(CloseTime-.15f,CloseTime,Age));
            if(Age>1.02f) reach*=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.02f,EndTime,Age));
        }
        Vector2 dir=(aim-Shoulder).normalized;
        Vector2 wristTarget=aim-dir*.29f;
        Palm=Vector2.Lerp(rest,wristTarget,reach);
        Vector2 elbow=(Shoulder+Palm)*.5f+Vector2.up*(.19f+.56f*Mathf.Sin(reach*Mathf.PI));
        Vector2 last=Shoulder;
        for(int i=1;i<=12;i++) {
            float t=i/12f;
            Vector2 p=(1-t)*(1-t)*Shoulder+2*(1-t)*t*elbow+t*t*Palm;
            Arm.Strip(last,p,Mathf.Lerp(.122f,.071f,(i-1)/12f),Mathf.Lerp(.122f,.071f,t));
            last=p;
        }
        Arm.Oval(Palm,.14f,.13f);
        dir=Active ? dir : new Vector2(.36f,-.93f);
        Vector2 n=new Vector2(-dir.y,dir.x);
        for(int f=-2;f<=2;f++) {
            float lane=f*.068f;
            Vector2 root=Palm+n*lane*.70f;
            Vector2 joint=Palm+dir*(.16f+.04f*(2-Mathf.Abs(f)))+n*lane*(1.35f-close*.40f);
            Vector2 tip=Palm+dir*(.29f+.10f*(1-close))+n*lane*(1.65f*(1-close));
            Arm.Strip(root,joint,.037f,.031f); Arm.Strip(joint,tip,.031f,.009f);
        }
        if(Active) DrawGlyph();
    }
    void DrawGlyph() {
        for(int j=-1;j<=1;j++) {
            Vector2 p=LockPoint+new Vector2(j*.085f,0);
            Glyph.Strip(p+new Vector2(-.033f,-.14f),p+new Vector2(.033f,.14f),.014f,.009f);
        }
    }
    public bool EnemyAt(Vector2 wallPoint) { return Witch.Contains(wallPoint)||Arm.Contains(wallPoint); }
}
