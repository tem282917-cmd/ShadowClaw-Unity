using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace IronCrownHall {
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class IronHallSurface : MonoBehaviour {
        public TextAsset geometry;
        static readonly Dictionary<TextAsset, Mesh> meshes = new Dictionary<TextAsset, Mesh>();

        void OnEnable() {
            if (geometry == null) return;
            if (!meshes.TryGetValue(geometry, out Mesh mesh) || mesh == null) {
                mesh = ReadMesh(geometry);
                meshes[geometry] = mesh;
            }
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
        static Mesh ReadMesh(TextAsset source) {
            using (var reader = new BinaryReader(new MemoryStream(source.bytes, false))) {
                int vertices = reader.ReadInt32(), indices = reader.ReadInt32();
                if (vertices <= 0 || indices <= 0 || indices % 3 != 0 ||
                    reader.BaseStream.Length != 8L + vertices * 32L + indices * 4L)
                    throw new InvalidDataException(source.name);
                var p = new Vector3[vertices];
                var n = new Vector3[vertices];
                var uv = new Vector2[vertices];
                for (int i = 0; i < vertices; i++) {
                    p[i] = new Vector3(-reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    n[i] = new Vector3(-reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    uv[i] = new Vector2(reader.ReadSingle(), 1f-reader.ReadSingle());
                }
                var tri = new int[indices];
                for (int i = 0; i < indices; i += 3) {
                    tri[i] = reader.ReadInt32();
                    tri[i+2] = reader.ReadInt32();
                    tri[i+1] = reader.ReadInt32();
                }
                var mesh = new Mesh { name = source.name, hideFlags = HideFlags.DontSave };
                mesh.indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.vertices = p; mesh.normals = n; mesh.uv = uv; mesh.triangles = tri;
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}

