using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chapter01.Editor
{
    /// <summary>Unity-only, repeatable castle soil and light understory authoring.</summary>
    public static class Chapter01GroundUpgrade
    {
        const string RootName = "V025 Castle Ground Dressing";
        const string MaterialFolder = "Assets/Chapter01/Materials/GroundV025";
        const string TextureFolder = "Assets/Chapter01/Textures/GroundV025";
        const int Seed = 251117;
        static readonly HashSet<string> GroundNames = new HashSet<string> {
            "Arrival_Ridge_Top", "Outer_Bailey_Rock_Top", "Garden_Rock_Top", "Chapel_Rock_Top",
            "Crown_Promontory_Top", "Upper_Stair_Rock_Top", "Connected_Lower_Massif",
            "Connected_Middle_Massif", "Crown_Massif_Foot", "Road_Return_Ridge"
        };

        [Serializable] public sealed class Report
        {
            public string schema = "shadow.castle-ground-unity/v025";
            public string authoringRevision = "short-curved-understory-r2-explicit-upload";
            public string timestampUtc;
            public int seed = Seed, replacedMaterialSlots, preservedGardenPavingSlots;
            public int grassPatches, fernPatches, shrubPatches, totalPatches, lodGroups, rendererInstances, uniqueMeshes;
            public int[] instanceTrianglesByLod = new int[3];
            public int[] uniqueTrianglesByLod = new int[3];
            public float minimumRouteEdgeClearance = float.MaxValue, maximumSeatError;
            public float minimumCullDistanceAt50DegreeFov = float.MaxValue, maximumCullDistanceAt50DegreeFov;
            public float actualQualityLodBias, referenceVerticalFieldOfView;
            public float minimumEstimatedCullDistance = float.MaxValue, maximumEstimatedCullDistance;
            public string referenceCamera;
            public string cullDistanceBasis = "Estimates use LODGroup local size × world scale × current QualitySettings.lodBias / (2 × tan(camera vertical FOV/2) × .025). The older 50-degree fields explicitly use lodBias=1. Perspective screen coverage, not a fixed metre cutoff; changing FOV or quality changes distances.";
            public bool sourceMeshesUnchanged = true, villageUnchanged = true;
            public bool allVegetationShadowCastingOff, allVegetationCrossFade;
            public bool persistedMeshGeometryVerified;
            public string meshDataUpdate = "Mesh Clear/SetVertices/SetNormals/SetColors/SetTriangles plus immediate UploadMeshData(false), stable asset GUIDs. After SaveAssets and forced reimport, all CPU position/normal/colour/index buffers are compared against the authored SHA-256 before scene renderers are created.";
            public string groundShader, vegetationShader;
            public string detailScale = "2 m repeat, 1024 pixels; soil/grass micro relief; low contrast 17/64 m coverage";
            public string vegetationLod = "Three authored silhouette-compatible meshes; screen-height .16/.065/.025 before QualitySettings.lodBias; 30% dither transition then cull; no alpha cards, colliders, or shadow casting";
            public string grassGeometry = "32 irregular tufts × 12 short curved blades per LOD0 patch; tight roots, 3-6 cm blade width, 14-32 cm authored height with occasional taller leaves; upward-biased normals and two-sided shadowed diffuse, no emission";
            public string verification = "Authoring statistics only; player-view appearance, FPS and transitions require the separate runtime review.";
            public List<string> surfaces = new List<string>();
            public string[] materialPaths = { MaterialFolder+"/V025_Castle_Forest_Floor.mat",MaterialFolder+"/V025_Understory.mat" };
            public string[] texturePaths = { TextureFolder+"/Soil-Albedo.png",TextureFolder+"/Grass-Albedo.png",TextureFolder+"/Soil-Relief.png",TextureFolder+"/Grass-Relief.png",TextureFolder+"/Coverage.png" };
            public List<RegionResult> regions = new List<RegionResult>();
            public List<MeshReadback> meshReadback = new List<MeshReadback>();
        }
        [Serializable] public sealed class RegionResult { public string name; public int requested, placed; }
        [Serializable] public sealed class MeshReadback
        {
            public string assetPath, assetGuid, expectedSha256, persistedSha256;
            public int vertices, indices, kind, variation, lod;
            public bool matches;
        }
        struct Region
        {
            public string name;
            public Vector2[] centers;
            public float radius;
            public int count;
            public Region(string name, float radius, int count, params Vector2[] centers)
            { this.name=name; this.radius=radius; this.count=count; this.centers=centers; }
        }
        struct Triangle
        {
            public Vector3 a,b,c,n;
            public float denominator;
        }
        sealed class SurfaceSampler
        {
            const float Cell = 6;
            readonly Dictionary<Vector2Int,List<Triangle>> cells = new Dictionary<Vector2Int,List<Triangle>>();
            public void Add(MeshRenderer renderer, int submesh)
            {
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || submesh >= mesh.subMeshCount) return;
                using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var data=snapshot[0];
                    using (var vertices=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp))
                    using (var indices=new NativeArray<int>((int)mesh.GetIndexCount(submesh,0),Allocator.Temp))
                    {
                        data.GetVertices(vertices); data.GetIndices(indices,submesh,0,true);
                        var matrix=renderer.localToWorldMatrix;
                        bool mirrored=matrix.determinant<0;
                        for(int i=0;i+2<indices.Length;i+=3)
                        {
                            var a=matrix.MultiplyPoint3x4(vertices[indices[i]]);
                            var b=matrix.MultiplyPoint3x4(vertices[indices[i+1]]);
                            var c=matrix.MultiplyPoint3x4(vertices[indices[i+2]]);
                            var n=Vector3.Cross(b-a,c-a).normalized;
                            if(mirrored)n=-n;
                            if(n.y<.68f) continue;
                            float denominator=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
                            if(Mathf.Abs(denominator)<1e-7f) continue;
                            var triangle=new Triangle { a=a,b=b,c=c,n=n,denominator=denominator };
                            int x0=Mathf.FloorToInt(Mathf.Min(a.x,Mathf.Min(b.x,c.x))/Cell);
                            int x1=Mathf.FloorToInt(Mathf.Max(a.x,Mathf.Max(b.x,c.x))/Cell);
                            int z0=Mathf.FloorToInt(Mathf.Min(a.z,Mathf.Min(b.z,c.z))/Cell);
                            int z1=Mathf.FloorToInt(Mathf.Max(a.z,Mathf.Max(b.z,c.z))/Cell);
                            for(int x=x0;x<=x1;x++) for(int z=z0;z<=z1;z++)
                            {
                                var key=new Vector2Int(x,z);
                                if(!cells.TryGetValue(key,out var list)) cells[key]=list=new List<Triangle>();
                                list.Add(triangle);
                            }
                        }
                    }
                }
            }
            public bool Sample(float x,float z,out Vector3 point,out Vector3 normal)
            {
                point=new Vector3(x,float.NegativeInfinity,z); normal=Vector3.up;
                if(!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(x/Cell),Mathf.FloorToInt(z/Cell)),out var list)) return false;
                foreach(var t in list)
                {
                    float u=((t.b.z-t.c.z)*(x-t.c.x)+(t.c.x-t.b.x)*(z-t.c.z))/t.denominator;
                    float v=((t.c.z-t.a.z)*(x-t.c.x)+(t.a.x-t.c.x)*(z-t.c.z))/t.denominator;
                    if(u<-.0001f||v<-.0001f||u+v>1.0001f) continue;
                    float y=u*t.a.y+v*t.b.y+(1-u-v)*t.c.y;
                    if(y>point.y) { point.y=y; normal=t.n; }
                }
                return !float.IsNegativeInfinity(point.y);
            }
        }

        public static Report Apply(Scene scene, Matrix4x4 blenderToUnity)
        {
            if(!scene.IsValid()||!scene.isLoaded) throw new ArgumentException("A loaded review scene is required.");
            EnsureFolder(MaterialFolder); EnsureFolder(TextureFolder); EnsureFolder(MaterialFolder+"/Meshes");
            var report=new Report { timestampUtc=DateTime.UtcNow.ToString("o") };
            var cameras=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Camera>(true)).ToArray();
            var referenceCamera=cameras.FirstOrDefault(x=>x.name.StartsWith("Perspective LOD inspection",StringComparison.Ordinal))??cameras.FirstOrDefault(x=>!x.orthographic);
            report.referenceCamera=referenceCamera!=null?referenceCamera.name:"Fallback 50 degree perspective";
            report.referenceVerticalFieldOfView=referenceCamera!=null?referenceCamera.fieldOfView:50;
            report.actualQualityLodBias=QualitySettings.lodBias;
            var previous=scene.GetRootGameObjects().FirstOrDefault(x=>x.name==RootName);
            if(previous!=null) Object.DestroyImmediate(previous);
            var renderers=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
            var groundShader=Shader.Find("Chapter01/Castle Forest Floor");
            var vegetationShader=Shader.Find("Chapter01/Castle Understory");
            if(groundShader==null||vegetationShader==null) throw new InvalidOperationException("Import both Chapter01 ground shaders before applying.");
            if(ShaderUtil.ShaderHasError(groundShader)||ShaderUtil.ShaderHasError(vegetationShader)) throw new InvalidOperationException("Ground or understory shader has compilation errors.");
            report.groundShader=groundShader.name; report.vegetationShader=vegetationShader.name;
            var material=MakeGroundMaterial(groundShader);
            var sampler=new SurfaceSampler();
            foreach(var renderer in renderers)
            {
                var slots=renderer.sharedMaterials;
                for(int i=0;i<slots.Length;i++)
                {
                    if(slots[i]!=null&&slots[i].name=="V024_Ground_Garden_Continuous_Paving") report.preservedGardenPavingSlots++;
                    if(!GroundNames.Contains(renderer.name)||slots[i]==null) continue;
                    if(!slots[i].name.StartsWith("V024_Ground_",StringComparison.Ordinal)&&slots[i].name!="V025_Castle_Forest_Floor") continue;
                    sampler.Add(renderer,i);
                    slots[i]=material; report.replacedMaterialSlots++;
                    if(!report.surfaces.Contains(renderer.name)) report.surfaces.Add(renderer.name);
                }
                renderer.sharedMaterials=slots;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            if(report.replacedMaterialSlots!=GroundNames.Count)
                throw new InvalidOperationException("Expected ten castle ground slots; found "+report.replacedMaterialSlots+". Refusing unverified placement.");

            var foliage=LoadOrCreateMaterial(MaterialFolder+"/V025_Understory.mat",vegetationShader);
            foliage.name="V025_Understory";
            foliage.SetColor("_Color",Color.white); foliage.SetFloat("_Wind",.018f);
            // Separate conventional LODGroup assets, never applied to imported native Mesh LOD materials.
            foliage.enableInstancing=true;
            EditorUtility.SetDirty(foliage);
            var prototypes=new Mesh[3,4,3];
            var placementRadii=new[]{2.05f,1.4f,1.2f};
            for(int kind=0;kind<3;kind++) for(int variation=0;variation<4;variation++) for(int lod=0;lod<3;lod++)
            {
                var made=MakePatch(kind,variation,lod);
                string meshPath=MaterialFolder+"/Meshes/"+made.name+".asset";
                var readback=new MeshReadback { assetPath=meshPath,expectedSha256=MeshDataHash(made),vertices=made.vertexCount,
                    indices=(int)made.GetIndexCount(0),kind=kind,variation=variation,lod=lod };
                prototypes[kind,variation,lod]=StoreMesh(made,meshPath);
                readback.assetGuid=AssetDatabase.AssetPathToGUID(meshPath);
                report.meshReadback.Add(readback);
                report.uniqueTrianglesByLod[lod]+=(int)prototypes[kind,variation,lod].GetIndexCount(0)/3;
                report.uniqueMeshes++;
                if(lod==0)
                    foreach(var vertex in prototypes[kind,variation,lod].vertices)
                        placementRadii[kind]=Mathf.Max(placementRadii[kind],vertex.magnitude);
            }
            AssetDatabase.SaveAssets();
            foreach(var readback in report.meshReadback)
            {
                if(!File.Exists(readback.assetPath)) throw new IOException("Mesh asset was not saved: "+readback.assetPath);
                AssetDatabase.ImportAsset(readback.assetPath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                var persisted=AssetDatabase.LoadAssetAtPath<Mesh>(readback.assetPath);
                if(persisted==null) throw new IOException("Mesh asset did not reload: "+readback.assetPath);
                readback.persistedSha256=MeshDataHash(persisted);
                readback.matches=readback.expectedSha256==readback.persistedSha256&&persisted.vertexCount==readback.vertices&&
                    (int)persisted.GetIndexCount(0)==readback.indices&&AssetDatabase.AssetPathToGUID(readback.assetPath)==readback.assetGuid;
                if(!readback.matches) throw new InvalidOperationException("Persisted mesh geometry or GUID mismatch: "+readback.assetPath);
                persisted.UploadMeshData(false);
                prototypes[readback.kind,readback.variation,readback.lod]=persisted;
            }
            report.persistedMeshGeometryVerified=report.meshReadback.Count==36&&report.meshReadback.All(x=>x.matches);
            var root=new GameObject(RootName); SceneManager.MoveGameObjectToScene(root,scene);
            var route=RoutePoints().Select(p=>blenderToUnity.MultiplyPoint3x4(p)).ToArray();
            // Individual architectural bounds keep doorways, foundations and occupied courtyards clear.
            var exclusions=renderers.Where(r=>IsArchitecture(r.name)).Select(r=>r.bounds).ToArray();
            var occupied=new List<(Vector3 point,float radius)>();
            var random=new System.Random(Seed);
            var regions=new[] {
                new Region("Arrival forest edges",13,72,new Vector2(-38,169),new Vector2(15,170),new Vector2(28,146),new Vector2(-40,142)),
                new Region("Outer bailey and garden edges",10,44,new Vector2(-58,47),new Vector2(-12,61),new Vector2(-56,-21),new Vector2(-22,-50)),
                new Region("Chapel and connected slopes",11,30,new Vector2(-109,-50),new Vector2(-98,-69),new Vector2(-62,64),new Vector2(-66,12)),
                new Region("Crown forest edge",9,18,new Vector2(-40,-120),new Vector2(34,-142),new Vector2(-24,-164))
            };
            foreach(var region in regions)
            {
                var result=new RegionResult { name=region.name,requested=region.count };
                report.regions.Add(result);
                for(int attempt=0;attempt<region.count*120&&result.placed<region.count;attempt++)
                {
                    var center=region.centers[(attempt/5)%region.centers.Length];
                    float angle=Next(random,0,Mathf.PI*2),rad=Mathf.Sqrt(Next(random,0,1))*region.radius;
                    var source=new Vector3(center.x+Mathf.Cos(angle)*rad,center.y+Mathf.Sin(angle)*rad,0);
                    var candidate=blenderToUnity.MultiplyPoint3x4(source);
                    int kind=attempt%11==0?2:attempt%4==0?1:0;
                    float scale=Next(random,.72f,1.25f),radius=placementRadii[kind]*scale;
                    if(!sampler.Sample(candidate.x,candidate.z,out var point,out var normal)||normal.y<.86f) continue;
                    float clearance=DistanceToRoute(point,route)-radius;
                    if(clearance<4.2f||clearance>39f) continue;
                    if(occupied.Any(p=>Vector2.Distance(new Vector2(p.point.x,p.point.z),new Vector2(point.x,point.z))<(p.radius+radius)*.72f)) continue;
                    if(exclusions.Any(b=>point.y+.9f>=b.min.y&&point.y<=b.max.y+.2f&&point.x>b.min.x-radius-.35f&&point.x<b.max.x+radius+.35f&&point.z>b.min.z-radius-.35f&&point.z<b.max.z+radius+.35f)) continue;
                    float seatError=0; bool supported=true;
                    for(int probe=0;probe<8;probe++)
                    {
                        float theta=probe*Mathf.PI/4,dx=Mathf.Cos(theta)*radius,dz=Mathf.Sin(theta)*radius;
                        if(!sampler.Sample(point.x+dx,point.z+dz,out var edge,out _)) { supported=false;break; }
                        float expected=point.y-(normal.x*dx+normal.z*dz)/normal.y;
                        seatError=Mathf.Max(seatError,Mathf.Abs(edge.y-expected));
                        if(seatError>.12f) { supported=false;break; }
                    }
                    if(!supported) continue;
                    int variation=random.Next(4);
                    var patch=new GameObject($"{region.name} {KindName(kind)} {result.placed:000}");
                    patch.transform.SetParent(root.transform,false);
                    patch.transform.position=point-Vector3.up*.075f;
                    patch.transform.rotation=Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(0,Next(random,0,360),0);
                    patch.transform.localScale=Vector3.one*scale;
                    var group=patch.AddComponent<LODGroup>();
                    group.fadeMode=LODFadeMode.CrossFade; group.animateCrossFading=false;
                    var lods=new LOD[3];
                    for(int lod=0;lod<3;lod++)
                    {
                        var item=new GameObject("LOD"+lod,typeof(MeshFilter),typeof(MeshRenderer));
                        item.transform.SetParent(patch.transform,false);
                        var mesh=prototypes[kind,variation,lod];
                        item.GetComponent<MeshFilter>().sharedMesh=mesh;
                        var renderer=item.GetComponent<MeshRenderer>(); renderer.sharedMaterial=foliage;
                        renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=true;
                        renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                        renderer.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
                        lods[lod]=new LOD(new[]{.16f,.065f,.025f}[lod],new Renderer[]{renderer}) { fadeTransitionWidth=.30f };
                        report.instanceTrianglesByLod[lod]+=(int)mesh.GetIndexCount(0)/3;
                    }
                    group.SetLODs(lods); group.RecalculateBounds();
                    float approximateCull=group.size*scale/(2*Mathf.Tan(25*Mathf.Deg2Rad)*.025f);
                    report.minimumCullDistanceAt50DegreeFov=Mathf.Min(report.minimumCullDistanceAt50DegreeFov,approximateCull);
                    report.maximumCullDistanceAt50DegreeFov=Mathf.Max(report.maximumCullDistanceAt50DegreeFov,approximateCull);
                    float actualCull=group.size*scale*report.actualQualityLodBias/(2*Mathf.Tan(report.referenceVerticalFieldOfView*.5f*Mathf.Deg2Rad)*.025f);
                    report.minimumEstimatedCullDistance=Mathf.Min(report.minimumEstimatedCullDistance,actualCull);
                    report.maximumEstimatedCullDistance=Mathf.Max(report.maximumEstimatedCullDistance,actualCull);
                    report.rendererInstances+=3;
                    occupied.Add((point,radius)); result.placed++; report.totalPatches++; report.lodGroups++;
                    if(kind==0) report.grassPatches++; else if(kind==1) report.fernPatches++; else report.shrubPatches++;
                    report.minimumRouteEdgeClearance=Mathf.Min(report.minimumRouteEdgeClearance,clearance);
                    report.maximumSeatError=Mathf.Max(report.maximumSeatError,seatError);
                }
            }
            if(report.totalPatches<60) throw new InvalidOperationException("Only "+report.totalPatches+" supported understory patches; inspect source mapping and geometry.");
            report.allVegetationShadowCastingOff=root.GetComponentsInChildren<MeshRenderer>().All(r=>r.shadowCastingMode==ShadowCastingMode.Off);
            report.allVegetationCrossFade=root.GetComponentsInChildren<LODGroup>().All(g=>g.fadeMode==LODFadeMode.CrossFade);
            AssetDatabase.SaveAssets();
            Debug.Log("GROUND_V025 "+JsonUtility.ToJson(report));
            return report;
        }

        static bool IsArchitecture(string name) => name.Length>3&&((name[0]=='A'||name[0]=='B'||name[0]=='D')&&char.IsDigit(name[1])&&char.IsDigit(name[2])||name.StartsWith("Court_",StringComparison.Ordinal)||name.StartsWith("Cloister_",StringComparison.Ordinal)||name.StartsWith("Chapel_Floor",StringComparison.Ordinal)||name.StartsWith("Citadel_",StringComparison.Ordinal));
        static string KindName(int kind) => kind==0?"Short grass":kind==1?"Fern":"Low shrub";
        static float Next(System.Random random,float a,float b) => a+(b-a)*(float)random.NextDouble();
        static float DistanceToRoute(Vector3 p,Vector3[] route)
        {
            float distance=float.PositiveInfinity;
            var q=new Vector2(p.x,p.z);
            for(int i=1;i<route.Length;i++)
            {
                var a=new Vector2(route[i-1].x,route[i-1].z);var b=new Vector2(route[i].x,route[i].z);
                var d=b-a;float t=Mathf.Clamp01(Vector2.Dot(q-a,d)/Mathf.Max(d.sqrMagnitude,.0001f));
                distance=Mathf.Min(distance,Vector2.Distance(q,a+d*t));
            }
            return distance;
        }
        // Authored main route in Blender coordinates (x, -Three.z, Three.y).
        static Vector3[] RoutePoints() => new[] {
            new Vector3(-4,168,0),new Vector3(-12,148,1),new Vector3(14,151,3),new Vector3(34,134,6),new Vector3(30,116,8),new Vector3(5,106,12),new Vector3(-18,112,14),new Vector3(-35,91,18),new Vector3(-35,72,18),new Vector3(-18,58,18),new Vector3(-14,40,20),new Vector3(-36,34,24),new Vector3(-65,43,28),new Vector3(-80,25,28),new Vector3(-80,-8,28),new Vector3(-60,-5,34),new Vector3(-57,-20,34),new Vector3(-35,-20,34),new Vector3(-20,-34,34),new Vector3(-25,-48,34),new Vector3(-46,-56,38),new Vector3(-62,-44,42),new Vector3(-70,-46,44),new Vector3(-76,-38,48),new Vector3(-93,-38,48),new Vector3(-87,-54,48),new Vector3(-87,-66,48),new Vector3(-78,-73,52),new Vector3(-60,-79,54),new Vector3(-45,-70,52),new Vector3(-24,-64,56),new Vector3(-10,-70,60),new Vector3(12,-65,64),new Vector3(23,-69,66),new Vector3(23,-75,66),new Vector3(0,-74,68),new Vector3(0,-86,68),new Vector3(0,-106,68),new Vector3(0,-130,68),new Vector3(0,-143,71),new Vector3(0,-146,71)
        };

        static Material MakeGroundMaterial(Shader shader)
        {
            BakeDetail("Soil",false); BakeDetail("Grass",true); BakeDistribution();
            var material=LoadOrCreateMaterial(MaterialFolder+"/V025_Castle_Forest_Floor.mat",shader);
            material.name="V025_Castle_Forest_Floor"; material.enableInstancing=false;
            material.SetTexture("_SoilTex",AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder+"/Soil-Albedo.png"));
            material.SetTexture("_GrassTex",AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder+"/Grass-Albedo.png"));
            material.SetTexture("_SoilDetail",AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder+"/Soil-Relief.png"));
            material.SetTexture("_GrassDetail",AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder+"/Grass-Relief.png"));
            material.SetTexture("_Distribution",AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder+"/Coverage.png"));
            material.SetFloat("_DetailScale",.5f); material.SetFloat("_NormalStrength",.48f); material.SetFloat("_Coverage",.62f);
            EditorUtility.SetDirty(material); return material;
        }
        static void BakeDetail(string name,bool grass)
        {
            const int n=1024; const float metresPerPixel=2f/n;
            var pixels=new Color[n*n]; var heights=new float[n*n];
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                float u=(float)x/n,v=(float)y/n;
                float coarse=Noise(u,v,8,71),grain=Noise(u,v,240,41),medium=Noise(u,v,48,121);
                var baseColor=grass?new Color(.245f,.280f,.145f):new Color(.27f,.231f,.168f);
                float value=.88f+medium*.16f+grain*.14f;
                pixels[y*n+x]=baseColor*value; pixels[y*n+x].a=1;
                heights[y*n+x]=medium*.0013f+grain*.0007f+coarse*.0007f;
            }
            var random=new System.Random(grass?252:251);
            for(int i=0;i<(grass?1800:4800);i++)
            {
                float x=Next(random,0,n),y=Next(random,0,n),r=Next(random,.7f,3.3f);
                var tint=Color.Lerp(new Color(.22f,.198f,.145f),new Color(.36f,.328f,.252f),Next(random,0,1));
                PaintStroke(pixels,heights,n,new Vector2(x-r,y),new Vector2(x+r,y),r,tint,Next(random,.001f,.004f));
            }
            for(int i=0;i<(grass?16500:1350);i++)
            {
                var a=new Vector2(Next(random,0,n),Next(random,0,n));
                float theta=Next(random,0,Mathf.PI*2),length=Next(random,8,34);
                var d=new Vector2(Mathf.Cos(theta),Mathf.Sin(theta));
                bool dead=Next(random,0,1)<(grass?.19f:.8f);
                var tint=dead?Color.Lerp(new Color(.25f,.213f,.121f),new Color(.37f,.325f,.204f),Next(random,0,1)):
                    Color.Lerp(new Color(.16f,.226f,.09f),new Color(.315f,.36f,.183f),Next(random,0,1));
                PaintStroke(pixels,heights,n,a,a+d*length,Next(random,.55f,1.25f),tint,.0015f);
            }
            var packed=new Color[n*n];
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                float dx=(heights[y*n+(x+1)%n]-heights[y*n+(x+n-1)%n])/(metresPerPixel*2);
                float dz=(heights[((y+1)%n)*n+x]-heights[((y+n-1)%n)*n+x])/(metresPerPixel*2);
                packed[y*n+x]=new Color(.5f-Mathf.Clamp(dx,-.8f,.8f)*.5f,.5f-Mathf.Clamp(dz,-.8f,.8f)*.5f,grass?.95f:.91f,1);
            }
            StoreTexture(name+"-Albedo",pixels,n,true); StoreTexture(name+"-Relief",packed,n,false);
        }
        static void PaintStroke(Color[] pixels,float[] heights,int n,Vector2 a,Vector2 b,float radius,Color tint,float relief)
        {
            int x0=Mathf.FloorToInt(Mathf.Min(a.x,b.x)-radius-1),x1=Mathf.CeilToInt(Mathf.Max(a.x,b.x)+radius+1);
            int y0=Mathf.FloorToInt(Mathf.Min(a.y,b.y)-radius-1),y1=Mathf.CeilToInt(Mathf.Max(a.y,b.y)+radius+1);
            var d=b-a;float denominator=Mathf.Max(d.sqrMagnitude,.001f);
            for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
            {
                var q=new Vector2(x+.5f,y+.5f);float t=Mathf.Clamp01(Vector2.Dot(q-a,d)/denominator);
                float distance=Vector2.Distance(q,a+d*t);float width=radius*(1-t*.65f);
                float alpha=Mathf.Clamp01(width+.6f-distance)*.87f;
                if(alpha<=0)continue;
                int ix=(x%n+n)%n,iy=(y%n+n)%n,index=iy*n+ix;
                pixels[index]=Color.Lerp(pixels[index],tint,alpha);pixels[index].a=1;
                heights[index]+=relief*alpha;
            }
        }
        static void BakeDistribution()
        {
            const int n=256;var pixels=new Color[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float u=(float)x/n,v=(float)y/n;
                pixels[y*n+x]=new Color(Noise(u,v,9,107)*.75f+Noise(u,v,27,121)*.25f,Noise(u,v,11,87),Noise(u,v,7,123),1);
            }
            StoreTexture("Coverage",pixels,n,false);
        }
        static float Noise(float x,float y,int cells,int seed)
        {
            float px=x*cells,py=y*cells;int ix=Mathf.FloorToInt(px),iy=Mathf.FloorToInt(py);
            float u=px-ix,v=py-iy;u=u*u*(3-2*u);v=v*v*(3-2*v);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix%cells,iy%cells,seed),Hash((ix+1)%cells,iy%cells,seed),u),Mathf.Lerp(Hash(ix%cells,(iy+1)%cells,seed),Hash((ix+1)%cells,(iy+1)%cells,seed),u),v);
        }
        static float Hash(int x,int y,int seed)
        {
            unchecked { uint h=(uint)(x*374761393+y*668265263+seed*1442695041);h=(h^(h>>13))*1274126177;return ((h^(h>>16))&0xffffff)/16777215f; }
        }
        static void StoreTexture(string name,Color[] pixels,int size,bool srgb)
        {
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false,!srgb); texture.SetPixels(pixels); texture.Apply();
            string path=TextureFolder+"/"+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default; importer.sRGBTexture=srgb; importer.alphaSource=TextureImporterAlphaSource.None;
            importer.wrapMode=TextureWrapMode.Repeat; importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=8;
            importer.mipmapEnabled=true; importer.textureCompression=TextureImporterCompression.CompressedHQ; importer.maxTextureSize=1024;
            importer.SaveAndReimport();
        }

        sealed class Geometry
        {
            public readonly List<Vector3> vertices=new List<Vector3>();
            public readonly List<Color> colors=new List<Color>();
            public readonly List<int> indices=new List<int>();
            public void Leaf(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color tint,float tipWeight=1)
            {
                int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
                var root=tint*.56f;root.a=0; var tip=tint;tip.a=tipWeight;
                colors.Add(root);colors.Add(tip);colors.Add(tip);colors.Add(root);
                indices.Add(start);indices.Add(start+1);indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+3);
            }
            public void Blade(Vector3 a,Vector3 bend,Vector3 tip,Vector3 side,Color tint)
            {
                int start=vertices.Count;
                vertices.Add(a-side);vertices.Add(a+side);vertices.Add(bend-side*.42f);vertices.Add(bend+side*.42f);vertices.Add(tip);
                var root=tint*.52f;root.a=0;var mid=tint*.91f;mid.a=.5f;var end=tint;end.a=1;
                colors.Add(root);colors.Add(root);colors.Add(mid);colors.Add(mid);colors.Add(end);
                indices.AddRange(new[]{start,start+2,start+1,start+1,start+2,start+3,start+2,start+4,start+3});
            }
            public void CurvedGrassBlade(Vector3 root,Vector3 outward,float height,Vector3 side,Color tint)
            {
                int start=vertices.Count;
                var lower=root+Vector3.up*height*.38f+outward*height*.08f;
                var upper=root+Vector3.up*height*.76f+outward*height*.32f;
                var tip=root+Vector3.up*height*.94f+outward*height*.68f;
                vertices.Add(root-side);vertices.Add(root+side);
                vertices.Add(lower-side*.84f);vertices.Add(lower+side*.84f);
                vertices.Add(upper-side*.43f);vertices.Add(upper+side*.43f);vertices.Add(tip);
                var baseColor=tint*.72f;baseColor.a=0;
                var lowColor=tint*.86f;lowColor.a=.22f;
                var highColor=tint*.96f;highColor.a=.65f;
                var tipColor=tint;tipColor.a=1;
                colors.AddRange(new[]{baseColor,baseColor,lowColor,lowColor,highColor,highColor,tipColor});
                indices.AddRange(new[]{start,start+2,start+1,start+1,start+2,start+3,
                    start+2,start+4,start+3,start+3,start+4,start+5,start+4,start+6,start+5});
            }
        }
        static Mesh MakePatch(int kind,int variation,int lod)
        {
            var g=new Geometry();var random=new System.Random(Seed+kind*1000+variation*37);
            if(kind==0)
            {
                for(int tuft=0;tuft<32;tuft++)
                {
                    float angle=Next(random,0,Mathf.PI*2),r=Mathf.Sqrt(Next(random,0,1))*1.75f;
                    var center=new Vector3(Mathf.Cos(angle)*r,0,Mathf.Sin(angle)*r);
                    var tuftColor=Color.Lerp(new Color(.22f,.285f,.12f),new Color(.34f,.37f,.19f),Next(random,0,1));
                    if(tuft%9==0)tuftColor=Color.Lerp(tuftColor,new Color(.34f,.30f,.17f),.65f);
                    for(int blade=0;blade<12;blade++)
                    {
                        float theta=Next(random,0,Mathf.PI*2),height=Next(random,.14f,.32f),width=Next(random,.015f,.030f);
                        if(blade==0&&tuft%4==0)height*=1.3f;
                        var outward=new Vector3(Mathf.Cos(theta),0,Mathf.Sin(theta));
                        var a=center+outward*Next(random,.03f,.09f);
                        var side=Vector3.Cross(Vector3.up,outward)*width;
                        var tint=tuftColor*Next(random,.93f,1.07f);tint.a=1;
                        if(lod==1&&blade%2!=0||lod==2&&blade%4!=0)continue;
                        g.CurvedGrassBlade(a,outward,height,side,tint);
                    }
                }
            }
            else if(kind==1)
            {
                for(int plant=0;plant<4;plant++)
                {
                    float theta=Next(random,0,Mathf.PI*2);var center=new Vector3(Mathf.Cos(theta),0,Mathf.Sin(theta))*Next(random,.15f,.63f);
                    for(int frond=0;frond<7;frond++)
                    {
                        float a=frond*Mathf.PI*2/7+Next(random,-.15f,.15f),length=Next(random,.48f,.86f),height=Next(random,.20f,.43f);
                        var dir=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var side=Vector3.Cross(Vector3.up,dir);
                        var tint=Color.Lerp(new Color(.17f,.25f,.095f),new Color(.29f,.35f,.15f),Next(random,0,1));
                        if(lod<2||frond%2==0)
                            g.Blade(center,center+dir*length*.48f+Vector3.up*height,center+dir*length+Vector3.up*height*.48f,side*.004f,tint*.63f);
                        for(int leaf=1;leaf<=7;leaf++)
                        {
                            float t=leaf/8f;
                            var stem=center+dir*length*t+Vector3.up*(Mathf.Sin(t*Mathf.PI*.82f)*height);
                            float width=Mathf.Sin(t*Mathf.PI)*length*.22f;
                            if(lod==1&&leaf%2==0||lod==2&&(leaf%3!=1||frond%2!=0))continue;
                            for(int sign=-1;sign<=1;sign+=2)
                            {
                                var end=stem+side*(width*sign)+dir*(length*.11f)+Vector3.up*.025f;
                                g.Leaf(stem-dir*.018f,Vector3.Lerp(stem,end,.55f)-dir*.027f,end,Vector3.Lerp(stem,end,.52f)+dir*.026f,tint,t);
                            }
                        }
                    }
                }
            }
            else
            {
                for(int stem=0;stem<12;stem++)
                {
                    float angle=Next(random,0,Mathf.PI*2),height=Next(random,.34f,.75f),spread=Next(random,.25f,.75f);
                    var dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var side=Vector3.Cross(Vector3.up,dir);
                    if(lod<2||stem%2==0)
                        g.Blade(Vector3.zero,dir*spread*.45f+Vector3.up*height*.6f,dir*spread+Vector3.up*height,side*.006f,new Color(.19f,.17f,.10f));
                    for(int leaf=0;leaf<8;leaf++)
                    {
                        float t=.15f+leaf*.105f;var a=dir*(spread*t)+Vector3.up*(height*t);
                        float size=Next(random,.07f,.14f);var tint=Color.Lerp(new Color(.17f,.235f,.09f),new Color(.29f,.34f,.14f),Next(random,0,1));
                        if(lod==1&&leaf%2!=0||lod==2&&(leaf%3!=0||stem%2!=0))continue;
                        float sign=leaf%2==0?1:-1;var b=a+side*size*sign+dir*size*.5f+Vector3.up*size*.4f;
                        g.Leaf(a,Vector3.Lerp(a,b,.5f)-dir*size*.35f,b,Vector3.Lerp(a,b,.5f)+dir*size*.35f,tint,t);
                    }
                }
            }
            var mesh=new Mesh { name=$"V025_{KindName(kind).Replace(" ","")}_{variation}_LOD{lod}" };
            mesh.SetVertices(g.vertices);mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            // Grass receives the same sky hemisphere as its ground; horizontal normals
            // on thin ribbons otherwise turn whole patches into dark sticks under trees.
            var normals=mesh.normals;
            float upWeight=kind==0?.75f:.45f;
            for(int i=0;i<normals.Length;i++)
            {
                var n=normals[i];n.y=Mathf.Abs(n.y);
                normals[i]=(n*(1-upWeight)+Vector3.up*upWeight).normalized;
            }
            mesh.normals=normals;
            return mesh;
        }
        static Material LoadOrCreateMaterial(string path,Shader shader)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) { material=new Material(shader);AssetDatabase.CreateAsset(material,path); }
            else material.shader=shader;
            return material;
        }
        static Mesh StoreMesh(Mesh made,string path)
        {
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null) { made.UploadMeshData(false);AssetDatabase.CreateAsset(made,path);return made; }
            // CopySerialized updates the serializable CPU representation, but can leave
            // an already resident native graphics buffer unchanged in the same session.
            // Mesh setters notify the render backend while preserving this asset's GUID.
            existing.Clear(false);
            existing.name=made.name;existing.indexFormat=made.indexFormat;existing.lodCount=1;
            existing.SetVertices(made.vertices);existing.SetNormals(made.normals);existing.SetColors(made.colors);
            existing.subMeshCount=1;existing.SetTriangles(made.GetIndices(0,0,true),0,false);existing.bounds=made.bounds;
            existing.UploadMeshData(false);
            Object.DestroyImmediate(made);EditorUtility.SetDirty(existing);return existing;
        }
        static string MeshDataHash(Mesh mesh)
        {
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
                {
                    var vertices=mesh.vertices;var normals=mesh.normals;var colors=mesh.colors;var indices=mesh.GetIndices(0,0,true);
                    writer.Write(vertices.Length);writer.Write(normals.Length);writer.Write(colors.Length);writer.Write(indices.Length);
                    foreach(var value in vertices) { writer.Write(value.x);writer.Write(value.y);writer.Write(value.z); }
                    foreach(var value in normals) { writer.Write(value.x);writer.Write(value.y);writer.Write(value.z); }
                    foreach(var value in colors) { writer.Write(value.r);writer.Write(value.g);writer.Write(value.b);writer.Write(value.a); }
                    foreach(var value in indices) writer.Write(value);
                }
                using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
            }
        }
        static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            var parent=path.Substring(0,path.LastIndexOf('/'));EnsureFolder(parent);AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));
        }
    }
}
