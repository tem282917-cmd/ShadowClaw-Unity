using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chapter01.Editor
{
    /// <summary>Local, reversible ground-edge repair of the loaded scene. Never opens or saves a scene.</summary>
    public static class GroundTransitionRepair
    {
        const string Output = "D:/Shadow/Builds/CastleSceneRepair-20261118";
        const string Assets = "Assets/Chapter01/GroundTransitionRepair";
        const string RootName = "Ground Transition Repair - Arrival Rock Banks";
        const string ShaderName = "Chapter01/Castle Ground Transition";
        static readonly HashSet<string> GroundNames = new HashSet<string> {
            "Arrival_Ridge_Top", "Outer_Bailey_Rock_Top", "Garden_Rock_Top", "Chapel_Rock_Top",
            "Crown_Promontory_Top", "Upper_Stair_Rock_Top", "Connected_Lower_Massif",
            "Connected_Middle_Massif", "Crown_Massif_Foot", "Road_Return_Ridge" };

        [Serializable] public sealed class Report
        {
            public string revision = "ground-edge-r4-soil-banks-with-broken-rock-exposure";
            public string scene, timestampUtc, operation;
            public bool originalMeshesUnchanged = true, originalCollidersUnchanged = true, sceneSaved = false;
            public int changedMaterialSlots, addedRenderers, addedTriangles, addedColliders;
            public int rimBoundaryEdges, rimBuiltSegments, rimSkippedSegments, outerSeatLodSamples;
            public float smallestOuterSeatEmbed = float.MaxValue;
            public string method = "Distance to actual grass-material boundary is baked in world XZ, shared by every native mesh LOD. Imported meshes, source textures, collision and object transforms stay intact. Only Arrival gets a low-poly soil/rock bank; its outside seats are below the combined visible Arrival rock surface at every sampled native LOD, including the decorative rock modules above the geological core. No grass instances or real-time effects are added.";
            public List<SurfaceInfo> surfaces = new List<SurfaceInfo>();
            public List<string> rimRockSources = new List<string>();
            public List<RimStationFailure> rimStationFailures = new List<RimStationFailure>();
            public List<RimSkippedSegment> rimSkippedDetails = new List<RimSkippedSegment>();
            public List<RimSeatRecovery> rimRecoveredSeats = new List<RimSeatRecovery>();
            public List<string> generatedAssets = new List<string>(), warnings = new List<string>();
        }
        [Serializable] public sealed class RimStationFailure
        {
            public Vector3 boundaryPoint, attemptedInnerPoint, attemptedNearPoint;
            public string failureType;
            public float nominalOuterWidth, innerTopY;
            public List<RimSeatAttempt> outerAttempts = new List<RimSeatAttempt>();
        }
        [Serializable] public sealed class RimSeatAttempt
        {
            public Vector3 position;
            public bool allLodsHit;
            public int missingLod = -1;
            public float lowestRockY, differenceFromInnerTop;
            public string rejection;
        }
        [Serializable] public sealed class RimSkippedSegment
        {
            public int boundaryEdge, subdivision;
            public Vector3 from, to, centre;
            public string startFailure, endFailure;
        }
        [Serializable] public sealed class RimSeatRecovery
        {
            public Vector3 boundaryPoint, outerSeat;
            public float nominalOuterWidth, actualOuterWidth, verticalDrop, minimumEmbed;
            public int verifiedNativeLods;
            public string method = "Normal outward probes missed the visible ledge; a shorter outward probe found real rock at every native LOD. No unsupported span was filled.";
        }
        [Serializable] public sealed class SurfaceInfo
        {
            public string name, path, meshAsset, originalMaterial, transitionMaterial, edgeTexture;
            public int materialSlot, sourceTriangles, grassTriangles, nativeLods, boundaryEdges, largestBoundaryEdges;
            public Vector3 boundsCenter, boundsSize;
            public float maskMetresPerTexel;
        }
        sealed class Source
        {
            public MeshFilter filter; public MeshRenderer renderer; public Mesh mesh;
            public Vector3[] vertices; public int slot; public Material material, rockMaterial;
            public List<Edge> edges, perimeter; public SurfaceInfo info;
        }
        struct Edge { public int a,b; public Vector3 pa,pb; public Vector2 outward; }
        sealed class EdgeCount { public int count; public Edge edge; }
        struct Triangle { public Vector3 a,b,c; public float d; }
        sealed class HeightSampler
        {
            const float Cell = 5;
            readonly Dictionary<Vector2Int,List<Triangle>> cells = new Dictionary<Vector2Int,List<Triangle>>();
            public HeightSampler() { }
            public HeightSampler(Vector3[] vertices, IEnumerable<int[]> submeshes, bool mirrored)
                { Add(vertices,submeshes,mirrored,float.NegativeInfinity); }
            public void Add(Vector3[] vertices, IEnumerable<int[]> submeshes, bool mirrored,float minimumY)
            {
                foreach(var indices in submeshes)
                for(int i=0;i+2<indices.Length;i+=3)
                {
                    var a=vertices[indices[i]]; var b=vertices[indices[i+1]]; var c=vertices[indices[i+2]];
                    if(Mathf.Max(a.y,Mathf.Max(b.y,c.y))<minimumY)continue;
                    var normal=Vector3.Cross(b-a,c-a); if(mirrored)normal=-normal;
                    if(normal.y<.05f*normal.magnitude)continue;
                    float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
                    if(Mathf.Abs(d)<1e-8f)continue;
                    var triangle=new Triangle{a=a,b=b,c=c,d=d};
                    int x0=Mathf.FloorToInt(Mathf.Min(a.x,Mathf.Min(b.x,c.x))/Cell), x1=Mathf.FloorToInt(Mathf.Max(a.x,Mathf.Max(b.x,c.x))/Cell);
                    int z0=Mathf.FloorToInt(Mathf.Min(a.z,Mathf.Min(b.z,c.z))/Cell), z1=Mathf.FloorToInt(Mathf.Max(a.z,Mathf.Max(b.z,c.z))/Cell);
                    for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)
                    { var key=new Vector2Int(x,z); if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<Triangle>(); list.Add(triangle); }
                }
            }
            public bool Height(Vector2 p,out float y)
            {
                y=float.NegativeInfinity;
                if(!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x/Cell),Mathf.FloorToInt(p.y/Cell)),out var list))return false;
                foreach(var t in list)
                {
                    float a=((t.b.z-t.c.z)*(p.x-t.c.x)+(t.c.x-t.b.x)*(p.y-t.c.z))/t.d;
                    float b=((t.c.z-t.a.z)*(p.x-t.c.x)+(t.a.x-t.c.x)*(p.y-t.c.z))/t.d;
                    if(a<-.0001f||b<-.0001f||a+b>1.0001f)continue;
                    y=Mathf.Max(y,a*t.a.y+b*t.b.y+(1-a-b)*t.c.y);
                }
                return !float.IsNegativeInfinity(y);
            }
        }
        sealed class Rim
        {
            public List<Vector3> vertices=new List<Vector3>();
            public List<Color> colours=new List<Color>();
            public List<int> indices=new List<int>();
            readonly Dictionary<Vector3Int,int> vertexLookup=new Dictionary<Vector3Int,int>();
            public void Strip(Vector3[] a,Vector3[] b)
            {
                var ai=new int[a.Length];var bi=new int[b.Length];
                for(int j=0;j<a.Length;j++)
                {ai[j]=Vertex(a[j],Colour(a[j],j));bi[j]=Vertex(b[j],Colour(b[j],j));}
                for(int j=0;j<a.Length-1;j++)
                {Tri(ai[j],bi[j],ai[j+1]);Tri(bi[j],bi[j+1],ai[j+1]);}
            }
            static Color Colour(Vector3 p,int ring)
            {return new Color(1,1,1,ring<=1?1:ring==2?.7f:ring==3?.15f+.35f*Mathf.PerlinNoise(p.x*.34f+23,p.z*.34f+71):0);}
            int Vertex(Vector3 p,Color colour)
            {var key=new Vector3Int(Mathf.RoundToInt(p.x*10000),Mathf.RoundToInt(p.y*10000),Mathf.RoundToInt(p.z*10000));if(vertexLookup.TryGetValue(key,out int index))return index;index=vertices.Count;vertexLookup.Add(key,index);vertices.Add(p);colours.Add(colour);return index;}
            void Tri(int a,int b,int c)
            {if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).y<0){int q=b;b=c;c=q;}indices.Add(a);indices.Add(b);indices.Add(c);}
            public Mesh Mesh()
            {var mesh=new Mesh{name="Arrival_Ridge_Seated_Rock_Bank"}; if(vertices.Count>65535)mesh.indexFormat=IndexFormat.UInt32; mesh.SetVertices(vertices);mesh.SetColors(colours);mesh.SetUVs(0,vertices.Select(XZ).ToList());mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;}
        }

        [MenuItem("Tools/Chapter 01/Scene Repair/Inspect Loaded Ground Edges")]
        public static void InspectActive()
        {
            var scene=LoadedScene(); var report=NewReport(scene,"inspect"); var sources=ReadSources(scene,report);
            var arrival=sources.Single(s=>s.filter.name=="Arrival_Ridge_Top");
            BuildArrivalRim(scene,arrival,report);
            WriteReport(report,"ground-inspection.json");
            Debug.Log("GROUND_TRANSITION_INSPECTED "+Output+"/ground-inspection.json");
        }

        [MenuItem("Tools/Chapter 01/Scene Repair/Apply Loaded Ground Edges")]
        public static void ApplyActive()
        {
            var scene=LoadedScene(); var report=NewReport(scene,"apply");
            var shader=Shader.Find(ShaderName);
            if(!shader||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Import the error-free CastleGroundTransition shader first.");
            var sources=ReadSources(scene,report);
            var arrival=sources.Single(s=>s.filter.name=="Arrival_Ridge_Top");
            var rim=BuildArrivalRim(scene,arrival,report);
            if(rim.indices.Count==0)throw new InvalidOperationException("No validated cliff-seated Arrival edge. No scene changes were made.");
            EnsureFolder(Assets+"/Materials");EnsureFolder(Assets+"/Masks");EnsureFolder(Assets+"/Meshes");
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Castle ground soil and rock transitions");
            Material arrivalMaterial=null;
            foreach(var source in sources)
            {
                var mask=StoreMask(source,report);
                string path=Assets+"/Materials/"+source.filter.name+"_Transition.mat";
                var made=new Material(shader){name=source.filter.name+"_Soil_Rock_Transition"};
                made.CopyPropertiesFromMaterial(source.material);made.shader=shader;
                made.SetTexture("_EdgeDistance",mask);
                var b=source.renderer.bounds;
                made.SetVector("_EdgeBounds",new Vector4(b.min.x-6,b.min.z-6,1/(b.size.x+12),1/(b.size.z+12)));
                made.SetTexture("_RockTex",source.rockMaterial.GetTexture("_MainTex"));
                made.SetTexture("_RockNormal",source.rockMaterial.HasProperty("_BumpMap")?source.rockMaterial.GetTexture("_BumpMap"):null);
                made.SetColor("_RockTint",source.rockMaterial.HasProperty("_Color")?source.rockMaterial.color:Color.white);
                made.SetFloat("_RockScale",1/6f);made.SetFloat("_UseRimVertexAlpha",0);
                var material=StoreAsset(made,path);report.generatedAssets.Add(path);source.info.transitionMaterial=path;
                Undo.RecordObject(source.renderer,"Blend grass into local cliff rock");
                var slots=source.renderer.sharedMaterials;slots[source.slot]=material;source.renderer.sharedMaterials=slots;
                PrefabUtility.RecordPrefabInstancePropertyModifications(source.renderer);
                report.changedMaterialSlots++;if(source==arrival)arrivalMaterial=material;
            }
            string rimPath=Assets+"/Materials/Arrival_Ridge_Rock_Bank.mat";
            var rimMade=new Material(arrivalMaterial){name="Arrival_Ridge_Rock_Bank"};rimMade.SetFloat("_UseRimVertexAlpha",1);
            var rimMaterial=StoreAsset(rimMade,rimPath);report.generatedAssets.Add(rimPath);
            string meshPath=Assets+"/Meshes/Arrival_Ridge_Seated_Rock_Bank.asset";
            var rimMesh=StoreAsset(rim.Mesh(),meshPath);report.generatedAssets.Add(meshPath);
            var previous=scene.GetRootGameObjects().FirstOrDefault(x=>x.name==RootName);
            if(previous)Undo.DestroyObjectImmediate(previous);
            var root=new GameObject(RootName);SceneManager.MoveGameObjectToScene(root,scene);Undo.RegisterCreatedObjectUndo(root,"Add cliff-seated soil bank");
            root.AddComponent<MeshFilter>().sharedMesh=rimMesh;
            var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=rimMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
            renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            report.addedRenderers=1;report.addedTriangles=rim.indices.Count/3;
            foreach(var source in sources)
                if(source.filter.sharedMesh!=source.mesh)throw new InvalidOperationException("Imported terrain mesh unexpectedly changed: "+source.filter.name);
            EditorSceneManager.MarkSceneDirty(scene);Undo.CollapseUndoOperations(undo);
            WriteReport(report,"ground-repair.json");
            Debug.Log("GROUND_TRANSITION_APPLIED materials="+report.changedMaterialSlots+" rimTriangles="+report.addedTriangles+" scene remains unsaved; "+Output+"/ground-repair.json");
        }

        static Scene LoadedScene()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run ground authoring outside Play mode.");
            var scene=SceneManager.GetActiveScene();
            if(!scene.IsValid()||!scene.isLoaded||!scene.name.StartsWith("Chapter01_",StringComparison.Ordinal))throw new InvalidOperationException("A currently loaded Chapter01 scene is required; no scene will be opened.");
            return scene;
        }
        static Report NewReport(Scene scene,string operation)=>new Report{scene=scene.path,timestampUtc=DateTime.UtcNow.ToString("o"),operation=operation};
        static List<Source> ReadSources(Scene scene,Report report)
        {
            var filters=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>GroundNames.Contains(f.name)).ToArray();
            var sources=new List<Source>();
            foreach(string name in GroundNames.OrderBy(n=>n))
            {
                var matches=filters.Where(f=>f.name==name).ToArray();
                if(matches.Length!=1)throw new InvalidOperationException("Expected one ground surface "+name+", found "+matches.Length+". Refusing an ambiguous edit.");
                var filter=matches[0];var renderer=filter.GetComponent<MeshRenderer>();var mesh=filter.sharedMesh;
                if(!renderer||!mesh||!renderer.enabled||!filter.gameObject.activeInHierarchy){report.warnings.Add("Inactive ground left unchanged: "+name);continue;}
                var materials=renderer.sharedMaterials;
                int slot=Array.FindIndex(materials,m=>m&&(m.shader.name=="Chapter01/Castle Forest Floor"||m.shader.name==ShaderName));
                if(slot<0){report.warnings.Add("No existing forest-floor material on "+name+"; preserved.");continue;}
                var rock=materials.FirstOrDefault(m=>m&&m.HasProperty("_MainTex")&&m.GetTexture("_MainTex")&&m.name.Contains("Terrain_Rock"));
                if(!rock)throw new InvalidOperationException("No adjacent authored rock material for "+name);
                var source=new Source{filter=filter,renderer=renderer,mesh=mesh,slot=slot,material=materials[slot],rockMaterial=rock,vertices=ReadWorldVertices(filter)};
                source.edges=Boundary(source,mesh.GetIndices(slot,0,true));source.perimeter=LargestComponent(source.edges);
                source.info=new SurfaceInfo{name=name,path=Hierarchy(filter.transform),meshAsset=AssetDatabase.GetAssetPath(mesh),originalMaterial=AssetDatabase.GetAssetPath(source.material),materialSlot=slot,
                    nativeLods=Mathf.Max(1,mesh.lodCount),sourceTriangles=Enumerable.Range(0,mesh.subMeshCount).Sum(s=>mesh.GetIndices(s,0,true).Length/3),grassTriangles=mesh.GetIndices(slot,0,true).Length/3,
                    boundaryEdges=source.edges.Count,largestBoundaryEdges=source.perimeter.Count,boundsCenter=renderer.bounds.center,boundsSize=renderer.bounds.size};
                if(source.edges.Count<3)throw new InvalidOperationException("No meaningful grass boundary on "+name);
                report.surfaces.Add(source.info);sources.Add(source);
            }
            if(sources.All(s=>s.filter.name!="Arrival_Ridge_Top"))throw new InvalidOperationException("The screenshot's Arrival_Ridge_Top could not be identified safely.");
            return sources;
        }
        static Vector3[] ReadWorldVertices(MeshFilter filter)
        {
            using(var snapshot=MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
            using(var values=new NativeArray<Vector3>(snapshot[0].vertexCount,Allocator.Temp))
            {snapshot[0].GetVertices(values);var result=new Vector3[values.Length];for(int i=0;i<result.Length;i++)result[i]=filter.transform.TransformPoint(values[i]);return result;}
        }
        static List<Edge> Boundary(Source source,int[] indices)
        {
            var welded=new Dictionary<Vector3Int,int>();var ids=new int[source.vertices.Length];
            for(int i=0;i<ids.Length;i++)
            {var p=source.vertices[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.y*1000),Mathf.RoundToInt(p.z*1000));if(!welded.TryGetValue(key,out int id))welded[key]=id=i;ids[i]=id;}
            var counts=new Dictionary<ulong,EdgeCount>();
            for(int i=0;i+2<indices.Length;i+=3)for(int j=0;j<3;j++)
            {
                int a=ids[indices[i+j]],b=ids[indices[i+(j+1)%3]],c=ids[indices[i+(j+2)%3]];
                if(a==b)continue;ulong key=((ulong)(uint)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
                if(counts.TryGetValue(key,out var found)){found.count++;continue;}
                var pa=source.vertices[a];var pb=source.vertices[b];var tangent=XZ(pb-pa);var outward=new Vector2(tangent.y,-tangent.x).normalized;
                if(Vector2.Dot(outward,XZ(source.vertices[c]-(pa+pb)*.5f))>0)outward=-outward;
                counts[key]=new EdgeCount{count=1,edge=new Edge{a=a,b=b,pa=pa,pb=pb,outward=outward}};
            }
            return counts.Values.Where(e=>e.count==1&&(XZ(e.edge.pb-e.edge.pa)).sqrMagnitude>.000001f).Select(e=>e.edge).ToList();
        }
        static List<Edge> LargestComponent(List<Edge> edges)
        {
            var neighbours=new Dictionary<int,List<int>>();
            for(int i=0;i<edges.Count;i++)foreach(int v in new[]{edges[i].a,edges[i].b})
            {if(!neighbours.TryGetValue(v,out var list))neighbours[v]=list=new List<int>();list.Add(i);}
            var visited=new HashSet<int>();var largest=new List<Edge>();float longest=0;
            for(int i=0;i<edges.Count;i++)
            {
                if(visited.Contains(i))continue;var stack=new Stack<int>();stack.Push(i);var group=new List<Edge>();float length=0;
                while(stack.Count>0){int e=stack.Pop();if(!visited.Add(e))continue;group.Add(edges[e]);length+=Vector2.Distance(XZ(edges[e].pa),XZ(edges[e].pb));foreach(int v in new[]{edges[e].a,edges[e].b})foreach(int next in neighbours[v])if(!visited.Contains(next))stack.Push(next);}
                if(length>longest){longest=length;largest=group;}
            }
            return largest;
        }

        static Texture2D StoreMask(Source source,Report report)
        {
            const int size=512;var b=source.renderer.bounds;var origin=new Vector2(b.min.x-6,b.min.z-6);var extent=new Vector2(b.size.x+12,b.size.z+12);
            var pixels=Enumerable.Repeat(new Color32(255,255,255,255),size*size).ToArray();
            // Rasterise only a bounded eight-metre band around each edge. Every
            // other texel is known to be farther away; avoids Npixels*Nedges.
            foreach(var edge in source.edges)
            {
                var a=XZ(edge.pa);var c=XZ(edge.pb);var d=c-a;float length=d.sqrMagnitude;
                int x0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x,c.x)-8-origin.x)/extent.x*size),0,size-1),x1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x,c.x)+8-origin.x)/extent.x*size),0,size-1);
                int y0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y,c.y)-8-origin.y)/extent.y*size),0,size-1),y1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y,c.y)+8-origin.y)/extent.y*size),0,size-1);
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {var p=origin+new Vector2((x+.5f)/size*extent.x,(y+.5f)/size*extent.y);float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(length,1e-8f));byte distance=(byte)Mathf.Clamp(Mathf.RoundToInt(Vector2.Distance(p,a+d*t)/8*255),0,255);int k=y*size+x;if(distance<pixels[k].r)pixels[k]=new Color32(distance,distance,distance,255);}
            }
            var texture=new Texture2D(size,size,TextureFormat.R8,true,true){name=source.filter.name+"_Grass_Rock_EdgeDistance",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,anisoLevel=1};
            texture.SetPixelData(pixels.Select(p=>p.r).ToArray(),0);texture.Apply(true,false);
            string path=Assets+"/Masks/"+source.filter.name+"_EdgeDistance.asset";
            texture=StoreAsset(texture,path);source.info.edgeTexture=path;source.info.maskMetresPerTexel=Mathf.Max(extent.x,extent.y)/size;report.generatedAssets.Add(path);return texture;
        }

        static Rim BuildArrivalRim(Scene scene,Source source,Report report)
        {
            // The geological core is deep below the visible ledges. Sample the
            // actual authored rock modules as well, never their AABBs/colliders.
            var bounds=source.renderer.bounds;bounds.Expand(new Vector3(14,12,14));
            var rocks=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>
            {
                if(f==source.filter||!f.sharedMesh||!f.gameObject.activeInHierarchy||!f.name.Contains("Arrival_Ridge"))return false;
                var r=f.GetComponent<MeshRenderer>();return r&&r.enabled&&r.bounds.Intersects(bounds)&&r.sharedMaterials.Any(m=>m&&(m.name.Contains("Cliff")||m.name.Contains("Terrain_Rock")));
            }).ToArray();
            if(rocks.Length==0)throw new InvalidOperationException("No visible Arrival rock neighbours were found.");
            int lodCount=rocks.Max(f=>Mathf.Max(1,f.sharedMesh.lodCount));
            var cliffs=Enumerable.Range(0,lodCount).Select(i=>new HeightSampler()).ToArray();
            foreach(var rock in rocks)
            {
                var mesh=rock.sharedMesh;var vertices=ReadWorldVertices(rock);report.rimRockSources.Add(Hierarchy(rock.transform));
                for(int lod=0;lod<lodCount;lod++)
                {int actual=Mathf.Min(lod,Mathf.Max(1,mesh.lodCount)-1);cliffs[lod].Add(vertices,Enumerable.Range(0,mesh.subMeshCount).Select(sub=>mesh.GetIndices(sub,actual,true)),rock.transform.localToWorldMatrix.determinant<0,source.renderer.bounds.max.y-5.5f);}
            }
            var top=new HeightSampler(source.vertices,new[]{source.mesh.GetIndices(source.slot,0,true)},source.filter.transform.localToWorldMatrix.determinant<0);
            var normals=new Dictionary<int,Vector2>();
            foreach(var edge in source.perimeter)foreach(int v in new[]{edge.a,edge.b}){normals.TryGetValue(v,out var n);normals[v]=n+edge.outward;}
            var rim=new Rim();report.rimBoundaryEdges=source.perimeter.Count;
            for(int edgeIndex=0;edgeIndex<source.perimeter.Count;edgeIndex++)
            {
                var edge=source.perimeter[edgeIndex];
                int steps=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(XZ(edge.pa),XZ(edge.pb))/1.35f));
                Vector3[] previous=null;Vector3 previousPosition=default;string previousFailure=null;
                for(int i=0;i<=steps;i++)
                {
                    float t=i/(float)steps;var p=Vector3.Lerp(edge.pa,edge.pb,t);var n=Vector2.Lerp(normals[edge.a].normalized,normals[edge.b].normalized,t).normalized;
                    var points=RimStation(p,n,top,cliffs,report,out string failure);
                    if(i>0)
                    {
                        if(previous!=null&&points!=null){rim.Strip(previous,points);report.rimBuiltSegments++;}
                        else
                        {
                            report.rimSkippedSegments++;
                            report.rimSkippedDetails.Add(new RimSkippedSegment{boundaryEdge=edgeIndex,subdivision=i-1,from=previousPosition,to=p,centre=(previousPosition+p)*.5f,startFailure=previousFailure??"none",endFailure=failure??"none"});
                        }
                    }
                    previous=points;previousPosition=p;previousFailure=failure;
                }
            }
            report.addedTriangles=rim.indices.Count/3;
            if(report.rimSkippedSegments>0)report.warnings.Add(report.rimSkippedSegments+" Arrival edge segments were left without a bank because actual rock/top seats could not be verified; the material transition still applies.");
            return rim;
        }
        static Vector3[] RimStation(Vector3 p,Vector2 outward,HeightSampler top,HeightSampler[] cliffs,Report report,out string failure)
        {
            failure=null;
            float noise=Mathf.PerlinNoise(p.x*.13f+19,p.z*.13f+41);
            float width=2.4f+noise*1.25f;
            float bankVariation=Mathf.PerlinNoise(p.x*.37f+3,p.z*.37f+29);
            var xz=XZ(p);var inner=xz-outward*(1.15f+bankVariation*1.75f);
            var issue=new RimStationFailure{boundaryPoint=p,attemptedInnerPoint=new Vector3(inner.x,p.y,inner.y),attemptedNearPoint=new Vector3(p.x-outward.x*.8f,p.y,p.z-outward.y*.8f),nominalOuterWidth=width};
            if(!top.Height(inner,out float topY))
            {failure=issue.failureType="inner_grass_surface_missing";report.rimStationFailures.Add(issue);return null;}
            issue.innerTopY=topY;issue.attemptedInnerPoint.y=topY;
            float outerY=float.PositiveInfinity;var outer=xz+outward*width;bool valid=false;int pickedAttempt=-1;
            // A fixed wide bank can overrun a narrow rock shelf. Shorter fallback
            // probes are allowed only after all ordinary probes fail, and still
            // require the real visible surface at every native LOD.
            var probeWidths=new[]{width,width+.65f,width+1.3f,width+1.95f,Mathf.Max(.85f,width*.75f),Mathf.Max(.85f,width*.55f),Mathf.Max(.85f,width*.35f),.85f};
            for(int attempt=0;attempt<probeWidths.Length;attempt++)
            {
                float maxDrop=attempt<4?5:Mathf.Min(5,1.2f+probeWidths[attempt]*1.4f);
                outer=xz+outward*probeWidths[attempt];outerY=float.PositiveInfinity;valid=true;
                var seat=new RimSeatAttempt{position=new Vector3(outer.x,topY,outer.y)};
                for(int lod=0;lod<cliffs.Length;lod++)
                {if(!cliffs[lod].Height(outer,out float y)){valid=false;seat.missingLod=lod;break;}outerY=Mathf.Min(outerY,y);}
                seat.allLodsHit=valid;
                if(valid){seat.position.y=seat.lowestRockY=outerY;seat.differenceFromInnerTop=outerY-topY;}
                seat.rejection=!valid?"rock_surface_missing_at_lod":outerY>topY+.15f?"rock_surface_above_cap_limit":outerY<topY-maxDrop?"rock_surface_below_bank_limit":"accepted";
                issue.outerAttempts.Add(seat);
                if(valid&&outerY<=topY+.15f&&outerY>=topY-maxDrop){pickedAttempt=attempt;break;}valid=false;
            }
            if(!valid){failure=issue.failureType="no_valid_rock_seat";report.rimStationFailures.Add(issue);return null;}
            float bodyFraction=.38f+bankVariation*.30f;
            var points=new Vector3[5];var offsets=new[]{inner,xz-outward*.8f,xz,Vector2.Lerp(xz,outer,bodyFraction),outer};
            for(int j=0;j<5;j++)
            {
                var q=offsets[j];float y;
                if(j<=1)
                {
                    if(!top.Height(q,out y)){failure=issue.failureType=j==0?"inner_grass_surface_missing":"near_edge_grass_surface_missing";report.rimStationFailures.Add(issue);return null;}
                    y+=j==0?.018f:.045f+bankVariation*.065f;
                }
                else if(j==2)y=Mathf.Max(p.y,top.Height(q,out float surface)?surface:p.y)+.065f+bankVariation*.18f;
                else if(j==3)
                {
                    float relief=Mathf.PerlinNoise(p.x*.64f+57,p.z*.64f+9);
                    y=Mathf.Lerp(points[2].y,outerY,bodyFraction)+.04f+relief*.20f;
                    // Let nearby real rock ridges shape the bank without moving
                    // its previously verified buried seat or exposing the cap.
                    if(cliffs[0].Height(q,out float rockSurface)&&rockSurface>y-.35f&&rockSurface<points[2].y+.25f)y=Mathf.Max(y,rockSurface+.04f);
                }
                else y=outerY-.22f;
                // Never let the cap's visible vertical face protrude through the bank.
                if(j>=2&&j<4&&top.Height(q,out float original))y=Mathf.Max(y,original+.065f);
                points[j]=new Vector3(q.x,y,q.y);
            }
            foreach(var cliff in cliffs){cliff.Height(outer,out float height);report.smallestOuterSeatEmbed=Mathf.Min(report.smallestOuterSeatEmbed,height-points[4].y);report.outerSeatLodSamples++;}
            if(pickedAttempt>=4)report.rimRecoveredSeats.Add(new RimSeatRecovery{boundaryPoint=p,outerSeat=points[4],nominalOuterWidth=width,actualOuterWidth=probeWidths[pickedAttempt],verticalDrop=topY-outerY,minimumEmbed=.22f,verifiedNativeLods=cliffs.Length});
            return points;
        }
        static T StoreAsset<T>(T made,string path) where T:Object
        {
            var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if(existing)
            {
                // Explicitly refresh authored GPU buffers on a repeated repair.
                // The imported native-LOD meshes never enter this asset writer.
                if(existing is Mesh targetMesh&&made is Mesh newMesh)
                {
                    targetMesh.Clear();targetMesh.indexFormat=newMesh.indexFormat;
                    targetMesh.vertices=newMesh.vertices;targetMesh.normals=newMesh.normals;
                    targetMesh.tangents=newMesh.tangents;targetMesh.colors=newMesh.colors;targetMesh.uv=newMesh.uv;
                    targetMesh.subMeshCount=newMesh.subMeshCount;
                    for(int sub=0;sub<newMesh.subMeshCount;sub++)targetMesh.SetTriangles(newMesh.GetTriangles(sub),sub,false);
                    targetMesh.bounds=newMesh.bounds;targetMesh.UploadMeshData(false);
                }
                else if(existing is Texture2D targetTexture&&made is Texture2D newTexture)
                {
                    targetTexture.Reinitialize(newTexture.width,newTexture.height,newTexture.format,true);
                    targetTexture.SetPixelData(newTexture.GetPixelData<byte>(0),0);
                    targetTexture.wrapMode=newTexture.wrapMode;targetTexture.filterMode=newTexture.filterMode;targetTexture.anisoLevel=newTexture.anisoLevel;
                    targetTexture.Apply(true,false);
                }
                else EditorUtility.CopySerialized(made,existing);
                Object.DestroyImmediate(made);EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);return existing;
            }
            AssetDatabase.CreateAsset(made,path);AssetDatabase.SaveAssetIfDirty(made);return made;
        }
        static void EnsureFolder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;string parent=path.Substring(0,path.LastIndexOf('/'));EnsureFolder(parent);AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));}
        static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
        static string Hierarchy(Transform t)=>t.parent?Hierarchy(t.parent)+"/"+t.name:t.name;
        static void WriteReport(Report report,string file)
        {Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,file),JsonUtility.ToJson(report,true));}
    }
}
