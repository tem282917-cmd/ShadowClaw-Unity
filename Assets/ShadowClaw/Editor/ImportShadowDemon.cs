using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public static class ImportShadowDemon
{
    const string Src="Assets/ShadowClaw/Editor/ShadowDemonSource";
    const string Out="Assets/ShadowClaw/Resources/ShadowDemon";
    const string Receipt="Library/ShadowDemonImport.ok";
    [InitializeOnLoadMethod]
    static void OnReload() { if(!Application.isBatchMode)EditorApplication.delayCall+=ImportPending; }
    internal static void ImportPending()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Src+"/revision.txt"))return;
        string revision=File.ReadAllText(Src+"/revision.txt").Trim();
        if(File.Exists(Receipt) && File.ReadAllText(Receipt).Trim()==revision && File.Exists(Out+"/Character.prefab"))return;
        Build();
    }
    [Serializable] class Bone { public string name; public int parent; public float[] offset; }
    [Serializable] class Track { public int bone; public float[] q; }
    [Serializable] class Motion { public string name; public bool loop; public float[] times; public Track[] tracks; }
    [Serializable] class Part { public int verts,indices,material; }
    [Serializable] class Surface { public int image; public float[] color; public float metal,smooth,cutoff; public bool packedMetal; public string alpha; }
    [Serializable] class Source { public Bone[] bones; public Motion[] clips; public Part[] parts; public Surface[] materials; }

    static T WriteAsset<T>(T fresh,string file) where T:UnityEngine.Object
    {
        string path=Out+"/"+file;
        var existing=AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing==null) { AssetDatabase.CreateAsset(fresh,path); return fresh; }
        EditorUtility.CopySerialized(fresh,existing);
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(fresh);
        return existing;
    }

    static Texture2D Texture(int index,bool metal=false)
    {
        string file=Out+(metal?"/Metallic":"/Albedo")+index+".png";
        string input=Src+(metal?"/metal":"/tex")+index+".png";
        if (!File.Exists(file) || !File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(input)))
        {
            File.Copy(input,file,true);
            AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceSynchronousImport);
        }
        if(metal)
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(file);
            if(importer.sRGBTexture) {importer.sRGBTexture=false;importer.SaveAndReimport();}
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
    }

    static Material MaterialFor(Surface s,int i)
    {
        var shader=Shader.Find("Standard");
        if (shader==null) throw new InvalidOperationException("Standard shader unavailable in this project");
        var m=new Material(shader);
        var c=s.color;m.color=new Color(c[0],c[1],c[2],c[3]);
        if(s.image>=0)m.mainTexture=Texture(s.image);
        m.SetFloat("_Metallic",s.metal);m.SetFloat("_Glossiness",s.smooth);
        if(s.packedMetal) {m.SetTexture("_MetallicGlossMap",Texture(i,true));m.SetFloat("_GlossMapScale",1);m.EnableKeyword("_METALLICGLOSSMAP");}
        if(s.alpha=="BLEND")
        {
            m.SetFloat("_Mode",3);m.SetOverrideTag("RenderType","Transparent");
            m.SetInt("_SrcBlend",(int)BlendMode.One);m.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite",0);m.EnableKeyword("_ALPHAPREMULTIPLY_ON");m.renderQueue=(int)RenderQueue.Transparent;
        }
        else if(s.alpha=="MASK")
        {
            m.SetFloat("_Mode",1);m.SetOverrideTag("RenderType","TransparentCutout");
            m.SetFloat("_Cutoff",s.cutoff);m.EnableKeyword("_ALPHATEST_ON");m.renderQueue=(int)RenderQueue.AlphaTest;
        }
        return WriteAsset(m,"Surface"+i+".mat");
    }

    static Vector3 ReadDirection(BinaryReader r) => new Vector3(-r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    static Mesh ReadMesh(BinaryReader r,Part part,Transform[] bones)
    {
        var mesh=new Mesh {indexFormat=part.verts>65535 ? IndexFormat.UInt32 : IndexFormat.UInt16};
        var pos=new Vector3[part.verts];var normals=new Vector3[part.verts];var uv=new Vector2[part.verts];
        var weights=new BoneWeight[part.verts];var tri=new int[part.indices];
        for(int i=0;i<pos.Length;i++)pos[i]=ReadDirection(r);
        for(int i=0;i<pos.Length;i++)normals[i]=ReadDirection(r);
        for(int i=0;i<pos.Length;i++)uv[i]=new Vector2(r.ReadSingle(),1-r.ReadSingle());
        for(int i=0;i<pos.Length;i++) {weights[i].boneIndex0=r.ReadUInt16();weights[i].boneIndex1=r.ReadUInt16();weights[i].boneIndex2=r.ReadUInt16();weights[i].boneIndex3=r.ReadUInt16();}
        for(int i=0;i<pos.Length;i++) {weights[i].weight0=r.ReadSingle();weights[i].weight1=r.ReadSingle();weights[i].weight2=r.ReadSingle();weights[i].weight3=r.ReadSingle();}
        for(int i=0;i<tri.Length;i+=3) {tri[i]=r.ReadInt32();tri[i+2]=r.ReadInt32();tri[i+1]=r.ReadInt32();}
        mesh.vertices=pos;mesh.normals=normals;mesh.uv=uv;mesh.triangles=tri;
        mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix).ToArray();mesh.boneWeights=weights;
        mesh.RecalculateBounds(); return mesh;
    }

    static AnimationClip Clip(Motion m,Transform[] bones,Transform root)
    {
        var clip=new AnimationClip {name=m.name,legacy=true,frameRate=24,wrapMode=m.loop?WrapMode.Loop:WrapMode.ClampForever};
        string[] fields={"localRotation.x","localRotation.y","localRotation.z","localRotation.w"};
        for(int b=0;b<bones.Length;b++)
        {
            var track=Array.Find(m.tracks,t=>t.bone==b);
            string path=AnimationUtility.CalculateTransformPath(bones[b],root);
            for(int c=0;c<4;c++)
            {
                var keys=new Keyframe[m.times.Length];
                for(int k=0;k<keys.Length;k++)
                {
                    float q=track==null?(c==3?1:0):track.q[k*4+c];
                    keys[k]=new Keyframe(m.times[k],(c==1 || c==2)?-q:q);
                }
                var curve=new AnimationCurve(keys);
                for(int k=0;k<keys.Length;k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);
                }
                clip.SetCurve(path,typeof(Transform),fields[c],curve);
            }
        }
        clip.EnsureQuaternionContinuity();return WriteAsset(clip,m.name+".anim");
    }

    [MenuItem("Shadow Lab/Import bell-sleeved demon")]
    public static void Build()
    {
        var src=JsonUtility.FromJson<Source>(File.ReadAllText(Src+"/rig.json"));
        if(src.bones.Length==0 || src.parts.Length==0 || src.clips.Length==0)throw new InvalidDataException("Empty character");
        Directory.CreateDirectory(Out);AssetDatabase.Refresh();
        var root=new GameObject("Character");
        try
        {
            var bones=new Transform[src.bones.Length];
            for(int i=0;i<bones.Length;i++)
            {
                var spec=src.bones[i];bones[i]=new GameObject(spec.name).transform;
                bones[i].SetParent(spec.parent<0?root.transform:bones[spec.parent],false);
                bones[i].localPosition=new Vector3(-spec.offset[0],spec.offset[1],spec.offset[2]);
            }
            var materials=src.materials.Select(MaterialFor).ToArray();
            using(var reader=new BinaryReader(File.OpenRead(Src+"/geometry.bytes")))
            {
                for(int i=0;i<src.parts.Length;i++)
                {
                    var p=src.parts[i];var obj=new GameObject("Surface "+i);obj.transform.SetParent(root.transform,false);
                    var renderer=obj.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh=WriteAsset(ReadMesh(reader,p,bones),"Geometry"+i+".asset");
                    renderer.bones=bones;renderer.rootBone=bones[0];renderer.sharedMaterial=materials[p.material];
                    renderer.localBounds=new Bounds(new Vector3(0,.83f,0),new Vector3(2.5f,2.2f,2.5f));
                }
                if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Geometry layout mismatch");
            }
            var driver=root.AddComponent<ShadowDemonView>();
            driver.Clips=src.clips.Select(c=>Clip(c,bones,root.transform)).ToArray();
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,Out+"/Character.prefab",out bool saved);
            if(!saved || !prefab)throw new IOException("Could not save character prefab");
            AssetDatabase.SaveAssets();
            var test=UnityEngine.Object.Instantiate(prefab);
            try
            {
                var view=test.GetComponent<ShadowDemonView>();
                var clip=Array.Find(view.Clips,c=>c.name=="Cast");
                var arm=test.GetComponentsInChildren<Transform>().First(t=>t.name=="UpperArm.R");
                clip.SampleAnimation(test,0f);var rest=arm.localRotation;
                clip.SampleAnimation(test,clip.length*.50f);
                if(Quaternion.Angle(rest,arm.localRotation)<15f)throw new InvalidDataException("Cast animation does not lift the sleeve");
                foreach(var r in test.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var baked=new Mesh();
                    try {r.BakeMesh(baked);if(baked.vertexCount!=r.sharedMesh.vertexCount || baked.vertexCount==0)throw new InvalidDataException("Invalid skinned renderer");}
                    finally {UnityEngine.Object.DestroyImmediate(baked);}
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(test);}
            if(File.Exists(Src+"/revision.txt"))File.WriteAllText(Receipt,File.ReadAllText(Src+"/revision.txt").Trim());
            Debug.Log("Shadow Demon imported: "+src.bones.Length+" bones, "+driver.Clips.Length+" clips, "+src.parts.Length+" skinned renderers");
        }
        finally {UnityEngine.Object.DestroyImmediate(root);}
    }

    [MenuItem("Shadow Lab/Build Windows with bell-sleeved demon")]
    public static void BuildGame()
    {
        Build();
        ClawEditor.BuildWindows();
    }
}

public sealed class ShadowDemonImportWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] movedFrom)
    {
        if(!Application.isBatchMode && Array.Exists(imported,s=>s=="Assets/ShadowClaw/Editor/ShadowDemonSource/revision.txt"))
            EditorApplication.delayCall+=ImportShadowDemon.ImportPending;
    }
}

