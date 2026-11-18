using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    public sealed class Chapter01VisualCapture : MonoBehaviour
    {
        [Serializable] sealed class Capture
        {
            public string file, keyPng, phase;
            public int frame, segment, width, height;
            public float travelledMeters, spacingSincePrevious;
            public Vector3 feet, cameraPosition;
            public Quaternion cameraRotation;
            public bool grounded;
        }
        Chapter01PlayerRoute route;
        float previousDistance = -100;
        int sequence;
        bool pending;
        readonly HashSet<int> keySegments = new HashSet<int>();
        StreamWriter manifest;
        static readonly int[] DenseSegments = { 0, 7, 13, 16, 24, 28, 36 };

        public void Initialize(Chapter01PlayerRoute owner)
        {
            route = owner;
            Directory.CreateDirectory(Path.Combine(route.OutputDirectory, "frames"));
            manifest = new StreamWriter(Path.Combine(route.OutputDirectory, "visual-manifest.jsonl"), false, new UTF8Encoding(false));
        }
        void LateUpdate()
        {
            if (!route || route.Phase != "visual" || pending) return;
            if (previousDistance < 0 || route.MovementBudgetBeforeCapture <= .0001f)
                StartCoroutine(CapturePresentedFrame());
        }
        IEnumerator CapturePresentedFrame()
        {
            pending = true; route.CapturePaused = true;
            yield return new WaitForEndOfFrame();
            // Actual presented game frame, including camera post effects. Capture mode is never used for FPS results.
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            string stem = "frames/" + sequence.ToString("D5") + "-s" + route.Segment.ToString("D2");
            string file = stem + ".jpg";
            File.WriteAllBytes(Path.Combine(route.OutputDirectory, file), texture.EncodeToJPG(85));
            Vector3 offset = transform.position - route.Route[route.Segment]; offset.y = 0;
            bool denseKey = Array.IndexOf(DenseSegments, route.Segment) >= 0 && offset.magnitude < 8 && sequence % 6 == 0;
            bool key = keySegments.Add(route.Segment) || denseKey;
            string keyPng = key ? stem + ".png" : "";
            if (key) File.WriteAllBytes(Path.Combine(route.OutputDirectory, keyPng), texture.EncodeToPNG());
            manifest.WriteLine(JsonUtility.ToJson(new Capture {
                file = file, keyPng = keyPng, frame = Time.frameCount, phase = route.Phase, segment = route.Segment,
                width = texture.width, height = texture.height, travelledMeters = route.TravelledMeters,
                spacingSincePrevious = previousDistance < 0 ? 0 : route.TravelledMeters - previousDistance,
                feet = transform.position, cameraPosition = route.ViewCamera.transform.position,
                cameraRotation = route.ViewCamera.transform.rotation, grounded = route.Grounded
            }));
            manifest.Flush(); Destroy(texture);
            previousDistance = route.TravelledMeters; sequence++;
            // One complete non-readback frame before walking resumes prevents encoding time becoming a movement jump.
            yield return null;
            route.MovementBudgetBeforeCapture = .30f;
            route.CapturePaused = false; pending = false;
        }
        void OnApplicationQuit() { manifest?.Dispose(); manifest = null; }
        void OnDestroy() { manifest?.Dispose(); manifest = null; }
    }
}
