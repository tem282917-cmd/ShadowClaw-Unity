using System;
using System.IO;
using UnityEngine;

// Standalone check uses the equipped bow, animated character, receiver and actual fire-arrow hit.
public static class ShadowBurnCheck {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() {
        var args=Environment.GetCommandLineArgs();
        if(Array.IndexOf(args,"-check-shadow-fire")<0)return;
        try {
            var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
            var camera=lab.GetComponentInChildren<Camera>();
            var burn=lab.GetComponentInChildren<ShadowBurnFx>();
            var floor=lab.transform.Find("Floor demon").GetComponent<MeshRenderer>();
            var mask=(Texture2D)floor.sharedMaterial.mainTexture;
            Check(mask && mask.width>=128 && floor.enabled,"animated character shadow mask is loaded");
            byte[] original=mask.GetRawTextureData();
            lab.Weapon.Request(Bolt.Fire);
            for(int i=0;i<75;i++) lab.SendMessage("Tick",.02f);
            Check(lab.Weapon.Loaded==Bolt.Fire,"fire arrows equipped");
            byte[] posed=mask.GetRawTextureData();
            int changed=0,covered=0;
            for(int i=0;i<posed.Length;i++){if(posed[i]!=original[i])changed++;if(posed[i]!=0)covered++;}
            Check(changed>100 && covered>500 && covered<posed.Length-500,
                "living silhouette changes with current model pose and leaves open floor around its perimeter");
            int lit=lab.LitLamps;
            Vector3 direction=lab.GameplayLight;
            Vector2 center=lab.DemonShadowCenter;
            string Folder(string option) {
                int i=Array.IndexOf(args,option);
                if(i<0||i+1>=args.Length)return null;
                Directory.CreateDirectory(args[i+1]);return args[i+1];
            }
            string images=Folder("-visual-evidence") ?? Folder("-shadow-evidence"), frames=Folder("-fx-frames");
            var receiver=floor.GetComponent<MeshFilter>().sharedMesh;
            var weapon=camera.GetComponentInChildren<HunterWeaponView>();
            Vector3 creature=lab.transform.Find("Caster").position;
            Vector3 longAxis=new Vector3(center.x-creature.x,0,center.y-creature.z).normalized;
            Vector3 side=Vector3.Cross(Vector3.up,longAxis);
            int frame=0;
            void Capture(string name,bool detail=false,bool sequence=false) {
                string dest=sequence?frames:images;
                if(dest==null)return;
                int w=sequence?960:1280,h=sequence?540:720;
                Vector2 target=lab.DemonShadowCenter;
                Vector3 savedPos=camera.transform.position;var savedRot=camera.transform.rotation;
                bool weaponActive=weapon.gameObject.activeSelf;
                if(detail) {
                    // Over-the-shoulder inspection of the full cast surface; only the viewmodel
                    // is withheld for these detail shots. The simulation still uses the hunter.
                    Vector3 mid=receiver.bounds.center;
                    Vector3 at=new Vector3(mid.x,.14f,mid.y);
                    float elevation=Mathf.Clamp(receiver.bounds.size.y*.76f,2.55f,4.15f);
                    camera.transform.position=at+longAxis*1.70f+side*.92f+Vector3.up*elevation;
                    camera.transform.LookAt(at);weapon.gameObject.SetActive(false);
                } else camera.transform.LookAt(new Vector3(target.x,.08f,target.y));
                var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                var tex=new Texture2D(w,h,TextureFormat.RGB24,false);
                tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();
                byte[] bytes=sequence?tex.EncodeToJPG(92):tex.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(dest,name+(sequence?".jpg":".png")),bytes);
                camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(tex);camera.transform.SetPositionAndRotation(savedPos,savedRot);
                if(detail)weapon.gameObject.SetActive(weaponActive);
            }
            void Frames(int ticks) {
                for(int i=0;i<ticks;i++) {
                    lab.SendMessage("Tick",.05f);
                    Capture("fire-"+(frame++).ToString("D3"),true,true);
                }
            }
            Capture("shadow-before-fire");
            Capture("fire-"+(frame++).ToString("D3"),true,true);
            camera.transform.LookAt(new Vector3(center.x,.02f,center.y));lab.Shoot();
            Check(burn.Burning && burn.EdgeSamples>35,"fire bolt ignites actual silhouette perimeter: "+lab.LastShot);
            var flames=burn.transform.Find("Silhouette flames").GetComponent<ParticleSystem>();
            var animation=flames.textureSheetAnimation;
            Check(animation.enabled && animation.numTilesX*animation.numTilesY==40 &&
                flames.GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture.name=="NyrthosFire",
                "the projected creature's perimeter uses the imported fire flipbook");
            Frames(4);
            Check(burn.LiveFlames>5 && lab.Damage>0,"edge flames emitted and burn damages the demon");
            Light reflection=burn.GetComponentInChildren<Light>();
            Check(reflection.enabled && reflection.shadows==LightShadows.None && lab.LitLamps==lit && lab.GameplayLight==direction,
                "local firelight and volume preserve the six-lamp projection");
            Capture("shadow-ignition");
            Frames(6);
            Capture("shadow-edge-fire");
            Capture("shadow-edge-detail",true);
            Check(floor.sharedMaterial.GetFloat("_FireHeat")>.8f,"fire connects the animated silhouette between individual flames");
            if(frames!=null)Frames(16);
            lab.SendMessage("ResetLab");
            Check(!burn.Burning && !reflection.enabled && burn.LiveFlames==0 && floor.sharedMaterial.GetFloat("_FireHeat")==0,
                "reset clears fire, its glowing perimeter and cosmetic light");
            Debug.Log("SHADOW_FIRE_PLAYER_PASS");Application.Quit(0);
        } catch(Exception e){Debug.LogException(e);Application.Quit(1);}
    }
    static void Check(bool value,string text) {
        if(!value)throw new InvalidOperationException(text);
        Debug.Log("PASS "+text);
    }
}
