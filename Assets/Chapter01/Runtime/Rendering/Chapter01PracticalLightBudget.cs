using System;
using UnityEngine;

namespace Chapter01.Rendering
{
    /// <summary>Keep every emissive surface visible while bounding nearby real-time light evaluation.</summary>
    [DisallowMultipleComponent]
    public sealed class Chapter01PracticalLightBudget : MonoBehaviour
    {
        public Transform viewer;
        public Light[] lights = Array.Empty<Light>();
        [Range(4, 38)] public int maximumActiveLights = 24;
        [Min(0.1f)] public float updateInterval = 0.4f;
        float nextUpdate;
        int[] order;
        float[] distances;

        void OnEnable() { nextUpdate = 0; }
        void OnDisable()
        {
            foreach (var light in lights) if (light != null) light.enabled = true;
        }

        void LateUpdate()
        {
            if (viewer == null || Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + updateInterval;
            if (order == null || order.Length != lights.Length)
            { order = new int[lights.Length]; distances = new float[lights.Length]; }
            for (int i = 0; i < lights.Length; ++i)
            {
                order[i] = i;
                distances[i] = lights[i] == null ? float.PositiveInfinity
                    : (lights[i].transform.position - viewer.position).sqrMagnitude / Mathf.Max(1, lights[i].range * lights[i].range);
            }
            // Only 38 lights: a compact allocation-free insertion sort is adequate at 2.5 Hz.
            for (int i = 1; i < order.Length; ++i)
            {
                int candidate = order[i], j = i - 1;
                while (j >= 0 && distances[order[j]] > distances[candidate])
                { order[j + 1] = order[j]; --j; }
                order[j + 1] = candidate;
            }
            for (int rank = 0; rank < order.Length; ++rank)
                if (lights[order[rank]] != null) lights[order[rank]].enabled = rank < maximumActiveLights;
        }
    }
}
