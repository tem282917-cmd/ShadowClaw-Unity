using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chapter01.Editor
{
    /// <summary>Data-driven castle route authoring; source scenes/FBX and the approved bridge sample remain untouched.</summary>
    public static class CastleWalkwayGeometry
    {
        const string Folder = "Assets/Chapter01/CastleWalkwayUpgrade/Meshes";
        static readonly HashSet<Vector3Int> PostPositions = new HashSet<Vector3Int>();
        public enum SurfaceKind { Stairs, Ramp, Flat, RetainedGround, LandingSpan, MergedIntoPrevious }
        [Serializable] public sealed class SegmentConfig
        {
            public int segment, endSegment;
            public string region;
            public SurfaceKind kind;
            public float width;
            public bool leftRail, rightRail;
            public float railOpeningAtStart, railOpeningAtEnd;
            public string note;
        }
        [Serializable] public sealed class SectionInfo
        {
            public int segment, endSegment, steps;
            public string groupName, region, kind;
            public Vector3 from, to, pivot;
            public float width, usableLength, minimumTread;
            public bool built;
        }
        [Serializable] public sealed class PlatformInfo
        {
            public int firstNode, lastNode;
            public string groupName;
            public Vector3 pivot;
            public Vector3[] polygon;
        }
        public static Vector3[] LastWalkPath { get; private set; } = Array.Empty<Vector3>();
        public static Vector3[] LastExplorationPath { get; private set; } = Array.Empty<Vector3>();
        public static SectionInfo[] LastSections { get; private set; } = Array.Empty<SectionInfo>();
        public static PlatformInfo[] LastPlatforms { get; private set; } = Array.Empty<PlatformInfo>();
        public static SegmentConfig[] LastConfig { get; private set; } = Array.Empty<SegmentConfig>();
        public static int[] ReplacedSegmentIds => (LastConfig.Length==40?LastConfig.Where(c=>c.kind!=SurfaceKind.RetainedGround).Select(c=>c.segment):Enumerable.Range(0,36).Where(i=>i<16||i>18)).ToArray();
        public static int[] ReplacedRailIds => ReplacedSegmentIds;
        public static string[] PreservedSourceClasses => new[]{"C03 bridge arches", "C04 bridge arches", "C05 piers", "C02 shared platforms", "Garden_Continuous_Paving", "Court/Cloister/Chapel floors", "Citadel floors and throne ramp", "Combat gates and triggers"};
        static readonly Regex RouteName=new Regex(@"(?:^|__)Route_(\d+)(?:_|$)");
        static readonly Regex EdgeName=new Regex(@"(?:^|__)Edge_(\d+)(?:_|$)");
        static readonly Regex DeckOverlayName=new Regex(@"^A05__C04_Approved_Deck_(\d+)_");
        public static bool IsReplacedRouteObject(string name, SegmentConfig[] configuration=null)
        {
            if(string.IsNullOrEmpty(name))return false;
            var ids=configuration==null?new HashSet<int>(ReplacedSegmentIds):new HashSet<int>(configuration.Where(c=>c.kind!=SurfaceKind.RetainedGround).Select(c=>c.segment));
            foreach(var pattern in new[]{RouteName,EdgeName,DeckOverlayName})
            {var m=pattern.Match(name);if(m.Success&&int.TryParse(m.Groups[1].Value,out var index)&&ids.Contains(index))return true;}
            return false;
        }
        public static SegmentConfig[] DefaultConfiguration(Vector3[] route)
        {
            if(route==null||route.Length!=41)throw new ArgumentException("Castle route must contain the 41 approved Unity-space nodes.");
            var result=new SegmentConfig[40];
            for(int i=0;i<40;i++)
            {
                float rise=route[i+1].y-route[i].y;
                bool retained=i>=16&&i<=18||i>=36;
                bool rail=i<=6||i>=10&&i<=12||i>=14&&i<=15||i>=19&&i<=23||i>=26&&i<=35;
                result[i]=new SegmentConfig{segment=i,endSegment=i,width=i==21||i==22?4.5f:i>=30?9:i>=27?7:6.5f,
                    kind=retained?SurfaceKind.RetainedGround:i==33?SurfaceKind.LandingSpan:i>=27&&i<=29?SurfaceKind.Ramp:Mathf.Abs(rise)<.7f?SurfaceKind.Flat:SurfaceKind.Stairs,
                    region=i<=6?"Arrival":i<=15?"OuterBailey":i<=18?"Garden":i<=25?"Chapel":i<=30?"UpperBridge":i<=35?"UpperApproach":"CitadelRetained",
                    leftRail=rail&&!retained,rightRail=rail&&!retained,
                    railOpeningAtStart=i==19?1.5f:0,railOpeningAtEnd=i==15||i==35?1.5f:0,
                    note=retained?"Existing continuous courtyard/citadel floor and gameplay retained; no elevated strip or enclosing fence.":i==33?"Nodes 33 and 34 share one level-66 turn landing; the short six-metre span is not built twice.":"Approved route node heights and original clear width."};
            }
            // Approved simplification: one clear flight between the high-garden
            // landing and chapel entrance, instead of the two short doglegs.
            result[21].width=5.5f;result[21].endSegment=22;
            result[21].note="Straight 30-riser flight from approved node21 (42m) to node23 (48m); replaces old segments21+22 and skips old node22.";
            result[22].width=5.5f;result[22].kind=SurfaceKind.MergedIntoPrevious;
            result[22].note="Owned by CW_Section_21_22; no duplicate floor or old node22 detour.";
            for(int i=32;i<=34;i++)
            {
                result[i].width=6.5f;
                result[i].note="Approved 6.5m upper approach: retains existing bearings and 64/66/68m levels; nodes33/34 form one simple landing with a direct internal walking line.";
            }
            return result;
        }
        sealed class Corner
        {
            public Vector3 center,incoming,outgoing,inL,inR,outL,outR,miterL,miterR,inDirection,outDirection;
            public float widthIn,widthOut;
            public bool bevelLeft,bevelRight;
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

        public static GameObject Build(Vector3[] route,Material deck,Material stone,Material trim,Material iron,Material metalAccent,SegmentConfig[] overrides=null)
        {
            var config=DefaultConfiguration(route);
            if(overrides!=null)foreach(var replacement in overrides)
            {
                if(replacement==null||replacement.segment<0||replacement.segment>=40)throw new ArgumentException("Invalid route configuration override.");
                if(replacement.endSegment<replacement.segment)replacement.endSegment=replacement.segment;
                config[replacement.segment]=replacement;
            }
            if(config.Any(c=>c.width<2||c.width>20))throw new ArgumentException("Walkway width outside the supported 2–20 metre range.");
            if(config[33].kind!=SurfaceKind.LandingSpan||Mathf.Abs(route[33].y-route[34].y)>.001f)
                throw new InvalidOperationException("The short upper switchback requires its same-height 33/34 landing cluster.");
            var materials=new[]{deck,stone,trim,iron,metalAccent};
            if(materials.Any(m=>m==null))throw new ArgumentException("Five approved walkway materials are required.");
            Directory.CreateDirectory(Folder);PostPositions.Clear();LastExplorationPath=Array.Empty<Vector3>();
            var root=new GameObject("CastleWalkwayUpgrade");
            var corners=new Corner[41];
            for(int node=1;node<40;node++)
            {
                if(config[node].kind==SurfaceKind.MergedIntoPrevious)continue;
                if(config[node-1].kind==SurfaceKind.RetainedGround||config[node].kind==SurfaceKind.RetainedGround)continue;
                int previous=node-1;
                while(previous>0&&config[previous].kind==SurfaceKind.MergedIntoPrevious)previous--;
                int next=config[node].endSegment+1;
                float margin=node>=21&&node<=23?.25f:.45f;
                corners[node]=MakeCorner(route[previous],route[node],route[next],config[previous].width,config[node].width,margin);
            }
            var walk=new List<Vector3>{route[0]};var sections=new List<SectionInfo>();var platforms=new List<PlatformInfo>();
            try
            {
                for(int i=0;i<40;i++)
                {
                    var cfg=config[i];var info=new SectionInfo{segment=i,endSegment=cfg.endSegment,region=cfg.region,kind=cfg.kind.ToString(),width=cfg.width,
                        from=route[i],to=route[i+1],pivot=(route[i]+route[i+1])*.5f,built=cfg.kind!=SurfaceKind.RetainedGround};
                    if(cfg.kind==SurfaceKind.RetainedGround)
                    {AddPath(walk,route[i]);AddPath(walk,route[i+1]);info.groupName="Retained existing floor";sections.Add(info);continue;}
                    if(cfg.kind==SurfaceKind.MergedIntoPrevious)
                    {
                        var owner=sections.Last(s=>s.segment<i&&s.endSegment>=i);
                        info.groupName=owner.groupName;info.from=owner.from;info.to=owner.to;info.pivot=owner.pivot;
                        info.usableLength=owner.usableLength;sections.Add(info);continue;
                    }
                    if(i==33)
                    {
                        var cluster=BuildLandingCluster(root.transform,corners[33],corners[34],route[33],route[34],materials,config[32],config[34],platforms,walk);
                        info.groupName=cluster.name;info.from=corners[33].incoming;info.to=corners[34].outgoing;
                        info.pivot=cluster.transform.position;info.usableLength=Horizontal(route[34]-route[33]).magnitude;sections.Add(info);continue;
                    }
                    var a=corners[i]!=null?corners[i].outgoing:route[i];
                    int endNode=cfg.endSegment+1;
                    var b=corners[endNode]!=null?corners[endNode].incoming:route[endNode];
                    float length=Horizontal(b-a).magnitude;bool stair=cfg.kind==SurfaceKind.Stairs;
                    int count=stair?Mathf.CeilToInt(Mathf.Abs(b.y-a.y)/.2f):0;
                    float tread=count>0?(length-.64f)/count:0;
                    if(length<.75f||count>0&&tread<.22f)
                        throw new InvalidOperationException("Segment "+i+" has insufficient run after landing cuts: "+length+"m, tread "+tread+"m. Adjust its configuration rather than deleting steps.");
                    var group=Group(root.transform,"CW_Section_"+i.ToString("00")+(cfg.endSegment!=i?"_"+cfg.endSegment.ToString("00"):"")+"_"+cfg.region);
                    var visual=new Geo();var collision=new Geo();BuildFlight(visual,collision,a,b,cfg.width,count,walk);
                    var direction=Horizontal(b-a).normalized;var side=Vector3.Cross(Vector3.up,direction)*cfg.width*.5f;
                    float start=Mathf.Clamp(cfg.railOpeningAtStart,0,length*.45f),end=Mathf.Clamp(cfg.railOpeningAtEnd,0,length*.45f);
                    var ra=Vector3.Lerp(a,b,start/length);var rb=Vector3.Lerp(a,b,1-end/length);
                    if(cfg.leftRail)RailEdge(visual,group.transform,ra+side,rb+side,"S"+i+"_L");
                    if(cfg.rightRail)RailEdge(visual,group.transform,ra-side,rb-side,"S"+i+"_R");
                    SaveVisual(group.transform,visual,"WalkSurface_CW_Segment_"+i,materials);
                    SaveGround(group.transform,collision,"Collision__CW_Segment_"+i);
                    CentrePivot(group.transform,(a+b)*.5f);
                    info.groupName=group.name;info.from=a;info.to=b;info.pivot=group.transform.position;info.steps=count;info.usableLength=length;info.minimumTread=tread;sections.Add(info);
                    int node=endNode;
                    if(corners[node]!=null&&node!=33&&node!=34)
                        BuildPlatformGroup(root.transform,corners[node],node,materials,cfg,config[node],platforms,walk);
                    if(i==35)LastExplorationPath=walk.ToArray();
                }
                LastWalkPath=walk.ToArray();LastSections=sections.ToArray();LastPlatforms=platforms.ToArray();LastConfig=config;
                if(LastExplorationPath.Length==0)throw new InvalidOperationException("Missing exploration path up to the retained citadel doorway.");
                AssetDatabase.SaveAssets();return root;
            }
            catch{UnityEngine.Object.DestroyImmediate(root);throw;}
        }
        static GameObject Group(Transform root,string name)
        {var go=new GameObject(name);go.transform.SetParent(root,false);return go;}
        static void CentrePivot(Transform group,Vector3 center)
        {
            // Mesh coordinates and UVs were authored in world metres. Countershift
            // direct children so geometry/collision retain their exact world pose.
            foreach(Transform child in group)child.localPosition-=center;
            group.position=center;
        }
        static Vector3 Horizontal(Vector3 v)=>new Vector3(v.x,0,v.z);
        static Vector3 AtHeight(Vector3 p,float h)=>new Vector3(p.x,h,p.z);
        static void AddPath(List<Vector3> path,Vector3 p)
        {if(path.Count==0||(path[path.Count-1]-p).sqrMagnitude>.0001f)path.Add(p);}
        static Corner MakeCorner(Vector3 previous,Vector3 p,Vector3 next,float incomingWidth,float outgoingWidth,float margin)
        {
            var a=Horizontal(p-previous).normalized;var b=Horizontal(next-p).normalized;
            var na=Vector3.Cross(Vector3.up,a);var nb=Vector3.Cross(Vector3.up,b);
            var left=LineIntersection(p+na*incomingWidth*.5f,a,p+nb*outgoingWidth*.5f,b);
            var right=LineIntersection(p-na*incomingWidth*.5f,a,p-nb*outgoingWidth*.5f,b);
            bool taper=Vector3.Angle(a,b)<25&&Mathf.Abs(incomingWidth-outgoingWidth)>.1f;
            if(taper){left=p+(na*incomingWidth+nb*outgoingWidth)*.25f;right=p-(na*incomingWidth+nb*outgoingWidth)*.25f;}
            float cutIn=Mathf.Max(margin<.4f?.75f:1.05f,Mathf.Max(Mathf.Abs(Vector3.Dot(left-p,a)),Mathf.Abs(Vector3.Dot(right-p,a)))+margin);
            float cutOut=Mathf.Max(margin<.4f?.75f:1.05f,Mathf.Max(Mathf.Abs(Vector3.Dot(left-p,b)),Mathf.Abs(Vector3.Dot(right-p,b)))+margin);
            if(taper)cutIn=cutOut=1.2f;
            bool sharp=Vector3.Angle(a,b)>105;
            return new Corner{center=p,incoming=p-a*cutIn,outgoing=p+b*cutOut,inDirection=a,outDirection=b,widthIn=incomingWidth,widthOut=outgoingWidth,
                inL=p-a*cutIn+na*incomingWidth*.5f,inR=p-a*cutIn-na*incomingWidth*.5f,outL=p+b*cutOut+nb*outgoingWidth*.5f,outR=p+b*cutOut-nb*outgoingWidth*.5f,
                miterL=left,miterR=right,bevelLeft=sharp&&CrossXZ(a,b)>0,bevelRight=sharp&&CrossXZ(a,b)<0};
        }
        static Vector3 LineIntersection(Vector3 a,Vector3 d,Vector3 b,Vector3 e)
        {
            float cross=CrossXZ(d,e);if(Mathf.Abs(cross)<.0001f)return(a+b)*.5f;
            return a+d*(CrossXZ(b-a,e)/cross);
        }
        static Vector3[] SideBoundary(Corner c,bool left)
        {
            float sign=left?1:-1;bool bevel=left?c.bevelLeft:c.bevelRight;
            var start=left?c.inL:c.inR;var end=left?c.outL:c.outR;
            if(!bevel)return new[]{start,left?c.miterL:c.miterR,end};
            return new[]{start,c.center+Vector3.Cross(Vector3.up,c.inDirection)*c.widthIn*.5f*sign,
                c.center+Vector3.Cross(Vector3.up,c.outDirection)*c.widthOut*.5f*sign,end};
        }
        static Vector3[] Polygon(Corner c)=>CleanPolygon(SideBoundary(c,true).Concat(SideBoundary(c,false).Reverse()).ToArray());
        static void BuildPlatformGroup(Transform root,Corner c,int node,Material[] materials,SegmentConfig incoming,SegmentConfig outgoing,List<PlatformInfo> report,List<Vector3> walk)
        {
            var group=Group(root,"CW_Platform_"+node.ToString("00"));var visual=new Geo();var collision=new Geo();var polygon=Polygon(c);
            Platform(visual,collision,polygon,c.center);
            // Shared courts open sideways. A route fence must never create a gate
            // across a formerly open courtyard connection.
            if(incoming.leftRail&&outgoing.leftRail)BoundaryRails(visual,group.transform,SideBoundary(c,true),"N"+node+"_L");
            if(incoming.rightRail&&outgoing.rightRail)BoundaryRails(visual,group.transform,SideBoundary(c,false),"N"+node+"_R");
            SaveVisual(group.transform,visual,"WalkSurface_CW_Platform_"+node,materials);
            SaveGround(group.transform,collision,"Collision__CW_Platform_"+node);CentrePivot(group.transform,c.center);
            report.Add(new PlatformInfo{firstNode=node,lastNode=node,groupName=group.name,pivot=c.center,polygon=polygon});
            AddPath(walk,c.incoming);AddPath(walk,c.center);AddPath(walk,c.outgoing);
        }
        static void BoundaryRails(Geo visual,Transform root,Vector3[] points,string name)
        {for(int i=0;i+1<points.Length;i++)RailEdge(visual,root,points[i],points[i+1],name+"_"+i);}
        static GameObject BuildLandingCluster(Transform root,Corner a,Corner b,Vector3 first,Vector3 last,Material[] materials,SegmentConfig incoming,SegmentConfig outgoing,List<PlatformInfo> report,List<Vector3> walk)
        {
            var polygon=ConvexHull(Polygon(a).Concat(Polygon(b)).ToArray());
            polygon=Clip(polygon,a.incoming,a.inDirection,true);
            polygon=Clip(polygon,b.outgoing,b.outDirection,false);
            polygon=CleanPolygon(polygon);
            var center=(first+last)*.5f;var group=Group(root,"CW_LandingCluster_33_34");var visual=new Geo();var collision=new Geo();
            Platform(visual,collision,polygon,center);
            for(int i=0;i<polygon.Length;i++)
            {
                var p=polygon[i];var q=polygon[(i+1)%polygon.Length];
                bool entrance=Mathf.Abs(Vector3.Dot(p-a.incoming,a.inDirection))<.003f&&Mathf.Abs(Vector3.Dot(q-a.incoming,a.inDirection))<.003f;
                bool exit=Mathf.Abs(Vector3.Dot(p-b.outgoing,b.outDirection))<.003f&&Mathf.Abs(Vector3.Dot(q-b.outgoing,b.outDirection))<.003f;
                if(!entrance&&!exit&&(incoming.leftRail||incoming.rightRail||outgoing.leftRail||outgoing.rightRail))
                    RailEdge(visual,group.transform,p,q,"Cluster33_34_"+i);
            }
            SaveVisual(group.transform,visual,"WalkSurface_CW_LandingCluster_33_34",materials);
            SaveGround(group.transform,collision,"Collision__CW_LandingCluster_33_34");CentrePivot(group.transform,center);
            report.Add(new PlatformInfo{firstNode=33,lastNode=34,groupName=group.name,pivot=center,polygon=polygon});
            AddPath(walk,a.incoming);AddPath(walk,center);AddPath(walk,b.outgoing);return group;
        }
        static Vector3[] CleanPolygon(Vector3[] input)
        {
            var points=new List<Vector3>();foreach(var p in input)if(points.Count==0||(points[points.Count-1]-p).sqrMagnitude>.000001f)points.Add(p);
            if(points.Count>1&&(points[0]-points[points.Count-1]).sqrMagnitude<.000001f)points.RemoveAt(points.Count-1);
            bool changed=true;while(changed&&points.Count>3)
            {
                changed=false;
                for(int i=0;i<points.Count;i++)if(Mathf.Abs(CrossXZ(points[i]-points[(i+points.Count-1)%points.Count],points[(i+1)%points.Count]-points[i]))<.00001f)
                {points.RemoveAt(i);changed=true;break;}
            }
            if(points.Count<3)throw new InvalidOperationException("Landing polygon collapsed.");return points.ToArray();
        }
        static Vector3[] ConvexHull(Vector3[] input)
        {
            var points=input.OrderBy(p=>p.x).ThenBy(p=>p.z).ToArray();var lower=new List<Vector3>();var upper=new List<Vector3>();
            foreach(var p in points){while(lower.Count>=2&&CrossXZ(lower[lower.Count-1]-lower[lower.Count-2],p-lower[lower.Count-1])<=.00001f)lower.RemoveAt(lower.Count-1);lower.Add(p);}
            for(int i=points.Length-1;i>=0;i--){var p=points[i];while(upper.Count>=2&&CrossXZ(upper[upper.Count-1]-upper[upper.Count-2],p-upper[upper.Count-1])<=.00001f)upper.RemoveAt(upper.Count-1);upper.Add(p);}
            lower.RemoveAt(lower.Count-1);upper.RemoveAt(upper.Count-1);return lower.Concat(upper).ToArray();
        }
        static Vector3[] Clip(Vector3[] polygon,Vector3 origin,Vector3 normal,bool positive)
        {
            var result=new List<Vector3>();float sign=positive?1:-1;
            for(int i=0;i<polygon.Length;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Length];float da=Vector3.Dot(a-origin,normal)*sign,db=Vector3.Dot(b-origin,normal)*sign;
                bool ia=da>=-.00001f,ib=db>=-.00001f;if(ia)result.Add(a);
                if(ia!=ib)result.Add(Vector3.LerpUnclamped(a,b,da/(da-db)));
            }
            return result.ToArray();
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
                    float old=heights[k-1];var low=AtHeight(p,old);var riserNormal=heights[k]>=old?-d:d;
                    visual.Quad(low+n,p+n-Vector3.up*bevel,p-n-Vector3.up*bevel,low-n,riserNormal,1);
                    collision.Quad(low+n,p+n,p-n,low-n,riserNormal,0);
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
            float signedArea=0;for(int a=0;a<polygon.Length;a++)signedArea+=CrossXZ(polygon[a],polygon[(a+1)%polygon.Length]);
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
                var outward=Vector3.Cross(Vector3.up,b-a).normalized*Mathf.Sign(signedArea);
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
            Barrier(root,a,b,side,"CW_Obstacle_PlayerBarrier_"+key);
            int panels=Mathf.Max(1,Mathf.CeilToInt(length/2.15f));
            for(int i=0;i<=panels;i++)
            {
                var p=Vector3.Lerp(a,b,(float)i/panels);
                var positionKey=new Vector3Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.y*1000),Mathf.RoundToInt(p.z*1000));
                if(!PostPositions.Add(positionKey))continue;
                Beam(geo,p+Vector3.up*.23f,p+Vector3.up*1.37f,.105f,.105f,3,root,"CW_Obstacle_RailPost_"+key+"_"+i);
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
            SaveGround(root,solid,"CW_Obstacle_RailMesh_"+key);
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
