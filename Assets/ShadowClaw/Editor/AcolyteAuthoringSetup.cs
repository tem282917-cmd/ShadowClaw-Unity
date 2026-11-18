using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Explicit, narrowly scoped authoring action: no importer callbacks, scene edits
// or full-project refresh. The generated Character prefab is normalized upstream.
public static class AcolyteAuthoringSetup
{
    public const string PrefabPath="Assets/ShadowClaw/Prefabs/Encounters/FireThiefAcolyte.prefab";
    public const string ModelPath="Assets/ShadowClaw/Resources/Acolyte/Character.prefab";
    const string ResourceFolder="Assets/ShadowClaw/Resources/Acolyte";
    [Serializable] sealed class SurfaceSpec {
        public string name,albedo,normal;
        public float[] baseColor;
        public float metallic,roughness=.85f;
    }
    [Serializable] sealed class SurfaceManifest { public SurfaceSpec[] materials; }
    [MenuItem("Shadow Lab/Acolyte/Create Character From Normalized Lux3D FBX")]
    public static void CreateCharacter() {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Create the acolyte character in edit mode.");
        string fbxPath=ResourceFolder+"/FireThiefAcolyte.fbx",specPath=ResourceFolder+"/CharacterMaterials.json";
        if(!File.Exists(fbxPath) || !File.Exists(specPath))throw new InvalidOperationException("Normalized Lux3D FBX and CharacterMaterials.json are required.");
        var spec=JsonUtility.FromJson<SurfaceManifest>(File.ReadAllText(specPath));
        if(spec?.materials==null || spec.materials.Length==0)throw new InvalidOperationException("Character material manifest is empty.");
        var shader=Shader.Find("Standard");
        if(shader==null)throw new InvalidOperationException("The project's Built-in Standard shader is unavailable.");
        string materialsFolder=ResourceFolder+"/Materials";
        if(!AssetDatabase.IsValidFolder(materialsFolder))AssetDatabase.CreateFolder(ResourceFolder,"Materials");
        var materials=new Dictionary<string,Material>(StringComparer.Ordinal);
        foreach(var surface in spec.materials) {
            string name=surface.name;
            foreach(char invalid in Path.GetInvalidFileNameChars())name=name.Replace(invalid,'_');
            string materialPath=materialsFolder+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) {material=new Material(shader);AssetDatabase.CreateAsset(material,materialPath);}
            material.shader=shader;
            material.SetColor("_Color",surface.baseColor!=null && surface.baseColor.Length>=4 ?
                new Color(surface.baseColor[0],surface.baseColor[1],surface.baseColor[2],surface.baseColor[3]) : Color.white);
            material.SetTexture("_MainTex",ImportTexture(surface.albedo,false));
            material.SetFloat("_Metallic",Mathf.Clamp01(surface.metallic));
            material.SetFloat("_Glossiness",Mathf.Clamp01(1-surface.roughness));
            material.SetColor("_EmissionColor",Color.black);material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            var normal=ImportTexture(surface.normal,true);material.SetTexture("_BumpMap",normal);
            if(normal!=null)material.EnableKeyword("_NORMALMAP");else material.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);materials[surface.name]=material;
        }
        AssetDatabase.ImportAsset(fbxPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if(importer==null)throw new InvalidOperationException("The normalized acolyte FBX could not be imported.");
        importer.globalScale=1;importer.useFileScale=true;importer.bakeAxisConversion=true;
        importer.animationType=ModelImporterAnimationType.Legacy;importer.importAnimation=true;
        importer.optimizeGameObjects=false;
        importer.addCollider=false;importer.isReadable=false;
        importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
        foreach(var pair in materials)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),pair.Key),pair.Value);
        importer.SaveAndReimport();
        var takes=importer.defaultClipAnimations;
        var expected=new HashSet<string>(new[]{"Idle","Walk","Cast","Channel","Stagger","Death"});
        foreach(var take in takes) {
            take.name=take.takeName.Substring(take.takeName.LastIndexOf('|')+1);
            if(!expected.Remove(take.name))throw new InvalidOperationException("Unexpected acolyte animation: "+take.takeName);
            bool loop=take.name=="Idle" || take.name=="Walk" || take.name=="Channel";
            take.loopTime=loop;
            take.wrapMode=loop ? WrapMode.Loop : WrapMode.ClampForever;
        }
        if(expected.Count!=0)throw new InvalidOperationException("Missing acolyte animations: "+string.Join(", ",expected));
        importer.clipAnimations=takes;
        importer.SaveAndReimport();
        var imported=AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if(imported==null)throw new InvalidOperationException("The normalized acolyte FBX did not produce a model.");
        var character=new GameObject("Character");
        try {
            var art=(GameObject)PrefabUtility.InstantiatePrefab(imported,character.transform);
            // Unity's FBX axis conversion imports this asset facing -Z. Keep
            // Character's gameplay forward at +Z and correct only the art child.
            art.transform.localRotation=Quaternion.Euler(0,180,0);
            var animation=art.GetComponentInChildren<Animation>(true);
            if(animation==null || animation.GetClip("Cast")==null)throw new InvalidOperationException("Missing acolyte Legacy clips on imported rig.");
            animation.playAutomatically=false;
            animation.cullingType=AnimationCullingType.AlwaysAnimate;
            foreach(var renderer in art.GetComponentsInChildren<Renderer>(true)) {
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                var assigned=renderer.sharedMaterials;
                for(int i=0;i<assigned.Length;i++) {
                    if(assigned[i]!=null && materials.TryGetValue(assigned[i].name,out var matched))assigned[i]=matched;
                    else if(materials.Count==1)foreach(var single in materials.Values)assigned[i]=single;
                }
                renderer.sharedMaterials=assigned;
            }
            PrefabUtility.SaveAsPrefabAsset(character,ModelPath);
        } finally {UnityEngine.Object.DestroyImmediate(character);}
        AssetDatabase.SaveAssets();Rebuild();
        Debug.Log("ACOLYTE_CHARACTER_READY model="+ModelPath);
    }
    static Texture2D ImportTexture(string relative,bool normal) {
        if(string.IsNullOrEmpty(relative))return null;
        string path=ResourceFolder+"/"+relative;
        if(!File.Exists(path))throw new InvalidOperationException("Missing acolyte texture: "+path);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=AssetImporter.GetAtPath(path) as TextureImporter;
        if(importer==null)throw new InvalidOperationException("Unsupported acolyte texture: "+path);
        importer.textureType=normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture=!normal;importer.mipmapEnabled=true;importer.maxTextureSize=2048;
        importer.alphaSource=TextureImporterAlphaSource.None;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    [MenuItem("Shadow Lab/Acolyte/Update Generated Model and Prefab Preview")]
    public static void Rebuild() {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Update the acolyte model in edit mode.");
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if(model==null || model.GetComponentsInChildren<Renderer>(true).Length==0)
            throw new InvalidOperationException("Acolyte Character.prefab must contain the imported, normalized Lux3D model before authoring.");
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try {
            var actor=root.GetComponent<FireThiefAcolyte>();
            if(actor==null)throw new InvalidOperationException("The acolyte encounter prefab is missing its actor.");
            foreach(var old in root.GetComponentsInChildren<EncounterAuthoringPreview>(true))UnityEngine.Object.DestroyImmediate(old.gameObject);
            actor.BodyPrefab=model;
            var preview=new GameObject("Lux3D acolyte authoring preview");preview.transform.SetParent(root.transform,false);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(model,preview.transform);
            instance.transform.localPosition=Vector3.zero;
            foreach(var collider in instance.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var light in instance.GetComponentsInChildren<Light>(true))light.enabled=false;
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach(var animator in instance.GetComponentsInChildren<Animator>(true))animator.enabled=false;
            foreach(var particle in instance.GetComponentsInChildren<ParticleSystem>(true))particle.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            preview.tag="EditorOnly";preview.AddComponent<EncounterAuthoringPreview>();
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        } finally {PrefabUtility.UnloadPrefabContents(root);}
        Debug.Log("ACOLYTE_MODEL_AUTHORING_READY prefab="+PrefabPath+" model="+ModelPath);
    }
}
