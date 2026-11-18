using System;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Authored acceptance route and probes; inert during normal play.
    public sealed class UpperBridgeSampleDescriptor : MonoBehaviour
    {
        public Transform root;
        public Vector3[] path = Array.Empty<Vector3>();
        public string[] replacedColliderNames = Array.Empty<string>();
        public string[] scopeNames = Array.Empty<string>();
        public string[] allowedConnectionColliderNames = Array.Empty<string>();
        public Vector3[] barrierStarts = Array.Empty<Vector3>();
        public Vector3[] barrierDirections = Array.Empty<Vector3>();
        public Vector3[] barrierNormals = Array.Empty<Vector3>();
        public float[] barrierMaxTravel = Array.Empty<float>();
        public Vector3[] rayStarts = Array.Empty<Vector3>();
        public Vector3[] rayEnds = Array.Empty<Vector3>();
        public bool[] rayShouldHit = Array.Empty<bool>();
        public string[] rayExpectedColliderNames = Array.Empty<string>();
        public float safeLateralOffset = .5f;
    }
}
