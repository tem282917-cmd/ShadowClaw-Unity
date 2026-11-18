using UnityEngine;

namespace IronCrownHall {
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MoonshaftDepth : MonoBehaviour {
        void OnEnable() { Camera.onPreCull+=SupplyDepth; }
        void OnDisable() { Camera.onPreCull-=SupplyDepth; }
        void SupplyDepth(Camera camera) {
            if(camera.gameObject.scene==gameObject.scene || camera.cameraType==CameraType.SceneView)
                camera.depthTextureMode|=DepthTextureMode.Depth;
        }
    }
}
