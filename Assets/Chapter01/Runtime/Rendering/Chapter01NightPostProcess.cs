using System;
using UnityEngine;

namespace Chapter01.Rendering
{
    /// <summary>Built-in render pipeline night grade. The shader reference is serialized for player builds.</summary>
    [ExecuteAlways, RequireComponent(typeof(Camera)), DisallowMultipleComponent]
    [AddComponentMenu("Chapter 01/Night atmosphere")]
    public sealed class Chapter01NightPostProcess : MonoBehaviour
    {
        [Serializable]
        public struct MistVolume
        {
            public Vector3 center;
            public Vector3 radius;
            [Range(0, 0.02f)] public float density;
            public Vector3 clearCenter;
            public float clearRadius;
            public float fullDensityRadius;
        }

        public Shader shader;
        int exteriorMistMask;
        internal void SetArenaBounds(Bounds? arena) {
            exteriorMistMask=0;
            if(!arena.HasValue || mistVolumes==null)return;
            for(int i=0;i<Math.Min(4,mistVolumes.Length);i++) {
                var volume=mistVolumes[i];
                if(!arena.Value.Intersects(new Bounds(volume.center,volume.radius*2)))
                    exteriorMistMask|=1<<i;
            }
        }
        [Tooltip("Apply lightweight FXAA after the finished color grade, only when the camera actually uses Deferred Shading.")]
        public bool fxaa;
        [Range(-2, 2)] public float exposure = 0.12f;
        [Range(0, 2)] public float saturation = 0.96f;
        [Range(0, 1)] public float bloomIntensity = 0.18f;
        [Range(0.2f, 4)] public float bloomThreshold = 1.05f;
        [Range(0, 1)] public float bloomSoftKnee = 0.55f;
        public Color fogColor = new Color(0.18f, 0.26f, 0.34f, 1);
        [Min(0)] public float fogStart = 85;
        [Range(0, 0.005f)] public float distanceFogDensity = 0.00024f;
        [Range(0, 0.9f)] public float maximumFogOpacity = 0.66f;
        [Min(50)] public float fogRayLimit = 2600;
        public MistVolume[] mistVolumes = Array.Empty<MistVolume>();
        [HideInInspector] public int diagnosticMode;

        Material material;
        Camera attachedCamera;
        readonly Vector4[] centers = new Vector4[4];
        readonly Vector4[] radii = new Vector4[4];
        readonly Vector4[] clearZones = new Vector4[4];
        static readonly Vector2[] ViewCorners = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        public bool FxaaActive => fxaa && attachedCamera != null && attachedCamera.actualRenderingPath == RenderingPath.DeferredShading;

        void OnEnable()
        {
            attachedCamera = GetComponent<Camera>();
            attachedCamera.allowHDR = true;
            attachedCamera.depthTextureMode |= DepthTextureMode.Depth;
        }

        void OnDisable()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            material = null;
        }

        void SetViewRays()
        {
            // World rays use eye depth, including the orthographic overview camera.
            var ray = Matrix4x4.zero;
            var origin = Matrix4x4.zero;
            for (int i = 0; i < 4; ++i)
            {
                var uv = ViewCorners[i];
                var start = attachedCamera.orthographic
                    ? attachedCamera.ViewportToWorldPoint(new Vector3(uv.x, uv.y, 0))
                    : attachedCamera.transform.position;
                var direction = attachedCamera.orthographic ? attachedCamera.transform.forward
                    : attachedCamera.ViewportToWorldPoint(new Vector3(uv.x, uv.y, 1)) - start;
                origin.SetRow(i, new Vector4(start.x, start.y, start.z, 1));
                ray.SetRow(i, new Vector4(direction.x, direction.y, direction.z, 0));
            }
            material.SetMatrix("_ViewOrigins", origin);
            material.SetMatrix("_ViewRays", ray);
            material.SetVector("_CameraClips", new Vector4(attachedCamera.nearClipPlane, attachedCamera.farClipPlane,
                attachedCamera.orthographic ? 1 : 0, 0));
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            // Run on the finished camera color, including sky. Camera depth retains opaque occlusion.
            if (shader == null || !shader.isSupported)
            { Graphics.Blit(source, destination); return; }
            if (material == null) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (attachedCamera == null) attachedCamera = GetComponent<Camera>();
            SetViewRays();
            material.SetFloat("_Exposure", Mathf.Pow(2, exposure));
            material.SetFloat("_Saturation", saturation);
            material.SetFloat("_BloomIntensity", bloomIntensity);
            material.SetVector("_BloomThreshold", new Vector4(bloomThreshold, bloomThreshold * bloomSoftKnee + 0.0001f, 0, 0));
            material.SetColor("_FogColor", fogColor.linear);
            material.SetVector("_FogSettings", new Vector4(fogStart, distanceFogDensity, maximumFogOpacity, fogRayLimit));
            int count = Math.Min(4, mistVolumes != null ? mistVolumes.Length : 0);
            for (int i = 0; i < 4; ++i)
            {
                if (i >= count || (exteriorMistMask & (1<<i))!=0) { centers[i] = Vector4.zero; radii[i] = Vector4.one; clearZones[i] = Vector4.zero; continue; }
                var v = mistVolumes[i];
                centers[i] = new Vector4(v.center.x, v.center.y, v.center.z, v.density);
                radii[i] = new Vector4(1 / Mathf.Max(0.1f, v.radius.x), 1 / Mathf.Max(0.1f, v.radius.y),
                    1 / Mathf.Max(0.1f, v.radius.z), 0);
                clearZones[i] = new Vector4(v.clearCenter.x, v.clearCenter.z, v.clearRadius, v.fullDensityRadius);
            }
            material.SetVectorArray("_MistCenters", centers);
            material.SetVectorArray("_MistInverseRadii", radii);
            material.SetVectorArray("_MistClearZones", clearZones);
            if (diagnosticMode != 0)
            {
                material.SetFloat("_DiagnosticMode", diagnosticMode);
                Graphics.Blit(source, destination, material, 4);
                return;
            }
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.DefaultHDR;
            RenderTexture scene = null, half = null, quarterA = null, quarterB = null, graded = null;
            bool previousWrite = GL.sRGBWrite;
            try
            {
                scene = GetTemporary(source.width, source.height, format);
                half = GetTemporary(source.width / 2, source.height / 2, format);
                quarterA = GetTemporary(source.width / 4, source.height / 4, format);
                quarterB = GetTemporary(source.width / 4, source.height / 4, format);
                GL.sRGBWrite = false;
                Graphics.Blit(source, scene, material, 0);
                Graphics.Blit(scene, half, material, 1);
                material.SetFloat("_BlurRadius", 1);
                Graphics.Blit(half, quarterA, material, 2);
                material.SetFloat("_BlurRadius", 1.5f);
                Graphics.Blit(quarterA, quarterB, material, 2);
                material.SetFloat("_BlurRadius", 2.5f);
                Graphics.Blit(quarterB, quarterA, material, 2);
                material.SetTexture("_BloomTex", quarterA);
                if (FxaaActive)
                {
                    // Keep the existing grade unchanged. Its completed output is filtered by one
                    // edge-only pass; Forward cameras retain the original direct output path.
                    graded = GetTemporary(source.width, source.height, format);
                    GL.sRGBWrite = false;
                    Graphics.Blit(scene, graded, material, 3);
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(graded, destination, material, 5);
                }
                else
                {
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(scene, destination, material, 3);
                }
            }
            finally
            {
                GL.sRGBWrite = previousWrite;
                if (scene != null) RenderTexture.ReleaseTemporary(scene);
                if (half != null) RenderTexture.ReleaseTemporary(half);
                if (quarterA != null) RenderTexture.ReleaseTemporary(quarterA);
                if (quarterB != null) RenderTexture.ReleaseTemporary(quarterB);
                if (graded != null) RenderTexture.ReleaseTemporary(graded);
            }
        }

        static RenderTexture GetTemporary(int width, int height, RenderTextureFormat format)
        {
            var texture = RenderTexture.GetTemporary(Mathf.Max(1, width), Mathf.Max(1, height), 0, format, RenderTextureReadWrite.Linear);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }
    }
}
