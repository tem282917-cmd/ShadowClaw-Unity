using System.Collections.Generic;
using UnityEngine;

// Each silhouette is a union of convex pieces. Hit tests read the same triangles
// sent to the renderer, in wall coordinates (world X/Y on this flat test wall).
public sealed class WallInk
{
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<int> triangles = new List<int>();
    readonly Mesh mesh;
    readonly MeshRenderer renderer;
    public BellShadowProjection Body {get;private set;}
    public void BindCharacter(SkinnedMeshRenderer character) {
        Body=new BellShadowProjection(character,mesh,renderer,bounds,surfaceHeight);
    }
    internal BellShadowProjection BindArmour() {
        Body=new BellShadowProjection(mesh,renderer,bounds,surfaceHeight,"Lamplighter armour shadow",localReceiver:true);
        return Body;
    }
    bool mergedIntoBody;
    public void MergeVisualIntoBody() { mergedIntoBody=true;renderer.enabled=false; }
    public void Show(bool visible) { if(renderer!=null) renderer.enabled=visible && !mergedIntoBody; }
    readonly float depth;
    readonly bool floor;
    readonly System.Func<float,float,float> surfaceHeight;
    readonly Rect bounds;
    float MinY => bounds.yMin;
    float MaxY => bounds.yMax;
    float MinX => bounds.xMin;
    float MaxX => bounds.xMax;
    public readonly List<Vector3> Vertices;
    public readonly List<int> Triangles;
    public WallInk(bool floor=false, Rect? area=null) {
        this.floor=floor; Vertices=vertices; Triangles=triangles;
        bounds=area ?? (floor ? Rect.MinMaxRect(-11,-7.5f,11,ClawState.WallZ) : Rect.MinMaxRect(-8.5f,0,8.5f,5.59f));
    }
    public WallInk(Transform parent, string name, Material material, float depth, bool floor=false, Rect? area=null, System.Func<float,float,float> surfaceHeight=null)
        : this(floor,area)
    {
        this.depth = depth;
        this.surfaceHeight=surfaceHeight;
        this.floor=floor;
        mesh = new Mesh();
        var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        if(floor) obj.transform.rotation=Quaternion.Euler(90,0,0);
        // Vertices and hit tests share world coordinates, even when an authored
        // encounter root is positioned away from the legacy hall's origin.
        obj.transform.SetParent(parent, true);
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        renderer=obj.GetComponent<MeshRenderer>();
        renderer.sharedMaterial=material;
        mesh.MarkDynamic();
    }
    public void Clear() { vertices.Clear(); triangles.Clear(); }
    public void Publish()
    {
        if (mesh != null && Body==null) { mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); }
    }
    public void Polygon(params Vector2[] points)
    {
        foreach(var point in points) {
            if(point.x>=MinX && point.x<=MaxX && point.y>=MinY && point.y<=MaxY) continue;
            // Clip convex pieces to the receiver before both rendering and hit testing.
            for(int axis=0; axis<4 && points.Length>0;axis++) {
                float Inside(Vector2 p) {
                    switch(axis) {
                        case 0: return p.x-MinX;
                        case 1: return MaxX-p.x;
                        case 2: return p.y-MinY;
                        default: return MaxY-p.y;
                    }
                }
                var clipped=new List<Vector2>();
                Vector2 prev=points[points.Length-1];
                foreach(var curr in points) {
                    float a=Inside(prev), b=Inside(curr);
                    if((a<0)!=(b<0)) clipped.Add(Vector2.Lerp(prev,curr,a/(a-b)));
                    if(b>=0) clipped.Add(curr);
                    prev=curr;
                }
                points=clipped.ToArray();
            }
            break;
        }
        if(points.Length<3) return;
        int offset = vertices.Count;
        foreach (var p in points) {
            float elevation=surfaceHeight?.Invoke(p.x,p.y) ?? 0;
            vertices.Add(new Vector3(p.x,p.y,depth-elevation-(elevation>0 ? .045f : 0)));
        }
        for (int i=1; i+1<points.Length; i++)
        { triangles.Add(offset); triangles.Add(offset+i); triangles.Add(offset+i+1); }
    }
    public void Oval(Vector2 center, float rx, float ry)
    {
        var p=new Vector2[20];
        for (int i=0; i<p.Length; i++) {
            float a=i*Mathf.PI*2/p.Length;
            p[i]=center+new Vector2(rx*Mathf.Cos(a), ry*Mathf.Sin(a));
        }
        Polygon(p);
    }
    public void Strip(Vector2 a, Vector2 b, float w1, float w2)
    {
        Vector2 v=(b-a).normalized, n=new Vector2(-v.y,v.x);
        Polygon(a+n*w1,b+n*w2,b-n*w2,a-n*w1);
    }
    static float Cross(Vector2 a, Vector2 b) { return a.x*b.y-a.y*b.x; }
    // A sweep intersects a triangle through its interior, an edge crossing or an edge within the sweep radius.
    public bool TouchesSegment(Vector2 start,Vector2 end,float radius) {
        if(Touches(start,radius) || Touches(end,radius)) return true;
        Vector2 path=end-start;
        if(path.sqrMagnitude<.000001f) return false;
        for(int i=0;i<triangles.Count;i+=3) for(int edge=0;edge<3;edge++) {
            Vector2 a=vertices[triangles[i+edge]], b=vertices[triangles[i+(edge+1)%3]];
            float t=Mathf.Clamp01(Vector2.Dot(a-start,path)/path.sqrMagnitude);
            if((a-start-t*path).sqrMagnitude<=radius*radius) return true;
            float cross=Cross(path,b-a);
            if(Mathf.Abs(cross)<.000001f) continue;
            float along=Cross(a-start,b-a)/cross, across=Cross(a-start,path)/cross;
            if(along>=0 && along<=1 && across>=0 && across<=1) return true;
        }
        return false;
    }
    public bool Touches(Vector2 center, float radius)
    {
        if(Body!=null)return Body.Touches(center,radius);
        if(Contains(center)) return true;
        for(int i=0;i<triangles.Count;i+=3)
            for(int edge=0;edge<3;edge++) {
                Vector2 a=vertices[triangles[i+edge]];
                Vector2 b=vertices[triangles[i+(edge+1)%3]];
                Vector2 line=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(center-a,line)/Mathf.Max(line.sqrMagnitude,.000001f));
                if((center-a-line*t).sqrMagnitude <= radius*radius) return true;
            }
        return false;
    }
    public float HitDistance(Ray ray)
    {
        if(Body!=null)return Body.HitDistance(ray);
        if(surfaceHeight!=null) {
            // Rotate the ray into the floor ink mesh's local space (x,z,-y).
            Vector3 o=new Vector3(ray.origin.x,ray.origin.z,-ray.origin.y);
            Vector3 d=new Vector3(ray.direction.x,ray.direction.z,-ray.direction.y);
            float nearest=float.PositiveInfinity;
            for(int i=0;i<triangles.Count;i+=3) {
                Vector3 a=vertices[triangles[i]], e=vertices[triangles[i+1]]-a, f=vertices[triangles[i+2]]-a;
                Vector3 h=Vector3.Cross(d,f);
                float det=Vector3.Dot(e,h);
                if(Mathf.Abs(det)<1e-8f) continue;
                Vector3 offset=o-a, q=Vector3.Cross(offset,e);
                float u=Vector3.Dot(offset,h)/det, v=Vector3.Dot(d,q)/det, t=Vector3.Dot(f,q)/det;
                if(u>=0 && v>=0 && u+v<=1 && t>=0) nearest=Mathf.Min(nearest,t);
            }
            return nearest;
        }
        float component=floor ? ray.direction.y : ray.direction.z;
        if(Mathf.Abs(component)<.00001f) return float.PositiveInfinity;
        float distance=floor ? (-depth-ray.origin.y)/component : (depth-ray.origin.z)/component;
        if(distance<0) return float.PositiveInfinity;
        var point=ray.GetPoint(distance);
        return Contains(floor ? new Vector2(point.x,point.z) : new Vector2(point.x,point.y)) ? distance : float.PositiveInfinity;
    }
    public bool Contains(Vector2 q)
    {
        if(Body!=null)return Body.Contains(q);
        for(int i=0;i<triangles.Count;i+=3) {
            Vector2 a=vertices[triangles[i]], b=vertices[triangles[i+1]], c=vertices[triangles[i+2]];
            if(Mathf.Abs(Cross(b-a,c-a)) < 0.000001f) continue;
            float x=Cross(b-a,q-a), y=Cross(c-b,q-b), z=Cross(a-c,q-c);
            if ((x>=-0.00001f && y>=-0.00001f && z>=-0.00001f) ||
                (x<=0.00001f && y<=0.00001f && z<=0.00001f)) return true;
        }
        return false;
    }
}
