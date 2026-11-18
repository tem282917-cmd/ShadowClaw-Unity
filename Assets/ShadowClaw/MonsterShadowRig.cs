using System;
using System.Collections.Generic;
using UnityEngine;

// Articulated cut-paper caster. Each outline is in its own joint's local space;
// concave profiles keep the neck, pinched waist, hooked talons and open spaces.
internal sealed class MonsterShadowRig {
    public float LinearScale=1;
    internal sealed class Outline {
        public readonly Vector2[] Points;
        public readonly bool Cutout;
        readonly Vector2[] rest;
        public Outline(bool cutout, params int[] xy) {
            Cutout=cutout;
            int n=xy.Length/2;
            var knots=new Vector2[n];
            for(int i=0;i<n;i++) knots[i]=new Vector2(xy[2*i],xy[2*i+1])*.01f;
            rest=new Vector2[n*3]; Points=new Vector2[rest.Length];
            for(int i=0;i<n;i++) {
                var a=knots[(i+n-1)%n]; var b=knots[i]; var c=knots[(i+1)%n]; var d=knots[(i+2)%n];
                // Short tangents preserve the points of the antlers and frayed cloak.
                for(int j=0;j<3;j++) {
                    float t=j/3f,s=1-t;
                    rest[i*3+j]=s*s*s*b+3*s*s*t*(b+(c-a)*.12f)+3*s*t*t*(c-(d-b)*.12f)+t*t*t*c;
                }
            }
        }
        public Outline(int count) {Points=new Vector2[count];}
        public void Place(Vector2 origin,Vector2 across,Vector2 along) {
            for(int i=0;i<Points.Length;i++) Points[i]=origin+across*rest[i].x+along*rest[i].y;
        }
    }
    // Artist coordinates are in hundredths: Y follows the cast shadow, X crosses it.
    static readonly int[] Mantle={
        0,42, 18,35, 38,42, 32,31, 58,39, 52,27, 76,31, 62,12, 71,14,
        54,-2, 47,5, 44,-10, 35,-4, 32,-18, 23,-23, 17,-42, 24,-62, 14,-54,
        12,-75, 5,-61, 0,-83, -8,-63, -19,-74, -16,-54, -30,-61, -19,-37,
        -26,-20,-36,-10,-43,-15,-45,0,-55,-6,-68,13,-60,11,-78,31,-56,26,
        -64,39,-39,32,-42,44,-23,36
    };
    static readonly int[] Skull={
        -8,-25,-12,-16,-21,-8,-24,4,-35,14,-20,13,-21,22,-12,19,
        -5,26,2,23,10,26,14,19,22,23,21,15,34,17,23,6,21,-5,
        11,-16,7,-25,3,-18,0,-29,-4,-17
    };
    static readonly int[] Antler={
        12,12, 20,17, 29,27, 33,38, 33,49, 37,41, 37,34, 44,41,
        49,52, 48,62, 53,55, 54,67, 58,75, 57,62, 57,55, 64,61,
        71,74, 68,60, 61,49, 54,45, 47,36, 51,38, 60,41, 65,47,
        63,39, 54,33, 45,31, 36,23, 31,17, 38,19, 48,18,
        42,15, 32,13, 24,7, 17,4
    };
    static readonly int[] Eye={4,9,11,7,19,10,15,4,10,1,6,2};
    static readonly int[] UpperArm={-13,-9,-22,2,-24,21,-19,40,-21,54,-13,49,-11,74,-5,69,
        -6,94,-1,104,9,98,15,72,24,35,34,20,23,22,37,0,21,5,24,-9,9,-5};
    static readonly int[] Bracer={-8,-9,-16,7,-11,25,-18,35,-12,37,-12,62,-7,83,-10,97,
        -1,109,11,99,10,82,17,57,26,40,16,44,23,20,13,24,18,0,6,2};
    static readonly int[] Palm={-10,-12,-16,-3,-21,8,-18,18,-10,24,1,26,13,23,20,18,20,9,
        15,2,12,-8,4,-11};
    static readonly int[] Thigh={-11,-11,-19,0,-20,29,-13,52,-12,77,-5,105,5,109,11,87,10,65,
        20,40,23,12,16,-5,4,-10};
    static readonly int[] Shank={-7,-7,-15,7,-11,19,-6,37,-9,61,-12,83,-8,99,-2,104,8,97,
        8,81,6,61,12,35,19,18,13,18,18,2,6,4};
    static readonly int[] Foot={-7,-8,-10,1,-11,10,-22,20,-9,17,-3,12,-6,25,2,20,7,13,10,23,
        13,12,12,6,7,-7};

    public readonly List<Outline> Outlines=new List<Outline>();
    readonly Outline mantle,skull;
    readonly Outline[] antlers=new Outline[2], eyes=new Outline[2];
    struct Limb {
        public Outline upper,fore,palm,thigh,shin,foot;
        public Outline[] fingers;
        public int shoulder,elbow,wrist,hip,knee,ankle;
    }
    readonly Limb[] limbs=new Limb[2];
    readonly Transform[] bones;
    readonly Transform meshSpace;
    readonly Vector3[] rest;
    readonly Vector2[] offset;
    readonly int head,chest;
    Vector2 origin,across,along;
    float width,length;
    public Vector2 Shoulder {get;private set;}
    public Vector2 PalmPosition {get;private set;}
    public float HandScale {get;private set;}
    public Vector2 CastTip {get;private set;}
    public Vector2 TorsoCenter {get;private set;}
    public Vector2 CastGap {get;private set;}

    public MonsterShadowRig(SkinnedMeshRenderer model) {
        bones=model.bones; meshSpace=model.transform;
        var binds=model.sharedMesh.bindposes;
        rest=new Vector3[bones.Length];offset=new Vector2[bones.Length];
        for(int i=0;i<rest.Length;i++)rest[i]=binds[i].inverse.MultiplyPoint3x4(Vector3.zero);
        int Bone(string name)=>Array.FindIndex(bones,b=>b.name==name);
        head=Bone("Head"); chest=Bone("Chest");
        Outline Add(int[] pts,bool hole=false) {var shape=new Outline(hole,pts);Outlines.Add(shape);return shape;}
        for(int side=0;side<2;side++) {
            string suffix=side==0?".L":".R";
            var limb=new Limb {shoulder=Bone("UpperArm"+suffix),elbow=Bone("Forearm"+suffix),wrist=Bone("Hand"+suffix),
                hip=Bone("Thigh"+suffix),knee=Bone("Shin"+suffix),ankle=Bone("Foot"+suffix),
                thigh=Add(Thigh),shin=Add(Shank),foot=Add(Foot),upper=Add(UpperArm),fore=Add(Bracer),palm=Add(Palm),
                fingers=new Outline[5]};
            for(int i=0;i<limb.fingers.Length;i++){limb.fingers[i]=new Outline(26);Outlines.Add(limb.fingers[i]);}
            limbs[side]=limb;
        }
        mantle=Add(Mantle);
        for(int i=0;i<2;i++)antlers[i]=Add(Antler);
        skull=Add(Skull);
        for(int i=0;i<2;i++)eyes[i]=Add(Eye,true);
    }
    Vector2 Landmark(float x,float y,int bone) => origin+across*(x*width)+along*(y*length)+offset[bone];
    void Link(Outline contour,Vector2 start,Vector2 end,int mirror) {
        Vector2 axis=end-start;
        var normal=new Vector2(axis.y,-axis.x).normalized;
        contour.Place(start,normal*(mirror*width),axis);
    }
    static Vector2 Perpendicular(Vector2 v)=>new Vector2(-v.y,v.x);

    public void Pose(ClawState state) {
        // Anchor to the feet even on raised floors. Clamping the light direction
        // before posing also keeps animated joints within the same projection.
        origin=new Vector2(state.Enemy.x,state.Enemy.z);
        Vector2 away=origin-new Vector2(state.Lamp.x,state.Lamp.z);
        along=away.sqrMagnitude>.0001f?away.normalized:Vector2.up;
        float headClearance=Mathf.Max(.1f,state.Lamp.y-state.Enemy.y-1.74f);
        float slope=Mathf.Clamp(away.magnitude/headClearance,1.02f,BellShadowProjection.MaxCastSlope);
        across=new Vector2(along.y,-along.x);
        float lampScale=state.SilhouetteScale;
        float enlargement=Mathf.Max(.1f,LinearScale);
        width=lampScale*enlargement;
        length=slope*width;
        for(int i=0;i<bones.Length;i++) {
            Vector3 motion=bones[i].position-meshSpace.TransformPoint(rest[i]);
            Vector2 delta=new Vector2(motion.x,motion.z)+along*(motion.y*slope);
            offset[i]=Vector2.ClampMagnitude(delta*width,.47f*width);
        }
        var sternum=Landmark(0,1.17f,chest);
        TorsoCenter=sternum;
        mantle.Place(sternum,across*width,along*length*.87f);
        Vector2 jaw=Landmark(.02f,1.66f,head);
        float headTurn=Vector3.Dot(meshSpace.forward,new Vector3(across.x,0,across.y));
        Vector2 brow=across+along*(headTurn*.10f);
        Vector2 crest=along-across*(headTurn*.11f);
        skull.Place(jaw,brow*width,crest*length*.9f);
        for(int side=0;side<2;side++) {
            int mirror=side==0?-1:1;
            antlers[side].Place(jaw,brow*(mirror*width),crest*(length*.88f));
            eyes[side].Place(jaw,brow*(mirror*width),crest*length*.9f);
            PoseLimb(state,side,mirror);
        }
    }
    void PoseLimb(ClawState state,int index,int side) {
        var limb=limbs[index];
        Vector2 hip=Landmark(side*.14f,.66f,limb.hip);
        Vector2 knee=Landmark(side*.38f,.38f,limb.knee);
        Vector2 ankle=Landmark(side*.19f,.075f,limb.ankle);
        Link(limb.thigh,hip,knee,side);Link(limb.shin,knee,ankle,side);
        limb.foot.Place(ankle,across*(side*width),-along*width*.66f);
        Vector2 shoulder=Landmark(side*.42f,1.40f,limb.shoulder);
        Vector2 elbow=Landmark(side*.83f,1.12f,limb.elbow);
        Vector2 wrist=Landmark(side*1.00f,.79f,limb.wrist);
        float reach=0,close=0;
        Vector2 direction=((across*side)*.49f-along).normalized;
        float size=width*.83f;
        if(index==1) {
            Shoulder=shoulder;
            if(state.Active) {
                float age=state.Age;
                float release=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.02f,ClawState.EndTime,age));
                reach=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,ClawState.CloseTime-.085f,age))*release;
                close=Mathf.SmoothStep(0,1,Mathf.InverseLerp(ClawState.CloseTime-.13f,ClawState.CloseTime,age))*release;
                Vector2 to=(state.LockPoint-shoulder).normalized;
                float grow=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.07f,.55f,age))*release;
                size*=1+1.05f*grow;
                direction=Vector2.Lerp(direction,to,reach).normalized;
                // Place the middle two hooks across the actual mark at closure.
                wrist=Vector2.Lerp(wrist,state.LockPoint-to*size*.36f,reach);
                var folded=(shoulder+wrist)*.5f+Perpendicular(to)*width*(.62f+.36f*Mathf.Sin(reach*Mathf.PI));
                elbow=Vector2.Lerp(elbow,folded,reach);
            }
            PalmPosition=wrist; HandScale=size;
        }
        Link(limb.upper,shoulder,elbow,side);Link(limb.fore,elbow,wrist,side);
        Vector2 lateral=Perpendicular(direction)*side;
        limb.palm.Place(wrist,lateral*size,direction*size);
        for(int i=0;i<5;i++) {
            bool thumb=i==0;
            float lane=thumb?-.17f:(i-2.4f)*.101f;
            float outer=thumb?-.55f:lane*2.75f;
            float reachY=thumb?.34f:.75f-.14f*Mathf.Abs(i-2.0f);
            Vector2 a=new Vector2(lane,thumb?.025f:.17f);
            Vector2 b=new Vector2(Mathf.Lerp(lane*1.75f,outer,close),thumb?.22f:.41f);
            Vector2 c=new Vector2(Mathf.Lerp(outer*1.25f+.075f,outer*.9f,close),reachY+.25f);
            Vector2 d=Vector2.Lerp(new Vector2(outer*.78f-.070f,reachY-.045f),new Vector2(lane*.25f,.35f),close);
            if(i==2 && close>0) {
                // At closure the last phalanx crosses the mark with a finite
                // width. A point exactly between two closing tips would miss.
                const float t=10f/12f,s=1-t;
                Vector2 folded=(new Vector2(0,.36f)-s*s*s*a-3*s*s*t*b-3*s*t*t*c)/(t*t*t);
                d=Vector2.Lerp(d,folded,close);
            }
            var points=limb.fingers[i].Points;
            for(int j=0;j<13;j++) {
                float t=j/12f,s=1-t;
                var mid=s*s*s*a+3*s*s*t*b+3*s*t*t*c+t*t*t*d;
                Vector2 tangent=(s*s*(b-a)+2*s*t*(c-b)+t*t*(d-c)).normalized;
                float r=(.050f+.023f*Mathf.Exp(-110*(t-.43f)*(t-.43f)))*Mathf.Pow(s,.81f);
                Vector2 edge=Perpendicular(tangent)*r;
                Vector2 hi=mid+edge,lo=mid-edge;
                points[j]=wrist+size*(lateral*hi.x+direction*hi.y);
                points[25-j]=wrist+size*(lateral*lo.x+direction*lo.y);
                if(index==1 && i==3 && j==8) CastTip=(points[j]+points[25-j])*.5f;
                if(index==1 && i==4 && j==8) CastGap=(CastTip+(points[j]+points[25-j])*.5f)*.5f;
            }
        }
    }
}
