using System;
using UnityEngine;

namespace Chapter01.Runtime.Testing
{
    // Installer-authored world-space acceptance data. This component is inert.
    public sealed class CastleWalkwayDescriptor : MonoBehaviour
    {
        [Serializable] public sealed class CornerRoute
        {
            public string name;
            public Vector3[] points = Array.Empty<Vector3>();
            public float lateralOffset = .5f;
        }

        public Transform root;
        public string buildId;
        public Vector3[] path = Array.Empty<Vector3>();
        public string[] allowedGroundColliderNames = Array.Empty<string>();
        public string[] replacedColliderNames = Array.Empty<string>();
        public string[] replacedRendererNames = Array.Empty<string>();
        public string[] scopeNames = Array.Empty<string>();
        public Vector3 entrancePosition;
        public float entranceMaximumDistance = 1.5f;
        public CornerRoute[] cornerChecks = Array.Empty<CornerRoute>();
        public Vector3[] screenshotPoints = Array.Empty<Vector3>();
        public Vector3[] screenshotLookAt = Array.Empty<Vector3>();

        public Vector3[] barrierStarts = Array.Empty<Vector3>();
        public Vector3[] barrierDirections = Array.Empty<Vector3>();
        public Vector3[] barrierNormals = Array.Empty<Vector3>();
        public float[] barrierMaxTravel = Array.Empty<float>();
        public string[] barrierExpectedColliderPrefixes = Array.Empty<string>();
        // rail / column / building. Scope IDs identify the bridge/stair/corner checked.
        public string[] barrierCategories = Array.Empty<string>();
        public string[] barrierScopeIds = Array.Empty<string>();
        public string[] requiredBarrierScopes = Array.Empty<string>();

        // All ray starts are standing eye positions, not arbitrary low debug rays.
        public Vector3[] rayStarts = Array.Empty<Vector3>();
        public Vector3[] rayEnds = Array.Empty<Vector3>();
        public bool[] rayShouldHit = Array.Empty<bool>();
        public string[] rayExpectedColliderNames = Array.Empty<string>();
        public Vector3[] jumpPoints = Array.Empty<Vector3>();

        // Absolute source files and BEFORE-install hashes, checked independently at
        // runtime on this verification machine. No asset is edited by the checker.
        public string[] protectedAssetPaths = Array.Empty<string>();
        public string[] protectedAssetSha256 = Array.Empty<string>();
    }
}
