using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public sealed partial class ShadowLab {
    [Serializable] sealed class SwarmFrameSample {
        public string location,profile;
        public int width,height,frames,cameraFrames,aboveBudget,over16ms,renderedSubtitleFrames;
        public float median,p95,p99,worst,tickMedian,tickP99,clip,shadowDistance;
        public List<string> hitches=new List<string>();
    }
    [Serializable] sealed class SwarmFrameReport {
        public string gpu,result;
        public int ghosts,groups,acolytes,guardians;
        public List<SwarmFrameSample> samples=new List<SwarmFrameSample>();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartSwarmFrameCheck() {
        var args=Environment.GetCommandLineArgs();
        bool stairs=Array.IndexOf(args,"-check-swarm-stairs")>=0;
        if(!stairs && Array.IndexOf(args,"-check-swarm-frames")<0)return;
        Application.runInBackground=true;
        var lab=FindFirstObjectByType<ShadowLab>();
        lab.StartCoroutine(stairs?lab.CheckSwarmStairs(args):lab.SampleSwarmFrames(args));
    }
    IEnumerator CheckSwarmStairs(string[] args) {
        for(int i=0;i<5;i++)yield return null;
        var folder=args[Array.IndexOf(args,"-chapter01-evidence")+1];
        try {
            RestartEncounter();
            var rows=MinorShadowGroundIntegrationCases.Run(this,folder);
            File.WriteAllLines(Path.Combine(folder,"stairs.txt"),rows);
            Application.Quit(0);
        } catch(Exception error) {
            File.WriteAllText(Path.Combine(folder,"stairs.txt"),error.ToString());
            UnityEngine.Debug.LogException(error);Application.Quit(1);
        }
    }
    IEnumerator SampleSwarmFrames(string[] args) {
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        for(int i=0;i<5;i++)yield return null;
        string folder=args[Array.IndexOf(args,"-chapter01-evidence")+1];
        string path=Path.Combine(folder,"swarm-frames.json");
        Directory.CreateDirectory(folder);
        var report=new SwarmFrameReport {gpu=SystemInfo.graphicsDeviceName,result="running"};
        var subtitle=UI.UiCanvas.transform.Find("Narration subtitles").GetComponent<UnityEngine.UI.Text>();
        int rendered=0;
        Camera.CameraCallback countCamera=c=>{if(c==Eye)rendered++;};
        Camera.onPostRender+=countCamera;
        enabled=false;
        bool tune=Array.IndexOf(args,"-compare-render-budgets")>=0;
        foreach(var profile in tune?new[]{"native","medium","fast","short","low"}:new[]{"shipping","shipping-repeat"}) {
            if(tune) {
                int w=profile=="native"?1920:profile=="low"?960:1280;
                Screen.SetResolution(w,w*9/16,FullScreenMode.Windowed);
                QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
                if(profile!="native") {
                    Eye.farClipPlane=profile=="medium"?140:profile=="fast"?85:60;
                    QualitySettings.meshLodThreshold=16;
                    QualitySettings.shadowDistance=profile=="medium"?30:18;
                    QualitySettings.shadowCascades=0;
                }
            }
            foreach(var location in new[]{"spawn","swarm"}) {
                RestartEncounter();
                const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
                UI.GetType().GetField("sessionStarted",flags).SetValue(UI,true);
                UI.GetType().GetMethod("SetView",flags).Invoke(UI,new object[]{"battle",null});
                Developer.ClearPauseReasons();boss.HunterHealthLocked=true;
                var groups=authoredActors.OfType<GroundShadowSwarm>().Where(a=>a.isActiveAndEnabled && a.Activated).ToArray();
                report.groups=groups.Length;report.ghosts=groups.Sum(a=>a.LivingCount);
                report.acolytes=authoredActors.OfType<FireThiefAcolyte>().Count(a=>a.isActiveAndEnabled && a.Activated);
                report.guardians=authoredActors.OfType<FixedLanternGuardian>().Count(a=>a.isActiveAndEnabled && a.Activated);
                if(location=="swarm") {
                    Vector3 p=groups.OrderBy(g=>(g.transform.position-state.Player).sqrMagnitude).First().transform.position+Vector3.forward*2.5f;
                    p.y=hall.SurfaceHeight(p.x,p.z);state.Player=PlaceBody(playerBody,p);
                }
                Physics.SyncTransforms();
                var row=new SwarmFrameSample {location=location,profile=profile};
                int n=tune?240:3600,warm=tune?45:120;
                var times=new float[n];var ticks=new float[n];
                long previous=Stopwatch.GetTimestamp(),previousEnd=previous;int first=0;
                int collections=GC.CollectionCount(0);
                for(int i=-warm;i<n;i++) {
                    long begin=Stopwatch.GetTimestamp();
                    float dt=tune?1f/60:Time.deltaTime;
                    Walk(Vector3.zero,dt);Tick(dt);
                    long end=Stopwatch.GetTimestamp();
                    if(i>=0) {
                        if(subtitle.isActiveAndEnabled && subtitle.text==narrator.CurrentSubtitle &&
                            subtitle.cachedTextGenerator.vertexCount>4)row.renderedSubtitleFrames++;
                        float elapsed=(float)((begin-previous)*1000.0/Stopwatch.Frequency);
                        times[i]=elapsed;ticks[i]=(float)((end-begin)*1000.0/Stopwatch.Frequency);
                        if(elapsed>1000f/150)row.aboveBudget++;
                        if(elapsed>1000f/60) {
                            row.over16ms++;
                            row.hitches.Add($"frame={i}, elapsedMs={elapsed:F2}, previousTickMs={(previousEnd-previous)*1000.0/Stopwatch.Frequency:F2}, collections={GC.CollectionCount(0)-collections}, narration={narrator?.CurrentId}");
                        }
                    }
                    if(i==0)first=rendered;
                    collections=GC.CollectionCount(0);previous=begin;previousEnd=end;yield return null;
                }
                Array.Sort(times);Array.Sort(ticks);
                row.width=Screen.width;row.height=Screen.height;row.frames=n;row.cameraFrames=rendered-first;
                row.median=times[n/2];row.p95=times[(int)(n*.95f)];row.p99=times[(int)(n*.99f)];row.worst=times[n-1];
                row.tickMedian=ticks[n/2];row.tickP99=ticks[(int)(n*.99f)];
                row.clip=Eye.farClipPlane;row.shadowDistance=QualitySettings.shadowDistance;
                report.samples.Add(row);File.WriteAllText(path,JsonUtility.ToJson(report,true));
            }
        }
        Camera.onPostRender-=countCamera;
        report.result=report.samples.All(s=>s.cameraFrames==s.frames)?"measured":"missing rendered frames";
        File.WriteAllText(path,JsonUtility.ToJson(report,true));
        Application.Quit(report.result=="measured"?0:1);
    }
}
