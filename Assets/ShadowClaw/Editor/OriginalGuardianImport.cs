using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// One-time conversion of the approved, single-primitive Lux3D source files.
// Imported Unity assets store their meshes, textures, bind poses and weights.
internal static class OriginalGuardianImport {
    const string Dest = "Assets/ShadowClaw/Resources/Guardian";
    const float Height = 7.8f;
    [Serializable] sealed class Document {
        public MeshInfo[] meshes; public Accessor[] accessors; public View[] bufferViews;
        public ImageInfo[] images; public TextureInfo[] textures; public MaterialInfo[] materials;
    }
    [Serializable] sealed class MeshInfo { public Primitive[] primitives; }
    [Serializable] sealed class Primitive { public Attributes attributes; public int indices, material; }
    [Serializable] sealed class Attributes { public int POSITION, NORMAL, TEXCOORD_0; }
    [Serializable] sealed class Accessor { public int bufferView, byteOffset, componentType, count; public string type; }
    [Serializable] sealed class View { public int byteOffset, byteLength, byteStride; }
    [Serializable] sealed class ImageInfo { public int bufferView; }
    [Serializable] sealed class TextureInfo { public int source; }
    [Serializable] sealed class MaterialInfo { public Pbr pbrMetallicRoughness; }
    [Serializable] sealed class TextureIndex { public int index; }
    [Serializable] sealed class Pbr { public TextureIndex baseColorTexture, metallicRoughnessTexture; public float metallicFactor,roughnessFactor; }

    internal static void BuildOriginalAssets() {
        string[] args=Environment.GetCommandLineArgs();
        int arg=Array.IndexOf(args,"-guardian-originals");
        string folder=arg>=0 ? args[arg+1] : Path.GetFullPath(Path.Combine(Application.dataPath,"../../Design/Chapter01-Production-v03/generation"));
        BuildOne(Path.Combine(folder,"F01","source_glb.glb"), "F01", Height, true);
        BuildOne(Path.Combine(folder,"F02","source_glb.glb"), "F02", 1.38f, false);
    }

    static void BuildOne(string path,string id,float height,bool body) {
        byte[] glb=File.ReadAllBytes(path);
        using var stream=new MemoryStream(glb);
        using var reader=new BinaryReader(stream);
        if(reader.ReadUInt32()!=0x46546c67 || reader.ReadUInt32()!=2)throw new InvalidDataException(path);
        reader.ReadUInt32();
        int jsonSize=reader.ReadInt32();
        if(reader.ReadUInt32()!=0x4e4f534a)throw new InvalidDataException("Missing GLB JSON");
        var doc=JsonUtility.FromJson<Document>(Encoding.UTF8.GetString(reader.ReadBytes(jsonSize)));
        reader.ReadUInt32();
        if(reader.ReadUInt32()!=0x004e4942)throw new InvalidDataException("Missing GLB binary chunk");
        long bin=stream.Position;
        if(doc.meshes.Length!=1 || doc.meshes[0].primitives.Length!=1)throw new InvalidDataException("Unexpected model layout: "+id);
        var prim=doc.meshes[0].primitives[0];

        Accessor Open(int index,string type,int bytes,int component=5126) {
            var a=doc.accessors[index];var view=doc.bufferViews[a.bufferView];
            if(a.type!=type || a.componentType!=component || (view.byteStride!=0 && view.byteStride!=bytes))
                throw new InvalidDataException("Unexpected typed accessor in "+id);
            stream.Position=bin+view.byteOffset+a.byteOffset;
            return a;
        }
        Vector3[] ReadVectors(int index) {
            var a=Open(index,"VEC3",12);var values=new Vector3[a.count];
            for(int i=0;i<values.Length;i++)values[i]=new Vector3(-reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            return values;
        }
        var vertices=ReadVectors(prim.attributes.POSITION);
        var normals=ReadVectors(prim.attributes.NORMAL);
        Bounds bounds=new Bounds(vertices[0],Vector3.zero);
        foreach(var p in vertices)bounds.Encapsulate(p);
        Vector3 origin=bounds.center;
        if(body)origin.y=bounds.min.y;
        float scale=height/bounds.size.y;
        for(int i=0;i<vertices.Length;i++)vertices[i]=(vertices[i]-origin)*scale;
        var uvDef=Open(prim.attributes.TEXCOORD_0,"VEC2",8);
        var uv=new Vector2[uvDef.count];
        for(int i=0;i<uv.Length;i++)uv[i]=new Vector2(reader.ReadSingle(),1-reader.ReadSingle());
        var indexDef=Open(prim.indices,"SCALAR",4,5125);
        var triangles=new int[indexDef.count];
        for(int i=0;i<triangles.Length;i+=3){
            triangles[i]=reader.ReadInt32();triangles[i+2]=reader.ReadInt32();triangles[i+1]=reader.ReadInt32();
        }
        var mesh=new Mesh {name=id+" original geometry",indexFormat=IndexFormat.UInt32};
        mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=triangles;
        mesh.RecalculateBounds();
        var pbr=doc.materials[prim.material].pbrMetallicRoughness;
        byte[] TextureBytes(TextureIndex texture) {
            var image=doc.images[doc.textures[texture.index].source];
            var view=doc.bufferViews[image.bufferView];stream.Position=bin+view.byteOffset;
            return reader.ReadBytes(view.byteLength);
        }
        var main=ImportImage(id+"_Albedo",TextureBytes(pbr.baseColorTexture),false);
        var packed=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
        if(!packed.LoadImage(TextureBytes(pbr.metallicRoughnessTexture)))throw new InvalidDataException("Could not decode "+id+" ORM");
        var pixels=packed.GetPixels32();
        for(int i=0;i<pixels.Length;i++) {
            var p=pixels[i];
            pixels[i]=new Color32((byte)Mathf.RoundToInt(p.b*pbr.metallicFactor),0,0,
                (byte)Mathf.RoundToInt(255-p.g*pbr.roughnessFactor));
        }
        packed.SetPixels32(pixels);packed.Apply();
        var metal=ImportImage(id+"_MetalSmooth",packed.EncodeToPNG(),true);
        UnityEngine.Object.DestroyImmediate(packed);
        var material=new Material(Shader.Find("Standard")) {name=id+" original surface",mainTexture=main};
        material.SetTexture("_MetallicGlossMap",metal);material.SetFloat("_GlossMapScale",1);
        material.EnableKeyword("_METALLICGLOSSMAP");
        // Preserve the engraved colour variation in the chapel's low ambient light.
        material.EnableKeyword("_EMISSION");material.SetTexture("_EmissionMap",main);
        material.SetColor("_EmissionColor",body ? new Color(.28f,.30f,.34f) : new Color(.40f,.31f,.19f));
        material=Persist(material,Dest+"/"+id+"Surface.mat");
        var root=new GameObject(id+(body?" Lamplighter":" Chest Lamp"));
        try {
            if(body) {
                var bones=BuildSkeleton(root.transform);
                var weights=new BoneWeight[vertices.Length];
                var masks=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ShadowClaw/Editor/F01ShoulderWeights.bytes");
                byte[] data=masks!=null?masks.bytes:null;
                if(data==null || data.Length!=weights.Length*2)throw new InvalidDataException("Missing F01 surface weights");
                for(int i=0;i<weights.Length;i++) {
                    float arm=(data[i*2]|(data[i*2+1]<<8))/65535f;
                    weights[i]=Weights(vertices[i]/Height,arm);
                }
                mesh.boneWeights=weights;mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix*root.transform.localToWorldMatrix).ToArray();
                mesh=Persist(mesh,Dest+"/"+id+"Mesh.asset");
                var skin=root.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh=mesh;skin.sharedMaterial=material;skin.bones=bones;skin.rootBone=root.transform;
                skin.localBounds=mesh.bounds;skin.updateWhenOffscreen=true;skin.shadowCastingMode=ShadowCastingMode.Off;
            } else {
                mesh=Persist(mesh,Dest+"/"+id+"Mesh.asset");
                root.AddComponent<MeshFilter>().sharedMesh=mesh;
                var r=root.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;
            }
            PrefabUtility.SaveAsPrefabAsset(root,Dest+"/"+id+".prefab");
        } finally {UnityEngine.Object.DestroyImmediate(root);}
        Debug.Log("Guardian original "+id+" imported "+vertices.Length+" vertices, "+triangles.Length/3+" triangles.");
    }

    static T Persist<T>(T value,string path) where T: UnityEngine.Object {
        var saved=AssetDatabase.LoadAssetAtPath<T>(path);
        if(saved!=null){EditorUtility.CopySerialized(value,saved);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(saved);return saved;}
        AssetDatabase.CreateAsset(value,path);return value;
    }
    static Texture2D ImportImage(string name,byte[] bytes,bool linear) {
        string path=Dest+"/"+name+".png";
        File.WriteAllBytes(path,bytes);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture=!linear;importer.maxTextureSize=2048;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Transform[] BuildSkeleton(Transform root) {
        Transform Bone(string name,Transform parent,float x,float y,float z) {
            var t=new GameObject(name).transform;t.SetParent(parent,false);
            t.position=root.TransformPoint(new Vector3(x,y,z)*Height);return t;
        }
        var pelvis=Bone("Hips",root,0,.46f,0);
        var torso=Bone("Torso",pelvis,0,.535f,0);
        var head=Bone("Head",torso,0,.785f,.005f);
        var bones=new Transform[9];bones[0]=pelvis;bones[1]=torso;bones[2]=head;
        for(int side=0;side<2;side++) {
            int at=side==0?3:6;int sign=side==0?-1:1;string suffix=side==0?".L":".R";
            bones[at]=Bone("UpperArm"+suffix,torso,sign*.112f,.733f,0);
            bones[at+1]=Bone("Forearm"+suffix,bones[at],sign*.161f,.566f,.01f);
            bones[at+2]=Bone("Palm"+suffix,bones[at+1],sign*.183f,.449f,.018f);
        }
        return bones;
    }
    static float Ramp(float min,float max,float value) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(min,max,value));
    static BoneWeight Weights(Vector3 p,float arm) {
        // Shoulder membership is baked along the connected original armour surface.
        // The table is reproducible with tools/prepare_guardian_arm_weights.py.
        int upper=p.x<0 ? 3:6;
        if(arm>0) {
            float forearm=1-Ramp(.54f,.592f,p.y);
            float palm=1-Ramp(.434f,.464f,p.y);
            // Arm/torso boundary only crosses the chest above the belt.
            if(arm<1 && p.y<.54f)
                return new BoneWeight {boneIndex0=upper+2,weight0=arm*palm,boneIndex1=upper+1,weight1=arm*(1-palm),
                    boneIndex2=1,weight2=(1-arm)*Ramp(.495f,.555f,p.y),boneIndex3=0,weight3=(1-arm)*(1-Ramp(.495f,.555f,p.y))};
            return new BoneWeight {boneIndex0=upper,weight0=arm*(1-forearm),boneIndex1=upper+1,weight1=arm*forearm*(1-palm),
                boneIndex2=upper+2,weight2=arm*forearm*palm,boneIndex3=1,weight3=1-arm};
        }
        float torso=Ramp(.495f,.555f,p.y),head=Ramp(.767f,.822f,p.y);
        return new BoneWeight {boneIndex0=0,weight0=1-torso,boneIndex1=1,weight1=torso*(1-head),boneIndex2=2,weight2=torso*head};
    }
}
