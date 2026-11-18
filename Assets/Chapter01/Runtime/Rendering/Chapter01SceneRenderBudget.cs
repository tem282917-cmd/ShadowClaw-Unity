using System;
using System.Linq;
using UnityEngine;

namespace Chapter01.Rendering
{
    // Scene-local defaults. The project's other scenes retain their own quality settings.
    [DefaultExecutionOrder(-2000), DisallowMultipleComponent]
    public sealed class Chapter01SceneRenderBudget : MonoBehaviour
    {
        public Camera[] cameras = Array.Empty<Camera>();
        public float shadowDistance = 60;
        public int shadowCascades = 2;
        public float meshLodThreshold = 4;
        public int antiAliasing = 0;
        public bool deferredShading = true;
        public bool fxaa = true;
        public float savedSupportSelectionBias = 1;
        public int expectedSupportRenderers = 950;

        QualityScope playScope;

        public void ConfigureCameras()
        {
            foreach (var camera in cameras)
            {
                if (!camera) continue;
                camera.renderingPath = deferredShading ? RenderingPath.DeferredShading : RenderingPath.Forward;
                camera.allowHDR = true;
                camera.allowMSAA = !deferredShading;
                var post = camera.GetComponent<Chapter01NightPostProcess>();
                if (post) post.fxaa = fxaa;
            }
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            playScope = new QualityScope(this);
            ConfigureCameras();
            var supports = gameObject.scene.GetRootGameObjects()
                .SelectMany(x => x.GetComponentsInChildren<MeshRenderer>(true))
                .Where(x => x.name.StartsWith("C05_", StringComparison.Ordinal) || x.name.StartsWith("C07_", StringComparison.Ordinal)).ToArray();
            int matching = supports.Count(x => Mathf.Approximately(x.meshLodSelectionBias, savedSupportSelectionBias) && x.forceMeshLod == -1);
            Debug.Log($"CHAPTER01_RENDER_BUDGET shadows={shadowDistance}m/{shadowCascades} cascades meshLodThreshold={meshLodThreshold} " +
                $"path={(deferredShading ? "DeferredShading" : "Forward")} fxaa={fxaa} msaa={antiAliasing} " +
                $"automaticSupportRenderers={supports.Length} savedBias={savedSupportSelectionBias} matching={matching}/{expectedSupportRenderers}");
            if (supports.Length != expectedSupportRenderers || matching != expectedSupportRenderers)
                Debug.LogError("Castle scene support LOD settings differ from the validated scene budget.");
        }

        void OnDisable()
        {
            playScope?.Dispose();
            playScope = null;
        }

        public IDisposable BeginPreviewQualityScope() => new QualityScope(this);

        sealed class QualityScope : IDisposable
        {
            readonly float distance, lod;
            readonly int cascades, aa, vSync;
            bool disposed;
            public QualityScope(Chapter01SceneRenderBudget profile)
            {
                distance = QualitySettings.shadowDistance;
                cascades = QualitySettings.shadowCascades;
                lod = QualitySettings.meshLodThreshold;
                aa = QualitySettings.antiAliasing;
                vSync = QualitySettings.vSyncCount;
                QualitySettings.shadowDistance = profile.shadowDistance;
                QualitySettings.shadowCascades = profile.shadowCascades;
                QualitySettings.meshLodThreshold = profile.meshLodThreshold;
                QualitySettings.antiAliasing = profile.antiAliasing;
                if(Application.isPlaying)QualitySettings.vSyncCount=0;
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                QualitySettings.shadowDistance = distance;
                QualitySettings.shadowCascades = cascades;
                QualitySettings.meshLodThreshold = lod;
                QualitySettings.antiAliasing = aa;
                if(Application.isPlaying)QualitySettings.vSyncCount=vSync;
            }
        }
    }
}
