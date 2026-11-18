using System;
using System.IO;
using UnityEngine;

public static class HunterWeaponCheck {
    static void Require(bool condition,string text) {
        if(!condition) throw new InvalidOperationException(text);
        Debug.Log("PASS "+text);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() {
        string[] args=Environment.GetCommandLineArgs();
        if(Array.IndexOf(args,"-check-weapon")<0) return;
        try {
            var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
            var view=lab.GetComponentInChildren<HunterWeaponView>();
            var eye=lab.GetComponentInChildren<Camera>();
            var bow=lab.Weapon;
            Require(view && view.Bow.sharedMesh.blendShapeCount==1 && view.RailBolt.enabled &&
                    view.RailBolt.GetComponent<MeshFilter>().sharedMesh.triangles.Length>20000,
                    "textured crossbow and universal quarrel equipped in first person");
            int frame=0;
            int arg=Array.IndexOf(args,"-weapon-frames");
            string dir=arg>=0 ? args[arg+1] : null;
            RenderTexture rt=null; Texture2D tex=null;
            if(dir!=null) {
                Directory.CreateDirectory(dir);
                rt=new RenderTexture(960,540,24);
                tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            }
            void Capture() {
                if(rt==null) return;
                var active=RenderTexture.active;
                eye.targetTexture=rt; eye.Render(); RenderTexture.active=rt;
                tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();
                File.WriteAllBytes(Path.Combine(dir,"weapon-"+frame.ToString("D3")+".jpg"),tex.EncodeToJPG(90));
                eye.targetTexture=null; RenderTexture.active=active; frame++;
            }
            void Step(int count) {
                for(int i=0;i<count;i++){lab.SendMessage("Tick",1f/30);Capture();}
            }
            Require(view.Bow.GetBlendShapeWeight(0)>99,"ready string drawn");
            var grip=view.Bow.transform.parent;
            Vector3 readyPosition=grip.localPosition;
            Step(5);
            lab.Shoot();
            Step(1);
            var flying=lab.transform.Find("Flying bolt");
            Require(bow.Rounds==3 && bow.Phase==HunterCrossbow.Motion.Cycle &&
                view.Bow.GetBlendShapeWeight(0)<15 && !view.RailBolt.enabled && flying.gameObject.activeSelf,
                "shot releases string, recoils and launches the shared bolt");
            lab.Shoot();
            Require(bow.Rounds==3,"cycle blocks second shot");
            Step(5);
            Require(view.RailBolt.enabled && view.Bow.GetBlendShapeWeight(0)>80,"cycling tensions limbs and feeds next bolt");
            Step(5);
            Require(bow.Phase==HunterCrossbow.Motion.Ready && !flying.gameObject.activeSelf,"ready at end of cycle");
            bow.Request(Bolt.Holy);
            Step(6);
            Require(bow.Phase==HunterCrossbow.Motion.Load && grip.localPosition.y<readyPosition.y-.035f,"reload lowers and rolls the crossbow");
            Step(7);
            Require(!view.RailBolt.enabled,"spent load is clear of the rail");
            Step(5);
            Require(view.RailBolt.enabled && view.RailBolt.transform.localPosition.y>.09f,"replacement bolt enters above the rail");
            Step(22);
            Require(bow.Rounds==4 && bow.Phase==HunterCrossbow.Motion.Ready &&
                Mathf.Abs(grip.localPosition.y-readyPosition.y)<.004f,"loaded bow raised back to aim");
            foreach(var type in new[]{Bolt.Fire,Bolt.Storm,Bolt.Light}) {
                bow.Request(type);
                Step(45);
                Require(bow.Loaded==type && bow.Phase==HunterCrossbow.Motion.Ready,"swap and raise "+type);
                lab.Shoot();Step(2);
                var tracer=flying.GetComponent<TrailRenderer>();
                Require(flying.gameObject.activeSelf && tracer.startColor.a>0,"shared mesh in flight with "+type+" trail");
                Step(10);
            }
            while(bow.Rounds>0) {lab.Shoot();Step(10);}
            Require(!view.RailBolt.enabled,"empty rail after last shot");
            lab.Shoot();
            Require(bow.Phase==HunterCrossbow.Motion.Lower && bow.Rounds==0,"empty trigger starts automatic reload");
            Step(8);lab.Shoot();
            Require(bow.Rounds==0 && !view.RailBolt.enabled,"empty reload cannot fire or display a phantom bolt");
            Step(32);
            Require(bow.Rounds==4 && view.RailBolt.enabled && bow.Phase==HunterCrossbow.Motion.Ready,
                "automatic reload seats the shared bolt and returns to aim");
            lab.SendMessage("ResetLab");
            Require(lab.Weapon.Rounds==4 && view.RailBolt.enabled && !flying.gameObject.activeSelf,"restart restores loaded pose");
            if(rt) UnityEngine.Object.Destroy(rt);
            if(tex) UnityEngine.Object.Destroy(tex);
            Debug.Log("WEAPON_PLAYER_PASS");Application.Quit(0);
        } catch(Exception e) { Debug.LogException(e);Application.Quit(1); }
    }
}
