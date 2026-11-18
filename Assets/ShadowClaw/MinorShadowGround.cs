using System.Collections.Generic;
using UnityEngine;

public sealed partial class ShadowLab
{
    readonly RaycastHit[] minorGroundHits=new RaycastHit[64];

    // A foot keeps its current storey. SurfaceHeight is deliberately unchanged:
    // the boss arena still uses its existing highest-surface projection contract.
    public bool TryMinorGround(Vector3 position,float referenceHeight,out Vector3 grounded) {
        var binding=hall!=null ? hall : GetComponent<IronHallEncounter>();
        bool found=MinorShadowGround.Sample(binding,new Vector2(position.x,position.z),referenceHeight,
            minorGroundHits,out var sample);
        grounded=new Vector3(position.x,found ? sample.Point.y : referenceHeight,position.z);
        return found;
    }
}

internal static class MinorShadowGround
{
    internal const float StepUp=.48f,StepDown=.62f,Lift=.035f;
    static IronHallEncounter cachedHall;
    static Collider[] cachedSources;
    static readonly HashSet<Collider> registeredGround=new HashSet<Collider>();
    internal struct SamplePoint { public Vector3 Point,Normal; }
    internal static bool IsGround(IronHallEncounter hall,Collider collider) {
        if(!hall.UseAuthoredLayout)return hall.IsGround(collider);
        if(cachedHall!=hall || !ReferenceEquals(cachedSources,hall.GroundColliders)) {
            cachedHall=hall;cachedSources=hall.GroundColliders;registeredGround.Clear();
            foreach(var ground in cachedSources ?? System.Array.Empty<Collider>())if(ground!=null)registeredGround.Add(ground);
        }
        return registeredGround.Contains(collider);
    }
    internal static bool Sample(IronHallEncounter hall,Vector2 point,float referenceHeight,RaycastHit[] hits,out SamplePoint sample) {
        sample=default;
        if(hall==null) {sample.Point=new Vector3(point.x,0,point.y);sample.Normal=Vector3.up;return true;}
        var ray=new Ray(new Vector3(point.x,referenceHeight+StepUp+.015f,point.y),Vector3.down);
        int count=Physics.RaycastNonAlloc(ray,hits,StepUp+StepDown+.03f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        float highest=float.NegativeInfinity;
        for(int i=0;i<count;i++) {
            var hit=hits[i];
            if(hit.normal.y<=.5f || !IsGround(hall,hit.collider) || hit.point.y>referenceHeight+StepUp+.005f ||
                hit.point.y<referenceHeight-StepDown-.005f || hit.point.y<=highest)continue;
            highest=hit.point.y;sample.Point=hit.point;sample.Normal=hit.normal;
        }
        return !float.IsNegativeInfinity(highest);
    }
}

// Exact, conservative validation for unchanged receiver geometry. Unsupported or
// large/deforming receivers simply retain the existing per-frame projection path.
internal sealed class MinorShadowReceiverCache
{
    sealed class Receiver {
        public Collider Collider;
        public Matrix4x4 Matrix;
        public Bounds Bounds;
        public Vector3 Center,Size;
        public Mesh Mesh;
        public bool Convex;
        public MeshColliderCookingOptions Cooking;
        public int Submeshes;
        public readonly List<Vector3> Vertices=new List<Vector3>();
        public readonly List<int> Indices=new List<int>();
    }
    sealed class ColliderOrder : IComparer<Collider> {
        public static readonly ColliderOrder Instance=new ColliderOrder();
        public int Compare(Collider a,Collider b) => a.GetInstanceID().CompareTo(b.GetInstanceID());
    }
    // The authored outer-bailey receiver contains 7,618 vertices. Keep exact
    // geometry validation within a bounded budget instead of rejecting it each frame.
    const int MaximumMeshVertices=8192,MaximumMeshIndices=65536;
    readonly Collider[] overlapping=new Collider[128];
    readonly List<Collider> current=new List<Collider>(16);
    readonly List<Receiver> receivers=new List<Receiver>(16);
    readonly List<Vector3> vertices=new List<Vector3>();
    readonly List<int> indices=new List<int>();
    IronHallEncounter hall;
    Collider[] sources;
    bool valid,authored;
    int count;
    Bounds bounds;
    string reason="not-captured";
    Collider rejectedCollider;
    int rejectedVertices;
    bool rejectedReadable;
    internal string Diagnostic => reason+(rejectedCollider!=null ? "; receiver="+rejectedCollider.name+
        "; meshVertices="+rejectedVertices+"; readable="+rejectedReadable : "");
    bool Reject(string value,Collider collider=null,Mesh mesh=null) {
        reason=value;rejectedCollider=collider;rejectedVertices=mesh!=null ? mesh.vertexCount : 0;
        rejectedReadable=mesh!=null && mesh.isReadable;return false;
    }
    public void Invalidate() {valid=false;}
    internal bool CopyFloorFaces(MinorShadowFloor floor) {
        if(!valid)return false;
        for(int i=0;i<count;i++) {
            var r=receivers[i];
            if(r.Collider is BoxCollider box) {
                if(!floor.AddBox(box))return false;
            } else {
                // A cooked convex hull need not have the input mesh's faces.
                if(r.Convex || r.Matrix.determinant<=0)return false;
                for(int j=0;j<r.Indices.Count;j+=3)
                    floor.AddFace(r.Matrix.MultiplyPoint3x4(r.Vertices[r.Indices[j]]),
                        r.Matrix.MultiplyPoint3x4(r.Vertices[r.Indices[j+1]]),
                        r.Matrix.MultiplyPoint3x4(r.Vertices[r.Indices[j+2]]));
            }
        }
        return true;
    }
    public void Capture(IronHallEncounter binding,Bounds area) {
        hall=binding;sources=binding!=null ? binding.GroundColliders : null;
        authored=binding!=null && binding.UseAuthoredLayout;bounds=area;valid=false;
        if(!Gather(binding))return;
        count=current.Count;
        while(receivers.Count<count)receivers.Add(new Receiver());
        for(int i=0;i<count;i++)if(!Read(current[i],receivers[i],true))return;
        valid=true;reason="ready";rejectedCollider=null;
    }
    public bool Unchanged(IronHallEncounter binding) {
        if(!valid)return false;
        if(binding!=hall || binding!=null &&
            (binding.UseAuthoredLayout!=authored || !ReferenceEquals(binding.GroundColliders,sources)))return Reject("registry-changed");
        if(!Gather(binding))return false;
        if(current.Count!=count)return Reject("receiver-set-changed");
        for(int i=0;i<count;i++)if(!Read(current[i],receivers[i],false))return false;
        return true;
    }
    bool Gather(IronHallEncounter binding) {
        current.Clear();if(binding==null)return true;
        int found=Physics.OverlapBoxNonAlloc(bounds.center,bounds.extents,overlapping,Quaternion.identity,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(found==overlapping.Length)return Reject("overlap-buffer-full");
        for(int i=0;i<found;i++)if(MinorShadowGround.IsGround(binding,overlapping[i]))current.Add(overlapping[i]);
        current.Sort(ColliderOrder.Instance);return true;
    }
    bool Read(Collider collider,Receiver previous,bool capture) {
        Matrix4x4 matrix=collider.transform.localToWorldMatrix;Bounds shapeBounds=collider.bounds;
        if(!capture && (collider!=previous.Collider || !matrix.Equals(previous.Matrix) || !shapeBounds.Equals(previous.Bounds)))return Reject("receiver-transform-or-bounds-changed",collider);
        if(collider is BoxCollider box) {
            if(!capture && (!box.center.Equals(previous.Center) || !box.size.Equals(previous.Size)))return Reject("box-shape-changed",collider);
            if(capture) {previous.Center=box.center;previous.Size=box.size;}
        } else if(collider is MeshCollider meshCollider) {
            var mesh=meshCollider.sharedMesh;
            if(mesh==null)return Reject("missing-mesh",collider);
            if(!mesh.isReadable)return Reject("unreadable-mesh",collider,mesh);
            if(mesh.vertexCount>MaximumMeshVertices)return Reject("large-mesh",collider,mesh);
            int total=0;
            for(int sub=0;sub<mesh.subMeshCount;sub++) {
                if(mesh.GetTopology(sub)!=MeshTopology.Triangles)return Reject("nontriangle-mesh",collider,mesh);
                total+=(int)mesh.GetIndexCount(sub);
            }
            if(total>MaximumMeshIndices)return Reject("large-index-buffer",collider,mesh);
            if(!capture && (mesh!=previous.Mesh || meshCollider.convex!=previous.Convex || meshCollider.cookingOptions!=previous.Cooking ||
                mesh.subMeshCount!=previous.Submeshes))return Reject("mesh-reference-or-settings-changed",collider,mesh);
            mesh.GetVertices(vertices);
            if(!capture && vertices.Count!=previous.Vertices.Count)return Reject("mesh-vertices-changed",collider,mesh);
            for(int i=0;!capture && i<vertices.Count;i++)if(!vertices[i].Equals(previous.Vertices[i]))return Reject("mesh-vertices-changed",collider,mesh);
            if(capture) {previous.Vertices.Clear();previous.Vertices.AddRange(vertices);previous.Indices.Clear();}
            int offset=0;
            for(int sub=0;sub<mesh.subMeshCount;sub++) {
                mesh.GetTriangles(indices,sub);
                if(capture)previous.Indices.AddRange(indices);
                else {
                    if(offset+indices.Count>previous.Indices.Count)return Reject("mesh-indices-changed",collider,mesh);
                    for(int i=0;i<indices.Count;i++)if(indices[i]!=previous.Indices[offset+i])return Reject("mesh-indices-changed",collider,mesh);
                }
                offset+=indices.Count;
            }
            if(!capture && offset!=previous.Indices.Count)return Reject("mesh-indices-changed",collider,mesh);
            if(capture) {previous.Mesh=mesh;previous.Submeshes=mesh.subMeshCount;previous.Convex=meshCollider.convex;previous.Cooking=meshCollider.cookingOptions;}
        } else return Reject("unsupported-collider",collider);
        if(capture) {previous.Collider=collider;previous.Matrix=matrix;previous.Bounds=shapeBounds;}
        return true;
    }
}

// A convex silhouette is cut into small receiver patches, then conformed to the
// physical treads. It never draws a ramp through stairs or fills a missing floor.
internal sealed class MinorShadowSurface
{
    const float Cell=.2f,PlaneTolerance=.012f;
    const int RefineDepth=3;
    struct PolygonRange { public int Start,Count; }
    struct CellSample { public bool Connected,Attempted;public MinorShadowGround.SamplePoint Ground; }
    readonly ShadowLab encounter;
    readonly List<Vector2> outlines=new List<Vector2>(128);
    readonly List<PolygonRange> polygons=new List<PolygonRange>(4);
    readonly List<Vector2>[] clipped=new List<Vector2>[RefineDepth+1];
    readonly List<Vector2>[] scratch=new List<Vector2>[RefineDepth+1];
    readonly RaycastHit[] hits=new RaycastHit[64];
    readonly List<Vector2> previousOutline=new List<Vector2>(128);
    readonly List<PolygonRange> previousPolygons=new List<PolygonRange>(4);
    readonly MinorShadowReceiverCache receivers=new MinorShadowReceiverCache();
    Vector3 previousFoot;
    readonly MinorShadowFloor terrain=new MinorShadowFloor();
    Matrix4x4 previousMatrix;
    bool previousInput,admitCache;
    int firstInputs,changedFoot,changedTransform,changedOutline,rejectedReceivers;
    internal string CacheDiagnostic => "first="+firstInputs+"; foot="+changedFoot+"; transform="+changedTransform+
        "; outline="+changedOutline+"; receivers="+rejectedReceivers+"; lastReceiver="+receivers.Diagnostic+"; floor="+terrain.Diagnostic;
    CellSample[] cells=new CellSample[256];
    int[] queue=new int[256];
    int width,height,minX,minZ;
    Vector3 foot;
    bool hasFoot;
    IronHallEncounter hall;
    Transform localSpace;
    List<Vector3> vertices;
    List<int> triangles;

    public MinorShadowSurface(ShadowLab encounter) {
        this.encounter=encounter;
        for(int i=0;i<clipped.Length;i++) {clipped[i]=new List<Vector2>(72);scratch[i]=new List<Vector2>(72);}
    }
    public void Clear() {outlines.Clear();polygons.Clear();hasFoot=false;}
    public void SetFoot(Vector3 value) {if(!hasFoot) {foot=value;hasFoot=true;}}
    public void Add(IList<Vector2> points) {
        if(points.Count<3)return;
        polygons.Add(new PolygonRange {Start=outlines.Count,Count=points.Count});
        for(int i=0;i<points.Count;i++)outlines.Add(points[i]);
    }
    Vector3 InputFoot(Transform transform) => hasFoot ? foot : transform.parent!=null ? transform.parent.position : transform.position;
    public bool CanReuse(Transform transform) {
        admitCache=false;
        if(!previousInput) {firstInputs++;return false;}
        if(!previousFoot.Equals(InputFoot(transform))) {changedFoot++;return false;}
        if(!previousMatrix.Equals(transform.localToWorldMatrix)) {changedTransform++;return false;}
        if(outlines.Count!=previousOutline.Count || polygons.Count!=previousPolygons.Count) {changedOutline++;return false;}
        for(int i=0;i<outlines.Count;i++)if(!outlines[i].Equals(previousOutline[i])) {changedOutline++;return false;}
        for(int i=0;i<polygons.Count;i++)if(polygons[i].Start!=previousPolygons[i].Start || polygons[i].Count!=previousPolygons[i].Count) {changedOutline++;return false;}
        // Moving/bobbing silhouettes never enter the ground-cache path. The first
        // repeated frame builds once more to capture a reliable receiver snapshot.
        admitCache=true;
        var binding=encounter!=null ? encounter.GetComponent<IronHallEncounter>() : null;
        bool reusable=receivers.Unchanged(binding);if(!reusable)rejectedReceivers++;return reusable;
    }
    public void Remember(Transform transform,List<Vector3> output) {
        previousInput=true;previousFoot=InputFoot(transform);previousMatrix=transform.localToWorldMatrix;
        previousOutline.Clear();previousOutline.AddRange(outlines);previousPolygons.Clear();previousPolygons.AddRange(polygons);
        receivers.Invalidate();
        if(!admitCache || output.Count==0)return;
        Bounds bounds=new Bounds(previousFoot,Vector3.zero);
        foreach(var point in outlines)bounds.Encapsulate(new Vector3(point.x,previousFoot.y,point.y));
        foreach(var vertex in output)bounds.Encapsulate(transform.TransformPoint(vertex));
        bounds.Expand(new Vector3(Cell*2,MinorShadowGround.StepUp+MinorShadowGround.StepDown+Cell*2,Cell*2));
        receivers.Capture(encounter!=null ? encounter.GetComponent<IronHallEncounter>() : null,bounds);
    }
    public void Build(Transform transform,List<Vector3> destination,List<int> indices) {
        localSpace=transform;vertices=destination;triangles=indices;
        if(polygons.Count==0)return;
        if(!hasFoot)foot=transform.parent!=null ? transform.parent.position : transform.position;
        hall=encounter!=null ? encounter.GetComponent<IronHallEncounter>() : null;
        if(hall==null) {
            foreach(var polygon in polygons) {
                var points=clipped[0];points.Clear();
                for(int i=0;i<polygon.Count;i++)points.Add(outlines[polygon.Start+i]);
                Emit(points,new MinorShadowGround.SamplePoint {Point=Vector3.zero,Normal=Vector3.up});
            }
            return;
        }
        Vector2 min=new Vector2(foot.x,foot.z),max=min;
        foreach(var point in outlines) {min=Vector2.Min(min,point);max=Vector2.Max(max,point);}
        minX=Mathf.FloorToInt(min.x/Cell);minZ=Mathf.FloorToInt(min.y/Cell);
        width=Mathf.FloorToInt(max.x/Cell)-minX+1;height=Mathf.FloorToInt(max.y/Cell)-minZ+1;
        int count=width*height;
        // Authoring mistakes must not turn a single shadow into an unbounded ground scan.
        if(count<=0 || count>8192)return;
        terrain.Prepare(hall,min,max,foot.y);
        if(!terrain.Sample(hall,new Vector2(foot.x,foot.z),foot.y,hits,out var anchor))return;
        if(terrain.ContinuousPlane(min,max,anchor)) {
            foreach(var polygon in polygons) {
                var points=clipped[0];points.Clear();
                for(int i=0;i<polygon.Count;i++)points.Add(outlines[polygon.Start+i]);
                Emit(points,anchor);
            }
            return;
        }
        if(cells.Length<count) {cells=new CellSample[Mathf.NextPowerOfTwo(count)];queue=new int[cells.Length];}
        System.Array.Clear(cells,0,count);
        int startX=Mathf.Clamp(Mathf.FloorToInt(foot.x/Cell)-minX,0,width-1);
        int startZ=Mathf.Clamp(Mathf.FloorToInt(foot.z/Cell)-minZ,0,height-1);
        int start=startX+startZ*width,read=0,write=0;
        cells[start]=new CellSample {Connected=true,Attempted=true,Ground=anchor};queue[write++]=start;
        while(read<write) {
            int current=queue[read++],x=current%width,z=current/width;
            Visit(x-1,z,current,ref write);Visit(x+1,z,current,ref write);
            Visit(x,z-1,current,ref write);Visit(x,z+1,current,ref write);
        }
        for(int z=0;z<height;z++)for(int x=0;x<width;x++) {
            int index=x+z*width;var node=cells[index];
            if(!node.Connected && !NeighbourGround(x,z,out node.Ground))continue;
            float left=(minX+x)*Cell,bottom=(minZ+z)*Cell;
            foreach(var polygon in polygons)Patch(polygon,left,bottom,Cell,node.Ground.Point.y,0);
        }
    }
    void Visit(int x,int z,int from,ref int write) {
        if(x<0 || x>=width || z<0 || z>=height)return;
        int index=x+z*width;if(cells[index].Connected || cells[index].Attempted)return;
        cells[index].Attempted=true;
        var point=new Vector2((minX+x+.5f)*Cell,(minZ+z+.5f)*Cell);
        if(!terrain.Sample(hall,point,cells[from].Ground.Point.y,hits,out var ground))return;
        cells[index].Connected=true;cells[index].Ground=ground;queue[write++]=index;
    }
    bool NeighbourGround(int x,int z,out MinorShadowGround.SamplePoint ground) {
        int index=x+z*width;
        if(x>0 && cells[index-1].Connected) {ground=cells[index-1].Ground;return true;}
        if(x+1<width && cells[index+1].Connected) {ground=cells[index+1].Ground;return true;}
        if(z>0 && cells[index-width].Connected) {ground=cells[index-width].Ground;return true;}
        if(z+1<height && cells[index+width].Connected) {ground=cells[index+width].Ground;return true;}
        ground=default;return false;
    }
    void Patch(PolygonRange polygon,float left,float bottom,float size,float reference,int depth) {
        var points=clipped[depth];var work=scratch[depth];points.Clear();
        for(int i=0;i<polygon.Count;i++)points.Add(outlines[polygon.Start+i]);
        Clip(points,work,0,left,true);Clip(work,points,0,left+size,false);
        Clip(points,work,1,bottom,true);Clip(work,points,1,bottom+size,false);
        if(points.Count<3)return;
        Vector2 center=Vector2.zero;
        foreach(var point in points)center+=point;
        center/=points.Count;
        bool found=terrain.Sample(hall,center,reference,hits,out var ground);
        bool conforms=found;
        if(found)for(int i=0;i<points.Count && conforms;i++) {
            conforms=Conforms(points[i],ground) && Conforms((points[i]+points[(i+1)%points.Count])*.5f,ground);
        }
        if(conforms) {Emit(points,ground);return;}
        if(depth>=RefineDepth)return;
        float half=size*.5f,nextReference=found ? ground.Point.y : reference;
        Patch(polygon,left,bottom,half,nextReference,depth+1);
        Patch(polygon,left+half,bottom,half,nextReference,depth+1);
        Patch(polygon,left,bottom+half,half,nextReference,depth+1);
        Patch(polygon,left+half,bottom+half,half,nextReference,depth+1);
    }
    bool Conforms(Vector2 point,MinorShadowGround.SamplePoint plane) {
        float y=PlaneY(point,plane);
        return terrain.Sample(hall,point,plane.Point.y,hits,out var actual) &&
            Mathf.Abs(actual.Point.y-y)<=PlaneTolerance && Vector3.Dot(actual.Normal,plane.Normal)>.985f;
    }
    static float PlaneY(Vector2 point,MinorShadowGround.SamplePoint plane) => plane.Point.y-
        ((point.x-plane.Point.x)*plane.Normal.x+(point.y-plane.Point.z)*plane.Normal.z)/plane.Normal.y;
    void Emit(List<Vector2> points,MinorShadowGround.SamplePoint plane) {
        int start=vertices.Count;
        foreach(var point in points)
            vertices.Add(localSpace.InverseTransformPoint(new Vector3(point.x,PlaneY(point,plane)+MinorShadowGround.Lift,point.y)));
        for(int i=1;i+1<points.Count;i++) {
            var a=points[0];var b=points[i];var c=points[i+1];
            float area=Mathf.Abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x));
            float precision=Mathf.Max(Mathf.Abs(a.x),Mathf.Abs(a.y))*4e-7f;
            float edgeSquared=Mathf.Max((b-a).sqrMagnitude,Mathf.Max((c-a).sqrMagnitude,(c-b).sqrMagnitude));
            if(area<.000002f || area*area<precision*precision*edgeSquared)continue;
            triangles.Add(start);triangles.Add(start+i);triangles.Add(start+i+1);
        }
    }
    static void Clip(List<Vector2> input,List<Vector2> output,int axis,float edge,bool greater) {
        output.Clear();if(input.Count==0)return;
        Vector2 previous=input[input.Count-1];float before=(axis==0 ? previous.x : previous.y)-edge;
        bool previousInside=greater ? before>=0 : before<=0;
        foreach(var current in input) {
            float after=(axis==0 ? current.x : current.y)-edge;bool inside=greater ? after>=0 : after<=0;
            if(inside!=previousInside)output.Add(Vector2.LerpUnclamped(previous,current,before/(before-after)));
            if(inside)output.Add(current);
            previous=current;before=after;previousInside=inside;
        }
    }
}
