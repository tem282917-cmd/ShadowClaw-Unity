using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit authoring command. It does not run on load and never edits the main scene.
public static class GuardianAuthoringSetup {
    const string PrefabPath="Assets/ShadowClaw/Prefabs/Encounters/FixedLanternGuardian.prefab";
    const string Materials="Assets/ShadowClaw/Prefabs/Encounters/GuardianPreviewMaterials";
    const string PreviewPath="Assets/ShadowClaw/Scenes/GuardianSizePreview.unity";
    static void Folder(string path) {
        if(AssetDatabase.IsValidFolder(path))return;
        int split=path.LastIndexOf('/');Folder(path.Substring(0,split));
        AssetDatabase.CreateFolder(path.Substring(0,split),path.Substring(split+1));
    }
    [MenuItem("Shadow Lab/Guardian/Rebuild Guardian Prefab and Size Preview")]
    public static void Rebuild() {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Rebuild the guardian preview in edit mode.");
        PrepareIsolatedUntitledScenes();
        OriginalGuardianImport.BuildOriginalAssets();
        Folder(Materials);Folder("Assets/ShadowClaw/Scenes");
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try {
            foreach(var old in root.GetComponentsInChildren<EncounterAuthoringPreview>(true))UnityEngine.Object.DestroyImmediate(old.gameObject);
            var actor=root.GetComponent<FixedLanternGuardian>();
            actor.BodyPrefab=Resources.Load<GameObject>("Guardian/F01");
            actor.ChestLanternPrefab=Resources.Load<GameObject>("Guardian/F02");
            if(!actor.BodyPrefab || !actor.ChestLanternPrefab)throw new InvalidOperationException("Guardian imported resources are missing.");
            var preview=new GameObject("Imported armour authoring preview");preview.transform.SetParent(root.transform,false);
            preview.transform.localScale=Vector3.one*(actor.BodyHeight/7.8f);
            // Runtime and edit-time previews use exactly the same assembly code.
            var type=typeof(FixedLanternGuardian).Assembly.GetType("FixedGuardianArmour",true);
            Activator.CreateInstance(type,new object[]{preview.transform,actor.BodyPrefab,actor.ChestLanternPrefab});
            foreach(var particles in preview.GetComponentsInChildren<ParticleSystem>(true))if(particles)UnityEngine.Object.DestroyImmediate(particles.gameObject);
            foreach(var fire in preview.GetComponentsInChildren<LanternFire>(true))UnityEngine.Object.DestroyImmediate(fire);
            var saved=new Dictionary<Material,Material>();int count=0;
            foreach(var renderer in preview.GetComponentsInChildren<Renderer>(true)) {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++) {
                    var material=materials[i];if(!material || AssetDatabase.Contains(material))continue;
                    if(!saved.TryGetValue(material,out var persisted)) {
                        string path=Materials+"/Armour_"+(count++)+".mat";
                        persisted=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(persisted)EditorUtility.CopySerialized(material,persisted);
                        else {persisted=new Material(material);AssetDatabase.CreateAsset(persisted,path);}
                        saved.Add(material,persisted);
                    }
                    materials[i]=persisted;
                }
                renderer.sharedMaterials=materials;
            }
            foreach(var material in saved.Keys)UnityEngine.Object.DestroyImmediate(material);
            preview.tag="EditorOnly";preview.AddComponent<EncounterAuthoringPreview>();
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        } finally {PrefabUtility.UnloadPrefabContents(root);}
        SaveComparisonScene();AssetDatabase.SaveAssets();
        Debug.Log("GUARDIAN_AUTHORING_READY prefab="+PrefabPath+" preview="+PreviewPath);
    }
    static void PrepareIsolatedUntitledScenes() {
        var untitled=new List<Scene>();
        for(int i=0;i<SceneManager.sceneCount;i++) {
            var scene=SceneManager.GetSceneAt(i);
            if(scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene) && string.IsNullOrEmpty(scene.path))
                untitled.Add(scene);
        }
        if(untitled.Count==0)return;
        string project=Path.GetFileName(Path.GetDirectoryName(Application.dataPath));
        bool isolatedBatch=Application.isBatchMode &&
            (project.StartsWith("ShadowGameplayVerification-",StringComparison.Ordinal) ||
             project.StartsWith("ShadowRound2PlayerBuild-",StringComparison.Ordinal));
        if(!isolatedBatch)
            throw new InvalidOperationException("Save the current untitled scene yourself before creating the guardian comparison scene. Guardian authoring does not automatically save user scenes.");
        const string scratch="Assets/GuardianAuthoringScratch";
        Folder(scratch);
        foreach(var scene in untitled) {
            string path=AssetDatabase.GenerateUniqueAssetPath(scratch+"/BatchWorkspaceBeforeGuardianPreview.unity");
            if(!EditorSceneManager.SaveScene(scene,path))throw new InvalidOperationException("Could not save the isolated batch workspace: "+path);
            Debug.Log("GUARDIAN_ISOLATED_SCRATCH_SCENE "+project+"/"+path);
        }
    }
    static void SaveComparisonScene() {
        Scene previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try {
            SceneManager.SetActiveScene(scene);
            var boss=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene);
            boss.GetComponent<FixedLanternGuardian>().enabled=false;
            foreach(var preview in boss.GetComponentsInChildren<EncounterAuthoringPreview>(true)) {
                preview.gameObject.tag="Untagged";UnityEngine.Object.DestroyImmediate(preview);
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="24 x 24 metre configurable combat footprint reference";
            floor.transform.position=Vector3.down*.15f;floor.transform.localScale=new Vector3(24,.3f,24);
            var human=GameObject.CreatePrimitive(PrimitiveType.Capsule);human.name="Hunter height reference 1.72 metres";
            human.transform.position=new Vector3(-4,.86f,0);human.transform.localScale=new Vector3(.48f,.86f,.48f);
            var light=new GameObject("Preview key light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;
            light.transform.rotation=Quaternion.Euler(35,-30,0);
            CameraAt("Front preview",new Vector3(0,5,20),new Vector3(0,3.8f,0),48,true);
            CameraAt("Side preview",new Vector3(20,5,0),new Vector3(0,3.8f,0),48,false);
            CameraAt("Player eye height preview",new Vector3(0,1.65f,12),new Vector3(0,4,0),70,false);
            RenderSettings.ambientLight=new Color(.4f,.43f,.5f);
            EditorSceneManager.SaveScene(scene,PreviewPath);
        } finally {
            EditorSceneManager.CloseScene(scene,true);
            if(previous.IsValid() && previous.isLoaded)SceneManager.SetActiveScene(previous);
        }
    }
    static void CameraAt(string name,Vector3 at,Vector3 look,float fov,bool active) {
        var camera=new GameObject(name).AddComponent<Camera>();camera.transform.position=at;camera.transform.LookAt(look);
        camera.fieldOfView=fov;camera.enabled=active;camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.065f,.08f,.11f);
    }
}

// Imported animation remains sampleable; no Animator controller or root motion is added.
public sealed class GuardianModelImport : AssetPostprocessor {
    void OnPreprocessModel() {
        if(assetPath!="Assets/ShadowClaw/Resources/Guardian/KnightCharacter.fbx")return;
        var model=(ModelImporter)assetImporter;
        model.animationType=ModelImporterAnimationType.Legacy;model.importAnimation=true;model.optimizeGameObjects=false;
        model.isReadable=true; // Fit the closed helmet to the authored head weights once.
    }
}
