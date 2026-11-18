using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chapter01.Editor
{
    /// <summary>Read-only acceptance of Unity's imported first-person crossbow repair.</summary>
    public static class CastleWeaponRepairValidation
    {
        public const string SourceMeshPath = "Assets/ShadowClaw/Art/HunterWeapon/Crossbow.asset";
        public const string RepairedMeshPath = "Assets/Chapter01/WeaponRepair/Meshes/Crossbow_Repaired.asset";
        public const string RuntimePrefabPath = "Assets/ShadowClaw/Resources/HunterWeapon.prefab";
        const string SourceSha256 = "CF49F671A7049F51FF27426323C214B09124C0B747D911C153D853595FF37E73";

        [Serializable] sealed class Topology
        {
            public int triangles, weldedPositions, openEdges, nonmanifoldEdges, windingConflicts, degenerateTriangles;
        }
        [Serializable] sealed class Report
        {
            public string result = "RUNNING", timestampUtc, unityVersion, failure;
            public string sourceMesh = SourceMeshPath, repairedMesh = RepairedMeshPath, runtimePrefab = RuntimePrefabPath;
            public string sourceMeshSha256, repairedMeshSha256, runtimePrefabSha256;
            public string topologyScope = "Exact Vector3.Equals coordinate welding; submesh 0 is the repaired bow body. The unchanged string in submesh 1 retains its original 16 open end edges. No tolerance welding or whole-mesh closure claim.";
            public int vertices, originalTriangles, repairedTriangles, addedTriangles, blendShapes, blendShapeFrames, uvChannelsCompared;
            public bool runtimeBindingCorrect, sourceFileUnchanged, positionsUnchanged, normalsUnchanged, tangentsUnchanged,
                allUvsUnchanged, colorsUnchanged, skinUnchanged, blendShapesUnchanged, originalTrianglesUnchanged,
                stringIndicesUnchanged, boundsUnchanged, bodyClosed;
            public Topology originalBody, repairedBody, originalString, repairedString;
            public List<string> checks = new List<string>();
        }
        readonly struct Edge : IEquatable<Edge>
        {
            public readonly int a, b;
            public Edge(int x, int y) { a = Math.Min(x, y); b = Math.Max(x, y); }
            public bool Equals(Edge other) => a == other.a && b == other.b;
            public override bool Equals(object value) => value is Edge other && Equals(other);
            public override int GetHashCode() { unchecked { return (a * 397) ^ b; } }
        }
        struct EdgeUse { public int count, orientation; }

        public static void Validate()
        {
            var report = new Report { timestampUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion };
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/WeaponRepair-Verification"));
            try
            {
                void Require(bool condition, string description)
                {
                    if (!condition) throw new InvalidOperationException("Weapon repair validation: " + description);
                    report.checks.Add(description);
                }

                report.sourceMeshSha256 = Hash(SourceMeshPath);
                report.sourceFileUnchanged = report.sourceMeshSha256 == SourceSha256;
                Require(report.sourceFileUnchanged, "Original Crossbow.asset SHA256 matches the before-repair source.");
                report.repairedMeshSha256 = Hash(RepairedMeshPath);
                report.runtimePrefabSha256 = Hash(RuntimePrefabPath);
                var source = AssetDatabase.LoadAssetAtPath<Mesh>(SourceMeshPath);
                var repaired = AssetDatabase.LoadAssetAtPath<Mesh>(RepairedMeshPath);
                Require(source && repaired && source != repaired, "Both the retained source and independent repaired Mesh assets import successfully.");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
                var view = prefab ? prefab.GetComponent<HunterWeaponView>() : null;
                var equipped = Resources.Load<HunterWeaponView>("HunterWeapon");
                report.runtimeBindingCorrect = view && view.Bow && view.Bow.sharedMesh == repaired &&
                    equipped && equipped.Bow && equipped.Bow.sharedMesh == repaired &&
                    AssetDatabase.GetAssetPath(equipped) == RuntimePrefabPath;
                Require(report.runtimeBindingCorrect, "Actual Resources.Load<HunterWeaponView>(HunterWeapon) uses the repaired Bow.sharedMesh.");
                report.vertices = repaired.vertexCount;
                report.originalTriangles = TriangleCount(source);
                report.repairedTriangles = TriangleCount(repaired);
                report.addedTriangles = report.repairedTriangles - report.originalTriangles;
                report.blendShapes = repaired.blendShapeCount;
                Require(source.vertexCount == 20966 && report.vertices == 20966 && report.originalTriangles == 27560 &&
                    report.repairedTriangles == 27662 && report.addedTriangles == 102 && report.blendShapes == 1,
                    "20966 original vertices, 27662 repaired triangles, exactly 102 added faces and one Draw blend shape.");
                Require(source.subMeshCount == 2 && repaired.subMeshCount == 2 && view.Bow.sharedMaterials.Length == 2,
                    "Bow body and string retain their two submeshes and two material slots.");
                for (int submesh = 0; submesh < 2; ++submesh)
                    Require(source.GetTopology(submesh) == MeshTopology.Triangles && repaired.GetTopology(submesh) == MeshTopology.Triangles,
                        "Submesh " + submesh + " retains triangle topology.");

                var sourcePositions = source.vertices;
                var repairedPositions = repaired.vertices;
                report.positionsUnchanged = sourcePositions.SequenceEqual(repairedPositions);
                report.normalsUnchanged = source.normals.SequenceEqual(repaired.normals);
                report.tangentsUnchanged = source.tangents.SequenceEqual(repaired.tangents);
                Require(report.positionsUnchanged && report.normalsUnchanged && report.tangentsUnchanged,
                    "All imported positions, normals and tangents remain exactly equal in their original vertex order.");
                report.allUvsUnchanged = true;
                for (int channel = 0; channel < 8; ++channel)
                {
                    var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                    bool exists = source.HasVertexAttribute(attribute);
                    bool same = exists == repaired.HasVertexAttribute(attribute);
                    if (exists && same)
                        same = source.GetVertexAttributeDimension(attribute) == repaired.GetVertexAttributeDimension(attribute) &&
                            source.GetVertexAttributeFormat(attribute) == repaired.GetVertexAttributeFormat(attribute);
                    var beforeUv = new List<Vector4>(); var afterUv = new List<Vector4>();
                    source.GetUVs(channel, beforeUv); repaired.GetUVs(channel, afterUv);
                    same &= beforeUv.SequenceEqual(afterUv);
                    report.allUvsUnchanged &= same; ++report.uvChannelsCompared;
                    Require(same, "UV channel " + channel + " presence, format, dimension and coordinates remain exactly equal.");
                }
                report.colorsUnchanged = source.colors.SequenceEqual(repaired.colors);
                report.skinUnchanged = source.bindposes.SequenceEqual(repaired.bindposes) && source.boneWeights.SequenceEqual(repaired.boneWeights);
                Require(report.colorsUnchanged && report.skinUnchanged, "Original vertex colors, bind poses and bone weights remain unchanged.");
                Require(source.blendShapeCount == repaired.blendShapeCount, "Blend-shape count remains unchanged.");
                for (int shape = 0; shape < source.blendShapeCount; ++shape)
                {
                    Require(source.GetBlendShapeName(shape) == repaired.GetBlendShapeName(shape) &&
                        source.GetBlendShapeFrameCount(shape) == repaired.GetBlendShapeFrameCount(shape),
                        "Blend-shape name and frame count remain unchanged at slot " + shape + ".");
                    for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); ++frame)
                    {
                        Require(source.GetBlendShapeFrameWeight(shape, frame).Equals(repaired.GetBlendShapeFrameWeight(shape, frame)),
                            "Blend-shape frame weight remains unchanged.");
                        var beforeDelta = new Vector3[source.vertexCount]; var afterDelta = new Vector3[source.vertexCount];
                        var beforeNormal = new Vector3[source.vertexCount]; var afterNormal = new Vector3[source.vertexCount];
                        var beforeTangent = new Vector3[source.vertexCount]; var afterTangent = new Vector3[source.vertexCount];
                        source.GetBlendShapeFrameVertices(shape, frame, beforeDelta, beforeNormal, beforeTangent);
                        repaired.GetBlendShapeFrameVertices(shape, frame, afterDelta, afterNormal, afterTangent);
                        Require(beforeDelta.SequenceEqual(afterDelta) && beforeNormal.SequenceEqual(afterNormal) && beforeTangent.SequenceEqual(afterTangent),
                            "Every vertex, normal and tangent blend-shape delta remains exactly equal.");
                        ++report.blendShapeFrames;
                    }
                }
                report.blendShapesUnchanged = true;
                report.boundsUnchanged = source.bounds.center.Equals(repaired.bounds.center) && source.bounds.extents.Equals(repaired.bounds.extents);
                Require(report.boundsUnchanged, "Original mesh bounds remain unchanged.");
                var originalBody = source.GetTriangles(0); var repairedBody = repaired.GetTriangles(0);
                var originalString = source.GetTriangles(1); var repairedString = repaired.GetTriangles(1);
                report.originalTrianglesUnchanged = repairedBody.Length == originalBody.Length + 306 &&
                    originalBody.SequenceEqual(repairedBody.Take(originalBody.Length));
                report.stringIndicesUnchanged = originalString.SequenceEqual(repairedString);
                Require(report.originalTrianglesUnchanged && report.stringIndicesUnchanged,
                    "All original body triangle indices retain their order; only 102 triangles are appended; string indices are unchanged.");
                report.originalBody = InspectTopology(sourcePositions, originalBody);
                report.repairedBody = InspectTopology(repairedPositions, repairedBody);
                report.originalString = InspectTopology(sourcePositions, originalString);
                report.repairedString = InspectTopology(repairedPositions, repairedString);
                report.bodyClosed = report.originalBody.openEdges == 112 && report.repairedBody.openEdges == 0 &&
                    report.repairedBody.nonmanifoldEdges == 0 && report.repairedBody.windingConflicts == 0 && report.repairedBody.degenerateTriangles == 0;
                Require(report.bodyClosed, "Exact-coordinate body topology changes from 112 open edges to zero, without nonmanifold edges, winding conflicts or degenerate faces.");
                Require(report.originalString.openEdges == 16 && report.repairedString.openEdges == 16,
                    "Original string's 16 open end edges are unchanged and reported separately from the repaired body.");
                report.result = "PASS";
                Debug.Log("CASTLE_WEAPON_REPAIR_VALIDATION_PASS vertices=20966 triangles=27662 bodyOpenEdges=112->0 stringEndEdges=16->16");
            }
            catch (Exception exception)
            {
                report.result = "FAIL"; report.failure = exception.ToString();
                throw;
            }
            finally
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "unity-import-validation.json"), JsonUtility.ToJson(report, true));
            }
        }

        static Topology InspectTopology(Vector3[] vertices, int[] indices)
        {
            if (indices.Length % 3 != 0) throw new InvalidOperationException("Triangle index count is not divisible by three.");
            // Dictionary<Vector3, ...> uses Vector3.Equals, which compares components
            // exactly; Vector3's approximate == operator is deliberately not used.
            var positions = new Dictionary<Vector3, int>(); var welded = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; ++i)
            {
                if (!positions.TryGetValue(vertices[i], out int index))
                { index = positions.Count; positions.Add(vertices[i], index); }
                welded[i] = index;
            }
            var uses = new Dictionary<Edge, EdgeUse>();
            var report = new Topology { triangles = indices.Length / 3, weldedPositions = positions.Count };
            for (int triangle = 0; triangle < indices.Length; triangle += 3)
            {
                int ia = indices[triangle], ib = indices[triangle + 1], ic = indices[triangle + 2];
                int a = welded[ia], b = welded[ib], c = welded[ic];
                if (a == b || b == c || c == a || Vector3.Cross(vertices[ib] - vertices[ia], vertices[ic] - vertices[ia]).sqrMagnitude == 0)
                    ++report.degenerateTriangles;
                Add(a, b); Add(b, c); Add(c, a);
            }
            foreach (var edge in uses.Values)
            {
                if (edge.count == 1) ++report.openEdges;
                if (edge.count > 2) ++report.nonmanifoldEdges;
                if (edge.count == 2 && edge.orientation != 0) ++report.windingConflicts;
            }
            return report;

            void Add(int a, int b)
            {
                var key = new Edge(a, b); uses.TryGetValue(key, out var value);
                ++value.count; value.orientation += a < b ? 1 : -1; uses[key] = value;
            }
        }
        static int TriangleCount(Mesh mesh) => Enumerable.Range(0, mesh.subMeshCount).Sum(i => (int)mesh.GetIndexCount(i)) / 3;
        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Application.dataPath, "..", path)))).Replace("-", "");
        }
    }
}
