using System;
using System.IO;
using UnityEngine;

// Opt-in test rendering only. The gameplay camera keeps its actual scene effects;
// uGUI is composited afterwards by a separate camera with no image effects.
[DefaultExecutionOrder(32000)]
public sealed class Round3FrameCapture : MonoBehaviour
{
    public const int Width=1920,Height=1080;
    const int EvidenceUiLayer=31;
    public string CaptureMode=>"controlled offscreen 1920x1080: real Eye with scene postprocessing, then separate native Canvas camera; not a screen capture; OnGUI excluded";
    public int RenderedFrames {get;private set;}
    public string Failure {get;private set;}
    ShadowLab lab;
    RenderTexture target;
    Camera uiCamera;

    public static Round3FrameCapture Attach(ShadowLab lab) {
        if(lab==null)throw new ArgumentNullException(nameof(lab));
        var existing=UnityEngine.Object.FindFirstObjectByType<Round3FrameCapture>();
        if(existing!=null) {
            if(existing.lab!=lab)throw new InvalidOperationException("The evidence renderer belongs to another encounter.");
            return existing;
        }
        var holder=new GameObject("Controlled round 3 offscreen frames - runtime only");
        var capture=holder.AddComponent<Round3FrameCapture>();capture.lab=lab;capture.Initialize();return capture;
    }
    public static void SaveCurrentFrame(ShadowLab lab,string folder,string name) {
        var capture=Attach(lab);
        capture.RenderFrame();
        capture.RenderedFrames++;
        var image=capture.ReadFrame();
        try {
            Chapter01.Runtime.Testing.Chapter01Round3Check.RequireVisibleScreen(
                Chapter01.Runtime.Testing.Chapter01Round3Check.InspectScreen(image,name));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());
        } finally { UnityEngine.Object.Destroy(image); }
    }
    void Initialize() {
        target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32) {
            name="Controlled chapter offscreen evidence",antiAliasing=1,useMipMap=false,autoGenerateMips=false
        };
        if(!target.Create())throw new InvalidOperationException("Could not create the controlled offscreen render target.");
        var cameraObject=new GameObject("Evidence Canvas camera - no scene postprocessing");cameraObject.transform.SetParent(transform,false);
        uiCamera=cameraObject.AddComponent<Camera>();uiCamera.enabled=false;
        uiCamera.orthographic=true;uiCamera.orthographicSize=Height*.5f;uiCamera.aspect=(float)Width/Height;
        uiCamera.nearClipPlane=.01f;uiCamera.farClipPlane=10;uiCamera.clearFlags=CameraClearFlags.Depth;
        uiCamera.cullingMask=1<<EvidenceUiLayer;uiCamera.allowHDR=false;uiCamera.allowMSAA=false;
        uiCamera.depthTextureMode=DepthTextureMode.None;uiCamera.targetTexture=target;
        // Put the temporary canvas volume far away from all authored world geometry.
        // The dedicated layer and short clipping interval isolate native UI further.
        uiCamera.transform.SetPositionAndRotation(new Vector3(100000,-100000,100000),Quaternion.identity);
    }
    void LateUpdate() {
        if(lab==null || lab.Eye==null || lab.UI==null || target==null || !string.IsNullOrEmpty(Failure))return;
        try {RenderFrame();RenderedFrames++;}
        catch(Exception error) {Failure=error.ToString();Debug.LogError("ROUND3_OFFSCREEN_RENDER_FAILED "+error);}
    }
    void RenderFrame() {
        var eye=lab.Eye;var canvas=lab.UI.UiCanvas;
        if(canvas==null)throw new InvalidOperationException("Native gameplay Canvas is not available for controlled compositing.");
        var oldTarget=eye.targetTexture;float oldAspect=eye.aspect;int oldMask=eye.cullingMask;
        RenderTexture previous=RenderTexture.active;
        RenderMode oldMode=canvas.renderMode;Camera oldUiCamera=canvas.worldCamera;float oldPlane=canvas.planeDistance;
        var nodes=canvas.GetComponentsInChildren<Transform>(true);var layers=new int[nodes.Length];
        for(int i=0;i<nodes.Length;i++)layers[i]=nodes[i].gameObject.layer;
        try {
            for(int i=0;i<nodes.Length;i++)nodes[i].gameObject.layer=EvidenceUiLayer;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=uiCamera;canvas.planeDistance=1;
            Canvas.ForceUpdateCanvases();
            eye.cullingMask=oldMask & ~(1<<EvidenceUiLayer);eye.aspect=(float)Width/Height;eye.targetTexture=target;
            // Keep scene image effects on the real Eye. They finish before any UI
            // pixels enter this target, so lettering is not posterized/graded.
            eye.Render();
            uiCamera.Render();
        } finally {
            eye.targetTexture=oldTarget;eye.aspect=oldAspect;eye.cullingMask=oldMask;
            canvas.renderMode=oldMode;canvas.worldCamera=oldUiCamera;canvas.planeDistance=oldPlane;
            for(int i=0;i<nodes.Length;i++)if(nodes[i]!=null)nodes[i].gameObject.layer=layers[i];
            Canvas.ForceUpdateCanvases();RenderTexture.active=previous;
        }
    }
    public Texture2D ReadFrame() {
        if(!string.IsNullOrEmpty(Failure))throw new InvalidOperationException("Offscreen evidence renderer failed: "+Failure);
        if(target==null || RenderedFrames==0)throw new InvalidOperationException("No completed offscreen evidence frame is available.");
        var previous=RenderTexture.active;Texture2D image=null;
        try {
            RenderTexture.active=target;image=new Texture2D(Width,Height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply(false,false);return image;
        } catch {if(image!=null)UnityEngine.Object.Destroy(image);throw;}
        finally {RenderTexture.active=previous;}
    }
    void OnDestroy() {
        if(uiCamera!=null)uiCamera.targetTexture=null;
        if(target!=null) {target.Release();UnityEngine.Object.Destroy(target);}
    }
}
