using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Chapter01.Editor
{
    /// <summary>Visual-only finishing of the existing throne access ramp.</summary>
    public static class CastleInteriorSurfaceFinisher
    {
        const string ObjectName="Throne_Processional_Ramp";
        const string MeshPath="Assets/Chapter01/CastleWalkwayUpgrade/Meshes/CW_Throne_Processional_Ramp_Visual.asset";

        [Serializable] public sealed class Report
        {
            public string renderer,meshAsset,oldMeshName,oldMeshAsset,oldMeshGuid;
            public long oldMeshLocalFileId;
            public string[] oldMaterialAssets,newMaterialAssets;
            public int sourceTriangles,finishedTriangles,deckTriangles,stoneTriangles,retainedColliders;
            public float maximumLocalVertexCopyError;
            public bool geometryPreserved,transformPreserved,collisionPreserved;
            public string scope="Only the existing ramp rendering mesh/materials receive a same-shape derivative with world-metre UV0. No source FBX, original material, transform, collider, gameplay object, or tread visibility is edited here.";
        }
        sealed class CollisionState
        {
            public Collider collider;
            public Mesh mesh;
            public Matrix4x4 world;
            public bool enabled;
        }

        public static Report Apply(Scene scene,Material deck,Material stone)
        {
            if(!scene.IsValid()||!scene.isLoaded)throw new ArgumentException("A loaded castle scene is required.");
            if(!deck||!stone)throw new ArgumentException("Approved deck and stone materials are required.");
            var filters=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MeshFilter>(true))
                .Where(f=>f.name==ObjectName).ToArray();
            if(filters.Length!=1)throw new InvalidOperationException("Expected one existing "+ObjectName+", found "+filters.Length);
            var filter=filters[0];var renderer=filter.GetComponent<MeshRenderer>();var source=filter.sharedMesh;
            if(!renderer||!source)throw new InvalidOperationException("The retained throne ramp has no rendering mesh.");

            var originalTransform=filter.transform.localToWorldMatrix;
            var collisions=filter.GetComponentsInChildren<Collider>(true).Select(c=>new CollisionState{
                collider=c,mesh=c is MeshCollider mc?mc.sharedMesh:null,world=c.transform.localToWorldMatrix,enabled=c.enabled}).ToArray();
            var report=new Report{renderer=ObjectName,meshAsset=MeshPath,oldMeshName=source.name,
                oldMeshAsset=AssetDatabase.GetAssetPath(source),retainedColliders=collisions.Length,
                oldMaterialAssets=renderer.sharedMaterials.Select(m=>m?AssetDatabase.GetAssetPath(m):"<null>").ToArray(),
                newMaterialAssets=new[]{AssetDatabase.GetAssetPath(deck),AssetDatabase.GetAssetPath(stone)}};
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId);
            report.oldMeshGuid=guid;report.oldMeshLocalFileId=localId;

            // Editor access to imported mesh data does not require enabling runtime
            // Read/Write on the shared FBX. Duplicate face vertices only for UV seams;
            // every triangle corner retains its exact original local coordinate.
            var originalVertices=source.vertices;var originalNormals=source.normals;
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();
            var deckIndices=new List<int>();var stoneIndices=new List<int>();
            var normalMatrix=originalTransform.inverse.transpose;
            for(int sub=0;sub<source.subMeshCount;sub++)
            {
                if(source.GetTopology(sub)!=MeshTopology.Triangles)throw new InvalidOperationException("Ramp source must use triangle topology.");
                var indices=source.GetTriangles(sub);
                for(int index=0;index+2<indices.Length;index+=3)
                {
                    int ia=indices[index],ib=indices[index+1],ic=indices[index+2];
                    var localNormal=Vector3.Cross(originalVertices[ib]-originalVertices[ia],originalVertices[ic]-originalVertices[ia]).normalized;
                    var worldNormal=normalMatrix.MultiplyVector(localNormal).normalized;
                    bool top=worldNormal.y>.55f;
                    var tangent=Vector3.Cross(Vector3.up,worldNormal).normalized;
                    var output=top?deckIndices:stoneIndices;
                    foreach(int oldIndex in new[]{ia,ib,ic})
                    {
                        var local=originalVertices[oldIndex];var world=originalTransform.MultiplyPoint3x4(local);
                        output.Add(vertices.Count);vertices.Add(local);
                        normals.Add(originalNormals.Length==originalVertices.Length?originalNormals[oldIndex]:localNormal);
                        uv.Add(Mathf.Abs(worldNormal.y)>.85f?new Vector2(world.x,world.z):new Vector2(Vector3.Dot(world,tangent),world.y));
                    }
                    report.sourceTriangles++;
                    if(top)report.deckTriangles++;else report.stoneTriangles++;
                }
            }
            if(report.sourceTriangles==0||report.deckTriangles==0)throw new InvalidOperationException("The retained ramp has no valid upward surface.");
            var made=new Mesh{name="CW_Throne_Processional_Ramp_Visual",indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            made.SetVertices(vertices);made.SetNormals(normals);made.SetUVs(0,uv);made.subMeshCount=2;
            made.SetTriangles(deckIndices,0,false);made.SetTriangles(stoneIndices,1,false);made.RecalculateBounds();made.RecalculateTangents();
            var saved=Store(made);
            var persisted=saved.vertices;
            if(persisted.Length!=vertices.Count)throw new InvalidOperationException("Ramp derivative vertex count changed unexpectedly.");
            for(int i=0;i<persisted.Length;i++)report.maximumLocalVertexCopyError=Mathf.Max(report.maximumLocalVertexCopyError,(persisted[i]-vertices[i]).magnitude);
            report.finishedTriangles=(int)(saved.GetIndexCount(0)+saved.GetIndexCount(1))/3;
            report.geometryPreserved=report.maximumLocalVertexCopyError==0&&report.finishedTriangles==report.sourceTriangles;
            if(!report.geometryPreserved)throw new InvalidOperationException("Ramp finishing altered the retained geometry.");

            filter.sharedMesh=saved;renderer.sharedMaterials=new[]{deck,stone};
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(filter);EditorUtility.SetDirty(renderer);
            report.transformPreserved=SameMatrix(originalTransform,filter.transform.localToWorldMatrix);
            report.collisionPreserved=collisions.All(s=>s.collider&&s.collider.enabled==s.enabled&&SameMatrix(s.world,s.collider.transform.localToWorldMatrix)&&
                (!(s.collider is MeshCollider mc)||mc.sharedMesh==s.mesh));
            if(!report.transformPreserved||!report.collisionPreserved)throw new InvalidOperationException("Visual ramp finishing unexpectedly affected transforms or collision.");
            AssetDatabase.SaveAssets();return report;
        }
        static bool SameMatrix(Matrix4x4 a,Matrix4x4 b)
        {for(int i=0;i<16;i++)if(a[i]!=b[i])return false;return true;}
        static Mesh Store(Mesh made)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if(!existing){made.UploadMeshData(false);AssetDatabase.CreateAsset(made,MeshPath);return made;}
            existing.Clear();existing.indexFormat=made.indexFormat;existing.vertices=made.vertices;existing.normals=made.normals;
            existing.uv=made.uv;existing.tangents=made.tangents;existing.subMeshCount=2;
            existing.SetTriangles(made.GetTriangles(0),0,false);existing.SetTriangles(made.GetTriangles(1),1,false);
            existing.bounds=made.bounds;existing.UploadMeshData(false);EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(made);return existing;
        }
    }
}
