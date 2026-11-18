using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Run in an isolated verification project/player, not the user's unsaved editor scene.</summary>
public static class AmbientDynamicsChecks
{
    public static string[] Run()
    {
        var rows = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); rows.Add("PASS ambient: " + label); }
        Check(!AmbientDynamicsDirector.OutsideClearZone(new Vector3(5, 100, 0), Vector3.zero, 10, 4), "clear zone excludes elevated particles too");
        Check(!AmbientDynamicsDirector.OutsideClearZone(new Vector3(13, 0, 0), Vector3.zero, 10, 4), "clear zone includes plume padding");
        Check(AmbientDynamicsDirector.OutsideClearZone(new Vector3(15, 0, 0), Vector3.zero, 10, 4), "outside authored clear zone remains eligible");
        Check(Resources.Load<AudioClip>("EnvironmentDynamics/WindWooshLoop") != null, "credited CC0 wind loop imports");
        var holder = new GameObject("Ambient isolated verification");
        try
        {
            var anchor = holder.AddComponent<AmbientEffectAnchor>();
            foreach (var kind in new[] { AmbientEffectAnchor.EffectKind.LampDust, AmbientEffectAnchor.EffectKind.SlowMist })
            {
                string path = kind == AmbientEffectAnchor.EffectKind.LampDust ? AmbientImportedEffect.DustSource : AmbientImportedEffect.MistSource;
                var source = Resources.Load<GameObject>(path);
                Check(source != null && source.GetComponentInChildren<ParticleSystem>(true) != null, "original " + kind + " prefab imports");
                Check(source.GetComponentInChildren<Light>(true) == null, kind + " source adds zero lights");
                anchor.Kind = kind;
                var instance = new AmbientImportedEffect(source, holder.transform, kind);
                instance.Assign(anchor); instance.Step(.1f, 1, true, false);
                Check(instance.ParticleCount > 0, kind + " emits real particles after activation, not merely a visible empty renderer");
                var system = holder.GetComponentInChildren<ParticleSystem>();
                var before = new ParticleSystem.Particle[64]; int count = system.GetParticles(before);
                float time = system.time; instance.Step(5, 1, true, true);
                var after = new ParticleSystem.Particle[64]; int afterCount = system.GetParticles(after);
                bool frozen = Mathf.Approximately(time, system.time) && count == afterCount;
                for (int i = 0; i < count; i++) frozen &= before[i].position == after[i].position && before[i].remainingLifetime == after[i].remainingLifetime;
                Check(frozen, kind + " pause freezes particle position and lifetime");
                Check(instance.ParticleCount <= (kind == AmbientEffectAnchor.EffectKind.LampDust ? 14 : 8), kind + " particle budget is bounded");
                instance.Step(1, 1, false, false); Check(!instance.Visible, kind + " distance/visibility culling removes draw");
                instance.Step(.1f, 1, true, false);
                Check(instance.Visible && instance.ParticleCount > 0, kind + " emits again after leaving and re-entering visibility range");
                for (int frame = 0; frame < 1000; frame++) instance.Step(1f / 60, 1, true, false);
                Check(instance.ParticleCount > 0, kind + " manual clock keeps emitting beyond the prewarmed particle lifetimes");
                Check(instance.ParticleCount <= (kind == AmbientEffectAnchor.EffectKind.LampDust ? 14 : 8), kind + " sustained emission stays within the same budget");
                foreach (Transform child in holder.transform) UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            var visual = GameObject.CreatePrimitive(PrimitiveType.Quad); visual.transform.SetParent(holder.transform, false);
            Check(!AmbientHangingSway.IsSafeTarget(visual.transform), "sway rejects collision-bearing objects");
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            Check(AmbientHangingSway.IsSafeTarget(visual.transform), "sway accepts an explicitly assigned decoration visual");
            visual.AddComponent<Light>(); Check(!AmbientHangingSway.IsSafeTarget(visual.transform), "sway rejects all light-bearing objects");
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); }
        return rows.ToArray();
    }
}
