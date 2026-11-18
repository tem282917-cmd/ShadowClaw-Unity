using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

// Warm flames track the burn clock; the violet flame at zero power only marks an unlit wick.
// These sprites never supply a Light or change the gameplay lamp strength.
public sealed class LanternFire : MonoBehaviour
{
    internal const float VisualSizeMultiplier=2f;
    ParticleSystem tongues, cinders;
    static Material flameMaterial, emberMaterial;
    MaterialPropertyBlock properties;
    ParticleSystemRenderer tongueRenderer, emberRenderer;
    List<MeshRenderer> candleFlames;
    float lastStrength=-1;

    public static LanternFire Create(Transform wick) {
        var obj=new GameObject("Flame");
        obj.transform.SetParent(wick,false);
        var fire=obj.AddComponent<LanternFire>();
        fire.properties=new MaterialPropertyBlock();
        fire.tongues=fire.BuildStream(false,out fire.tongueRenderer);
        fire.cinders=fire.BuildStream(true,out fire.emberRenderer);
        return fire;
    }

    internal void ScaleImportedCandleFlame(MeshRenderer surface) {
        if(!surface.name.StartsWith("Candle_Flame__",System.StringComparison.Ordinal))return;
        if(candleFlames==null)candleFlames=new List<MeshRenderer>();
        if(candleFlames.Contains(surface))return;
        ScaleAboutCandleBase(surface,VisualSizeMultiplier);
        candleFlames.Add(surface);
    }

    static void ScaleAboutCandleBase(MeshRenderer surface,float factor) {
        // The imported flame is elongated along local +Z and sits on its lower pole.
        var bounds=surface.localBounds;
        var basePoint=new Vector3(bounds.center.x,bounds.center.y,bounds.min.z);
        var t=surface.transform;
        Vector3 wick=t.TransformPoint(basePoint);
        t.localScale*=factor;
        t.position+=wick-t.TransformPoint(basePoint);
    }

    void OnDestroy() {
        if(candleFlames==null)return;
        foreach(var surface in candleFlames)
            if(surface!=null)ScaleAboutCandleBase(surface,1f/VisualSizeMultiplier);
    }

    // Shared by the wall wicks and the flames emitted along the projected creature.
    internal static void BindFireAnimation(ParticleSystem particles,float firstFrame) {
        if(!flameMaterial) {
            flameMaterial=new Material(Resources.Load<Shader>("LanternFire"));
            flameMaterial.mainTexture=Resources.Load<Texture2D>("NyrthosFire");
        }
        particles.GetComponent<ParticleSystemRenderer>().sharedMaterial=flameMaterial;
        var sheet=particles.textureSheetAnimation;
        sheet.enabled=true;
        sheet.numTilesX=8; sheet.numTilesY=5;
        sheet.timeMode=ParticleSystemAnimationTimeMode.FPS; sheet.fps=25;
        sheet.startFrame=firstFrame<0 ? new ParticleSystem.MinMaxCurve(0,.999f) :
            new ParticleSystem.MinMaxCurve(firstFrame);
    }

    ParticleSystem BuildStream(bool sparks,out ParticleSystemRenderer renderer) {
        var obj=new GameObject(sparks ? "Cinders" : "Flame tongues");
        obj.transform.SetParent(transform,false);
        obj.transform.localPosition=Vector3.up*(sparks ? .08f : .13f);
        var ps=obj.AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;
        main.loop=true; main.playOnAwake=false;
        main.simulationSpace=ParticleSystemSimulationSpace.Local;
        main.scalingMode=ParticleSystemScalingMode.Hierarchy;
        main.startSize3D=true;
        renderer=obj.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        renderer.renderMode=ParticleSystemRenderMode.Billboard;
        var emission=ps.emission;
        var shape=ps.shape;
        if(!sparks) {
            // One stationary, continuously cycling flame keeps its base attached to the wick.
            main.maxParticles=2; main.duration=1.6f; main.startLifetime=main.duration;
            main.startSpeed=0; main.startSizeX=.31f; main.startSizeY=.40f; main.startSizeZ=.25f;
            shape.enabled=false; emission.rateOverTime=0;
            emission.SetBursts(new[]{new ParticleSystem.Burst(0f,(short)1)});
            Vector3 wick=transform.position;
            BindFireAnimation(ps,Mathf.Repeat(wick.x*.71f+wick.z*.37f,1));
            renderer.renderMode=ParticleSystemRenderMode.VerticalBillboard;
            return ps;
        }
        if(!emberMaterial) {
            emberMaterial=new Material(Resources.Load<Shader>("LanternFire"));
            emberMaterial.SetFloat("_Cinder",1);
        }
        renderer.sharedMaterial=emberMaterial;
        main.maxParticles=22; main.startLifetime=new ParticleSystem.MinMaxCurve(.46f,.88f);
        main.startSpeed=new ParticleSystem.MinMaxCurve(.38f,.80f);
        main.startSizeX=new ParticleSystem.MinMaxCurve(.014f,.028f);
        main.startSizeY=new ParticleSystem.MinMaxCurve(.028f,.053f);
        main.startSizeZ=.12f; main.startRotation=new ParticleSystem.MinMaxCurve(-.19f,.19f);
        shape.shapeType=ParticleSystemShapeType.Cone; shape.radius=.042f; shape.angle=16;
        shape.rotation=new Vector3(-90,0,0);
        emission.rateOverTime=9;
        var size=ps.sizeOverLifetime; size.enabled=true;
        size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,1,1,.22f));
        var color=ps.colorOverLifetime; color.enabled=true;
        var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(.15f,0),new GradientAlphaKey(.92f,.16f),new GradientAlphaKey(0,1)});
        color.color=gradient;
        return ps;
    }

    public void SetStrength(float strength) {
        strength=Mathf.Clamp01(strength);
        if(Mathf.Approximately(lastStrength,strength)) return;
        bool firstDraw=lastStrength<0;
        lastStrength=strength;
        bool dormant=strength<=0;
        float visualStrength=dormant ? .42f : strength;
        transform.localScale=Vector3.one*(VisualSizeMultiplier*Mathf.Lerp(.14f,1.22f,Mathf.Sqrt(visualStrength)));
        properties.SetFloat("_Power",visualStrength);
        properties.SetFloat("_Dormant",dormant ? 1 : 0);
        tongueRenderer.SetPropertyBlock(properties); emberRenderer.SetPropertyBlock(properties);
        if(firstDraw) {
            tongues.Simulate(.35f,false,true); tongues.Play(false);
            cinders.Simulate(.32f,false,true); cinders.Play(false);
        }
    }
}
