using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Chapter01.Editor
{
    // Opt-in folders only. Source GLBs/Blends and other chapters are never modified.
    public sealed class Chapter01LodImportPolicy : AssetPostprocessor
    {
        public const string ModelRoot = "Assets/Chapter01/Models/";

        public struct Profile
        {
            public string name;
            public int maximumLod;
            public int selectionBias;
            public Profile(string name, int maximumLod, int selectionBias)
            { this.name = name; this.maximumLod = maximumLod; this.selectionBias = selectionBias; }
        }

        public static bool TryGetProfile(string path, out Profile profile)
        {
            profile = default;
            path = path.Replace('\\', '/');
            if (!path.StartsWith(ModelRoot, StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase)) return false;
            string category = path.Substring(ModelRoot.Length).Split('/')[0];
            switch (category)
            {
                case "Rocks": profile = new Profile(category, 4, 0); return true;
                case "Trees": profile = new Profile(category, 3, 0); return true;
                case "Distant": profile = new Profile(category, 4, 0); return true;
                case "Architecture": profile = new Profile(category, 2, -1); return true;
                case "Infrastructure": profile = new Profile(category, 4, -1); return true;
                default: return false; // ManualLOD and unclassified models are untouched.
            }
        }

        public override uint GetVersion() => 1;

        public static int GetSelectionBias(Profile profile, string rendererName)
        {
            if (profile.name == "Infrastructure" &&
                (rendererName.StartsWith("C01_", StringComparison.Ordinal) || rendererName.StartsWith("C02_", StringComparison.Ordinal) ||
                 rendererName.StartsWith("C03_", StringComparison.Ordinal) || rendererName.StartsWith("C04_", StringComparison.Ordinal))) return -2;
            return profile.selectionBias;
        }

        void OnPreprocessModel()
        {
            if (!TryGetProfile(assetPath, out var profile)) return;
            var importer = (ModelImporter)assetImporter;
            if (importer.userData.Contains("chapter01.manual-lod"))
            {
                importer.generateMeshLods = false;
                return;
            }
            importer.generateMeshLods = true;
            importer.maximumMeshLod = profile.maximumLod;
            importer.meshLodGenerationFlags = (MeshLodUtility.LodGenerationFlags)0;
            // Do not alter mesh compression, normals, materials, colliders, or original LOD0.
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!TryGetProfile(assetPath, out var profile) ||
                assetImporter.userData.Contains("chapter01.manual-lod")) return;
            if (root.GetComponentInChildren<LODGroup>(true) != null)
            {
                Debug.LogWarning($"{assetPath}: manual LODGroup found. Move this model to Models/ManualLOD; do not combine automatic Mesh LOD and LODGroup.", root);
                return;
            }
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.forceMeshLod = -1;
                renderer.meshLodSelectionBias = GetSelectionBias(profile, renderer.name);
                // Static batching always renders LOD0 in Unity 6.3. Keep the other flags.
                var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.enableInstancing)
                        Debug.LogWarning($"{assetPath}: material '{material.name}' enables classic GPU instancing, which bypasses Mesh LOD. Use a Chapter01 material with instancing disabled or a manual LODGroup.", renderer);
            }
        }
    }
}
