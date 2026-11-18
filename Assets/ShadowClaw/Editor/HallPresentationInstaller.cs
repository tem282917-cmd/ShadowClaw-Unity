using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Explicit authoring only. Does not open, save or replace a scene.</summary>
public static class HallPresentationInstaller
{
    public const string Output="Assets/ShadowClaw/Prefabs/HallPresentation";
    const string Art="Assets/ShadowClaw/Art/GrandLantern/Source";
    const string FogSource="Assets/ShadowClaw/Resources/ChapelBlackMist.mat";
    public const string LanternPrefabPath=Output+"/GrandCombatLantern.prefab";
    public const string GatePrefabPath=Output+"/BossFogGate.prefab";

    [MenuItem("Shadow Lab/Hall presentation/Build lamp and fog gate prefabs")]
    public static void BuildAssets() {
        Directory.CreateDirectory(Output);
        AssetDatabase.Refresh();
        var modelImporter=AssetImporter.GetAtPath(Art+"/lantern_chandelier_01_2k.fbx") as ModelImporter;
        if(modelImporter!=null && !modelImporter.isReadable) {
            modelImporter.isReadable=true;modelImporter.SaveAndReimport();
        }
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/lantern_chandelier_01_2k.fbx");
        var fog=AssetDatabase.LoadAssetAtPath<Material>(FogSource);
        if(source==null || fog==null)throw new InvalidOperationException("Run tools/import_hall_presentation_assets.py before creating the prefabs.");
        var normal=AssetImporter.GetAtPath(Art+"/textures/lantern_chandelier_01_nor_gl_2k.png") as TextureImporter;
        if(normal!=null && normal.textureType!=TextureImporterType.NormalMap) {
            normal.textureType=TextureImporterType.NormalMap;normal.SaveAndReimport();
        }
        var metal=MaterialAsset("Ornate Brass",false);
        var brassTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/textures/lantern_chandelier_01_diff_2k.jpg");
        metal.SetTexture("_MainTex",brassTexture);
        metal.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/textures/lantern_chandelier_01_nor_gl_2k.png"));
        metal.EnableKeyword("_NORMALMAP");metal.SetFloat("_BumpScale",.8f);
        metal.SetFloat("_Metallic",.42f);metal.SetFloat("_Glossiness",.4f);
        // Reuse the authored surface texture as faint, non-illuminating fill.
        // The ornament remains readable when the six actual lights are off.
        metal.SetTexture("_EmissionMap",brassTexture);
        metal.SetColor("_EmissionColor",new Color(.28f,.30f,.36f));metal.EnableKeyword("_EMISSION");
        metal.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
        var glass=MaterialAsset("Lantern Glass",true);
        glass.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/textures/lantern_chandelier_01_glass_diff_2k.png"));
        glass.color=new Color(.8f,.86f,.9f,.30f);
        glass.SetFloat("_Glossiness",.55f);
        glass.SetColor("_EmissionColor",new Color(.8f,.26f,.05f));glass.EnableKeyword("_EMISSION");
        glass.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
        var root=new GameObject("Grand Combat Lantern");
        try {
            var view=root.AddComponent<GrandCombatLantern>();
            var model=(GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(root.transform,false);
            var surfaces=model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds=CombinedBounds(surfaces);
            if(bounds.size.y<.05f)throw new InvalidOperationException("Imported lantern has no visible height.");
            model.transform.localScale*=4f/bounds.size.y;
            bounds=CombinedBounds(surfaces);
            model.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            foreach(var surface in surfaces) {
                var original=surface.sharedMaterials;
                surface.sharedMaterials=original.Select(material=>
                    material!=null && (material.name.ToLowerInvariant().Contains("glass") || material.name.EndsWith("_lamp",StringComparison.OrdinalIgnoreCase)) ? glass : metal).ToArray();
                surface.shadowCastingMode=ShadowCastingMode.On;
            }
            view.PrimaryRenderer=surfaces.First(renderer=>renderer.sharedMaterials.Contains(metal));
            view.GlassSurfaces=surfaces.Where(renderer=>renderer.sharedMaterials.Contains(glass)).ToArray();
            if(view.GlassSurfaces.Length==0)throw new InvalidOperationException("Imported lantern glass material slot was not found.");
            // The original asset's transparent central chamber gives the readable target.
            var chamber=MaterialBounds(view.GlassSurfaces,glass);
            view.Wick=new GameObject("Combat Wick").transform;
            view.Wick.SetParent(root.transform,false);
            view.Wick.position=chamber.center-Vector3.up*.45f;
            view.TargetRadius=.9f;view.FlameScale=3.8f;
            view.FrameEmission=metal.GetColor("_EmissionColor");
            PrefabUtility.SaveAsPrefabAsset(root,LanternPrefabPath);
        } finally {UnityEngine.Object.DestroyImmediate(root);}

        root=new GameObject("Boss Fog Gate");
        try {
            var gate=root.AddComponent<BossFogGate>();gate.CurtainMaterial=fog;
            gate.Width=7.6f;gate.Height=13.2f;gate.Thickness=.6f;gate.EntryClearance=.7f;
            var barrier=new GameObject("Sealed mist collision");barrier.transform.SetParent(root.transform,false);
            gate.Barrier=barrier.AddComponent<BoxCollider>();
            gate.Barrier.center=Vector3.up*gate.Height*.5f;
            gate.Barrier.size=new Vector3(gate.Width,gate.Height,gate.Thickness);
            gate.Barrier.enabled=false;
            PrefabUtility.SaveAsPrefabAsset(root,GatePrefabPath);
        } finally {UnityEngine.Object.DestroyImmediate(root);}
        EditorUtility.SetDirty(metal);EditorUtility.SetDirty(glass);
        AssetDatabase.SaveAssets();
        Debug.Log("HALL_PRESENTATION_PREFABS_READY "+LanternPrefabPath+" | "+GatePrefabPath);
    }
    static Material MaterialAsset(string name,bool transparent) {
        string path=Output+"/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null) {material=new Material(Shader.Find("Standard")) {name=name};AssetDatabase.CreateAsset(material,path);}
        if(transparent) {
            material.SetFloat("_Mode",2);
            material.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite",0);material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue=(int)RenderQueue.Transparent;
        }
        return material;
    }
    static Bounds CombinedBounds(Renderer[] surfaces) {
        Bounds bounds=surfaces[0].bounds;
        foreach(var surface in surfaces)bounds.Encapsulate(surface.bounds);
        return bounds;
    }
    static Bounds MaterialBounds(Renderer[] surfaces,Material material) {
        Bounds bounds=default;bool found=false;
        foreach(var surface in surfaces) {
            var filter=surface.GetComponent<MeshFilter>();
            if(filter==null || filter.sharedMesh==null)continue;
            var mesh=filter.sharedMesh;var vertices=mesh.vertices;var materials=surface.sharedMaterials;
            for(int slot=0;slot<Mathf.Min(materials.Length,mesh.subMeshCount);slot++) {
                if(materials[slot]!=material)continue;
                foreach(int vertex in mesh.GetIndices(slot)) {
                    var position=surface.transform.TransformPoint(vertices[vertex]);
                    if(!found) {bounds=new Bounds(position,Vector3.zero);found=true;} else bounds.Encapsulate(position);
                }
            }
        }
        return found ? bounds : CombinedBounds(surfaces);
    }

    public static string[] CheckPrefabs() {
        var rows=new List<string>();
        void Check(bool condition,string label) {
            if(!condition)throw new InvalidOperationException("FAIL hall presentation prefab: "+label);
            rows.Add("PASS hall presentation prefab: "+label);
        }
        var lampAsset=AssetDatabase.LoadAssetAtPath<GameObject>(LanternPrefabPath);
        Check(lampAsset!=null,"large lamp prefab is available");
        var lamp=(GameObject)PrefabUtility.InstantiatePrefab(lampAsset);
        try {
            var view=lamp.GetComponent<GrandCombatLantern>();
            Check(view!=null && view.Wick!=null && view.PrimaryRenderer!=null,"large lamp has a wick and a visible target");
            var bounds=CombinedBounds(lamp.GetComponentsInChildren<Renderer>(true));
            Check(Mathf.Abs(bounds.size.y-4)<.015f,"original ornate model has a four-metre body");
            Check(bounds.Contains(view.Wick.position) && view.TargetRadius>=.85f,"shooting target sits inside the large lamp body");
            Check(lamp.GetComponentsInChildren<Light>(true).Length==0,"replacement art adds no extra shadow projection light");
            var block=new MaterialPropertyBlock();
            view.SetStrength(0);
            int glassSlots=0;
            foreach(var renderer in view.GlassSurfaces) {
                var materials=renderer.sharedMaterials;
                for(int slot=0;slot<materials.Length;slot++) {
                    if(materials[slot]==null || !materials[slot].name.Contains("Glass"))continue;
                    renderer.GetPropertyBlock(block,slot);var emission=block.GetColor("_EmissionColor");
                    Check(emission.b>emission.r && emission.r>emission.g,"extinguished glass gives a violet visual cue");
                    glassSlots++;
                }
            }
            Check(glassSlots>0,"the original glass material slots are preserved");
        } finally {UnityEngine.Object.DestroyImmediate(lamp);}
        var gateAsset=AssetDatabase.LoadAssetAtPath<GameObject>(GatePrefabPath);
        Check(gateAsset!=null,"visible mist gate prefab is available");
        var gate=gateAsset.GetComponent<BossFogGate>();
        Check(gate.CurtainMaterial!=null,"gate references its scrolling mist material");
        Check(Mathf.Abs(gate.Width-7.6f)<.001f && Mathf.Abs(gate.Height-13.2f)<.001f,"mist fills the measured entrance opening");
        Check(gate.Barrier!=null && !gate.Barrier.enabled &&
            Vector3.Distance(gate.Barrier.size,new Vector3(gate.Width,gate.Height,gate.Thickness))<.001f,
            "gate starts open with a matching battle barrier");
        return rows.ToArray();
    }

    // Caller owns scene backup/save and exact measured gate dimensions.
    // No new monsters are placed and no original architecture is moved.
    [MenuItem("Shadow Lab/Hall presentation/Apply to current Chapter01 scene")]
    public static void ApplyToCurrentScene() {
        var scene=SceneManager.GetActiveScene();
        if(scene.name!="Chapter01_Main")throw new InvalidOperationException("Select the current Chapter01_Main scene before applying hall presentation.");
        var hall=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<IronHallEncounter>(true)).Single();
        var lab=hall.GetComponent<ShadowLab>();
        if(hall.Lanterns.Length!=6 || hall.GalleryLights.Length!=6)throw new InvalidOperationException("Expected six authored combat lights.");
        BuildAssets();
        var lampPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(LanternPrefabPath);
        Undo.RecordObject(hall,"Bind combat lanterns");
        var retiredLightObjects=new HashSet<GameObject>();
        for(int slot=0;slot<6;slot++) {
            var prior=hall.Lanterns[slot];
            var presentation=prior.GetComponentInParent<GrandCombatLantern>();
            if(presentation==null) {
                Bounds bounds=prior.bounds;
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(lampPrefab,scene);
                instance.name="Grand Combat Lantern "+(slot+1);
                instance.transform.position=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                instance.transform.SetParent(prior.transform,true);
                Undo.RegisterCreatedObjectUndo(instance,"Install large combat lamp");
                presentation=instance.GetComponent<GrandCombatLantern>();
                Undo.RecordObject(prior,"Hide replaced small lamp");prior.enabled=false;

                // Place a new lamp clear of the columns. Later installs keep authored placements.
                var lampBounds=presentation.PrimaryRenderer.bounds;
                float inwardX=(lampBounds.center.x<0 ? -1 : 1)*10.2f;
                presentation.transform.position+=new Vector3(inwardX-lampBounds.center.x,70.5f-lampBounds.min.y,0);
                PrefabUtility.RecordPrefabInstancePropertyModifications(presentation.transform);
            }
            var previousLight=hall.GalleryLights[slot];
            var wickLight=presentation.Wick.GetComponent<Light>();
            if(wickLight==null) {
                wickLight=Undo.AddComponent<Light>(presentation.Wick.gameObject);
                EditorUtility.CopySerialized(previousLight,wickLight);
            }
            Undo.RecordObject(presentation,"Bind combat lantern light");
            presentation.GameplayLight=wickLight;
            hall.GalleryLights[slot]=wickLight;
            if(previousLight!=wickLight) {
                // The encounter owns the six sources, even while far from the camera.
                foreach(var budget in hall.gameObject.scene.GetRootGameObjects()
                    .SelectMany(root=>root.GetComponentsInChildren<Chapter01.Rendering.Chapter01PracticalLightBudget>(true))) {
                    if(!budget.lights.Contains(previousLight) && !budget.lights.Contains(wickLight))continue;
                    Undo.RecordObject(budget,"Remove combat lamp from scenery light budget");
                    budget.lights=budget.lights.Where(light=>light!=previousLight && light!=wickLight).ToArray();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(budget);
                }
                Undo.DestroyObjectImmediate(previousLight);
            }
            var originalFixture=presentation.transform.parent;
            if(originalFixture!=null && originalFixture.name.StartsWith("D02__Six_Combat_Lantern_",StringComparison.Ordinal))
                foreach(Transform child in originalFixture)
                    if(child.name.StartsWith("WarmPool__D02__Six_Combat_Lantern_",StringComparison.Ordinal) &&
                        child.childCount==0 && child.GetComponents<Component>().Length==1)
                        retiredLightObjects.Add(child.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(presentation);
            EditorUtility.SetDirty(presentation);
            hall.WickAnchors[slot]=presentation.Wick;
            hall.Lanterns[slot]=presentation.PrimaryRenderer;
        }
        RemoveUnreferencedLightObjects(scene,retiredLightObjects);
        var gates=hall.GetComponentsInChildren<BossFogGate>(true);
        BossFogGate gate;
        if(gates.Length==0) {
            var gatePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(GatePrefabPath);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(gatePrefab,scene);
            instance.transform.SetParent(hall.transform,true);
            instance.transform.position=new Vector3(0,68,83.475f);
            instance.transform.rotation=Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(instance,"Install visible boss fog gate");
            gate=instance.GetComponent<BossFogGate>();
        } else gate=gates.Single();
        gate.Encounter=lab;
        // Serialized hookup is done by the integrating caller on IronHallEncounter.
        EditorUtility.SetDirty(hall);EditorUtility.SetDirty(gate);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("HALL_PRESENTATION_APPLIED_CURRENT_SCENE: scene remains open and unsaved for the caller.");
    }

    static void RemoveUnreferencedLightObjects(Scene scene,HashSet<GameObject> retired) {
        if(retired.Count==0)return;
        foreach(var root in scene.GetRootGameObjects())
            foreach(var component in root.GetComponentsInChildren<Component>(true)) {
                // Transforms contain the structural parent/child references of every scene object.
                if(!component || component is Transform)continue;
                using(var serialized=new SerializedObject(component)) {
                    var property=serialized.GetIterator();
                    while(property.Next(true)) {
                        if(property.propertyType!=SerializedPropertyType.ObjectReference)continue;
                        var value=property.objectReferenceValue;
                        if(value is Transform target)retired.Remove(target.gameObject);
                        else if(value is GameObject referencedObject)retired.Remove(referencedObject);
                        if(retired.Count==0)return;
                    }
                }
            }
        foreach(var unused in retired)Undo.DestroyObjectImmediate(unused);
    }
}
