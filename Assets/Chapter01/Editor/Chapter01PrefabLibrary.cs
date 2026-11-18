using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chapter01.Editor
{
    /// <summary>One-time authoring export from the finished, material-restored scene.</summary>
    public static class Chapter01PrefabLibrary
    {
        public const string Folder = "Assets/Chapter01/Prefabs";
        const string CollisionFolder = Folder + "/CollisionMeshes";

        [Serializable] public sealed class Entry
        {
            public string category, prefab, sourceMesh;
            public string[] sceneObjects;
            public bool includesAuthoredCollision;
        }
        [Serializable] public sealed class Report
        {
            public string scene;
            public int attachedColliders, prefabCount, mappedObjects;
            public List<Entry> entries = new List<Entry>();
        }

        public static Report Generate(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("A loaded scene is required.");
            EnsureFolder(Folder);
            var report = new Report { scene = scene.path, attachedColliders = AttachSceneCollisions(scene) };
            var all = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
            var candidates = all.Where(IsModelObject).ToArray();
            // Foliage is exported at its LODGroup root, preserving all three meshes and transitions.
            var foliage = all.Where(x => x.GetComponent<LODGroup>() &&
                x.GetComponentsInChildren<MeshFilter>(true).Any(m => AssetDatabase.GetAssetPath(m.sharedMesh).StartsWith("Assets/Chapter01/Materials/GroundV025/", StringComparison.Ordinal)));
            candidates = candidates.Concat(foliage).Distinct().ToArray();
            var groups = candidates.GroupBy(ModelKey).OrderBy(g => Category(g.First())).ThenBy(g => g.First().name, StringComparer.Ordinal).ToArray();
            foreach (var group in groups)
            {
                var members = group.OrderBy(x => x.name, StringComparer.Ordinal).ToArray();
                var source = members[0];
                string category = Category(source);
                EnsureFolder(Folder + "/" + category);
                string stem = source.GetComponent<LODGroup>() ? source.GetComponentInChildren<MeshFilter>().sharedMesh.name : source.name;
                string path = Folder + "/" + category + "/" + SafeName(stem) + "_" + Hash128.Compute(group.Key).ToString().Substring(0, 10) + ".prefab";
                SaveAndConnectWithoutReplacing(source, path);
                var entry = new Entry {
                    category = category, prefab = path,
                    sourceMesh = AssetDatabase.GetAssetPath(source.GetComponentInChildren<MeshFilter>(true).sharedMesh),
                    sceneObjects = members.Select(x => HierarchyPath(x)).ToArray(),
                    includesAuthoredCollision = source.GetComponentsInChildren<Collider>(true).Length > 0
                };
                report.entries.Add(entry);
                report.mappedObjects += members.Length;
            }
            report.prefabCount = report.entries.Count;
            var index = new StringBuilder("Scene object\tPrefab\tModel asset\tCategory\tIncludes authored collision\n");
            foreach (var entry in report.entries)
                foreach (string source in entry.sceneObjects)
                    index.Append(source).Append('\t').Append(entry.prefab).Append('\t').Append(entry.sourceMesh)
                        .Append('\t').Append(entry.category).Append('\t').Append(entry.includesAuthoredCollision).Append('\n');
            File.WriteAllText(Folder + "/PrefabIndex.tsv", index.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Folder + "/PrefabIndex.json", JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(Folder + "/PrefabIndex.tsv");
            AssetDatabase.ImportAsset(Folder + "/PrefabIndex.json");
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"CHAPTER01_PREFAB_LIBRARY_PASS: {report.prefabCount} prefabs, {report.mappedObjects} scene objects, {report.attachedColliders} attached colliders. Index: {Folder}/PrefabIndex.tsv");
            return report;
        }

        public static int AttachSceneCollisions(Scene scene)
        {
            EnsureFolder(CollisionFolder);
            var all = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
            var sources = all.Where(x => x.GetComponent<MeshFilter>() && !x.name.StartsWith("Collision__", StringComparison.Ordinal))
                .GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.ToArray());
            var colliders = all.Select(x => x.GetComponent<MeshCollider>()).Where(x => x && x.name.StartsWith("Collision__", StringComparison.Ordinal)).ToArray();
            int attached = 0;
            foreach (var collider in colliders)
            {
                string sourceName = collider.name.Substring("Collision__".Length);
                if (!sources.TryGetValue(sourceName, out var matches) || matches.Length != 1)
                    throw new InvalidOperationException("Collision has no unique source object: " + collider.name);
                var source = matches[0];
                if (collider.transform.parent == source) continue;
                var original = collider.sharedMesh;
                if (!original) throw new InvalidOperationException("Missing collision mesh: " + collider.name);
                var conversion = source.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                var mesh = Object.Instantiate(original);
                mesh.name = "LocalCollision_" + sourceName;
                mesh.vertices = original.vertices.Select(conversion.MultiplyPoint3x4).ToArray();
                // A reflected source needs source-local triangle winding, as with its visible mesh.
                if (conversion.determinant < 0)
                {
                    for (int sub = 0; sub < mesh.subMeshCount; ++sub)
                    {
                        var indices = mesh.GetTriangles(sub);
                        for (int i = 0; i < indices.Length; i += 3) { int value = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = value; }
                        mesh.SetTriangles(indices, sub);
                    }
                }
                mesh.RecalculateBounds();
                string path = CollisionFolder + "/" + SafeName(sourceName) + "_" + Hash128.Compute(sourceName).ToString().Substring(0, 8) + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (!saved) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
                collider.sharedMesh = null;
                collider.transform.SetParent(source, false);
                collider.transform.localPosition = Vector3.zero;
                collider.transform.localRotation = Quaternion.identity;
                collider.transform.localScale = Vector3.one;
                collider.sharedMesh = saved;
                EditorUtility.SetDirty(collider);
                ++attached;
            }
            return attached;
        }

        static bool IsModelObject(Transform value)
        {
            // An attached candle flame is included in its fixture's prefab.
            if (value.name.StartsWith("Candle_Flame__", StringComparison.Ordinal) && value.parent &&
                value.parent.name.StartsWith("D03__Chapel_Candle_", StringComparison.Ordinal)) return false;
            var filter = value.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh || filter.sharedMesh.vertexCount == 0 || !value.GetComponent<MeshRenderer>()) return false;
            string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
            return path.StartsWith("Assets/Chapter01/Models/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/Chapter01/CollisionV025/VisibleRouteJunction_", StringComparison.Ordinal);
        }

        static string ModelKey(Transform source)
        {
            // Model/material variants share one library entry. Distinct collision shapes and
            // practical-light/wick assemblies retain their own prefab instead of losing data.
            var key = new StringBuilder(Category(source));
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                key.Append('|').Append(Identity(filter.sharedMesh));
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer) foreach (var material in renderer.sharedMaterials) key.Append(':').Append(Identity(material));
            }
            foreach (var collider in source.GetComponentsInChildren<MeshCollider>(true)) key.Append("|collision:").Append(Identity(collider.sharedMesh));
            if (source.GetComponentsInChildren<Light>(true).Length > 0 || source.name.StartsWith("D02__Six_Combat_Lantern_", StringComparison.Ordinal))
                key.Append("|assembly:").Append(source.name);
            return key.ToString();
        }

        static string Category(Transform source)
        {
            if (source.GetComponent<LODGroup>()) return "Ground";
            if (Regex.IsMatch(source.name, @"^D\d\d")) return "Props";
            string path = AssetDatabase.GetAssetPath(source.GetComponent<MeshFilter>().sharedMesh);
            string marker = "Assets/Chapter01/Models/";
            if (path.StartsWith(marker, StringComparison.Ordinal)) return path.Substring(marker.Length).Split('/')[0];
            return "Infrastructure";
        }

        static void SaveAndConnectWithoutReplacing(Transform source, string path)
        {
            var parent = source.parent;
            int sibling = source.GetSiblingIndex();
            Vector3 position = source.localPosition, scale = source.localScale;
            Quaternion rotation = source.localRotation;
            try
            {
                // The library root must carry the actual world orientation/scale, including
                // the FBX coordinate conversion. Scene objects and all serialized refs survive.
                source.SetParent(null, true);
                source.position = Vector3.zero;
                PrefabUtility.SaveAsPrefabAssetAndConnect(source.gameObject, path, InteractionMode.AutomatedAction, out bool success);
                if (!success) throw new IOException("Could not save model prefab: " + path);
            }
            finally
            {
                source.SetParent(parent, false);
                source.localPosition = position;
                source.localRotation = rotation;
                source.localScale = scale;
                source.SetSiblingIndex(sibling);
                PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            }
        }

        static string Identity(Object value)
        {
            if (!value) return "null";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id) ? guid + ":" + id : value.name;
        }
        static string HierarchyPath(Transform source) => source.parent ? HierarchyPath(source.parent) + "/" + source.name : source.name;
        static string SafeName(string value)
        {
            string safe = Regex.Replace(value, "[^A-Za-z0-9_-]", "_");
            return safe.Length > 100 ? safe.Substring(0, 100) : safe;
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
