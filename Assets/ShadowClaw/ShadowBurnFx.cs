using System.Collections.Generic;
using UnityEngine;

// The soft additive envelope represents illuminated smoke above the flames.
// Its local point light is cosmetic and never participates in ClawState.SetLamp.
public sealed class ShadowBurnFx : MonoBehaviour {
    ParticleSystem flames,smoke,embers;
    Light reflected;
    readonly List<Vector3> border=new List<Vector3>(1600);
    BellShadowProjection burningShadow;
    float remaining,elapsed,carry;
    int sequence;
    float hitAngle;
    Material hazeMat,cinderMat,ashMat,volumeMat;
    readonly Transform[] volumes=new Transform[5];
    Mesh ashQuad;
    public bool Burning=>remaining>0;
    public int LiveFlames=>flames.particleCount;
    public int EdgeSamples=>border.Count;

    void Awake() {
        var shader=Resources.Load<Shader>("ShadowBurn");
        hazeMat=new Material(shader);cinderMat=new Material(shader);
        hazeMat.SetFloat("_Style",1);cinderMat.SetFloat("_Style",2);
        ashMat=new Material(shader);ashMat.SetFloat("_Style",3);
        ashQuad=new Mesh{name="Cinder bed"};
        ashQuad.vertices=new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,1)};
        ashQuad.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
        var tint=new Color(1,.27f,.045f,.5f);
        ashQuad.colors=new[]{tint,tint,tint,tint};ashQuad.triangles=new[]{0,2,1,1,2,3};
        flames=Create("Silhouette flames",null,460);
        LanternFire.BindFireAnimation(flames,-1);
        smoke=Create("Firelit volume",hazeMat,190);
        embers=Create("Lifting cinders",cinderMat,440);
        volumeMat=new Material(Resources.Load<Shader>("ShadowFireVolume"));
        for(int i=0;i<volumes.Length;i++) {
            var cloud=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cloud.name="Heat scattering "+(i+1);cloud.transform.SetParent(transform,false);
            var bounds=cloud.GetComponent<Collider>();bounds.enabled=false;Destroy(bounds);
            var shell=cloud.GetComponent<MeshRenderer>();shell.sharedMaterial=volumeMat;
            shell.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;shell.receiveShadows=false;
            cloud.transform.localScale=new Vector3(.86f,.68f,.86f)*LanternFire.VisualSizeMultiplier;
            cloud.SetActive(false);volumes[i]=cloud.transform;
        }
        var lightRoot=new GameObject("Reflected fire");lightRoot.transform.SetParent(transform,false);
        reflected=lightRoot.AddComponent<Light>();reflected.type=LightType.Point;
        reflected.color=new Color(1,.32f,.065f);reflected.range=2.25f;
        reflected.shadows=LightShadows.None;reflected.enabled=false;
    }
    ParticleSystem Create(string label,Material material,int capacity) {
        var obj=new GameObject(label);obj.transform.SetParent(transform,false);
        var ps=obj.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.playOnAwake=false;main.loop=true;main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.maxParticles=capacity;main.startSpeed=0;main.startSize3D=true;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
        var emission=ps.emission;emission.enabled=false;
        var shape=ps.shape;shape.enabled=false;
        var color=ps.colorOverLifetime;color.enabled=true;var g=new Gradient();
        g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.84f,.26f,.07f),1)},
            new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.76f,.55f),new GradientAlphaKey(0,1)});
        color.color=g;
        var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,
            new AnimationCurve(new Keyframe(0,.72f),new Keyframe(.18f,1),new Keyframe(1,.12f)));
        var rend=ps.GetComponent<ParticleSystemRenderer>();rend.sharedMaterial=material;
        rend.renderMode=ParticleSystemRenderMode.Billboard;rend.maxParticleSize=.6f;
        rend.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;rend.receiveShadows=false;
        return ps;
    }
    public void Ignite(BellShadowProjection projection,Vector2 hit) {
        burningShadow=projection;
        remaining=WallCombat.BurnSeconds;elapsed=0;carry=0;sequence=0;
        hitAngle=Mathf.Atan2(hit.y-projection.Aim.y,hit.x-projection.Aim.x);
        projection.ReadBorder(border);
        projection.SetFireRim(1,hitAngle,.25f);
        Sprinkle(projection.Aim,120,.25f,1);
    }
    public void Tick(BellShadowProjection projection,float dt,bool lit) {
        if(!lit){if(Burning||reflected.enabled)Clear();return;}
        if(dt<=0)return;
        remaining=Mathf.Max(0,remaining-dt);
        if(remaining>0) {
            elapsed+=dt;projection.ReadBorder(border);
            float strength=Mathf.Clamp01(remaining/.45f);
            float spread=Mathf.Clamp01(.16f+elapsed/.32f);
            projection.SetFireRim(strength,hitAngle,spread);
            carry+=dt*480*strength;
            int n=Mathf.Min(170,(int)carry);carry-=n;
            Sprinkle(projection.Aim,n,spread,strength);
            volumeMat.SetFloat("_Strength",strength*spread);
            for(int i=0;i<volumes.Length && border.Count>0;i++) {
                volumes[i].position=border[(2*i+1)*border.Count/(2*volumes.Length)]+Vector3.up*(.18f*LanternFire.VisualSizeMultiplier);
                volumes[i].gameObject.SetActive(true);
            }
            reflected.enabled=true;reflected.intensity=.52f*strength*(1+.1f*Mathf.Sin(elapsed*24));
            float h=border.Count>0?border[border.Count/2].y:0;
            reflected.transform.position=new Vector3(projection.Aim.x,h+.25f,projection.Aim.y);
        } else if(burningShadow!=null) {
            burningShadow.SetFireRim(0,0,0);
            burningShadow=null;
            reflected.enabled=false;
            foreach(var cloud in volumes)cloud.gameObject.SetActive(false);
        }
        if(flames.particleCount+smoke.particleCount+embers.particleCount>0) {
            flames.Simulate(dt,false,false,false);smoke.Simulate(dt,false,false,false);embers.Simulate(dt,false,false,false);
        }
    }
    void Sprinkle(Vector2 center,int count,float spread,float strength) {
        if(border.Count==0)return;
        const float scale=LanternFire.VisualSizeMultiplier;
        for(int i=0;i<count;i++) {
            int serial=++sequence;
            // Vary the chosen contour pixel as it moves instead of emitting at triangulation vertices.
            float u=Mathf.Repeat(serial*.618034f,1),v=Mathf.Repeat(serial*.414214f,1);
            Vector3 p=border[(int)(u*border.Count)];
            float a=Mathf.Atan2(p.z-center.y,p.x-center.x);
            if(Mathf.Abs(Mathf.DeltaAngle(hitAngle*Mathf.Rad2Deg,a*Mathf.Rad2Deg))>spread*180)continue;
            p+=Vector3.up*.016f;
            float height=.24f+.23f*v;
            var param=new ParticleSystem.EmitParams{position=p+Vector3.up*(height*.35f*scale),
                velocity=new Vector3(.06f*(u-.5f),.045f+.07f*v,.07f*(v-.5f))*scale,
                startSize3D=new Vector3(height*(.79f+.23f*u),height,.12f)*scale,startLifetime=.36f+.24f*u,
                startColor=new Color(1,.52f+.31f*u,.22f+.37f*v,.60f*strength),rotation=(u-.5f)*11};
            flames.Emit(param,1);
            if((serial%4)==0) {
                param.position=p+Vector3.up*(.13f*scale);param.startColor=new Color(1,.34f,.08f,.20f*strength);
                param.startSize3D=new Vector3(.55f,.63f,.4f)*scale;param.startLifetime=.55f;
                smoke.Emit(param,1);
            }
            if((serial%3)==0) {
                param.position=p;param.velocity=new Vector3((u-.5f)*.35f,.65f+v,(v-.5f)*.36f)*scale;
                param.startSize3D=new Vector3(.012f+.018f*u,.044f,.025f)*scale;param.startLifetime=.4f+.25f*v;
                param.startColor=new Color(1,.76f,.34f,strength);
                embers.Emit(param,1);
            }
        }
    }
    public void Clear() {
        burningShadow?.SetFireRim(0,0,0);
        burningShadow=null;
        remaining=elapsed=carry=0;reflected.enabled=false;border.Clear();
        foreach(var cloud in volumes)cloud.gameObject.SetActive(false);
        flames.Clear();smoke.Clear();embers.Clear();
    }
    public void PlaceCinders(GameObject mark,Vector2 point,System.Func<float,float,float> ground) {
        float x=point.x,z=point.y,h=ground(x,z),step=.04f;
        Vector3 normal=new Vector3(h-ground(x+step,z),step,h-ground(x,z+step)).normalized;
        mark.transform.position=new Vector3(x,h+.052f,z);
        mark.transform.rotation=Quaternion.FromToRotation(Vector3.up,normal);
        mark.transform.localScale=Vector3.one*WallCombat.FireRadius;
        mark.AddComponent<MeshFilter>().sharedMesh=ashQuad;
        var renderer=mark.AddComponent<MeshRenderer>();renderer.sharedMaterial=ashMat;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    void OnDestroy() {Destroy(hazeMat);Destroy(cinderMat);Destroy(ashMat);Destroy(ashQuad);Destroy(volumeMat);}
}
