using System.Collections.Generic;
using UnityEngine;

// The final encounter shares a scene with the castle. Keep its collision and authored
// state resident, but stop submitting distant geometry and simulating exterior encounters.
public sealed partial class ShadowLab {
    readonly List<Renderer> exteriorRenderers=new List<Renderer>();
    readonly List<Renderer> hiddenForFinalRoom=new List<Renderer>();
    readonly List<ParticleSystem> pausedForFinalRoom=new List<ParticleSystem>();
    ParticleSystem[] castleParticles;
    float explorationShadowDistance;
    int explorationCascades;
    Chapter01.Rendering.Chapter01NightPostProcess roomGrade;
    public bool FinalRoomIsolated { get; private set; }
    internal int HiddenExteriorCount => hiddenForFinalRoom.Count;
    FinalHallEnvelope roomEnvelope;
    internal void SetFinalRoomSeal(bool value) => roomEnvelope?.Seal(value);

    void Start() {
        if(hall==null || !hall.UseAuthoredLayout || hall.FogGate==null)return;
        roomEnvelope=FinalHallEnvelope.Create(hall);
        if(roomEnvelope==null)return;
        roomGrade=eye.GetComponent<Chapter01.Rendering.Chapter01NightPostProcess>();
        // The E04 meshes form the horizon even when the distant village is culled.
        // Keep every instance of those meshes, including renamed copies.
        var mountainMeshes=new HashSet<Mesh>();
        foreach(var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            if(r.gameObject.scene!=gameObject.scene || r.transform.IsChildOf(transform) ||
               r.GetComponentInParent<Canvas>()!=null)continue;
            exteriorRenderers.Add(r);
            string name=r.name;
            if((name.StartsWith("E04a__",System.StringComparison.Ordinal) ||
                name.StartsWith("E04b__",System.StringComparison.Ordinal) ||
                name.StartsWith("E04c__",System.StringComparison.Ordinal)) &&
                r.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh!=null)
                mountainMeshes.Add(filter.sharedMesh);
        }
        exteriorRenderers.RemoveAll(r=>r.TryGetComponent<MeshFilter>(out var filter) && mountainMeshes.Contains(filter.sharedMesh));
        castleParticles=FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
    }
    void IsolateFinalRoom() {
        if(FinalRoomIsolated || roomEnvelope==null)return;
        FinalRoomIsolated=true;
        explorationShadowDistance=QualitySettings.shadowDistance;explorationCascades=QualitySettings.shadowCascades;
        QualitySettings.shadowDistance=Mathf.Min(explorationShadowDistance,30);
        QualitySettings.shadowCascades=0;
        if(roomGrade!=null)roomGrade.SetArenaBounds(roomEnvelope.MistBounds);
        foreach(var r in exteriorRenderers) {
            if(r==null || !r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff || roomEnvelope.Keeps(r.bounds))continue;
            r.forceRenderingOff=true;hiddenForFinalRoom.Add(r);
        }
        if(castleParticles!=null)foreach(var p in castleParticles) {
            if(p==null || !p.isPlaying || p.gameObject.scene!=gameObject.scene ||
                p.transform.IsChildOf(transform) || roomEnvelope.Keeps(new Bounds(p.transform.position,Vector3.zero)))continue;
            var particleRenderer=p.GetComponent<ParticleSystemRenderer>();
            if(particleRenderer!=null && roomEnvelope.Keeps(particleRenderer.bounds))continue;
            p.Pause(false);pausedForFinalRoom.Add(p);
        }
        contactFraction=contactDamage=0;contactSlow=1;firstEncounterGuides=CombatGuidance.None;
    }
    internal void RestoreExterior() {
        if(!FinalRoomIsolated)return;
        FinalRoomIsolated=false;
        roomEnvelope.Seal(false);
        QualitySettings.shadowDistance=explorationShadowDistance;QualitySettings.shadowCascades=explorationCascades;
        if(roomGrade!=null)roomGrade.SetArenaBounds(null);
        foreach(var r in hiddenForFinalRoom)if(r!=null)r.forceRenderingOff=false;
        foreach(var p in pausedForFinalRoom)if(p!=null && p.gameObject.activeInHierarchy && p.isPaused)p.Play(false);
        pausedForFinalRoom.Clear();hiddenForFinalRoom.Clear();
    }
}
