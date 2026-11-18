using System.Collections.Generic;
using UnityEngine;

// A small world-space window of the registered floor colliders. The window stays
// fixed as a silhouette changes shape. Unknown collider cooking uses PhysX.
internal sealed class MinorShadowFloor {
    struct Face {
        public Vector2 A,B,C;
        public Vector3 Point,Normal;
        public float MinX,MaxX,MinZ,MaxZ;
        public bool Covers(Vector2 p) {
            if(p.x<MinX || p.x>MaxX || p.y<MinZ || p.y>MaxZ)return false;
            return Side(A,B,p)<=.000001f && Side(B,C,p)<=.000001f && Side(C,A,p)<=.000001f;
        }
        static float Side(Vector2 a,Vector2 b,Vector2 p)=>(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);
        public float Height(Vector2 p)=>Point.y-((p.x-Point.x)*Normal.x+(p.y-Point.z)*Normal.z)/Normal.y;
    }
    readonly List<Face> faces=new List<Face>();
    readonly MinorShadowReceiverCache receivers=new MinorShadowReceiverCache();
    Bounds window;
    bool ready;
    internal string Diagnostic => (ready?"cached":"physics")+" faces="+faces.Count+" "+receivers.Diagnostic;
    public void Prepare(IronHallEncounter hall,Vector2 min,Vector2 max,float footY) {
        // The ray window must fit too, including the step above and below its origin.
        if(ready && window.Contains(new Vector3(min.x,footY-1,min.y)) &&
            window.Contains(new Vector3(max.x,footY+1,max.y)) && receivers.Unchanged(hall))return;
        window.SetMinMax(new Vector3(min.x-1,footY-3,min.y-1),new Vector3(max.x+1,footY+3,max.y+1));
        receivers.Capture(hall,window);
        faces.Clear();ready=receivers.CopyFloorFaces(this) && faces.Count<=160;
    }
    public void AddFace(Vector3 a,Vector3 b,Vector3 c) {
        float minX=Mathf.Min(a.x,Mathf.Min(b.x,c.x)),maxX=Mathf.Max(a.x,Mathf.Max(b.x,c.x));
        float minZ=Mathf.Min(a.z,Mathf.Min(b.z,c.z)),maxZ=Mathf.Max(a.z,Mathf.Max(b.z,c.z));
        if(maxX<window.min.x || minX>window.max.x || maxZ<window.min.z || minZ>window.max.z ||
            Mathf.Max(a.y,Mathf.Max(b.y,c.y))<window.min.y || Mathf.Min(a.y,Mathf.Min(b.y,c.y))>window.max.y)return;
        Vector3 n=Vector3.Cross(b-a,c-a).normalized;
        if(n.y<=.5f)return;
        faces.Add(new Face{A=new Vector2(a.x,a.z),B=new Vector2(b.x,b.z),C=new Vector2(c.x,c.z),
            Point=a,Normal=n,MinX=minX,MaxX=maxX,MinZ=minZ,MaxZ=maxZ});
    }
    public bool AddBox(BoxCollider box) {
        Matrix4x4 m=box.transform.localToWorldMatrix;
        Vector3 x=m.MultiplyVector(Vector3.right),y=m.MultiplyVector(Vector3.up),z=m.MultiplyVector(Vector3.forward);
        if(m.determinant<=0 || Mathf.Abs(Vector3.Dot(x.normalized,y.normalized))>.0001f ||
            Mathf.Abs(Vector3.Dot(x.normalized,z.normalized))>.0001f || Mathf.Abs(Vector3.Dot(y.normalized,z.normalized))>.0001f)return false;
        Vector3 center=m.MultiplyPoint3x4(box.center);
        x*=box.size.x*.5f;y*=box.size.y*.5f;z*=box.size.z*.5f;
        Quad(center+x,y,z);Quad(center-x,z,y);
        Quad(center+y,z,x);Quad(center-y,x,z);
        Quad(center+z,x,y);Quad(center-z,y,x);
        return true;
    }
    void Quad(Vector3 center,Vector3 u,Vector3 v) {
        Vector3 a=center-u-v,c=center+u+v;
        AddFace(a,center+u-v,c);AddFace(a,c,center-u+v);
    }
    internal bool Sample(IronHallEncounter hall,Vector2 p,float reference,RaycastHit[] hits,out MinorShadowGround.SamplePoint sample) {
        float high=reference+MinorShadowGround.StepUp+.005f,low=reference-MinorShadowGround.StepDown-.005f;
        if(!ready || !window.Contains(new Vector3(p.x,low,p.y)) || !window.Contains(new Vector3(p.x,high,p.y)))
            return MinorShadowGround.Sample(hall,p,reference,hits,out sample);
        float best=float.NegativeInfinity;sample=default;
        for(int i=0;i<faces.Count;i++) {
            var f=faces[i];if(!f.Covers(p))continue;
            float y=f.Height(p);
            if(y<low || y>high || y<=best)continue;
            best=y;sample.Point=new Vector3(p.x,y,p.y);sample.Normal=f.Normal;
        }
        return !float.IsNegativeInfinity(best);
    }

    // Publish an entire hull at once only when one face supports the whole
    // rectangle and every overlapping candidate is on that same plane.
    internal bool ContinuousPlane(Vector2 min,Vector2 max,MinorShadowGround.SamplePoint floor) {
        if(!ready)return false;
        bool covered=false;
        foreach(var f in faces) {
            if(f.MaxX<min.x || f.MinX>max.x || f.MaxZ<min.y || f.MinZ>max.y)continue;
            if(Vector3.Dot(f.Normal,floor.Normal)<.99999f || Mathf.Abs(Vector3.Dot(f.Point-floor.Point,floor.Normal))>.001f)return false;
            covered|=f.Covers(min) && f.Covers(max) && f.Covers(new Vector2(min.x,max.y)) && f.Covers(new Vector2(max.x,min.y));
        }
        return covered;
    }
}
