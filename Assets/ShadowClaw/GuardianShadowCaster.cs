using System.IO;
using UnityEngine;

// The low-detail skin comes from F01's original surface. Its bind matrices and
// live pose come from the very same bones that animate the visible suit.
internal sealed class GuardianShadowCaster {
    readonly Vector3[] rest;
    readonly BoneWeight[] weights;
    readonly int[] triangles;
    readonly Transform[] bones;
    readonly Matrix4x4[] inverseBind,pose;
    readonly Vector2[] points,pixels;
    readonly Transform body;
    readonly BellShadowProjection surface;
    bool published;

    static bool MatchesF01(Transform[] skeleton,Matrix4x4[] bind) {
        string[] roles={"Hips","Torso","Head","UpperArm.L","Forearm.L","Palm.L","UpperArm.R","Forearm.R","Palm.R"};
        if(skeleton==null || skeleton.Length!=roles.Length || bind==null || bind.Length!=roles.Length)return false;
        for(int i=0;i<roles.Length;i++)if(skeleton[i]==null || skeleton[i].name!=roles[i])return false;
        return true;
    }

    internal GuardianShadowCaster(BellShadowProjection surface,SkinnedMeshRenderer skin,Transform body,
        Transform torso,Transform leftArm,Transform rightArm) {
        this.surface=surface;
        this.body=body;
        using(var reader=new BinaryReader(new MemoryStream(Resources.Load<TextAsset>("Guardian/F01Shadow").bytes))) {
            int count=reader.ReadInt32();
            triangles=new int[reader.ReadInt32()];
            rest=new Vector3[count];weights=new BoneWeight[count];
            points=new Vector2[count];pixels=new Vector2[count];
            for(int i=0;i<count;i++) {
                rest[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                weights[i]=new BoneWeight {boneIndex0=reader.ReadByte(),boneIndex1=reader.ReadByte(),
                    boneIndex2=reader.ReadByte(),boneIndex3=reader.ReadByte(),weight0=reader.ReadSingle(),
                    weight1=reader.ReadSingle(),weight2=reader.ReadSingle(),weight3=reader.ReadSingle()};
            }
            for(int i=0;i<triangles.Length;i++)triangles[i]=reader.ReadUInt16();
        }
        var skeleton=skin!=null ? skin.bones:null;
        var bind=skin!=null && skin.sharedMesh!=null ? skin.sharedMesh.bindposes:null;
        if(MatchesF01(skeleton,bind)) {
            bones=skeleton;
            inverseBind=bind;
        } else {
            // The development/custom body still supplies its turning waist and arms.
            bones=new[]{body,torso,torso,leftArm,leftArm,leftArm,rightArm,rightArm,rightArm};
            inverseBind=new Matrix4x4[bones.Length];
            for(int i=0;i<bones.Length;i++)inverseBind[i]=bones[i].worldToLocalMatrix*body.localToWorldMatrix;
        }
        pose=new Matrix4x4[bones.Length];
    }

    static Vector3 CastDirection(FixedGuardianFrame frame,Vector3 light,float reach) {
        Vector3 away=Vector3.ProjectOnPlane(frame.Origin-light,Vector3.up);
        Vector3 inward=Vector3.ProjectOnPlane(frame.ArenaCenter-frame.Origin,Vector3.up);
        if(away.sqrMagnitude<.01f)away=inward.sqrMagnitude>.01f ? inward : frame.ArenaRight;
        if(inward.sqrMagnitude>.1f) {
            // With a lamp at the entrance, a literal ray would put the entire
            // weak point behind the opaque altar. Keep the cast on its lit side
            // of the centreline while allowing it to turn onto the side aisle.
            float angle=Vector3.SignedAngle(inward,away,Vector3.up);
            angle=Mathf.Clamp(angle,-100,100);
            inward.Normalize();
            // Turn only as far toward the open floor as needed to fit the head.
            // In particular, a single western chapel lamp would otherwise put
            // the enlarged helmet and an entire shoulder outside the east wall.
            for(int i=0;i<=10;i++) {
                away=Quaternion.AngleAxis(angle,Vector3.up)*inward;
                Vector3 tip=frame.Origin+away*reach-frame.ArenaCenter;
                if(Mathf.Abs(Vector3.Dot(tip,frame.ArenaRight))<=frame.ArenaSize.x*.5f-.8f &&
                   Mathf.Abs(Vector3.Dot(tip,frame.ArenaForward))<=frame.ArenaSize.y*.5f-.8f)break;
                angle=Mathf.MoveTowards(angle,0,10);
            }
        }
        return away.normalized;
    }

    internal void Draw(FixedGuardianFrame frame,Vector3 light,float size) {
        float litFraction=(float)frame.LitCount/Mathf.Max(1,frame.LampCount);
        float spread=Mathf.Lerp(1.61f,.94f,litFraction);
        float scale=size*spread;
        float castScale=size*Mathf.Min(spread,BellShadowProjection.MaxCastSlope);
        Vector3 bodyScale=body.lossyScale;
        Vector3 direction=CastDirection(frame,light,8.1f*bodyScale.y*castScale);
        Vector3 cross=new Vector3(direction.z,0,-direction.x);
        if(Vector3.Dot(cross,body.right)<0)cross=-cross;
        Matrix4x4 toWorld=body.localToWorldMatrix;
        Vector3 anchor=body.position;
        Vector2 center=new Vector2(anchor.x,anchor.z);
        Vector2 across=new Vector2(cross.x,cross.z)*scale;
        Vector2 along=new Vector2(direction.x,direction.z)*castScale;
        // Keep the front of the armour legible under a side light: lamp bearing
        // controls ground orientation; the live waist and shoulders control pose.
        Matrix4x4 toBody=body.worldToLocalMatrix;
        for(int i=0;i<bones.Length;i++)pose[i]=toBody*bones[i].localToWorldMatrix*inverseBind[i];
        bool changed=!published;
        Vector2 target=center+along*(4*bodyScale.y);
        for(int i=0;i<rest.Length;i++) {
            BoneWeight w=weights[i];
            Vector3 p=rest[i];
            Vector3 v=pose[w.boneIndex0].MultiplyPoint3x4(p)*w.weight0;
            if(w.weight1>0)v+=pose[w.boneIndex1].MultiplyPoint3x4(p)*w.weight1;
            if(w.weight2>0)v+=pose[w.boneIndex2].MultiplyPoint3x4(p)*w.weight2;
            if(w.weight3>0)v+=pose[w.boneIndex3].MultiplyPoint3x4(p)*w.weight3;
            // Soles keep the actual skinned vertex X/Z. Blend from that footprint
            // up the shins before turning and enlarging the upper silhouette.
            Vector3 world=toWorld.MultiplyPoint3x4(v);
            float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,1.5f,v.y));
            v=Vector3.Scale(v,bodyScale);
            Vector2 projected=Vector2.Lerp(new Vector2(world.x,world.z),
                center+across*v.x+along*(v.y+.38f*v.z),blend);
            pixels[i]=projected;
            if(!changed && (projected-points[i]).sqrMagnitude>.000225f)changed=true;
        }
        if(!changed)return;
        published=true;
        System.Array.Copy(pixels,points,points.Length);
        surface.UpdateArmour(points,pixels,triangles,target);
    }
}
