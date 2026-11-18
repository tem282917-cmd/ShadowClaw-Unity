using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class MonsterShadowCheck {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() {
        var args=Environment.GetCommandLineArgs();
        if(Array.IndexOf(args,"-check-monster-shadow")<0)return;
        try {
            var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();
            var state=lab.ShadowState;
            var proj=state.Witch.Body;
            var model=lab.GetComponentInChildren<ShadowDemonView>().GetComponentInChildren<SkinnedMeshRenderer>();
            var rim=new List<Vector3>();
            proj.ReadBorder(rim);
            Vector2 foot=state.Project(state.Enemy), head=state.At(state.Enemy,0,1.74f);
            Vector2 axis=(head-foot).normalized;
            Vector2 across=new Vector2(axis.y,-axis.x);
            var actual=new Mesh();
            model.BakeMesh(actual);
            float low=float.MaxValue,high=float.MinValue;
            foreach(Vector3 vertex in actual.vertices) {
                Vector2 point=foot+(state.Project(model.transform.TransformPoint(vertex))-foot)*state.SilhouetteScale;
                float span=Vector2.Dot(across,point-foot);
                low=Mathf.Min(low,span);high=Mathf.Max(high,span);
            }
            UnityEngine.Object.Destroy(actual);
            float left=float.MaxValue,right=float.MinValue;
            foreach(Vector3 vertex in rim) {
                float span=Vector2.Dot(across,new Vector2(vertex.x,vertex.z)-foot);
                left=Mathf.Min(left,span);right=Mathf.Max(right,span);
            }
            Check(right-left>(high-low)*1.20f,"monster spans "+(right-left).ToString("F2")+" m across the light; visible character casts "+(high-low).ToString("F2")+" m");
            int path=Array.IndexOf(args,"-monster-evidence");
            string folder=path>=0?args[path+1]:null;
            float restSize=proj.Rig.HandScale;
            if(folder!=null) Capture(lab,folder,"monster-rest");
            Vector2 before=proj.Hand;
            bool open=false,closed=false,clearGap=false;
            for(int i=0;i<650 && !closed && !lab.Finished;i++) {
                lab.SendMessage("Tick",.02f);
                if(!state.Active)continue;
                if(!open && state.Age>.45f && state.Age<.60f) {
                    Check(proj.Rig.HandScale>restSize*1.65f && Vector2.Distance(proj.Hand,before)>.25f,
                        "animated right hand grows and leaves its resting pose during the cast");
                    if(folder!=null)Capture(lab,folder,"monster-reaching");
                    open=true;
                }
                if(state.Age>.33f && state.Age<.61f && !clearGap) {
                    Vector2 tip=proj.Rig.CastTip, gap=proj.Rig.CastGap;
                    float ArrowAt(Vector2 p)=>proj.HitDistance(new Ray(new Vector3(p.x,8,p.y),Vector3.down));
                    clearGap=proj.Contains(tip) && !proj.Contains(gap) && !float.IsInfinity(ArrowAt(tip)) && float.IsInfinity(ArrowAt(gap));
                }
                if(state.Resolved && open) {
                    Check(proj.Contains(state.LockPoint),"closing hooks cover the original locked floor coordinate");
                    if(folder!=null) Capture(lab,folder,"monster-closure");
                    closed=true;
                }
            }
            Check(open && closed && clearGap,"real cast has an exposed, shootable talon separated by a clear arrow path");
            Debug.Log("MONSTER_SHADOW_PLAYER_PASS"); Application.Quit(0);
        } catch(Exception ex){Debug.LogException(ex);Application.Quit(1);}
    }
    static void Check(bool passed,string message) {
        if(!passed) throw new InvalidOperationException(message);
        Debug.Log("PASS "+message);
    }
    static void Capture(ShadowLab lab,string folder,string name) {
        Directory.CreateDirectory(folder);
        var state=lab.ShadowState;
        var edge=new List<Vector3>();state.Witch.Body.ReadBorder(edge);
        var camera=lab.GetComponentInChildren<Camera>();
        var copy=new GameObject("Shadow inspection camera").AddComponent<Camera>();
        copy.CopyFrom(camera);copy.enabled=false;
        var axes=state.At(state.Enemy,0,1.74f)-state.Project(state.Enemy);
        Vector3 up=new Vector3(axes.x,0,axes.y).normalized,side=Vector3.Cross(up,Vector3.up).normalized;
        float x0=float.MaxValue,x1=float.MinValue,y0=float.MaxValue,y1=float.MinValue;
        foreach(var v in edge) {
            float x=Vector3.Dot(side,v),y=Vector3.Dot(up,v);
            x0=Mathf.Min(x0,x);x1=Mathf.Max(x1,x);y0=Mathf.Min(y0,y);y1=Mathf.Max(y1,y);
        }
        var center=up*((y0+y1)*.5f)+side*((x0+x1)*.5f);
        copy.transform.SetPositionAndRotation(center+Vector3.up*4.9f,Quaternion.LookRotation(Vector3.down,up));
        copy.orthographic=true;copy.orthographicSize=Mathf.Max((y1-y0)*.56f,(x1-x0)*.56f/1.5f);
        var previous=RenderTexture.active;
        var buffer=RenderTexture.GetTemporary(1080,720,24);
        var pixels=new Texture2D(1080,720,TextureFormat.RGB24,false);
        var weapon=camera.GetComponentInChildren<HunterWeaponView>();
        bool visible=weapon.gameObject.activeSelf;
        try {
            weapon.gameObject.SetActive(false);
            copy.targetTexture=buffer;copy.Render();RenderTexture.active=buffer;
            pixels.ReadPixels(new Rect(0,0,1080,720),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());
        } finally {
            weapon.gameObject.SetActive(visible);copy.targetTexture=null;
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(buffer);
            UnityEngine.Object.Destroy(pixels);UnityEngine.Object.Destroy(copy.gameObject);
        }
    }
}
