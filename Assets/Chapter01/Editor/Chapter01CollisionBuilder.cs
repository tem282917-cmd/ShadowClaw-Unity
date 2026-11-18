using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    // Collision is independent of render LOD. Source FBX assets stay untouched.
    // Audited low-poly landing entrance conflicts use matching visible/collision
    // derivatives that remove only the footprint already occupied by stair treads.
    public static class Chapter01CollisionBuilder
    {
        const string MeshFolder = "Assets/Chapter01/CollisionV025";
        static readonly HashSet<string> Surfaces = new HashSet<string> {
            "Arrival_Ridge_Top", "Outer_Bailey_Rock_Top", "Garden_Rock_Top", "Chapel_Rock_Top",
            "Crown_Promontory_Top", "Upper_Stair_Rock_Top", "Garden_Continuous_Paving",
            "Connected_Lower_Massif", "Connected_Middle_Massif", "Crown_Massif_Foot", "Road_Return_Ridge",
            "Court_Paving", "Cloister_Floor", "Chapel_Floor", "Citadel_Nave_Floor", "Citadel_Transept_Floor",
            "Throne_Apse_Floor", "Throne_Processional_Ramp", "Throne_Dais_Upper_Lip"
        };
        [Serializable] public sealed class Entry { public string source, collisionAsset, method; public int triangles; }
        [Serializable] public sealed class FloorSample { public int segment, intersectedSurfaces; public float t, lateralOffset, expectedHeight, height, error, normalY,centerFloorHeight,lateralStepHeight; public string collider; public bool found, walkable,centerFloorValid; }
        [Serializable] public sealed class Junction
        {
            public int incomingSegment,landingSegment,sourceTriangles,trimmedTriangles;
            public string support,sourceAsset,derivedVisibleAsset,status;
            public string[] sourceMaterials;
            public float maximumObstructionHeight,stairHalfWidth,cutAlongIncoming,removedFootprintSquareMetres;
            public bool trimmed,visibleAndCollisionMatch;
        }
        [Serializable] public sealed class Report
        {
            public string root = "Chapter01_Collision";
            public List<Entry> colliders = new List<Entry>();
            public List<FloorSample> floorSamples = new List<FloorSample>();
            public int triangleCount, missingFloors, unacceptableFloors;
            public float maximumRouteHeightError,maximumLateralStepHeight;
            public List<Junction> junctions = new List<Junction>();
            public string scope = "Static route supports/floors/terrain; four C02 platforms use an extruded convex top footprint. Source FBX assets stay untouched. Only audited low-poly landing entrance conflicts receive matching visible/collision derivatives, subtracting a footprint already occupied by existing stairs. Independent of visual LOD; not a complete architectural obstacle/NavMesh pass.";
            public string floorAcceptance = "Highest ray hit retained, including overlapping surfaces. Centre ground must match the authored route within .42m; lateral probes must be within one legal .42m step of that actual centre tread. Every normal must satisfy 48 degrees. Original error against the linear route is retained even for a legal lateral step.";
        }
        public static Report Apply(Scene scene, Vector3[] route)
        {
            Directory.CreateDirectory(MeshFolder);
            var old = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "Chapter01_Collision");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject("Chapter01_Collision");
            SceneManager.MoveGameObjectToScene(root, scene);
            var report = new Report();
            var sources = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MeshFilter>(true)).ToArray();
            RepairLandingEntrances(sources,route,report);
            foreach (var filter in sources)
            {
                string name = filter.name;
                bool routeSupport = Regex.IsMatch(name, @"^Route_\d+(?:_Step_\d+)?$");
                bool platform = name.StartsWith("C02__", StringComparison.Ordinal);
                if (!routeSupport && !platform && !Surfaces.Contains(name)) continue;
                var mesh = filter.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                // Editor access is supported even when the imported rendering mesh is not runtime-readable.
                var worldVertices = mesh.vertices.Select(v => filter.transform.TransformPoint(v)).ToArray();
                var indices = new List<int>();
                if (platform)
                {
                    float top = worldVertices.Max(v => v.y);
                    var hull = ConvexHull(worldVertices.Where(v => v.y >= top - 0.035f).Select(v => new Vector2(v.x, v.z)));
                    if (hull.Count < 3) throw new InvalidOperationException("No platform top: " + name);
                    worldVertices = hull.Select(v => new Vector3(v.x, top, v.y))
                        .Concat(hull.Select(v => new Vector3(v.x, top - .35f, v.y))).ToArray();
                    int n = hull.Count;
                    for (int i = 1; i < n - 1; ++i)
                    {
                        indices.AddRange(new[] { 0, i + 1, i });
                        indices.AddRange(new[] { n, n + i, n + i + 1 });
                    }
                    for (int i = 0; i < n; ++i)
                    {
                        int j = (i + 1) % n;
                        indices.AddRange(new[] { i, j, n + j, i, n + j, n + i });
                    }
                }
                else
                {
                    bool mirrored = filter.transform.localToWorldMatrix.determinant < 0;
                    for (int sub = 0; sub < mesh.subMeshCount; ++sub)
                    {
                        var tri = mesh.GetTriangles(sub);
                        for (int i = 0; i < tri.Length; i += 3)
                        {
                            int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                            if (Vector3.Cross(worldVertices[b] - worldVertices[a], worldVertices[c] - worldVertices[a]).sqrMagnitude < 1e-12f) continue;
                            indices.Add(a); indices.Add(mirrored ? c : b); indices.Add(mirrored ? b : c);
                        }
                    }
                }
                if (indices.Count == 0) continue;
                var result = new Mesh { name = "Collision_" + name, indexFormat = worldVertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                result.vertices = worldVertices; result.SetTriangles(indices, 0); result.RecalculateBounds();
                string path = MeshFolder + "/" + Regex.Replace(name, "[^A-Za-z0-9_-]", "_") + ".asset";
                result=StoreMesh(result,path);
                var obj = new GameObject("Collision__" + name); obj.transform.SetParent(root.transform, false);
                var collider = obj.AddComponent<MeshCollider>();
                collider.sharedMesh = result;
                collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning |
                    MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
                report.colliders.Add(new Entry { source = name, collisionAsset = path, method = platform ? "convex horizontal top footprint extruded down 0.35m" :
                    report.junctions.Any(x=>x.support==name&&x.trimmed)?"same entrance-trimmed visible support, baked to world coordinates":"source LOD0 support geometry baked to world coordinates", triangles = indices.Count / 3 });
                report.triangleCount += indices.Count / 3;
            }
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            for (int segment = 0; segment < route.Length - 1; ++segment)
            {
                float length = Vector3.Distance(route[segment], route[segment + 1]);
                int steps = Mathf.Max(2, Mathf.CeilToInt(length / .25f));
                var direction = route[segment + 1] - route[segment]; direction.y = 0;
                var lateral = Vector3.Cross(Vector3.up, direction.normalized);
                for (int i = 0; i <= steps; ++i)
                foreach (float offset in new[] { -.28f, 0f, .28f })
                {
                    float t = i / (float)steps;
                    var p = Vector3.Lerp(route[segment], route[segment + 1], t) + lateral * offset;
                    var hits = Physics.RaycastAll(p + Vector3.up * 1.1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(h => h.collider.transform.IsChildOf(root.transform)).OrderBy(h => h.distance).ToArray();
                    var sample = new FloorSample { segment = segment, t = t, lateralOffset = offset, expectedHeight = p.y, found = hits.Length > 0,intersectedSurfaces=hits.Length };
                    if (sample.found)
                    {
                        sample.height = hits[0].point.y; sample.error = sample.height - p.y; sample.collider = hits[0].collider.name;
                        sample.normalY = hits[0].normal.y;
                        var centre=p-lateral*offset;
                        var centerHits=offset==0?hits:Physics.RaycastAll(centre+Vector3.up*1.1f,Vector3.down,3f,~0,QueryTriggerInteraction.Ignore)
                            .Where(h=>h.collider.transform.IsChildOf(root.transform)).OrderBy(h=>h.distance).ToArray();
                        sample.centerFloorHeight=centerHits.Length>0?centerHits[0].point.y:-999;
                        sample.centerFloorValid=centerHits.Length>0&&Mathf.Abs(sample.centerFloorHeight-centre.y)<=.42f&&centerHits[0].normal.y>=Mathf.Cos(48*Mathf.Deg2Rad);
                        sample.lateralStepHeight=sample.height-sample.centerFloorHeight;
                        sample.walkable=sample.centerFloorValid&&Mathf.Abs(sample.lateralStepHeight)<=.42f&&sample.normalY>=Mathf.Cos(48*Mathf.Deg2Rad);
                        report.maximumLateralStepHeight=Mathf.Max(report.maximumLateralStepHeight,Mathf.Abs(sample.lateralStepHeight));
                        report.maximumRouteHeightError = Mathf.Max(report.maximumRouteHeightError, Mathf.Abs(sample.error));
                    }
                    else ++report.missingFloors;
                    if (!sample.walkable) ++report.unacceptableFloors;
                    report.floorSamples.Add(sample);
                }
            }
            return report;
        }
        sealed class StepFootprint { public List<Vector2> polygon; public float top; }
        static void RepairLandingEntrances(MeshFilter[] sources,Vector3[] route,Report report)
        {
            for(int incoming=0;incoming<route.Length-2;incoming++)
            {
                if(route[incoming+1].y-route[incoming].y<.05f||Mathf.Abs(route[incoming+2].y-route[incoming+1].y)>.05f)continue;
                int landing=incoming+1;
                var filter=sources.FirstOrDefault(x=>x.name=="Route_"+landing);
                if(filter==null||filter.sharedMesh==null)continue;
                var audit=new Junction { incomingSegment=incoming,landingSegment=landing,support=filter.name,
                    sourceAsset=AssetDatabase.GetAssetPath(filter.sharedMesh) };
                report.junctions.Add(audit);
                if(audit.sourceAsset.Contains("/VisibleRouteJunction_")) { audit.status="Already trimmed derived support retained";audit.trimmed=true;audit.visibleAndCollisionMatch=true;continue; }
                var stepFilters=sources.Where(x=>Regex.IsMatch(x.name,"^Route_"+incoming+@"_Step_\d+$"))
                    .OrderBy(x=>int.Parse(x.name.Substring(x.name.LastIndexOf('_')+1))).ToArray();
                if(stepFilters.Length==0) { audit.status="No authored stair treads, unchanged";continue; }
                var mesh=filter.sharedMesh;
                audit.sourceTriangles=Enumerable.Range(0,mesh.subMeshCount).Sum(s=>(int)mesh.GetIndexCount(s,0)/3);
                if(audit.sourceTriangles!=12) { audit.status="Not an exact 12-triangle route support box; unchanged";continue; }
                var world=mesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                float top=world.Max(v=>v.y),bottom=world.Min(v=>v.y);
                if(Mathf.Abs(top-route[landing].y)>.1f) { audit.status="Landing elevation differs from route; unchanged";continue; }
                var polygon=ConvexHull(world.Where(v=>v.y>top-.003f).Select(Xz));
                var steps=new List<StepFootprint>();
                foreach(var stepFilter in stepFilters)
                {
                    var points=stepFilter.sharedMesh.vertices.Select(stepFilter.transform.TransformPoint).ToArray();
                    float y=points.Max(v=>v.y);
                    steps.Add(new StepFootprint { top=y,polygon=ConvexHull(points.Where(v=>v.y>y-.003f).Select(Xz)) });
                }
                var direction=route[landing]-route[incoming];direction.y=0;
                float incomingLength=direction.magnitude;direction.Normalize();
                var lateral=Vector3.Cross(Vector3.up,direction);
                var origin=Xz(route[landing]);var forward=Xz(direction);var sideways=Xz(lateral);
                // Check the actual .28m capsule plus .035m skin against the next landing.
                // Only landings that overhang existing treads by more than a legal step are cut.
                for(float back=.05f;back<Mathf.Min(6,incomingLength);back+=.05f)
                {
                    var p=origin-forward*back;
                    if(DistanceToPolygon(p,polygon)>.315f)continue;
                    var supports=steps.Where(x=>DistanceToPolygon(p,x.polygon)<.001f).ToArray();
                    if(supports.Length>0)audit.maximumObstructionHeight=Mathf.Max(audit.maximumObstructionHeight,top-supports.Max(x=>x.top));
                }
                if(audit.maximumObstructionHeight<=.42f) { audit.status="No excessive entrance obstruction within the player capsule corridor";continue; }
                var last=stepFilters[stepFilters.Length-1];var lastWorld=last.sharedMesh.vertices.Select(last.transform.TransformPoint).ToArray();
                audit.stairHalfWidth=lastWorld.Max(v=>Mathf.Abs(Vector3.Dot(v-route[landing],lateral)))+.01f;
                // The final tread extends 1.75cm beyond the waypoint. Retain a 3cm overlap.
                audit.cutAlongIncoming=lastWorld.Max(v=>Vector3.Dot(v-route[landing],direction))-.03f;
                var behind=Clip(polygon,origin,forward,audit.cutAlongIncoming,false);
                var pieces=new List<List<Vector2>> {
                    Clip(polygon,origin,forward,audit.cutAlongIncoming,true),
                    Clip(behind,origin,sideways,-audit.stairHalfWidth,false),
                    Clip(behind,origin,sideways,audit.stairHalfWidth,true)
                }.Where(x=>x.Count>=3&&Area(x)>.0001f).ToList();
                audit.removedFootprintSquareMetres=Area(polygon)-pieces.Sum(Area);
                if(audit.removedFootprintSquareMetres<=.001f) { audit.status="No intersecting support footprint";continue; }
                var renderer=filter.GetComponent<MeshRenderer>();
                audit.sourceMaterials=renderer.sharedMaterials.Select(m=>m==null?"null":m.name+"; alpha="+(m.HasProperty("_Color")?m.GetColor("_Color").a:1)).ToArray();
                var derived=ExtrudeVisibleSupport(filter,pieces,top,bottom);
                audit.derivedVisibleAsset=MeshFolder+"/VisibleRouteJunction_"+filter.name+".asset";
                filter.sharedMesh=StoreMesh(derived,audit.derivedVisibleAsset);
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                audit.trimmedTriangles=Enumerable.Range(0,filter.sharedMesh.subMeshCount).Sum(s=>(int)filter.sharedMesh.GetIndexCount(s)/3);
                audit.trimmed=true;audit.visibleAndCollisionMatch=true;
                audit.status="Removed only the landing entrance footprint occupying the existing incoming stair width; visible material slots/metric UVs retained, C01 treads unchanged. No new horizontal support added.";
            }
        }
        static Vector2 Xz(Vector3 point) => new Vector2(point.x,point.z);
        static float Area(List<Vector2> polygon)
        {
            float sum=0;for(int i=0;i<polygon.Count;i++) { var a=polygon[i];var b=polygon[(i+1)%polygon.Count];sum+=a.x*b.y-a.y*b.x; }
            return Mathf.Abs(sum)*.5f;
        }
        static float DistanceToPolygon(Vector2 point,List<Vector2> polygon)
        {
            bool inside=true;float distance=float.PositiveInfinity;
            for(int i=0;i<polygon.Count;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Count];var d=b-a;
                if(d.x*(point.y-a.y)-d.y*(point.x-a.x)<-.0001f)inside=false;
                float t=Mathf.Clamp01(Vector2.Dot(point-a,d)/Mathf.Max(d.sqrMagnitude,1e-8f));
                distance=Mathf.Min(distance,Vector2.Distance(point,a+d*t));
            }
            return inside?0:distance;
        }
        static List<Vector2> Clip(List<Vector2> polygon,Vector2 origin,Vector2 axis,float threshold,bool positive)
        {
            var result=new List<Vector2>();if(polygon.Count==0)return result;
            for(int i=0;i<polygon.Count;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Count];
                float da=(Vector2.Dot(a-origin,axis)-threshold)*(positive?1:-1);
                float db=(Vector2.Dot(b-origin,axis)-threshold)*(positive?1:-1);
                if(da>=0)result.Add(a);
                if((da>=0)!=(db>=0))result.Add(Vector2.LerpUnclamped(a,b,da/(da-db)));
            }
            return result;
        }
        static Mesh ExtrudeVisibleSupport(MeshFilter filter,List<List<Vector2>> pieces,float top,float bottom)
        {
            var source=filter.sharedMesh;var matrix=filter.transform.localToWorldMatrix;bool mirrored=matrix.determinant<0;
            var original=source.vertices.Select(filter.transform.TransformPoint).ToArray();var sourceUv=source.uv;
            int topSlot=0,bottomSlot=0,sideSlot=0;Vector3[] topTriangle=null;Vector2[] topUvs=null;
            for(int sub=0;sub<source.subMeshCount;sub++)
            {
                var tri=source.GetTriangles(sub);
                for(int i=0;i<tri.Length;i+=3)
                {
                    var a=original[tri[i]];var b=original[tri[i+1]];var c=original[tri[i+2]];
                    var normal=Vector3.Cross(b-a,c-a).normalized*(mirrored?-1:1);
                    if(normal.y>.9f) { topSlot=sub;topTriangle=new[]{a,b,c};if(sourceUv.Length==original.Length)topUvs=new[]{sourceUv[tri[i]],sourceUv[tri[i+1]],sourceUv[tri[i+2]]}; }
                    else if(normal.y<-.9f)bottomSlot=sub;else sideSlot=sub;
                }
            }
            Vector2 TopUv(Vector3 p)
            {
                if(topTriangle==null||topUvs==null)return Xz(p)/6.4f;
                var a=Xz(topTriangle[0]);var b=Xz(topTriangle[1]);var c=Xz(topTriangle[2]);var q=Xz(p);
                float den=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);
                float u=((b.y-c.y)*(q.x-c.x)+(c.x-b.x)*(q.y-c.y))/den;
                float v=((c.y-a.y)*(q.x-c.x)+(a.x-c.x)*(q.y-c.y))/den;
                return topUvs[0]*u+topUvs[1]*v+topUvs[2]*(1-u-v);
            }
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uvs=new List<Vector2>();
            var submesh=Enumerable.Range(0,source.subMeshCount).Select(_=>new List<int>()).ToArray();
            void Face(List<Vector3> points,Vector3 worldNormal,int slot,Func<Vector3,Vector2> uv)
            {
                int start=vertices.Count;
                foreach(var p in points) { vertices.Add(filter.transform.InverseTransformPoint(p));normals.Add(matrix.transpose.MultiplyVector(worldNormal).normalized);uvs.Add(uv(p)); }
                for(int i=1;i<points.Count-1;i++)
                {
                    int b=i,c=i+1;
                    bool reverse=Vector3.Dot(Vector3.Cross(points[b]-points[0],points[c]-points[0]),worldNormal)<0;
                    if(reverse!=mirrored) { int swap=b;b=c;c=swap; }
                    submesh[slot].AddRange(new[]{start,start+b,start+c});
                }
            }
            foreach(var piece in pieces)
            {
                Face(piece.Select(p=>new Vector3(p.x,top,p.y)).ToList(),Vector3.up,topSlot,TopUv);
                Face(piece.Select(p=>new Vector3(p.x,bottom,p.y)).ToList(),Vector3.down,bottomSlot,p=>Xz(p)/6.4f);
                for(int i=0;i<piece.Count;i++)
                {
                    var a=piece[i];var b=piece[(i+1)%piece.Count];var edge=b-a;
                    if(edge.sqrMagnitude<1e-10f)continue;
                    var normal=new Vector3(edge.y,0,-edge.x).normalized;
                    Face(new List<Vector3>{new Vector3(a.x,top,a.y),new Vector3(b.x,top,b.y),new Vector3(b.x,bottom,b.y),new Vector3(a.x,bottom,a.y)},
                        normal,sideSlot,p=>new Vector2(Vector2.Dot(Xz(p)-a,edge.normalized)/6.4f,p.y/6.4f));
                }
            }
            var result=new Mesh { name="RouteJunction_"+filter.name };
            result.SetVertices(vertices);result.SetNormals(normals);result.SetUVs(0,uvs);result.subMeshCount=submesh.Length;
            for(int s=0;s<submesh.Length;s++)result.SetTriangles(submesh[s],s);
            result.RecalculateBounds();result.RecalculateTangents();return result;
        }
        static Mesh StoreMesh(Mesh made,string path)
        {
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null) { AssetDatabase.CreateAsset(made,path);return made; }
            existing.Clear(false);existing.indexFormat=made.indexFormat;existing.name=made.name;existing.lodCount=1;
            existing.SetVertices(made.vertices);
            if(made.normals.Length>0)existing.SetNormals(made.normals);
            if(made.uv.Length>0)existing.SetUVs(0,made.uv);
            if(made.tangents.Length>0)existing.SetTangents(made.tangents);
            existing.subMeshCount=made.subMeshCount;
            for(int sub=0;sub<made.subMeshCount;sub++)existing.SetTriangles(made.GetTriangles(sub),sub,false);
            existing.bounds=made.bounds;
            if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null)existing.UploadMeshData(false);
            UnityEngine.Object.DestroyImmediate(made);EditorUtility.SetDirty(existing);return existing;
        }
        static List<Vector2> ConvexHull(IEnumerable<Vector2> points)
        {
            var p = points.GroupBy(v => (Mathf.RoundToInt(v.x * 10000), Mathf.RoundToInt(v.y * 10000)))
                .Select(x => x.First()).OrderBy(v => v.x).ThenBy(v => v.y).ToArray();
            if (p.Length <= 2) return p.ToList();
            float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            var hull = new List<Vector2>();
            foreach (var v in p) { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], v) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(v); }
            int lower = hull.Count;
            for (int i = p.Length - 2; i >= 0; --i) { while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p[i]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p[i]); }
            hull.RemoveAt(hull.Count - 1); return hull;
        }
    }
}
