using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Chapter01.Editor
{
    public static class Chapter01ReviewDiagnostics
    {
        [Serializable] sealed class MeshInfo
        {
            public string meshName;
            public long localFileId, lod0Triangles;
            public int vertexCount, lodCount;
            public long[] submeshLod0Triangles;
            public string[] renderers;
        }
        [Serializable] sealed class DefaultSlot
        {
            public string assetPath, rendererName, meshName;
            public int slot;
            public long meshLod0Triangles, slotLod0Triangles;
        }
        [Serializable] sealed class Report
        {
            public List<MeshInfo> infrastructure = new List<MeshInfo>();
            public List<DefaultSlot> defaultMaterialSlots = new List<DefaultSlot>();
        }

        public static void Run()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Design/Chapter01-Production-v03/engine-lod-validation-v001"));
            var export = JsonUtility.FromJson<Chapter01ReviewScene.Export>(File.ReadAllText(Path.Combine(Application.dataPath, "Chapter01/chapter01-export.json")));
            var report = new Report();
            foreach (var file in export.files)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(file.assetPath);
                var filters = model.GetComponentsInChildren<MeshFilter>(true);
                if (file.category == "Infrastructure")
                {
                    foreach (var group in filters.Where(x => x.sharedMesh != null).GroupBy(x => x.sharedMesh))
                    {
                        var mesh = group.Key;
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId);
                        report.infrastructure.Add(new MeshInfo { meshName = mesh.name, localFileId = localId,
                            vertexCount = mesh.vertexCount, lodCount = mesh.lodCount,
                            submeshLod0Triangles = Counts(mesh), lod0Triangles = Counts(mesh).Sum(),
                            renderers = group.Select(x => x.name).ToArray() });
                    }
                }
                foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    var counts = mesh != null ? Counts(mesh) : Array.Empty<long>();
                    var materials = renderer.sharedMaterials;
                    for (int slot = 0; slot < materials.Length; ++slot)
                        if (materials[slot] == null || materials[slot].name == "Default-Material")
                            report.defaultMaterialSlots.Add(new DefaultSlot { assetPath = file.assetPath, rendererName = renderer.name,
                                meshName = mesh != null ? mesh.name : "<null>", slot = slot, meshLod0Triangles = counts.Sum(),
                                slotLod0Triangles = counts.Length > 0 ? counts[Math.Min(slot, counts.Length - 1)] : 0 });
                }
            }
            File.WriteAllText(Path.Combine(output, "unity-mesh-diagnostic.json"), JsonUtility.ToJson(report, true));
            Debug.Log("CHAPTER01_MESH_DIAGNOSTIC_DONE: " + report.infrastructure.Count + " infrastructure meshes, " + report.defaultMaterialSlots.Count + " default material slots.");
        }

        static long[] Counts(Mesh mesh)
        {
            var counts = new long[mesh.subMeshCount];
            for (int sub = 0; sub < counts.Length; ++sub)
                counts[sub] = mesh.lodCount > 0 ? mesh.GetLod(sub, 0).indexCount / 3 : (long)mesh.GetIndexCount(sub) / 3;
            return counts;
        }
    }
}
