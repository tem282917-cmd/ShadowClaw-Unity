using System.Collections.Generic;
using UnityEngine;

internal static class MinorEnemyVisuals
{
    internal const string ImportedEffects="ImportedVFX/FreeQuickEffectsVol1/Prefabs/";
    internal static GameObject DefaultEffect(GameObject assigned,string name,bool enabled) =>
        assigned!=null || !enabled ? assigned : Resources.Load<GameObject>(ImportedEffects+name);
    internal static Material Material(Color color,bool transparent=false) {
        Shader shader=transparent ? Shader.Find("Sprites/Default") : Resources.Load<Shader>("Ink");
        if(shader==null)shader=Shader.Find("Sprites/Default");
        var material=new Material(shader);material.color=color;
        if(material.HasProperty("_SelfLit"))material.SetFloat("_SelfLit",.75f);
        return material;
    }
    internal static Transform Part(string name,PrimitiveType shape,Transform parent,Vector3 position,Vector3 scale,Material material) {
        var part=GameObject.CreatePrimitive(shape);
        part.name=name;part.transform.SetParent(parent,false);
        part.transform.localPosition=position;part.transform.localScale=scale;
        part.GetComponent<Collider>().enabled=false;
        var renderer=part.GetComponent<Renderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows=false;
        return part.transform;
    }
    internal static LineRenderer Line(string name,Transform parent,Material material,float width,int count) {
        var obj=new GameObject(name);obj.transform.SetParent(parent,false);
        var line=obj.AddComponent<LineRenderer>();line.useWorldSpace=true;
        line.sharedMaterial=material;line.widthMultiplier=width;line.positionCount=count;
        line.numCapVertices=3;line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }
    internal static void ResolveLamps(ShadowLab encounter,EncounterLantern[] links,int[] fallback,List<int> destination) {
        destination.Clear();
        if(encounter==null)return;
        if(links!=null && links.Length>0) {
            foreach(var link in links) {
                int index=link!=null ? encounter.GetLampIndex(link) : -1;
                if(index>=0 && !destination.Contains(index))destination.Add(index);
            }
        } else if(fallback!=null) foreach(int index in fallback)
            if(index>=0 && index<encounter.LampCount && !destination.Contains(index))destination.Add(index);
    }
    internal static bool Lighting(ShadowLab encounter,List<int> indices,out Vector3 center,out int count) {
        center=Vector3.zero;count=0;
        if(encounter==null)return false;
        foreach(int index in indices)if(encounter.IsLampLit(index)) {center+=encounter.GetLampPosition(index);count++;}
        if(count>0)center/=count;
        return count>0;
    }
    internal static void Release(Object obj) {if(obj!=null)Object.Destroy(obj);}
}

// Draw and shoot the exact same surface triangles, including authored terrain height.
internal sealed class MinorEnemyShadow
{
    readonly Mesh mesh;
    readonly MeshRenderer renderer;
    readonly GameObject root;
    readonly List<Vector3> vertices=new List<Vector3>();
    readonly List<int> triangles=new List<int>();
    readonly List<Vector2> volumeSamples=new List<Vector2>(64);
    readonly List<Vector2> volumeHull=new List<Vector2>(64);
    readonly MinorShadowSurface surface;
    readonly ShadowLab encounter;
    public bool Visible {get;private set;}
    bool fullDetail;
    // 48 m leaves room beyond the 40 m shot for a silhouette cast towards the hunter.
    // Separate enter/leave distances keep a stationary player at a boundary stable.
    internal static bool Prepare(ref MinorEnemyShadow shadow,ShadowLab encounter,Transform parent,string name,
        Material material,Vector3 foot,bool force,out bool full) {
        float distance=(foot-encounter.HunterGroundPosition).sqrMagnitude;
        float limit=shadow==null?48f:54f;
        full=false;
        if(distance>limit*limit) {
            Release(ref shadow);
            return false;
        }
        if(shadow==null) {
            shadow=new MinorEnemyShadow(name,parent,material,encounter);
            encounter.RegisterMinorShadow(shadow);force=true;
        }
        float close=shadow.fullDetail?22f:18f;
        full=distance<=close*close;
        bool changed=shadow.fullDetail!=full;
        shadow.fullDetail=full;
        return force || changed || full || encounter.SimpleShadowDue(shadow);
    }
    internal static void Release(ref MinorEnemyShadow shadow) {
        if(shadow==null)return;
        shadow.Publish(false);shadow.Dispose();shadow=null;
    }
    internal int SurfaceBuildCount {get;private set;}
    internal int SurfaceReuseCount {get;private set;}
    internal string SurfaceCacheDiagnostic => surface.CacheDiagnostic;
    public MinorEnemyShadow(string name,Transform parent,Material material,ShadowLab encounter) {
        this.encounter=encounter;
        surface=new MinorShadowSurface(encounter);
        root=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        root.transform.SetParent(parent,false);
        mesh=new Mesh{name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.MarkDynamic();
        root.GetComponent<MeshFilter>().sharedMesh=mesh;
        renderer=root.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    public void Clear() {surface.Clear();}
    public void Polygon(params Vector2[] points) {
        Polygon((IList<Vector2>)points);
    }
    void Polygon(IList<Vector2> points) {
        surface.Add(points);
    }
    public void Volume(Vector3[] points,Vector3 foot,Quaternion rotation,MinorShadowLighting lighting,float size,
        Vector3 offset,Vector3 scale,float footprintScale=1f) {
        MinorEnemyRules.ProjectVolumeHull(points,foot,rotation,offset,scale,lighting,size,volumeSamples,volumeHull);
        if(footprintScale!=1f && volumeHull.Count>=3) {
            Vector2 center=Vector2.zero;
            foreach(var point in volumeHull)center+=point;
            center/=volumeHull.Count;
            // Keep the model-side edge at its projected height as the mantle rises.
            Vector2 away=center-new Vector2(foot.x,foot.z);
            if(away.sqrMagnitude>.01f) {
                away.Normalize();
                float nearEdge=0;
                foreach(var point in volumeHull)nearEdge=Mathf.Min(nearEdge,Vector2.Dot(point-center,away));
                center+=away*nearEdge;
            }
            for(int i=0;i<volumeHull.Count;i++)volumeHull[i]=center+(volumeHull[i]-center)*footprintScale;
        }
        surface.SetFoot(foot);
        Polygon(volumeHull);
    }
    public void Oval(Vector2 point,float x,float z) {
        var ring=new Vector2[16];
        for(int i=0;i<ring.Length;i++) {
            float angle=i*Mathf.PI*2/ring.Length;ring[i]=point+new Vector2(Mathf.Cos(angle)*x,Mathf.Sin(angle)*z);
        }
        Polygon(ring);
    }
    public void Publish(bool visible) {
        if(!visible) {Visible=false;renderer.enabled=false;return;}
        if(surface.CanReuse(root.transform))SurfaceReuseCount++;
        else {
            vertices.Clear();triangles.Clear();surface.Build(root.transform,vertices,triangles);SurfaceBuildCount++;
            surface.Remember(root.transform,vertices);
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
        }
        Visible=triangles.Count>0;renderer.enabled=Visible;
    }
    public float Hit(Ray ray) {
        if(!Visible || !renderer.bounds.IntersectRay(ray))return float.PositiveInfinity;
        float closest=float.PositiveInfinity;
        var matrix=root.transform.worldToLocalMatrix;
        // Convert the local hit distance back to world-ray units under scaled parents.
        var localRay=new Ray {origin=matrix.MultiplyPoint3x4(ray.origin)};
        Vector3 direction=matrix.MultiplyVector(ray.direction);
        float scale=direction.magnitude;
        if(scale<.000001f)return closest;
        localRay.direction=direction/scale;
        for(int i=0;i<triangles.Count;i+=3)closest=Mathf.Min(closest,MinorEnemyRules.TriangleHit(localRay,
            vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]]));
        return closest/scale;
    }
    public void Dispose() {
        if(encounter!=null)encounter.UnregisterMinorShadow(this);
        MinorEnemyVisuals.Release(mesh);MinorEnemyVisuals.Release(root);
    }
}

// Imported prefab effects use the encounter clock, so pause/restart do not leave
// running particles or animations behind. This class does not author VFX content.
internal sealed class ImportedEnemyEffect
{
    readonly GameObject instance;
    readonly ParticleSystem[] particles;
    readonly Animator[] animators;
    float remaining;
    public Transform Transform => instance!=null ? instance.transform : null;
    public ImportedEnemyEffect(GameObject prefab,Transform parent,Vector3 position,bool local,float lifetime=float.PositiveInfinity) {
        instance=Object.Instantiate(prefab,parent);
        if(local)instance.transform.localPosition=position;else instance.transform.position=position;
        remaining=lifetime;
        particles=instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach(var particle in particles) {
            var collision=particle.collision;collision.enabled=false;
            var trigger=particle.trigger;trigger.enabled=false;
            var lights=particle.lights;lights.enabled=false;
            var main=particle.main;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            particle.Play(false);particle.Pause(false);
        }
        animators=instance.GetComponentsInChildren<Animator>(true);
        foreach(var animator in animators)animator.speed=0;
        foreach(var collider in instance.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        foreach(var light in instance.GetComponentsInChildren<Light>(true))light.enabled=false;
    }
    public void SetScale(float scale) {if(instance!=null)instance.transform.localScale=Vector3.one*Mathf.Max(.01f,scale);}
    public void DisableNativeShadows() {
        if(instance==null)return;
        foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    public void StopAndClear() {
        foreach(var particle in particles)if(particle!=null)particle.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach(var animator in animators)if(animator!=null)animator.speed=0;
    }
    public void Restart() {
        foreach(var particle in particles)if(particle!=null) {particle.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);particle.Play(false);particle.Pause(false);}
        foreach(var animator in animators)if(animator!=null && animator.runtimeAnimatorController!=null)animator.Rebind();
    }
    public void SetVisible(bool visible) {
        if(instance==null)return;
        bool changed=instance.activeSelf!=visible;instance.SetActive(visible);
        if(visible && changed)Restart();
    }
    public bool Tick(float dt) {
        if(instance==null)return false;
        if(instance.activeInHierarchy) {
            foreach(var particle in particles)if(particle!=null)particle.Simulate(dt,false,false,true);
            foreach(var animator in animators)if(animator!=null && animator.runtimeAnimatorController!=null) {
                animator.speed=1;animator.Update(dt);animator.speed=0;
            }
        }
        remaining-=dt;return remaining>0;
    }
    public void Dispose() {MinorEnemyVisuals.Release(instance);}
}
