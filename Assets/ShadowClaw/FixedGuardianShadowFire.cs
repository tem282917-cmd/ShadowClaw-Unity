using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class FixedGuardianShadowFire {
    readonly BellShadowProjection projection;
    readonly ShadowBurnFx effect;
    readonly Func<Vector2,float,bool> touches;
    readonly Func<float,float,float> ground;
    readonly List<(WallCombat.Mark mark,GameObject bed)> cinders=new List<(WallCombat.Mark,GameObject)>();
    WallCombat combat=new WallCombat();
    int appliedDamage;

    internal bool Burning => effect.Burning;
    internal FixedGuardianShadowFire(FixedLanternGuardian guardian,BellShadowProjection projection) {
        this.projection=projection;
        touches=projection.Touches;
        ground=guardian.Encounter.GroundHeightAt;
        var child=new GameObject("Guardian shadow fire");
        child.transform.SetParent(guardian.transform,false);
        effect=child.AddComponent<ShadowBurnFx>();
    }
    internal void Ignite(Vector2 hit) {
        combat.Shoot(Bolt.Fire,hit,true,true);
        var bed=new GameObject("Holy fire cinders");
        bed.transform.SetParent(effect.transform,false);
        effect.PlaceCinders(bed,hit,ground);
        cinders.Add((combat.Marks[combat.Marks.Count-1],bed));
        effect.Ignite(projection,hit);
    }
    internal int Step(float dt) {
        if(cinders.Count==0 || dt<=0)return 0;
        combat.StepGroundFire(dt,touches);
        effect.Tick(projection,dt,true);
        for(int i=cinders.Count-1;i>=0;i--)
            if(cinders[i].mark.Time<=0)Remove(i);
        int total=Mathf.FloorToInt(combat.Damage+.0001f);
        int delta=total-appliedDamage;appliedDamage=total;
        return delta;
    }
    void Remove(int index) {
        var bed=cinders[index].bed;
        if(bed!=null){bed.SetActive(false);UnityEngine.Object.Destroy(bed);}
        cinders.RemoveAt(index);
    }
    internal void Clear() {
        if(cinders.Count==0 && appliedDamage==0)return;
        if(effect!=null)effect.Clear();
        for(int i=cinders.Count-1;i>=0;i--)Remove(i);
        combat=new WallCombat();appliedDamage=0;
    }
}
