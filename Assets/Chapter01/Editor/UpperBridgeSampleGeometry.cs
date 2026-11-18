using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chapter01.Editor
{
    // Bounded sample authoring only: original FBX files and scene objects are untouched.
    public static class UpperBridgeSampleGeometry
    {
        const string Folder = "Assets/Chapter01/UpperBridgeSample/Meshes";
        static readonly float[] Widths = { 6.5f, 7, 7, 7, 9 };
        static readonly HashSet<Vector3Int> PostPositions = new HashSet<Vector3Int>();
        public static Vector3[] LastWalkPath { get; private set; } = Array.Empty<Vector3>();
        public static SectionInfo[] LastSections { get; private set; } = Array.Empty<SectionInfo>();
        [Serializable] public sealed class SectionInfo
        {
            public int segment, steps;
            public Vector3 from, to;
            public float width;
        }
        sealed class Corner
        {
            public Vector3 center, incoming, outgoing, inL, inR, outL, outR, miterL, miterR, inDirection, outDirection;
        }
        sealed class Geo
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int>[] indices = Enumerable.Range(0,5).Select(_=>new List<int>()).ToArray();
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 desired, int material)
            {
                var n=Vector3.Cross(b-a,c-a);
                if(n.sqrMagnitude<1e-12f)return;
                if(Vector3.Dot(n,desired)<0){var q=b;b=c;c=q;n=-n;}
                n.Normalize();int first=vertices.Count;
                var tangent=Vector3.Cross(Vector3.up,n).normalized;
                foreach(var p in new[]{a,b,c})
                {
                    vertices.Add(p);normals.Add(n);
                    uv.Add(Mathf.Abs(n.y)>.85f?new Vector2(p.x,p.z):new Vector2(Vector3.Dot(p,tangent),p.y));
                }
                indices[material].AddRange(new[]{first,first+1,first+2});
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal,int material)
            {Triangle(a,b,c,normal,material);Triangle(a,c,d,normal,material);}
            public Mesh Mesh(string name)
            {
                var mesh=new Mesh{name=name,indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.subMeshCount=5;
                for(int i=0;i<5;i++)mesh.SetTriangles(indices[i],i,false);
                mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
            }
        }

        public static GameObject Build(Vector3[] route, Material deck,Material stone,Material trim,Material iron,Material metalAccent)
        {
            if(route==null||route.Length<32)throw new ArgumentException("Upper bridge needs Unity-space route nodes 26 through 31.");
            if(new[]{deck,stone,trim,iron,metalAccent}.Any(x=>x==null))throw new ArgumentException("All five sample materials are required.");
            Directory.CreateDirectory(Folder);
            var root=new GameObject("UpperBridgeSample");
            var materials=new[]{deck,stone,trim,iron,metalAccent};
            var nodes=route.Skip(26).Take(6).ToArray();
            var corners=new Corner[6];
            for(int i=1;i<5;i++)corners[i]=MakeCorner(nodes[i-1],nodes[i],nodes[i+1],Widths[i-1],Widths[i]);
            corners[0]=MakeTerminal(route[25],nodes[0],nodes[1],6.5f,Widths[0],true);
            corners[5]=MakeTerminal(nodes[4],nodes[5],route[32],Widths[4],9,false);
            PostPositions.Clear();
            var walk=new List<Vector3>{nodes[0]};var sections=new List<SectionInfo>();
            try
            {
                BuildCorner(root.transform,corners[0],26,materials,walk,true,false);
                TerminalRailExtensions(root.transform,corners[0],route[25],nodes[1],true,materials);
                for(int i=0;i<5;i++)
                {
                    var a=corners[i].outgoing;
                    var b=corners[i+1].incoming;
                    var direction=Horizontal(b-a).normalized;float length=Horizontal(b-a).magnitude;
                    bool stair=i==0||i==4;int count=stair?Mathf.CeilToInt(Mathf.Abs(b.y-a.y)/.2f):0;
                    if(length<2f||stair&&(length-.6f)/Mathf.Max(count,1)<.22f)
                        throw new InvalidOperationException("Platform leaves insufficient stair tread depth on segment "+(26+i));
                    var visual=new Geo();var collision=new Geo();
                    BuildFlight(visual,collision,a,b,Widths[i],count,walk);
                    var side=Vector3.Cross(Vector3.up,direction)*Widths[i]*.5f;
                    RailEdge(visual,root.transform,a+side,b+side,"S"+(26+i)+"_L");
                    RailEdge(visual,root.transform,a-side,b-side,"S"+(26+i)+"_R");
                    SaveVisual(root.transform,visual,"WalkSurface_UB_Segment_"+(26+i),materials);
                    SaveGround(root.transform,collision,"Collision__UB_Segment_"+(26+i));
                    sections.Add(new SectionInfo{segment=26+i,from=a,to=b,width=Widths[i],steps=count});
                    if(i<4)
                    {
                        BuildCorner(root.transform,corners[i+1],27+i,materials,walk,false,false);
                    }
                }
                BuildCorner(root.transform,corners[5],31,materials,walk,false,true);
                TerminalRailExtensions(root.transform,corners[5],nodes[4],route[32],false,materials);
                LastWalkPath=walk.ToArray();LastSections=sections.ToArray();
                AssetDatabase.SaveAssets();
                return root;
            }
            catch {UnityEngine.Object.DestroyImmediate(root);throw;}
        }

        static Vector3 Horizontal(Vector3 v)=>new Vector3(v.x,0,v.z);
        static Vector3 AtHeight(Vector3 p,float h)=>new Vector3(p.x,h,p.z);
        static void AddPath(List<Vector3> path,Vector3 p)
        {if(path.Count==0||(path[path.Count-1]-p).sqrMagnitude>.0001f)path.Add(p);}
        static Corner MakeCorner(Vector3 previous,Vector3 p,Vector3 next,float incomingWidth,float outgoingWidth)
        {
            var a=Horizontal(p-previous).normalized;var b=Horizontal(next-p).normalized;
            var na=Vector3.Cross(Vector3.up,a);var nb=Vector3.Cross(Vector3.up,b);
            var left=LineIntersection(p+na*incomingWidth*.5f,a,p+nb*outgoingWidth*.5f,b);
            var right=LineIntersection(p-na*incomingWidth*.5f,a,p-nb*outgoingWidth*.5f,b);
            float cutIn=Mathf.Max(1.05f,Mathf.Max(Mathf.Abs(Vector3.Dot(left-p,a)),Mathf.Abs(Vector3.Dot(right-p,a)))+.45f);
            float cutOut=Mathf.Max(1.05f,Mathf.Max(Mathf.Abs(Vector3.Dot(left-p,b)),Mathf.Abs(Vector3.Dot(right-p,b)))+.45f);
            if(cutIn>Horizontal(p-previous).magnitude*.35f||cutOut>Horizontal(next-p).magnitude*.35f)
                throw new InvalidOperationException("Corner miter exceeds bounded platform allowance at "+p);
            var ci=p-a*cutIn;var co=p+b*cutOut;
            return new Corner{center=p,incoming=ci,outgoing=co,miterL=left,miterR=right,inDirection=a,outDirection=b,
                inL=ci+na*incomingWidth*.5f,inR=ci-na*incomingWidth*.5f,outL=co+nb*outgoingWidth*.5f,outR=co-nb*outgoingWidth*.5f};
        }
        static Corner MakeTerminal(Vector3 previous,Vector3 p,Vector3 next,float incomingWidth,float outgoingWidth,bool start)
        {
            var c=MakeCorner(previous,p,next,incomingWidth,outgoingWidth);
            var a=Horizontal(p-previous).normalized;var b=Horizontal(next-p).normalized;
            var na=Vector3.Cross(Vector3.up,a);var nb=Vector3.Cross(Vector3.up,b);
            if(start)
            {
                float cut=Mathf.Max(Horizontal(c.outgoing-p).magnitude,Mathf.Abs(Vector3.Dot(na*incomingWidth*.5f,b))+.45f);
                c.incoming=p;c.inL=p+na*incomingWidth*.5f;c.inR=p-na*incomingWidth*.5f;
                c.outgoing=p+b*cut;c.outL=c.outgoing+nb*outgoingWidth*.5f;c.outR=c.outgoing-nb*outgoingWidth*.5f;
            }
            else
            {
                float cut=Mathf.Max(Horizontal(c.incoming-p).magnitude,Mathf.Abs(Vector3.Dot(nb*outgoingWidth*.5f,a))+.45f);
                c.outgoing=p;c.outL=p+nb*outgoingWidth*.5f;c.outR=p-nb*outgoingWidth*.5f;
                c.incoming=p-a*cut;c.inL=c.incoming+na*incomingWidth*.5f;c.inR=c.incoming-na*incomingWidth*.5f;
            }
            return c;
        }
        static void BuildCorner(Transform root,Corner c,int node,Material[] materials,List<Vector3> walk,bool start,bool end)
        {
            var v=new Geo();var g=new Geo();
            Platform(v,g,new[]{c.inL,c.miterL,c.outL,c.outR,c.miterR,c.inR},c.center);
            var incoming=c.inDirection;var outgoing=c.outDirection;
            // At a terminal the short re-entrant edge touches retained walkable
            // pavement. It is an internal seam, not a place for a blocking rail.
            if(!start||Vector3.Dot(c.miterL-c.center,incoming)>=0)RailEdge(v,root,c.inL,c.miterL,"N"+node+"_L0");
            if(!end||Vector3.Dot(c.miterL-c.center,outgoing)<=0)RailEdge(v,root,c.miterL,c.outL,"N"+node+"_L1");
            if(!start||Vector3.Dot(c.miterR-c.center,incoming)>=0)RailEdge(v,root,c.inR,c.miterR,"N"+node+"_R0");
            if(!end||Vector3.Dot(c.miterR-c.center,outgoing)<=0)RailEdge(v,root,c.miterR,c.outR,"N"+node+"_R1");
            SaveVisual(root,v,"WalkSurface_UB_Platform_"+node,materials);
            SaveGround(root,g,"Collision__UB_Platform_"+node);
            AddPath(walk,c.incoming);AddPath(walk,c.center);AddPath(walk,c.outgoing);
        }
        static void TerminalRailExtensions(Transform root,Corner c,Vector3 previous,Vector3 next,bool start,Material[] materials)
        {
            var geo=new Geo();var direction=Horizontal(start?c.center-previous:next-c.center).normalized;
            var side=Vector3.Cross(Vector3.up,direction);float width=start?6.5f:9f;
            float rise=start?0:(next.y-c.center.y)*2.05f/Horizontal(next-c.center).magnitude;
            var far=c.center+direction*(start?-2.05f:2.05f)+Vector3.up*rise;
            for(int sign=-1;sign<=1;sign+=2)
            {
                var miter=sign==1?c.miterL:c.miterR;
                var near=Vector3.Dot(miter-c.center,direction)*(start?-1:1)>0?miter:c.center+side*width*.5f*sign;
                var away=far+side*width*.5f*sign;
                RailEdge(geo,root,start?away:near,start?near:away,"Terminal"+(start?26:31)+"_"+sign);
            }
            SaveVisual(root,geo,"UB_RailExtension_"+(start?26:31),materials);
        }
        static Vector3 LineIntersection(Vector3 a,Vector3 d,Vector3 b,Vector3 e)
        {
            float cross=d.x*e.z-d.z*e.x;
            if(Mathf.Abs(cross)<.0001f)return(a+b)*.5f;
            var delta=b-a;float t=(delta.x*e.z-delta.z*e.x)/cross;
            return a+d*t;
        }
        static void BuildFlight(Geo visual,Geo collision,Vector3 a,Vector3 b,float width,int steps,List<Vector3> walk)
        {
            var d=Horizontal(b-a).normalized;var n=Vector3.Cross(Vector3.up,d)*width*.5f;
            float length=Horizontal(b-a).magnitude;
            var boundaries=new List<float>{0};var heights=new List<float>();
            if(steps==0){boundaries.Add(length);heights.Add(a.y);}
            else
            {
                const float entry=.32f;float run=(length-.64f)/steps;
                boundaries.Add(entry);heights.Add(a.y);
                for(int s=0;s<steps;s++){boundaries.Add(s==steps-1?length:entry+(s+1)*run);heights.Add(Mathf.Lerp(a.y,b.y,(s+1f)/steps));}
            }
            AddPath(walk,a);
            for(int k=0;k<heights.Count;k++)
            {
                float x=boundaries[k],z=boundaries[k+1];float h0=steps==0?a.y:heights[k],h1=steps==0?b.y:heights[k];
                var p=AtHeight(a+d*x,h0);var q=AtHeight(a+d*z,h1);
                // A restrained 2 cm stone nosing bevel keeps the step silhouette legible.
                float bevel=steps>0&&k>0?Mathf.Min(.025f,(z-x)*.12f):0;
                var pInset=p+d*bevel;
                visual.Quad(pInset+n,q+n,q-n,pInset-n,Vector3.up,0);
                if(bevel>0)visual.Quad(p+n-Vector3.up*bevel,pInset+n,pInset-n,p-n-Vector3.up*bevel,Vector3.up-d,2);
                collision.Quad(p+n,q+n,q-n,p-n,Vector3.up,0);
                if(k>0)
                {
                    float old=heights[k-1];var low=AtHeight(p,old);
                    visual.Quad(low+n,p+n-Vector3.up*bevel,p-n-Vector3.up*bevel,low-n,-d,1);
                    collision.Quad(low+n,p+n,p-n,low-n,-d,0);
                }
                var bottomP=AtHeight(p,Mathf.Lerp(a.y,b.y,x/length)-.7f);
                var bottomQ=AtHeight(q,Mathf.Lerp(a.y,b.y,z/length)-.7f);
                foreach(var g in new[]{visual,collision})
                {
                    int material=g==visual?1:0;
                    g.Quad(p+n,bottomP+n,bottomQ+n,q+n,n,material);
                    g.Quad(p-n,q-n,bottomQ-n,bottomP-n,-n,material);
                    g.Quad(bottomP+n,bottomP-n,bottomQ-n,bottomQ+n,Vector3.down,material);
                }
                if(steps>0)AddPath(walk,AtHeight((p+q)*.5f,h0));
            }
            foreach(var g in new[]{visual,collision})
            {
                int mat=g==visual?1:0;
                g.Quad(a+n,a-n,a-n-Vector3.up*.7f,a+n-Vector3.up*.7f,-d,mat);
                g.Quad(b+n,b+n-Vector3.up*.7f,b-n-Vector3.up*.7f,b-n,d,mat);
            }
            AddPath(walk,b);
        }
        static void Platform(Geo visual,Geo collision,Vector3[] polygon,Vector3 center)
        {
            // Ear clipping also handles terminal re-entrant corners. A centre fan
            // would cover the retained old road with duplicate coplanar triangles.
            var triangles=Triangulate(polygon);
            for(int i=0;i<triangles.Count;i+=3)
            foreach(var g in new[]{visual,collision})
            {
                var a=polygon[triangles[i]];var b=polygon[triangles[i+1]];var c=polygon[triangles[i+2]];
                g.Triangle(a,b,c,Vector3.up,0);
                g.Triangle(a-Vector3.up*.7f,b-Vector3.up*.7f,c-Vector3.up*.7f,Vector3.down,g==visual?1:0);
            }
            for(int i=0;i<polygon.Length;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Length];
                var outward=Horizontal((a+b)*.5f-center).normalized;
                foreach(var g in new[]{visual,collision})
                {
                    g.Quad(a,b,b-Vector3.up*.7f,a-Vector3.up*.7f,outward,g==visual?1:0);
                }
            }
        }
        static List<int> Triangulate(Vector3[] polygon)
        {
            float area=0;for(int i=0;i<polygon.Length;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Length];area+=a.x*b.z-b.x*a.z;}
            float sign=Mathf.Sign(area);var remaining=Enumerable.Range(0,polygon.Length).ToList();var result=new List<int>();
            while(remaining.Count>3)
            {
                bool found=false;
                for(int i=0;i<remaining.Count;i++)
                {
                    int ia=remaining[(i+remaining.Count-1)%remaining.Count],ib=remaining[i],ic=remaining[(i+1)%remaining.Count];
                    var a=polygon[ia];var b=polygon[ib];var c=polygon[ic];
                    if(CrossXZ(b-a,c-b)*sign<.00001f)continue;
                    bool contains=remaining.Any(k=>k!=ia&&k!=ib&&k!=ic&&InsideTriangle(polygon[k],a,b,c,sign));
                    if(contains)continue;
                    result.AddRange(new[]{ia,ib,ic});remaining.RemoveAt(i);found=true;break;
                }
                if(!found)throw new InvalidOperationException("Invalid terminal platform polygon: cannot triangulate safely.");
            }
            result.AddRange(remaining);return result;
        }
        static float CrossXZ(Vector3 a,Vector3 b)=>a.x*b.z-a.z*b.x;
        static bool InsideTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c,float sign)=>
            CrossXZ(b-a,p-a)*sign>=-.00001f&&CrossXZ(c-b,p-b)*sign>=-.00001f&&CrossXZ(a-c,p-c)*sign>=-.00001f;

        static void RailEdge(Geo geo,Transform root,Vector3 a,Vector3 b,string key)
        {
            float length=Horizontal(b-a).magnitude;if(length<.05f)return;
            var forward=Horizontal(b-a).normalized;var side=Vector3.Cross(Vector3.up,forward);
            var solid=new Geo();
            // Two stone courses, a narrow pale coping and a dark Gothic iron fence.
            Beam(geo,a-Vector3.up*.12f,b-Vector3.up*.12f,.36f,.46f,1,null,null);
            Beam(geo,a+Vector3.up*.19f,b+Vector3.up*.19f,.42f,.12f,2,null,null);
            Beam(geo,a-Vector3.up*.32f,b-Vector3.up*.32f,.43f,.10f,2,null,null);
            Beam(solid,a-Vector3.up*.12f,b-Vector3.up*.12f,.36f,.46f,0,null,null);
            Beam(solid,a+Vector3.up*.19f,b+Vector3.up*.19f,.42f,.12f,0,null,null);
            Beam(solid,a-Vector3.up*.32f,b-Vector3.up*.32f,.43f,.10f,0,null,null);
            Barrier(root,a,b,side,"UB_Obstacle_PlayerBarrier_"+key);
            int panels=Mathf.Max(1,Mathf.CeilToInt(length/2.15f));
            for(int i=0;i<=panels;i++)
            {
                var p=Vector3.Lerp(a,b,(float)i/panels);
                var positionKey=new Vector3Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.y*1000),Mathf.RoundToInt(p.z*1000));
                if(!PostPositions.Add(positionKey))continue;
                Beam(geo,p+Vector3.up*.23f,p+Vector3.up*1.37f,.105f,.105f,3,root,"UB_Obstacle_RailPost_"+key+"_"+i);
                Beam(geo,p+Vector3.up*.29f,p+Vector3.up*.40f,.16f,.16f,4,null,null);
                Beam(geo,p+Vector3.up*1.21f,p+Vector3.up*1.29f,.15f,.15f,4,null,null);
                Diamond(geo,p+Vector3.up*1.39f,side,forward,.105f,.17f);
            }
            Beam(geo,a+Vector3.up*1.23f,b+Vector3.up*1.23f,.065f,.075f,3,null,null,solid);
            Beam(geo,a+Vector3.up*.42f,b+Vector3.up*.42f,.045f,.045f,3,null,null,solid);
            for(int panel=0;panel<panels;panel++)
            {
                var p=Vector3.Lerp(a,b,(float)panel/panels);var q=Vector3.Lerp(a,b,(panel+1f)/panels);
                for(int j=1;j<=3;j++)
                {
                    var x=Vector3.Lerp(p,q,j/4f);
                    Beam(geo,x+Vector3.up*.42f,x+Vector3.up*(j==2?1.15f:1.05f),.033f,.033f,3,null,null,solid);
                }
                float[] fractions={.08f,.12f,.25f,.5f,.75f,.88f,.92f};
                float[] heights={.45f,.77f,1.00f,1.19f,1.00f,.77f,.45f};
                for(int j=0;j<fractions.Length-1;j++)
                    Beam(geo,Vector3.Lerp(p,q,fractions[j])+Vector3.up*heights[j],Vector3.Lerp(p,q,fractions[j+1])+Vector3.up*heights[j+1],.033f,.033f,3,
                        null,null,solid);
            }
            SaveGround(root,solid,"UB_Obstacle_RailMesh_"+key);
        }
        static void Beam(Geo g,Vector3 a,Vector3 b,float width,float depth,int material,Transform colliderRoot,string name,Geo exactCollider=null)
        {
            var dir=b-a;float length=dir.magnitude;if(length<.001f)return;dir/=length;
            var side=Vector3.Cross(dir,Vector3.up);
            if(side.sqrMagnitude<.001f)side=Vector3.right;else side.Normalize();
            var other=Vector3.Cross(side,dir).normalized;
            var x=side*width*.5f;var y=other*depth*.5f;
            Vector3[] pa={a-x-y,a+x-y,a+x+y,a-x+y};
            Vector3[] pb={b-x-y,b+x-y,b+x+y,b-x+y};
            for(int i=0;i<4;i++){int j=(i+1)%4;g.Quad(pa[i],pb[i],pb[j],pa[j],(pa[i]+pa[j])*.5f-a,material);}
            g.Quad(pa[0],pa[1],pa[2],pa[3],-dir,material);g.Quad(pb[0],pb[1],pb[2],pb[3],dir,material);
            if(colliderRoot!=null)Box(colliderRoot,name,(a+b)*.5f,Quaternion.LookRotation(other,dir),new Vector3(width,length,depth),0);
            if(exactCollider!=null)Beam(exactCollider,a,b,width,depth,0,null,null);
        }
        static void Diamond(Geo g,Vector3 c,Vector3 side,Vector3 forward,float radius,float height)
        {
            var ring=new[]{c+side*radius,c+forward*radius,c-side*radius,c-forward*radius};
            for(int i=0;i<4;i++)
            {
                var a=ring[i];var b=ring[(i+1)%4];var n=Horizontal((a+b)*.5f-c);
                g.Triangle(a,c+Vector3.up*height,b,n+Vector3.up,4);
                g.Triangle(a,b,c-Vector3.up*.065f,n-Vector3.up,4);
            }
        }
        static void Barrier(Transform root,Vector3 a,Vector3 b,Vector3 side,string name)
        {
            // A world-space prism follows the full sloping edge. A horizontal box
            // spanning a four-metre rise would float above the lower stair treads.
            var g=new Geo();var x=side*.075f;
            var a0=a-Vector3.up*.30f;var b0=b-Vector3.up*.30f;
            var a1=a+Vector3.up*1.60f;var b1=b+Vector3.up*1.60f;
            g.Quad(a0+x,a1+x,b1+x,b0+x,side,0);
            g.Quad(a0-x,b0-x,b1-x,a1-x,-side,0);
            g.Quad(a1+x,a1-x,b1-x,b1+x,Vector3.up,0);
            g.Quad(a0+x,b0+x,b0-x,a0-x,Vector3.down,0);
            g.Quad(a0+x,a0-x,a1-x,a1+x,a-b,0);
            g.Quad(b0+x,b1+x,b1-x,b0-x,b-a,0);
            SaveGround(root,g,name).layer=2;
        }
        static void Box(Transform root,string name,Vector3 center,Quaternion rotation,Vector3 size,int layer)
        {
            var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.SetPositionAndRotation(center,rotation);go.layer=layer;
            go.AddComponent<BoxCollider>().size=size;
        }
        static void SaveVisual(Transform root,Geo geo,string name,Material[] materials)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);
            go.GetComponent<MeshFilter>().sharedMesh=Store(geo.Mesh(name),name);
            var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterials=materials;renderer.shadowCastingMode=ShadowCastingMode.On;
            renderer.receiveShadows=true;renderer.forceMeshLod=-1;
        }
        static GameObject SaveGround(Transform root,Geo geo,string name)
        {
            var go=new GameObject(name);go.transform.SetParent(root,false);
            go.AddComponent<MeshCollider>().sharedMesh=Store(geo.Mesh(name),name);
            return go;
        }
        static Mesh Store(Mesh made,string name)
        {
            string path=Folder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null){made.UploadMeshData(false);AssetDatabase.CreateAsset(made,path);return made;}
            existing.Clear();existing.indexFormat=made.indexFormat;existing.vertices=made.vertices;existing.normals=made.normals;existing.uv=made.uv;existing.tangents=made.tangents;
            existing.subMeshCount=made.subMeshCount;
            for(int i=0;i<made.subMeshCount;i++)existing.SetTriangles(made.GetTriangles(i),i,false);
            existing.bounds=made.bounds;existing.UploadMeshData(false);EditorUtility.SetDirty(existing);UnityEngine.Object.DestroyImmediate(made);return existing;
        }
    }
}
