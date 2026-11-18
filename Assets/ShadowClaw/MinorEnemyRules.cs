using System.Collections.Generic;
using UnityEngine;

// Rules shared by the authored minor enemies and their isolated checks.
public static class MinorEnemyRules
{
    public static float ShadowScale(int lit,int capacity) => lit<=0 ? 0 :
        Mathf.Lerp(1.35f,.68f,Mathf.InverseLerp(1,Mathf.Max(2,capacity),lit));

    public static Vector2 Project(Vector3 foot,Vector3 offset,Vector3 light,float scale) {
        Vector3 point=foot+offset;
        float height=Mathf.Max(.2f,light.y-foot.y);
        float ratio=height/Mathf.Max(.25f,light.y-point.y);
        Vector2 projected=new Vector2(light.x+(point.x-light.x)*ratio,light.z+(point.z-light.z)*ratio);
        Vector2 origin=new Vector2(foot.x,foot.z);
        // Keep unusually low authored lights from creating an unbounded target.
        return origin+Vector2.ClampMagnitude((projected-origin)*scale,6f);
    }

    public static Vector2 ProjectLocal(Vector3 foot,Vector3 offset,Vector3 light,float scale,float silhouetteHeight=2.1f) {
        // Use one raised proxy for the whole volume. Capping individual projected
        // vertices at a radius collapses the hood and turns side-lit bodies into slivers.
        float height=Mathf.Max(2.1f,silhouetteHeight);
        float distance=new Vector2(light.x-foot.x,light.z-foot.z).magnitude;
        light.y=Mathf.Max(light.y,foot.y+Mathf.Max(height+.25f,height*(1+distance/4.5f)));
        Vector3 point=foot+offset;
        float ratio=(light.y-foot.y)/Mathf.Max(.25f,light.y-point.y);
        Vector2 origin=new Vector2(foot.x,foot.z);
        Vector2 projected=new Vector2(light.x+(point.x-light.x)*ratio,light.z+(point.z-light.z)*ratio);
        return origin+(projected-origin)*Mathf.Max(.1f,scale);
    }
    public static Vector2 ProjectSky(Vector3 foot,Vector3 offset,Vector3 direction,float scale) {
        // Keep a grazing authored moon readable while retaining its azimuth.
        Vector3 ray=direction.sqrMagnitude>.00001f ? direction.normalized : Vector3.down;
        float fall=Mathf.Max(.35f,-ray.y);
        Vector2 point=new Vector2(offset.x+ray.x*Mathf.Max(0,offset.y)/fall,
            offset.z+ray.z*Mathf.Max(0,offset.y)/fall);
        return new Vector2(foot.x,foot.z)+point*Mathf.Max(.1f,scale);
    }

    // Monotone-chain convex hull with reusable buffers. Every sample belongs to a
    // solid proxy, so changing the light/body azimuth cannot make it edge-on.
    public static void ProjectVolumeHull(Vector3[] volume,Vector3 foot,Quaternion rotation,
        Vector3 localOffset,Vector3 localScale,MinorShadowLighting lighting,float scale,
        List<Vector2> samples,List<Vector2> hull) {
        samples.Clear();hull.Clear();
        foreach(var point in volume)
            samples.Add(lighting.Project(foot,rotation*(Vector3.Scale(point,localScale)+localOffset),scale));
        samples.Sort(PointComparer.Instance);
        if(samples.Count<3)return;
        for(int i=0;i<samples.Count;i++) {
            var p=samples[i];
            while(hull.Count>=2 && Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=.000001f)hull.RemoveAt(hull.Count-1);
            hull.Add(p);
        }
        int lower=hull.Count;
        for(int i=samples.Count-2;i>=0;i--) {
            var p=samples[i];
            while(hull.Count>lower && Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=.000001f)hull.RemoveAt(hull.Count-1);
            hull.Add(p);
        }
        hull.RemoveAt(hull.Count-1);
    }
    sealed class PointComparer : IComparer<Vector2> {
        public static readonly PointComparer Instance=new PointComparer();
        public int Compare(Vector2 a,Vector2 b) => a.x!=b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
    }
    static float Cross(Vector2 a,Vector2 b,Vector2 c) => (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);

    public static bool SegmentHitsHunter(Vector3 start,Vector3 end,Vector3 hunterFeet,float projectileRadius) {
        // The actual hunter body is a vertical capsule, independent of its shadow.
        Vector3 lower=hunterFeet+Vector3.up*.35f,upper=hunterFeet+Vector3.up*1.35f;
        Vector3 u=end-start,v=upper-lower,w=start-lower;
        float a=Vector3.Dot(u,u),b=Vector3.Dot(u,v),c=Vector3.Dot(v,v);
        float d=Vector3.Dot(u,w),e=Vector3.Dot(v,w),den=a*c-b*b;
        float s=a<.000001f ? 0 : Mathf.Clamp01(den>.000001f ? (b*e-c*d)/den : -d/a);
        float t=Mathf.Clamp01((b*s+e)/Mathf.Max(.000001f,c));
        s=a<.000001f ? 0 : Mathf.Clamp01((b*t-d)/a);
        t=Mathf.Clamp01((b*s+e)/Mathf.Max(.000001f,c));
        float radius=.35f+projectileRadius;
        return (start+u*s-(lower+v*t)).sqrMagnitude<=radius*radius;
    }

    public static float TriangleHit(Ray ray,Vector3 a,Vector3 b,Vector3 c) {
        Vector3 e1=b-a,e2=c-a,p=Vector3.Cross(ray.direction,e2);
        float det=Vector3.Dot(e1,p);
        if(Mathf.Abs(det)<.000001f)return float.PositiveInfinity;
        float inv=1f/det;
        Vector3 s=ray.origin-a;
        float u=Vector3.Dot(s,p)*inv;
        if(u<0 || u>1)return float.PositiveInfinity;
        Vector3 q=Vector3.Cross(s,e1);
        float v=Vector3.Dot(ray.direction,q)*inv;
        if(v<0 || u+v>1)return float.PositiveInfinity;
        float distance=Vector3.Dot(e2,q)*inv;
        return distance>=0 ? distance : float.PositiveInfinity;
    }
}

// Small, shared gameplay volumes, not visual effects. Their projected hulls are
// both the visible target and the shooting geometry; no per-frame arrays are made.
public static class MinorEnemyVolumes
{
    const int Sides=12;
    public static readonly Vector3[] AcolyteRobe=Rings(
        new Vector3(0,.08f,0),new Vector2(.35f,.27f),
        new Vector3(0,1.4f,0),new Vector2(.28f,.23f),
        new Vector3(0,1.52f,0),new Vector2(.17f,.16f));
    public static readonly Vector3[] AcolyteHood=Ellipsoid(new Vector3(0,1.65f,0),new Vector3(.26f,.29f,.23f));
    public static readonly Vector3[] AcolyteSleeve=Sleeve(.26f);
    public static readonly Vector3[] AcolytePrayerSleeve=Sleeve(.65f);
    static Mesh defaultGhostMesh;
    static Vector3[] defaultGhostVertices;
    public static Vector3[] GhostBody => GhostVertices(null);
    internal static Vector3[] SimpleBody(Vector3[] points) {
        var bounds=new Bounds(points[0],Vector3.zero);
        foreach(var p in points)bounds.Encapsulate(p);
        var c=bounds.center;var e=bounds.extents;
        return new[]{c+Vector3.up*e.y,c-Vector3.up*e.y,c+Vector3.right*e.x,
            c-Vector3.right*e.x,c+Vector3.forward*e.z,c-Vector3.forward*e.z};
    }
    public static Vector3[] GhostVertices(Mesh mesh) {
        if(defaultGhostMesh==null)defaultGhostMesh=Resources.Load<Mesh>("HollowWisp/Mantle");
        bool useDefault=mesh==null || mesh==defaultGhostMesh;
        if(useDefault && defaultGhostVertices!=null)return defaultGhostVertices;
        if(useDefault)mesh=defaultGhostMesh;
        // Flat-shaded folds duplicate positions at UV and normal seams. The
        // projected hull needs each position once, shared by all five spirits.
        var unique=new HashSet<Vector3>(mesh.vertices);
        var points=new Vector3[unique.Count];unique.CopyTo(points);
        if(useDefault)defaultGhostVertices=points;
        return points;
    }
    static Vector3[] Sleeve(float reach) => Rings(
        new Vector3(.25f,1.37f,.02f),new Vector2(.15f,.13f),
        new Vector3(.41f,.95f,reach),new Vector2(.12f,.11f));
    static Vector3[] Rings(Vector3 first,Vector2 firstRadius,Vector3 second,Vector2 secondRadius) {
        var points=new Vector3[Sides*2];Ring(points,0,first,firstRadius);Ring(points,Sides,second,secondRadius);return points;
    }
    static Vector3[] Rings(Vector3 first,Vector2 firstRadius,Vector3 second,Vector2 secondRadius,Vector3 third,Vector2 thirdRadius) {
        var points=new Vector3[Sides*3];Ring(points,0,first,firstRadius);Ring(points,Sides,second,secondRadius);
        Ring(points,Sides*2,third,thirdRadius);return points;
    }
    static void Ring(Vector3[] points,int start,Vector3 center,Vector2 radius) {
        for(int i=0;i<Sides;i++) {
            float angle=i*Mathf.PI*2/Sides;
            points[start+i]=center+new Vector3(Mathf.Cos(angle)*radius.x,0,Mathf.Sin(angle)*radius.y);
        }
    }
    static Vector3[] Ellipsoid(Vector3 center,Vector3 radius) {
        const int rings=5;
        var points=new Vector3[rings*Sides+2];
        points[points.Length-2]=center+Vector3.up*radius.y;points[points.Length-1]=center-Vector3.up*radius.y;
        for(int i=0;i<rings;i++) {
            float latitude=(i+1)*Mathf.PI/(rings+1)-Mathf.PI*.5f;
            Ring(points,i*Sides,center+Vector3.up*(Mathf.Sin(latitude)*radius.y),
                new Vector2(radius.x,radius.z)*Mathf.Cos(latitude));
        }
        return points;
    }
}

