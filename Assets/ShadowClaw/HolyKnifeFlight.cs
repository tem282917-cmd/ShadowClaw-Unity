using UnityEngine;
using UnityEngine.Rendering;

// Reuses an imported CC0 dagger and the existing imported fire animation.
// The same imported blade moves from the left-side draw into its physical flight.
// Contact is swept through each travelled segment; damage is never a launch-time raycast.
public sealed class HolyKnifeFlight
{
    public delegate bool HitTest(Ray ray,float distance,out Vector3 contact);
    readonly GameObject root;
    readonly Transform world;
    readonly Transform model;
    readonly Material material;
    readonly LanternFire flame;
    readonly ParticleSystem[] particles;
    Vector3 start,end;
    Quaternion heading;
    float age,duration;
    bool active;
    public bool Active=>active;
    public bool Visible=>root!=null && root.activeSelf;
    public Vector3 Position=>root.transform.position;
    public Vector3 Origin=>start;
    public HolyKnifeFlight(Transform parent) {
        world=parent;
        root=new GameObject("Holy fire knife flight");root.transform.SetParent(parent,false);
        var source=Resources.Load<GameObject>("HunterItems/DaggerSource/Dagger");
        if(source!=null) {
            model=Object.Instantiate(source,root.transform,false).transform;
            var renderers=model.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length>0) {
                var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                Vector3 axis=bounds.size.y>bounds.size.x && bounds.size.y>bounds.size.z ? Vector3.up :
                    bounds.size.x>bounds.size.z ? Vector3.right : Vector3.forward;
                model.localRotation=Quaternion.FromToRotation(axis,Vector3.forward)*model.localRotation;
                float longest=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
                model.localScale*=.65f/Mathf.Max(.001f,longest);
                bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                model.position-=bounds.center-root.transform.position;
                material=new Material(Shader.Find("Standard")) {name="Imported holy dagger material"};
                material.mainTexture=Resources.Load<Texture2D>("HunterItems/DaggerSource/Unity/DaggerLP_Dagger_AlbedoTransparency");
                material.SetTexture("_MetallicGlossMap",Resources.Load<Texture2D>("HunterItems/DaggerSource/Unity/DaggerLP_Dagger_MetallicSmoothness"));
                material.EnableKeyword("_METALLICGLOSSMAP");material.SetFloat("_GlossMapScale",.7f);
                material.SetTexture("_BumpMap",Resources.Load<Texture2D>("HunterItems/DaggerSource/Unity/DaggerLP_Dagger_Normal"));
                material.EnableKeyword("_NORMALMAP");
                material.SetTexture("_EmissionMap",material.mainTexture);material.SetColor("_EmissionColor",new Color(.38f,.26f,.12f));
                material.EnableKeyword("_EMISSION");material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
                foreach(var r in renderers) {
                    var slots=r.sharedMaterials;for(int i=0;i<slots.Length;i++)slots[i]=material;r.sharedMaterials=slots;
                    r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
                }
            }
            foreach(var collider in model.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        } else Debug.LogError("Imported holy dagger model is missing.");
        flame=LanternFire.Create(root.transform);flame.transform.localPosition=Vector3.forward*.12f;
        flame.SetStrength(1);flame.transform.localScale*=.36f;
        particles=flame.GetComponentsInChildren<ParticleSystem>(true);
        foreach(var particle in particles)particle.Pause(false);
        root.SetActive(false);
    }
    public void Hold(Transform camera,float poseAge,float dt) {
        root.transform.SetParent(camera,false);
        root.transform.localPosition=HolyKnifeThrowMotion.Position(poseAge);
        root.transform.localRotation=HolyKnifeThrowMotion.Rotation(poseAge);
        if(!root.activeSelf) {
            root.SetActive(true);
            foreach(var particle in particles) {particle.Simulate(.12f,false,true,true);particle.Pause(false);}
        }
        if(dt>0)foreach(var particle in particles) {particle.Simulate(dt,false,false,true);particle.Pause(false);}
    }
    public void Release(Vector3 from,Vector3 to) {
        root.transform.SetParent(world,true);
        start=from;end=to;age=0;duration=Mathf.Max(.001f,Vector3.Distance(from,to)/42f);
        heading=Quaternion.LookRotation((to-from).sqrMagnitude>.001f ? to-from : Vector3.forward);
        root.transform.SetPositionAndRotation(from,heading);active=true;root.SetActive(true);
        foreach(var particle in particles) {particle.Simulate(.12f,false,true,true);particle.Pause(false);}
    }
    public void Step(float dt,HitTest hitTest) {
        if(!active || dt<=0)return;
        age+=dt;float t=Mathf.Clamp01(age/duration);
        Vector3 next=Vector3.Lerp(start,end,t),delta=next-root.transform.position;
        if(delta.sqrMagnitude>0.0000001f && hitTest!=null && hitTest(new Ray(root.transform.position,delta.normalized),delta.magnitude,out var contact)) {
            root.transform.position=contact;Clear();return;
        }
        foreach(var particle in particles) {particle.Simulate(dt,false,false,true);particle.Pause(false);}
        root.transform.SetPositionAndRotation(next,heading*Quaternion.Euler(0,0,age*540));
        if(t>=1)Clear();
    }
    public void Clear() {active=false;if(root!=null){root.SetActive(false);root.transform.SetParent(world,true);}}
    public void Dispose() {if(root!=null)Object.Destroy(root);if(material!=null)Object.Destroy(material);}
}
