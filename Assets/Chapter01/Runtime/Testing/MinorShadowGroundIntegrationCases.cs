using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

// Explicit runtime fixtures. Nothing is saved, and authored enemies are untouched.
// The same entry point runs in the isolated harness and an opt-in main-scene player.
public static class MinorShadowGroundIntegrationCases
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Field(object value,string name) => value.GetType().GetField(name,Fields).GetValue(value);
    public static string[] Run(ShadowLab lab) => Run(lab,null);
    public static string[] Run(ShadowLab lab,string evidenceFolder) {
        var rows=new List<string>();
        void Check(bool value,string message) {
            if(!value)throw new InvalidOperationException("Minor shadow stairs: "+message);
            rows.Add("PASS minor shadow stairs: "+message);
        }
        var fixture=new GameObject("Controlled stair-shadow fixtures - runtime only, never saved");
        var hall=lab.GetComponent<IronHallEncounter>();bool addedHall=hall==null;
        if(addedHall)hall=lab.gameObject.AddComponent<IronHallEncounter>();
        var oldGround=hall.GroundColliders;bool oldAuthored=hall.UseAuthoredLayout;
        var oldMoon=hall.Moonlight;var oldSun=RenderSettings.sun;
        Vector3 oldPlayer=lab.ShadowState.Player;
        var colliders=new List<Collider>(oldGround ?? Array.Empty<Collider>());
        var material=MinorEnemyVisuals.Material(new Color(.65f,.65f,.65f,1));
        var ink=MinorEnemyVisuals.Material(new Color(.015f,.015f,.015f,1));
        MinorEnemyShadow shadow=null;
        try {
            Vector3 origin=new Vector3(1200,40,1200);
            GameObject Block(string name,Vector3 center,Vector3 size,bool mesh=false) {
                var block=GameObject.CreatePrimitive(PrimitiveType.Cube);block.name=name;
                block.transform.SetParent(fixture.transform);block.transform.position=origin+center;block.transform.localScale=size;
                block.GetComponent<Renderer>().sharedMaterial=material;
                Collider collider=block.GetComponent<Collider>();
                if(mesh) {UnityEngine.Object.DestroyImmediate(collider);collider=block.AddComponent<MeshCollider>();
                    ((MeshCollider)collider).sharedMesh=block.GetComponent<MeshFilter>().sharedMesh;}
                colliders.Add(collider);return block;
            }
            Block("Lower landing",new Vector3(0,-.2f,-2),new Vector3(4,.4f,4));
            for(int i=0;i<10;i++) {
                float top=(i+1)*.22f;
                Block("Physical tread "+i,new Vector3(0,top-.12f,i*.4f+.2f),new Vector3(4,.24f,.4f),i%2==1);
            }
            Block("Upper landing",new Vector3(0,2.0f,6),new Vector3(4,.4f,4));
            var lowerStorey=Block("Lower storey",new Vector3(10,-.2f,1),new Vector3(6,.4f,8));
            Block("Separate upper storey",new Vector3(10,2.8f,1),new Vector3(6,.4f,8),true);
            Block("Gap near side",new Vector3(18.825f,-.2f,1),new Vector3(1.65f,.4f,4));
            Block("Gap far side",new Vector3(21.175f,-.2f,1),new Vector3(1.65f,.4f,4));
            hall.UseAuthoredLayout=true;hall.GroundColliders=colliders.ToArray();Physics.SyncTransforms();
            var holder=new GameObject("Controlled acolyte volume");holder.transform.SetParent(fixture.transform);
            shadow=new MinorEnemyShadow("Shootable stair acceptance shadow",holder.transform,ink,lab);
            var lighting=new MinorShadowLighting {Direction=new Vector3(0,-.8f,.6f)};
            Quaternion bodyRotation=Quaternion.identity;
            void Draw(Vector3 foot) {
                holder.transform.position=foot;shadow.Clear();
                shadow.Volume(MinorEnemyVolumes.AcolyteRobe,foot,bodyRotation,lighting,1,Vector3.zero,Vector3.one);
                shadow.Volume(MinorEnemyVolumes.AcolyteHood,foot,bodyRotation,lighting,1,Vector3.zero,Vector3.one);
                shadow.Publish(true);
            }
            bool upVisible=true,upAttached=true,upHits=true,downVisible=true,downAttached=true,downHits=true;
            float reference=origin.y;
            for(int i=0;i<31;i++) {
                var target=origin+new Vector3(0,0,-.2f+i*.15f);
                bool supported=lab.TryMinorGround(target,reference,out var foot);reference=foot.y;Draw(foot);
                upVisible &= supported && shadow.Visible;
                CheckMesh(holder.transform,shadow,hall,out bool attached,out bool hits);
                upAttached &= attached;upHits &= hits;
                if(i==15)Check(RenderedShadowPixels(holder.transform,origin+new Vector3(0,1,2),evidenceFolder,"stairs-ascending")>45,
                    "an oblique camera sees actual shadow pixels on the treads during ascent");
            }
            Check(upVisible && upAttached,"all 31 ascending positions retain visible triangles resting on real treads, including triangle interiors");
            Check(upHits,"ascending rendered triangles and shadow ray hits agree at every tested triangle interior");
            for(int i=30;i>=0;i--) {
                var target=origin+new Vector3(0,0,-.2f+i*.15f);
                bool supported=lab.TryMinorGround(target,reference,out var foot);reference=foot.y;Draw(foot);
                downVisible &= supported && shadow.Visible;
                CheckMesh(holder.transform,shadow,hall,out bool attached,out bool hits);
                downAttached &= attached;downHits &= hits;
                if(i==8)Check(RenderedShadowPixels(holder.transform,origin+new Vector3(0,.6f,1),evidenceFolder,"stairs-descending")>45,
                    "an oblique camera sees actual shadow pixels while descending across risers");
            }
            Check(downVisible && downAttached && downHits,
                "all 31 descending positions keep the same visible and shootable tread surfaces without ramp triangles through stairs");
            Vector3 cacheFoot=origin+new Vector3(10,0,0);Draw(cacheFoot);Draw(cacheFoot);
            int builds=shadow.SurfaceBuildCount,reuses=shadow.SurfaceReuseCount;
            var published=holder.GetComponentInChildren<MeshFilter>().sharedMesh;var originalVertices=published.vertices;
            for(int i=0;i<20;i++)Draw(cacheFoot);
            bool identical=originalVertices.Length==published.vertexCount;var reusedVertices=published.vertices;
            for(int i=0;identical && i<originalVertices.Length;i++)identical &= originalVertices[i].Equals(reusedVertices[i]);
            Check(shadow.SurfaceBuildCount==builds && shadow.SurfaceReuseCount==reuses+20 && identical,
                "twenty unchanged stationary frames reuse the exact published mesh without rebuilding ground patches");
            lighting=new MinorShadowLighting {IsLocal=true,Position=cacheFoot+new Vector3(-3,3,0)};Draw(cacheFoot);Draw(cacheFoot);
            builds=shadow.SurfaceBuildCount;lighting.Position+=Vector3.right*.6f;Draw(cacheFoot);
            Check(shadow.SurfaceBuildCount==builds+1,"moving the selected lamp immediately invalidates the stationary shadow cache");
            builds=shadow.SurfaceBuildCount;cacheFoot+=Vector3.right*.12f;Draw(cacheFoot);
            Check(shadow.SurfaceBuildCount==builds+1,"moving the actor foot rebuilds at full ground-projection precision");
            Draw(cacheFoot);builds=shadow.SurfaceBuildCount;bodyRotation=Quaternion.Euler(0,65,0);Draw(cacheFoot);
            Check(shadow.SurfaceBuildCount==builds+1,"changing the body orientation invalidates the projected-volume mesh cache");
            Draw(cacheFoot);builds=shadow.SurfaceBuildCount;
            var oldFloorPosition=lowerStorey.transform.position;lowerStorey.transform.position+=Vector3.up*.12f;Physics.SyncTransforms();Draw(cacheFoot);
            CheckMesh(holder.transform,shadow,hall,out bool cacheAttached,out bool cacheHits);
            Check(shadow.SurfaceBuildCount==builds+1 && cacheAttached && cacheHits,
                "moving a receiving floor immediately rebuilds both the visible and hittable surface");
            Draw(cacheFoot);builds=shadow.SurfaceBuildCount;shadow.Publish(false);
            bool hidden=!shadow.Visible && float.IsInfinity(shadow.Hit(new Ray(cacheFoot+Vector3.up*2,Vector3.down)));
            Draw(cacheFoot);
            Check(hidden && shadow.Visible && shadow.SurfaceBuildCount==builds,
                "hiding disables all shadow hits and showing an unchanged surface safely reuses its mesh");
            shadow.Publish(false);lowerStorey.GetComponent<BoxCollider>().enabled=false;Physics.SyncTransforms();Draw(cacheFoot);
            bool removed=!shadow.Visible && shadow.SurfaceBuildCount==builds+1;
            lowerStorey.GetComponent<BoxCollider>().enabled=true;Physics.SyncTransforms();Draw(cacheFoot);
            Check(removed && shadow.Visible,"a receiver disabled while hidden invalidates the cache and re-enabling restores the actual floor");
            var meshReceiver=lowerStorey.AddComponent<MeshCollider>();
            var receiverMesh=UnityEngine.Object.Instantiate(lowerStorey.GetComponent<MeshFilter>().sharedMesh);meshReceiver.sharedMesh=receiverMesh;
            var withReceiver=new List<Collider>(colliders) {meshReceiver};hall.GroundColliders=withReceiver.ToArray();Physics.SyncTransforms();Draw(cacheFoot);Draw(cacheFoot);
            builds=shadow.SurfaceBuildCount;var edited=receiverMesh.vertices;
            for(int i=0;i<edited.Length;i++)if(edited[i].y>0) {edited[i].y-=.05f;break;}
            receiverMesh.vertices=edited;meshReceiver.sharedMesh=null;meshReceiver.sharedMesh=receiverMesh;Physics.SyncTransforms();Draw(cacheFoot);
            Check(shadow.SurfaceBuildCount==builds+1,
                "editing a small receiver mesh in place invalidates reuse even when its mesh identity remains unchanged");
            UnityEngine.Object.DestroyImmediate(meshReceiver);UnityEngine.Object.DestroyImmediate(receiverMesh);
            hall.GroundColliders=colliders.ToArray();lowerStorey.transform.position=oldFloorPosition;
            bodyRotation=Quaternion.identity;lighting=new MinorShadowLighting {Direction=new Vector3(0,-.8f,.6f)};Physics.SyncTransforms();
            Vector3 below=origin+new Vector3(10,0,0);
            Check(lab.TryMinorGround(below,below.y,out var lower) && Mathf.Abs(lower.y-origin.y)<.01f,
                "minor-enemy grounding keeps the lower storey instead of selecting the overhead floor");
            Draw(lower);var mesh=holder.GetComponentInChildren<MeshFilter>();bool remainsBelow=shadow.Visible;
            foreach(var vertex in mesh.sharedMesh.vertices)remainsBelow &= mesh.transform.TransformPoint(vertex).y<origin.y+.08f;
            Check(remainsBelow,"projected shadow vertices never jump onto the separate floor three metres above the actor");
            Vector3 nearGap=origin+new Vector3(19.4f,0,1);lighting.Direction=new Vector3(.75f,-.66f,0);Draw(nearGap);
            bool gapClear=shadow.Visible;
            for(float x=19.7f;x<20.31f;x+=.05f)for(float z=.75f;z<=1.25f;z+=.125f)
                gapClear &= float.IsInfinity(shadow.Hit(new Ray(origin+new Vector3(x,2,z),Vector3.down)));
            CheckMesh(holder.transform,shadow,hall,out bool gapAttached,out _);
            Check(gapClear && gapAttached,"a real stair opening has no displayed or hittable triangles bridging its missing floor");
            Check(RenderedShadowPixels(holder.transform,origin+new Vector3(19.5f,0,1),evidenceFolder,"stairs-gap-clipped")>20,
                "clipping the gap preserves the remaining visible shadow on its supported side");
            shadow.Publish(false);

            // Exercise the actual floating enemies' pursuit rather than only a mesh helper.
            var moon=new GameObject("Stair fixture moon").AddComponent<Light>();
            moon.transform.SetParent(fixture.transform);moon.type=LightType.Directional;
            moon.transform.rotation=Quaternion.LookRotation(new Vector3(0,-.8f,.6f));
            hall.Moonlight=moon;RenderSettings.sun=moon;
            var swarmObject=new GameObject("Controlled ascending and descending ground-shadow group");
            swarmObject.SetActive(false);swarmObject.transform.SetParent(fixture.transform);swarmObject.transform.position=origin+Vector3.back*.4f;
            var swarm=swarmObject.AddComponent<GroundShadowSwarm>();swarm.Encounter=lab;swarm.Count=4;
            swarm.UseImportedDefaultEffects=false;swarm.NoticeDistance=0;
            swarm.PursuitSpeed=2;swarm.GroupDamagePerSecond=0;swarm.MemberAnchors=new Transform[4];
            for(int i=0;i<4;i++) {
                var anchor=new GameObject("Controlled member start "+i).transform;anchor.SetParent(fixture.transform);
                anchor.position=origin+new Vector3((i-1.5f)*.55f,0,-.4f);swarm.MemberAnchors[i]=anchor;
            }
            swarmObject.SetActive(true);lab.ShadowState.Player=origin+new Vector3(0,2.2f,6);
            var lampObject=new GameObject("Low side lamp on the landing");lampObject.SetActive(false);
            lampObject.transform.SetParent(fixture.transform);lampObject.transform.position=origin+new Vector3(0,.35f,-4);
            var wick=lampObject.AddComponent<EncounterLantern>();wick.Encounter=lab;lampObject.SetActive(true);wick.ResetLantern();
            void VerifyLift(bool local) {
                if(local)wick.Ignite();else wick.Extinguish();
                swarm.FloatHeight=.85f;swarm.ResetEncounter();
                var target=swarm.transform.Find("Shootable ground shadow 1");
                var visibleMesh=target.GetComponent<MeshFilter>().sharedMesh;
                var ghost=swarm.transform.Find("Ground shadow 1");
                Vector3 foot=swarm.MemberAnchors[0].position;
                Check(lab.GetMinorShadowLightingAt(ghost.position,swarm.ShadowLightRadius).IsLocal==local,"fixture selected the requested ghost light");
                float near=target.GetComponent<MeshRenderer>().bounds.min.z;
                swarm.FloatHeight=1.75f;swarm.ResetEncounter();
                Check(target.GetComponent<MeshRenderer>().bounds.min.z>near+.32f &&
                    ghost.GetComponentInChildren<MeshRenderer>().bounds.min.y>foot.y+1 &&
                    !swarm.TryHitShadow(new Ray(foot+Vector3.up*2.5f,Vector3.down),3f,out _),
                    (local ? "low side lamp" : "sky light")+" moves the near edge away from the old ground anchor as the full mantle rises");
                var idx=visibleMesh.triangles;var pos=visibleMesh.vertices;
                int mid=idx.Length/3/2*3;
                Vector3 triangleCenter=target.TransformPoint((pos[idx[mid]]+pos[idx[mid+1]]+pos[idx[mid+2]])/3);
                Check(swarm.TryHitShadow(new Ray(triangleCenter+Vector3.up,Vector3.down),1.05f,out float distance) &&
                    Mathf.Abs(distance-1)<.025f && lab.TryMinorGround(triangleCenter,triangleCenter.y,out var receiver) &&
                    Mathf.Abs(triangleCenter.y-receiver.y-MinorShadowGround.Lift)<.02f,
                    (local ? "low side lamp" : "sky light")+" produces hittable ink on a supported surface for the raised model");
            }
            VerifyLift(true);VerifyLift(false);
            bool movingVisible=true,movingGrounded=true,hovering=true;
            bool movingHits=true;
            void StepSwarm(int frame) {
                swarm.Tick(.05f);CheckMembers(swarm,lab,ref movingVisible,ref movingGrounded,ref hovering);
                if(frame%10!=0)return;
                foreach(var member in (IEnumerable)Field(swarm,"members")) {
                    var inkSurface=(MinorEnemyShadow)Field(member,"Shadow");
                    var filter=((GameObject)Field(inkSurface,"root")).GetComponent<MeshFilter>();
                    var mesh=filter.sharedMesh;var v=mesh.vertices;var t=mesh.triangles;
                    if(t.Length<3){movingHits=false;continue;}
                    for(int j=0;j<t.Length;j+=3*Mathf.Max(1,t.Length/24)) {
                        Vector3 center=filter.transform.TransformPoint((v[t[j]]+v[t[j+1]]+v[t[j+2]])/3);
                        float distance=inkSurface.Hit(new Ray(center+Vector3.up*.1f,Vector3.down));
                        movingHits &= Mathf.Abs(distance-.1f)<.012f && lab.TryMinorGround(center,center.y,out var support) &&
                            Mathf.Abs(support.y+MinorShadowGround.Lift-center.y)<.018f;
                    }
                }
            }
            for(int i=0;i<80;i++)StepSwarm(i);
            bool reachedTop=true;
            foreach(var member in (IEnumerable)Field(swarm,"members"))reachedTop &= ((Vector3)Field(member,"Foot")).y>origin.y+2;
            lab.ShadowState.Player=origin+new Vector3(0,0,-2);
            for(int i=0;i<100;i++)StepSwarm(i);
            bool returnedBelow=true;
            foreach(var member in (IEnumerable)Field(swarm,"members"))returnedBelow &= ((Vector3)Field(member,"Foot")).y<origin.y+.25f;
            Check(reachedTop && returnedBelow && movingGrounded,
                "the real four-member swarm follows successive treads up and down instead of snapping to a global highest floor");
            Check(movingVisible,"all four actual moving ghost shadows remain visible across 180 pursuit ticks on the staircase");
            Check(movingHits,"during ascent and descent each sampled ghost mesh triangle rests on a tread and is hit at the displayed depth");
            Check(hovering,"the four ghost bodies hover at 1.75 metres plus or minus their bob above each tread during ascent and descent");
            var firstMember=((IList)Field(swarm,"members"))[0];
            MinorEnemyShadow First()=> (MinorEnemyShadow)Field(firstMember,"Shadow");
            Vector3 lastFoot=(Vector3)Field(firstMember,"Foot");
            void AtDistance(float meters) {
                lab.ShadowState.Player=lastFoot+Vector3.right*meters;swarm.Tick(.001f);
            }
            AtDistance(30);var simple=First();
            Check(simple!=null && simple.Visible && !(bool)Field(simple,"fullDetail"),"middle distance publishes a simple hittable shadow");
            AtDistance(52);
            Check(First()==simple,"the middle-distance shadow stays loaded inside the outer hysteresis band");
            AtDistance(60);
            Check(First()==null && !simple.Visible && float.IsInfinity(simple.Hit(new Ray(lastFoot+Vector3.up,Vector3.down))),
                "leaving the outer band releases the ghost shadow and immediately disables its old hits");
            AtDistance(50);Check(First()==null,"a distant shadow stays unloaded inside the return band");
            AtDistance(35);Check(First()!=null && First().Visible && !(bool)Field(First(),"fullDetail"),"approaching reloads the simple shadow");
            AtDistance(16);Check((bool)Field(First(),"fullDetail"),"the full ghost silhouette returns before close combat");
            return rows.ToArray();
        } finally {
            hall.GroundColliders=oldGround;hall.UseAuthoredLayout=oldAuthored;hall.Moonlight=oldMoon;RenderSettings.sun=oldSun;
            lab.ShadowState.Player=oldPlayer;shadow?.Dispose();UnityEngine.Object.DestroyImmediate(fixture);
            UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(ink);
            if(addedHall)UnityEngine.Object.DestroyImmediate(hall);
            Physics.SyncTransforms();
        }
    }
    static void CheckMembers(GroundShadowSwarm swarm,ShadowLab lab,ref bool visible,ref bool grounded,ref bool hovering) {
        foreach(var member in (IEnumerable)Field(swarm,"members")) {
            var foot=(Vector3)Field(member,"Foot");
            grounded &= lab.TryMinorGround(foot,foot.y,out var support) && Mathf.Abs(support.y-foot.y)<.015f;
            var visual=(Transform)Field(member,"Visual");
            hovering &= Mathf.Abs(visual.position.y-support.y-1.75f)<=.081f;
            visible &= ((MinorEnemyShadow)Field(member,"Shadow")).Visible;
        }
    }
    static void CheckMesh(Transform holder,MinorEnemyShadow shadow,IronHallEncounter hall,out bool attached,out bool hittable) {
        attached=hittable=true;int checkedTriangles=0;
        var rayHits=new RaycastHit[64];
        foreach(var filter in holder.GetComponentsInChildren<MeshFilter>()) {
            var vertices=filter.sharedMesh.vertices;var indices=filter.sharedMesh.triangles;
            int rayStride=Mathf.Max(1,indices.Length/(3*24));
            for(int i=0;i<vertices.Length;i++)vertices[i]=filter.transform.TransformPoint(vertices[i]);
            for(int i=0;i<indices.Length;i+=3) {
                Vector3 a=vertices[indices[i]],b=vertices[indices[i+1]],c=vertices[indices[i+2]];
                Vector3 middle=(a+b+c)/3;
                foreach(var point in new[]{middle,a*.6f+b*.2f+c*.2f,a*.2f+b*.6f+c*.2f,a*.2f+b*.2f+c*.6f})
                    attached &= MinorShadowGround.Sample(hall,new Vector2(point.x,point.z),point.y,rayHits,out var floor) &&
                        Mathf.Abs(point.y-floor.Point.y-MinorShadowGround.Lift)<.018f;
                if((i/3)%rayStride==0) {
                    float distance=shadow.Hit(new Ray(middle+Vector3.up*1.5f,Vector3.down));
                    hittable &= !float.IsInfinity(distance) && distance<=1.505f;
                }
                checkedTriangles++;
            }
        }
        attached &= checkedTriangles>0;hittable &= checkedTriangles>0;
    }
    static int RenderedShadowPixels(Transform holder,Vector3 focus,string folder,string name) {
        var cameraObject=new GameObject("Controlled stair evidence camera");var camera=cameraObject.AddComponent<Camera>();
        camera.enabled=false;camera.orthographic=true;camera.orthographicSize=2.9f;camera.nearClipPlane=.1f;camera.farClipPlane=30;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.gray;
        camera.transform.position=focus+new Vector3(4,5,-5);camera.transform.LookAt(focus);
        var target=RenderTexture.GetTemporary(512,512,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;
        var pixels=new Texture2D(512,512,TextureFormat.RGB24,false);
        var renderers=holder.GetComponentsInChildren<MeshRenderer>();
        try {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();var withShadow=pixels.GetPixels32();
            if(!string.IsNullOrEmpty(folder)) {Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
            foreach(var renderer in renderers)renderer.enabled=false;
            camera.Render();pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();var without=pixels.GetPixels32();
            int difference=0;
            for(int i=0;i<withShadow.Length;i++)if(without[i].r-withShadow[i].r>20 && without[i].g-withShadow[i].g>20)difference++;
            return difference;
        } finally {
            foreach(var renderer in renderers)renderer.enabled=true;
            RenderTexture.active=old;camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
