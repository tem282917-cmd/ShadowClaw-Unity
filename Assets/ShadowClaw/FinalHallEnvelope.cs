using System.Collections.Generic;
using UnityEngine;

// The boss targeting rectangle covers only the nave. The building also has a porch,
// two transepts and a throne apse, each with its own authored walkable floor.
internal sealed class FinalHallEnvelope {
    public readonly Vector3 Center;
    public readonly float Radius;
    public readonly Bounds MistBounds;
    readonly GameObject perimeter;

    public static FinalHallEnvelope Create(IronHallEncounter hall) {
        var floors=new List<Rect>(4);
        Bounds floorBounds=default;
        float naveHeight=0;
        foreach(var floor in hall.GroundColliders) {
            if(floor==null)continue;
            Bounds b=floor.bounds;
            switch(floor.name) {
                case "Collision__Citadel_Nave_Floor": naveHeight=b.max.y;break;
                case "Collision__Citadel_Transept_Floor":
                case "Collision__Throne_Apse_Floor":
                case "Collision__C02__Boss_Porch__0": break;
                default: continue;
            }
            if(floors.Count==0)floorBounds=b;else floorBounds.Encapsulate(b);
            var rect=Rect.MinMaxRect(b.min.x,b.min.z,b.max.x,b.max.z);
            floors.Add(rect);
        }
        if(floors.Count!=4) {
            Debug.LogWarning("Missing final hall floor bindings; retaining the full scene.",hall);
            return null;
        }
        return new FinalHallEnvelope(hall,floors,floorBounds,naveHeight);
    }

    FinalHallEnvelope(IronHallEncounter hall,List<Rect> floors,Bounds bounds,float baseY) {
        Center=new Vector3(bounds.center.x,baseY-1,bounds.center.z);
        Radius=Mathf.Max(hall.RoomRenderRadius,new Vector2(bounds.extents.x,bounds.extents.z).magnitude+1);
        MistBounds=new Bounds(Center+Vector3.up*Radius*.5f,new Vector3(2*Radius,Radius,2*Radius));
        perimeter=new GameObject("Final hall floor perimeter");
        perimeter.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        perimeter.transform.SetParent(hall.transform,true);
        perimeter.SetActive(false);
        // Subdivide at floor edges. Only exposed edges get a collider, so a capsule
        // can pass freely through overlaps and through the mouths of both transepts.
        var xs=new SortedSet<float>();var zs=new SortedSet<float>();
        foreach(var floor in floors){xs.Add(floor.xMin);xs.Add(floor.xMax);zs.Add(floor.yMin);zs.Add(floor.yMax);}
        var x=new List<float>(xs);var z=new List<float>(zs);
        var inside=new bool[x.Count+1,z.Count+1];
        for(int i=1;i<x.Count;i++)for(int j=1;j<z.Count;j++) {
            var center=new Vector2((x[i-1]+x[i])*.5f,(z[j-1]+z[j])*.5f);
            foreach(var floor in floors)if(floor.Contains(center)){inside[i,j]=true;break;}
        }
        for(int i=1;i<x.Count;i++)for(int j=1;j<z.Count;j++) {
            if(!inside[i,j])continue;
            float left=x[i-1],right=x[i],front=z[j-1],back=z[j];
            if(!inside[i-1,j])Wall(left-.5f,left,front,back);
            if(!inside[i+1,j])Wall(right,right+.5f,front,back);
            if(!inside[i,j-1])Wall(left,right,front-.5f,front);
            if(!inside[i,j+1])Wall(left,right,back,back+.5f);
        }
        // Close the porch shoulders alongside the existing fog collider.
        var gate=hall.FogGate;
        var gateSeal=new GameObject("Porch threshold");
        gateSeal.transform.SetParent(perimeter.transform,false);
        gateSeal.transform.SetPositionAndRotation(gate.FloorCenter,gate.transform.rotation);
        float porchWidth=floors.Find(f=>f.Contains(new Vector2(gate.FloorCenter.x,gate.FloorCenter.z))).width;
        float shoulder=(porchWidth-gate.Width)*.5f;
        if(shoulder>0)foreach(int side in new[]{-1,1}) {
            var box=gateSeal.AddComponent<BoxCollider>();
            box.center=new Vector3(side*(gate.Width+shoulder)*.5f,gate.Height*.5f,0);
            box.size=new Vector3(shoulder,gate.Height,gate.Thickness);
        }
    }

    void Wall(float left,float right,float front,float back) {
        var box=perimeter.AddComponent<BoxCollider>();
        box.center=new Vector3((left+right)*.5f,Center.y+6,(front+back)*.5f);
        box.size=new Vector3(right-left,14,back-front);
    }
    public void Seal(bool value) { if(perimeter!=null)perimeter.SetActive(value); }
    public bool Keeps(Bounds bounds) {
        // If the box crosses the equator the nearest point is already on or above it.
        return bounds.max.y>=Center.y && bounds.SqrDistance(Center)<=Radius*Radius;
    }
}
