using System.Collections.Generic;
using UnityEngine;

public enum Bolt { Holy, Fire, Storm, Light }

public sealed class WallCombat
{
    public sealed class Mark
    {
        public Bolt Kind;
        public bool Ground;
        public Vector2 Point;
        public float Time;
    }

    public const float FireRadius = .55f, BurnSeconds=3f;
    public readonly List<Mark> Marks = new List<Mark>();
    public float Damage { get; private set; }
    public float Stun { get; private set; }
    float burn;

    public string Shoot(Bolt kind, Vector2 point, bool hit, bool ground=false)
    {
        switch(kind) {
            case Bolt.Holy:
                if(hit) Damage += 10;
                return hit ? "HOLY HIT  +10" : "HOLY MISS";
            case Bolt.Fire:
                if(hit) burn=BurnSeconds;
                Marks.Add(new Mark { Kind=kind, Ground=ground, Point=point, Time=5 });
                return hit ? "SHADOW BURNING" : "FIRE PLACED";
            case Bolt.Storm:
                Marks.Add(new Mark { Kind=kind, Ground=ground, Point=point, Time=15 });
                return "STORM ANCHOR PLACED";
            default:
                throw new System.ArgumentOutOfRangeException(nameof(kind));
        }
    }

    void CheckStorm(ClawState shadow)
    {
        int count=0;
        foreach(var mark in Marks)
            if(mark.Kind==Bolt.Storm && mark.Ground==shadow.Ground && shadow.EnemyAt(mark.Point)) count++;
        if(count<3) return;
        int consumed=0;
        for(int i=Marks.Count-1;i>=0 && consumed<3;i--) {
            var mark=Marks[i];
            if(mark.Kind!=Bolt.Storm || mark.Ground!=shadow.Ground || !shadow.EnemyAt(mark.Point)) continue;
            Marks.RemoveAt(i);
            consumed++;
        }
        Stun=2;
    }

    public void Step(float dt, ClawState primary, ClawState extra, ClawState ground=null)
    {
        Step(dt,new[] {primary,extra,ground});
    }
    public void Step(float dt,IReadOnlyList<ClawState> shadows) => StepDamage(dt,shadows,null);
    public void StepGroundFire(float dt,System.Func<Vector2,float,bool> touches)
        => StepDamage(dt,System.Array.Empty<ClawState>(),touches);
    void StepDamage(float dt,IReadOnlyList<ClawState> shadows,System.Func<Vector2,float,bool> groundTouches)
    {
        Stun=Mathf.Max(0,Stun-dt);
        foreach(var shadow in shadows) if(shadow!=null) CheckStorm(shadow);
        if(Stun>0) foreach(var shadow in shadows) shadow?.Interrupt();
        float exposure=Mathf.Min(dt,burn);
        burn=Mathf.Max(0,burn-dt);
        for(int i=Marks.Count-1;i>=0;i--) {
            var mark=Marks[i];
            if(mark.Kind==Bolt.Fire && mark.Ground && groundTouches!=null && groundTouches(mark.Point,FireRadius))
                exposure=Mathf.Max(exposure,Mathf.Min(dt,mark.Time));
            if(mark.Kind==Bolt.Fire) foreach(var shadow in shadows) {
                if(shadow!=null && shadow.Ground==mark.Ground && InFire(shadow,mark.Point)) {
                    exposure=Mathf.Max(exposure,Mathf.Min(dt,mark.Time));
                    break;
                }
            }
            mark.Time-=dt;
            if(mark.Time<=0) Marks.RemoveAt(i);
        }
        if(shadows.Count>0 || groundTouches!=null) Damage+=exposure*4;
    }

    static bool InFire(ClawState shadow, Vector2 point) =>
        shadow.Witch.Touches(point,FireRadius) || shadow.Arm.Touches(point,FireRadius);
}
