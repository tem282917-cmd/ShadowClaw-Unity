using UnityEditor;
using UnityEngine;

/// <summary>Explicit authoring tools; never automatically edits or saves an open scene.</summary>
public static class AmbientDynamicsAuthoring
{
    [MenuItem("ShadowClaw/Environment/Create slow mist anchor at selection")]
    static void Mist() { CreateAnchor(AmbientEffectAnchor.EffectKind.SlowMist); }
    [MenuItem("ShadowClaw/Environment/Create sparse dust anchor at selection")]
    static void Dust() { CreateAnchor(AmbientEffectAnchor.EffectKind.LampDust); }
    static void CreateAnchor(AmbientEffectAnchor.EffectKind kind)
    {
        var obj = new GameObject("Ambient " + kind + " - author placed"); Undo.RegisterCreatedObjectUndo(obj, "Create atmosphere anchor");
        if (Selection.activeTransform != null) obj.transform.position = Selection.activeTransform.position;
        var anchor = obj.AddComponent<AmbientEffectAnchor>(); anchor.Kind = kind;
        anchor.MaximumDistance = kind == AmbientEffectAnchor.EffectKind.SlowMist ? 40 : 24;
        anchor.Opacity = kind == AmbientEffectAnchor.EffectKind.SlowMist ? .24f : .3f;
        Selection.activeGameObject = obj;
    }
    [MenuItem("ShadowClaw/Environment/Add sway to selected decoration")]
    static void Sway()
    {
        var target = Selection.activeTransform;
        if (!AmbientHangingSway.IsSafeTarget(target)) { Debug.LogWarning("Choose a pure decorative renderer without lights, collision, animator or combat scripts."); return; }
        var component = Undo.AddComponent<AmbientHangingSway>(target.gameObject); component.Visual = target;
        var surface = target.GetComponentInChildren<Renderer>();
        Vector3 top = surface.bounds.center + Vector3.up * surface.bounds.extents.y;
        component.LocalPivot = target.parent != null ? target.parent.InverseTransformPoint(top) : top;
        EditorUtility.SetDirty(component);
    }
    [MenuItem("ShadowClaw/Environment/Benchmark On-Off in Play Mode")]
    static void Benchmark()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Run the isolated verification player/project or enter Play Mode yourself first."); return; }
        var director = Object.FindFirstObjectByType<AmbientDynamicsDirector>();
        if (director == null) return;
        var sample = director.GetComponent<AmbientDynamicsBenchmark>();
        if (sample == null) sample = director.gameObject.AddComponent<AmbientDynamicsBenchmark>();
        sample.Begin(director);
    }
}
