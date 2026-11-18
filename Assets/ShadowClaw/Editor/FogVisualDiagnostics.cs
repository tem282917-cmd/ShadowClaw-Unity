using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FogVisualDiagnostics {
    const string Key="FogVisualDiagnostics.Active";
    public static void Run() {
        EditorSceneManager.OpenScene(ShiyinBuild.MainScene);
        SessionState.SetInt(Key+"Frames",0);SessionState.SetBool(Key,true);Monitor();EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod] static void Monitor() {
        EditorApplication.update-=Poll;if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;
    }
    static void Poll() {
        if(!EditorApplication.isPlaying || EditorApplication.isCompiling)return;
        int frames=SessionState.GetInt(Key+"Frames",0)+1;SessionState.SetInt(Key+"Frames",frames);if(frames<10)return;
        SessionState.SetBool(Key,false);
        try {Capture();EditorApplication.Exit(0);} catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Capture() {
        string dir=Path.GetFullPath("../Builds/Gameplay-Revision-20261118/fog-diagnostic");Directory.CreateDirectory(dir);
        var lab=UnityEngine.Object.FindFirstObjectByType<ShadowLab>();lab.enabled=false;
        var gate=lab.GetComponent<IronHallEncounter>().FogGate;var eye=lab.Eye;
        eye.transform.position=gate.FloorCenter-gate.Inward*15+Vector3.up*6;
        eye.transform.LookAt(gate.FloorCenter+Vector3.up*6.2f);eye.depthTextureMode|=DepthTextureMode.Depth;
        var systems=gate.GetComponentsInChildren<ParticleSystem>(true);
        var rows=new System.Collections.Generic.List<string>();
        foreach(var system in systems) {
            var r=system.GetComponent<ParticleSystemRenderer>();var p=new ParticleSystem.Particle[system.main.maxParticles];int n=system.GetParticles(p);
            rows.Add("count="+n+" shader="+r.sharedMaterial.shader.name+" supported="+r.sharedMaterial.shader.isSupported+" enabled="+r.enabled+
                " bounds="+r.bounds+" localBounds="+r.localBounds+" frustum="+GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(eye),r.bounds));
            for(int i=0;i<Mathf.Min(n,5);i++)rows.Add("particle "+i+" local="+p[i].position+" world="+system.transform.TransformPoint(p[i].position)+
                " size="+p[i].GetCurrentSize(system)+" color="+p[i].GetCurrentColor(system)+" life="+p[i].remainingLifetime);
        }
        void Shot(string label)=>typeof(Chapter01.Runtime.Testing.Chapter01CombatCheck).GetMethod("Capture",
            System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{eye,dir,label});
        Shot("00-baseline");
        foreach(var system in systems) {
            var r=system.GetComponent<ParticleSystemRenderer>();r.allowOcclusionWhenDynamic=false;
            r.localBounds=new Bounds(Vector3.down*gate.Height*.5f,new Vector3(gate.Width+6,gate.Height+6,6));
            var main=system.main;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
        }
        Shot("01-explicit-bounds");
        foreach(var system in systems) {
            var r=system.GetComponent<ParticleSystemRenderer>();var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);
            b.SetFloat("_SimpleNoiseAmount",0);b.SetFloat("_SimplexNoiseAmount",0);b.SetFloat("_VoronoiNoiseAmount",0);
            b.SetFloat("_CombinedNoiseRemap",0);b.SetFloat("_CameraDepthFadeRange",.001f);b.SetFloat("_CameraDepthFadeOffset",-1000);
            b.SetFloat("_SurfaceDepthFade",.0001f);b.SetColor("_Albedo",Color.white);r.SetPropertyBlock(b);
        }
        Shot("02-material-isolation");
        foreach(var system in systems) {
            var c=system.colorOverLifetime;c.enabled=false;
            system.Simulate(gate.Height/gate.FallSpeed,false,true,true);
        }
        Shot("03-resimulated");File.WriteAllLines(Path.Combine(dir,"particles.txt"),rows);
    }
}
