using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class EncounterPrefabBuilder
{
    public const string Folder="Assets/ShadowClaw/Prefabs/Encounters";
    static Material body, trim, glow;
    static void EnsureFolder(string path) {
        if(AssetDatabase.IsValidFolder(path))return;
        int split=path.LastIndexOf('/');EnsureFolder(path.Substring(0,split));
        AssetDatabase.CreateFolder(path.Substring(0,split),path.Substring(split+1));
    }
    static Material Material(string name,Color color,bool translucent=false) {
        string path=Folder+"/PreviewMaterials/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat)return mat;
        mat=new Material(Shader.Find("Standard")){name=name,color=color};
        if(translucent) {
            mat.SetFloat("_Mode",3);mat.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);mat.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite",0);mat.EnableKeyword("_ALPHABLEND_ON");mat.renderQueue=3000;
        }
        AssetDatabase.CreateAsset(mat,path);return mat;
    }
    static Transform Part(Transform parent,string name,PrimitiveType shape,Vector3 at,Vector3 scale,Material mat) {
        var obj=GameObject.CreatePrimitive(shape);obj.name=name;obj.transform.SetParent(parent,false);
        obj.transform.localPosition=at;obj.transform.localScale=scale;
        UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
        var r=obj.GetComponent<Renderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;
        return obj.transform;
    }
    static Transform Group(Transform parent,string name,Vector3 at) {
        var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.localPosition=at;return obj.transform;
    }
    static Transform Preview(Transform root) {
        var preview=Group(root,"Authoring Preview (runtime creates animated body)",Vector3.zero);
        preview.gameObject.tag="EditorOnly";preview.gameObject.AddComponent<EncounterAuthoringPreview>();return preview;
    }
    static void Create<T>(string name,Action<T,Transform> setup) where T:Component {
        string path=Folder+"/"+name+".prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(path))return; // Keep user-edited prefab settings.
        var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject(name);
        SceneManager.MoveGameObjectToScene(root,scene);
        try {
            root.SetActive(false);var actor=root.AddComponent<T>();setup(actor,root.transform);
            root.SetActive(true);PrefabUtility.SaveAsPrefabAsset(root,path);
        } finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
    [MenuItem("Shadow Lab/Create Missing Encounter Prefabs")]
    public static void EnsureAll() {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Create encounter prefabs in edit mode.");
        EnsureFolder(Folder+"/PreviewMaterials");
        body=Material("DevelopmentArmour",new Color(.19f,.16f,.23f));
        trim=Material("DevelopmentBrass",new Color(.55f,.42f,.23f));
        glow=Material("DevelopmentLamp",new Color(1,.55f,.14f));
        Create<GroundShadowSwarm>("GroundShadowSwarm",(actor,root)=> {
            actor.BodyPrefab=Resources.Load<GameObject>("HollowWisp/HollowWisp");
            actor.Count=4;actor.MemberAnchors=new Transform[5];var preview=Preview(root);
            for(int i=0;i<5;i++) {
                var p=new Vector3(Mathf.Cos(i*Mathf.PI*2/5),0,Mathf.Sin(i*Mathf.PI*2/5))*actor.FormationRadius;
                actor.MemberAnchors[i]=Group(root,"Member anchor "+(i+1),p);
                var ghost=Group(preview,"Ground shadow "+(i+1),p+Vector3.up*actor.FloatHeight);
                var bodyPreview=UnityEngine.Object.Instantiate(actor.BodyPrefab,ghost,false);
                bodyPreview.name="Hollow Wisp";
            }
        });
        Create<FireThiefAcolyte>("FireThiefAcolyte",(actor,root)=> {
            var preview=Preview(root);
            Part(preview,"Robe",PrimitiveType.Capsule,new Vector3(0,.87f,0),new Vector3(.57f,.76f,.42f),body);
            Part(preview,"Hood",PrimitiveType.Sphere,new Vector3(0,1.65f,0),new Vector3(.52f,.58f,.44f),body);
            Part(preview,"Face",PrimitiveType.Sphere,new Vector3(0,1.64f,.17f),new Vector3(.23f,.29f,.15f),trim);
        });
        Create<FixedLanternGuardian>("FixedLanternGuardian",(actor,root)=> {
            var p=Preview(root);
            Part(p,"Rooted plinth",PrimitiveType.Cylinder,new Vector3(0,.4f,0),new Vector3(3.2f,.4f,2.6f),body);
            for(int side=-1;side<=1;side+=2) {
                Part(p,"Hollow skirt",PrimitiveType.Cube,new Vector3(side*.75f,1.9f,0),new Vector3(.7f,2.6f,1.2f),body);
                Part(p,"Rib",PrimitiveType.Cube,new Vector3(side*1.1f,4.35f,.3f),new Vector3(.34f,2.5f,1.2f),trim);
                Part(p,"Shoulder bell",PrimitiveType.Sphere,new Vector3(side*1.5f,5.8f,0),new Vector3(1.7f,1.4f,1.7f),body);
                Part(p,"Upper arm",PrimitiveType.Cube,new Vector3(side*1.85f,4.6f,0),new Vector3(.7f,2.1f,.8f),body);
                Part(p,"Forearm",PrimitiveType.Cube,new Vector3(side*2.15f,3.1f,.2f),new Vector3(1.2f,1.6f,1.2f),trim);
                Part(p,"Fist",PrimitiveType.Cube,new Vector3(side*2.15f,2.1f,.2f),new Vector3(1.35f,.9f,1.5f),body);
            }
            Part(p,"Breastplate",PrimitiveType.Cube,new Vector3(0,4.6f,-.3f),new Vector3(2.6f,2.8f,.65f),body);
            Part(p,"Helm",PrimitiveType.Cube,new Vector3(0,6.7f,0),new Vector3(1.35f,1.65f,1.2f),body);
            Part(p,"Chest lamp",PrimitiveType.Sphere,new Vector3(0,4.7f,.65f),Vector3.one*.8f,glow);
        });
        Create<EncounterLantern>("EncounterLantern",(lamp,root)=> {
            lamp.Wick=Group(root,"Wick",Vector3.up*1.6f);
            var p=Preview(root);Part(p,"Lamp stand preview",PrimitiveType.Cylinder,new Vector3(0,.8f,0),new Vector3(.15f,.8f,.15f),trim);
            Part(p,"Wick preview",PrimitiveType.Sphere,Vector3.up*1.6f,Vector3.one*.3f,glow);
        });
        Create<AuthoredEncounterTrigger>("AuthoredEncounterTrigger",(trigger,root)=> {
            var box=root.GetComponent<BoxCollider>();if(!box)box=root.gameObject.AddComponent<BoxCollider>();
            box.isTrigger=true;box.center=Vector3.up;box.size=new Vector3(8,2,8);
        });
        AssetDatabase.SaveAssets();Debug.Log("ENCOUNTER_PREFABS_READY "+Folder);
    }
    public static GameObject LoadOrCreate<T>(string name) where T:Component {
        string path=Folder+"/"+typeof(T).Name+".prefab";
        if(!AssetDatabase.LoadAssetAtPath<GameObject>(path))EnsureAll();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var obj=(GameObject)PrefabUtility.InstantiatePrefab(prefab);obj.name=name;return obj;
    }
}
