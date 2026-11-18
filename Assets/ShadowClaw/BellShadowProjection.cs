using System;
using System.Collections.Generic;
using UnityEngine;

// The gameplay lamp projects an independent, bone-driven creature onto the receiver.
public sealed class BellShadowProjection {
    // Horizontal travel per unit of silhouette height, measured from vertical.
    internal static readonly float MaxCastSlope=Mathf.Tan(50f*Mathf.Deg2Rad);
    const int Resolution=224, Divisions=12;
    readonly MonsterShadowRig rig;
    readonly float[] intersections;
    readonly ushort[] rowCoverage=new ushort[Resolution];
    readonly Vector2[] pixelContour;
    readonly byte[] mask=new byte[Resolution*Resolution];
    readonly Texture2D coverage;
    readonly Material surfaceMaterial;
    readonly Mesh mesh;
    readonly Vector3[] grid=new Vector3[(Divisions+1)*(Divisions+1)];
    readonly Transform armourReceiver;
    readonly Vector3[] localGrid;
    Matrix4x4 lastReceiverMatrix;
    Rect area;
    readonly Func<float,float,float> ground;
    readonly Rect floorBounds;
    public Vector2 Aim {get; private set;}
    internal MonsterShadowRig Rig => rig;
    public Vector2 Hand => rig.PalmPosition;
    public Vector2 Shoulder => rig.Shoulder;
    // Applied before rasterization, so the visual coverage, fire rim, aim point
    // and ray hits all use the same enlarged articulated silhouette.
    public float LinearScale { get=>rig.LinearScale; set=>rig.LinearScale=Mathf.Max(.1f,value); }

    public BellShadowProjection(SkinnedMeshRenderer model,Mesh receiver,MeshRenderer renderer,Rect bounds,Func<float,float,float> ground)
        : this(receiver,renderer,bounds,ground,"Antlered shadow creature") {
        model.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        rig=new MonsterShadowRig(model);
        int longest=0;
        foreach(var outline in rig.Outlines) longest=Mathf.Max(longest,outline.Points.Length);
        intersections=new float[longest];
        pixelContour=new Vector2[longest];
    }
    internal BellShadowProjection(Mesh receiver,MeshRenderer renderer,Rect bounds,Func<float,float,float> ground,string name,bool localReceiver=false) {
        mesh=receiver;floorBounds=bounds;this.ground=ground;
        if(localReceiver) {
            // A rotated, non-uniformly scaled guardian cannot cancel its parent's
            // transform with another TRS. Convert each world floor vertex instead.
            armourReceiver=renderer.transform;
            armourReceiver.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            armourReceiver.localScale=Vector3.one;
            localGrid=new Vector3[grid.Length];
        }
        coverage=new Texture2D(Resolution,Resolution,TextureFormat.R8,false,true) {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,name=name};
        surfaceMaterial=new Material(Resources.Load<Shader>("BellSilhouette"));
        surfaceMaterial.mainTexture=coverage;
        renderer.sharedMaterial=surfaceMaterial;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows=false;
        Vector2[] uv=new Vector2[grid.Length];
        var triangles=new List<int>(Divisions*Divisions*6);
        for(int y=0;y<=Divisions;y++) for(int x=0;x<=Divisions;x++) {
            int i=y*(Divisions+1)+x;uv[i]=new Vector2((float)x/Divisions,(float)y/Divisions);
            if(x==Divisions || y==Divisions)continue;
            int k=i+Divisions+1;
            triangles.Add(i);triangles.Add(k);triangles.Add(i+1);
            triangles.Add(i+1);triangles.Add(k);triangles.Add(k+1);
        }
        mesh.Clear();mesh.vertices=grid;mesh.uv=uv;mesh.SetTriangles(triangles,0);
    }

    public void Update(ClawState state) {
        rig.Pose(state);
        Vector2 lo=new Vector2(float.MaxValue,float.MaxValue),hi=-lo;
        foreach(var contour in rig.Outlines) {
            foreach(var p in contour.Points) {
                lo=Vector2.Min(lo,p);hi=Vector2.Max(hi,p);
            }
        }
        SetArea(lo,hi);
        foreach(var contour in rig.Outlines) Raster(contour);
        Publish(rig.TorsoCenter,true);
    }
    Rect SetArea(Vector2 lo,Vector2 hi) {
        Rect previous=area;
        Vector2 pad=Vector2.Max((hi-lo)*.035f,new Vector2(.025f,.025f));
        lo=Vector2.Max(floorBounds.min,lo-pad);hi=Vector2.Min(floorBounds.max,hi+pad);
        area=Rect.MinMaxRect(lo.x,lo.y,Mathf.Max(hi.x,lo.x+.001f),Mathf.Max(hi.y,lo.y+.001f));
        Array.Clear(mask,0,mask.Length);
        return previous;
    }
    internal void UpdateArmour(Vector2[] points,Vector2[] pixels,int[] triangles,Vector2 target) {
        Vector2 lo=points[0],hi=lo;
        foreach(Vector2 p in points){lo=Vector2.Min(lo,p);hi=Vector2.Max(hi,p);}
        // Keep the receiver stationary during small breathing motions on static floors.
        lo=new Vector2(Mathf.Floor(lo.x*2),Mathf.Floor(lo.y*2))*.5f;
        hi=new Vector2(Mathf.Ceil(hi.x*2),Mathf.Ceil(hi.y*2))*.5f;
        Rect previous=SetArea(lo,hi);
        for(int i=0;i<points.Length;i++)pixels[i]=Pixel(points[i]);
        for(int i=0;i<triangles.Length;i+=3)
            RasterTriangle(pixels[triangles[i]],pixels[triangles[i+1]],pixels[triangles[i+2]]);
        Publish(target,previous!=area || (armourReceiver!=null && armourReceiver.localToWorldMatrix!=lastReceiverMatrix));
    }
    void RasterTriangle(Vector2 a,Vector2 b,Vector2 c) {
        if(a.y>b.y){Vector2 swap=a;a=b;b=swap;}
        if(b.y>c.y){Vector2 swap=b;b=c;c=swap;}
        if(a.y>b.y){Vector2 swap=a;a=b;b=swap;}
        int start=Mathf.Max(0,Mathf.CeilToInt(a.y-.5f));
        int end=Mathf.Min(Resolution,Mathf.CeilToInt(c.y-.5f));
        if(start>=end)return;
        float longSlope=(c.x-a.x)/(c.y-a.y);
        float bottomSlope=b.y>a.y ? (b.x-a.x)/(b.y-a.y):0;
        float topSlope=c.y>b.y ? (c.x-b.x)/(c.y-b.y):0;
        for(int y=start;y<end;y++) {
            float row=y+.5f;
            float x1=a.x+(row-a.y)*longSlope;
            float x2=row<b.y ? a.x+(row-a.y)*bottomSlope : b.x+(row-b.y)*topSlope;
            int left=Mathf.Max(0,Mathf.CeilToInt(Mathf.Min(x1,x2)-.5f));
            int right=Mathf.Min(Resolution,Mathf.CeilToInt(Mathf.Max(x1,x2)-.5f));
            for(int x=left;x<right;x++)mask[y*Resolution+x]=255;
        }
    }
    void Publish(Vector2 target,bool updateFloor) {
        coverage.LoadRawTextureData(mask);coverage.Apply(false);
        if(updateFloor) {
            var toLocal=armourReceiver!=null ? armourReceiver.worldToLocalMatrix : Matrix4x4.identity;
            for(int y=0;y<=Divisions;y++) for(int x=0;x<=Divisions;x++) {
                float wx=area.xMin+area.width*x/Divisions,wz=area.yMin+area.height*y/Divisions;
                int i=y*(Divisions+1)+x;
                float height=Surface(wx,wz);
                grid[i]=new Vector3(wx,wz,-height);
                if(armourReceiver!=null)localGrid[i]=toLocal.MultiplyPoint3x4(new Vector3(wx,height,wz));
            }
            if(armourReceiver!=null)lastReceiverMatrix=armourReceiver.localToWorldMatrix;
            mesh.vertices=localGrid??grid;mesh.RecalculateBounds();
        }
        if(Contains(target)){Aim=target;return;}
        float best=float.PositiveInfinity;
        for(int i=0;i<mask.Length;i+=3) if(mask[i]>=128) {
            Vector2 p=World(i%Resolution,i/Resolution);
            float d=(p-target).sqrMagnitude;
            if(d<best){best=d;Aim=p;}
        }
    }
    float Surface(float x,float z) {
        float h=ground(x,z);
        return h+.019f+(h>0?.045f:0);
    }
    float ReceiverHeight(int x,int y) {
        float gx=(x+.5f)*Divisions/Resolution,gy=(y+.5f)*Divisions/Resolution;
        int ix=(int)gx,iy=(int)gy,i=iy*(Divisions+1)+ix,k=i+Divisions+1;
        float u=gx-ix,v=gy-iy;
        return u+v<=1 ? -(grid[i].z*(1-u-v)+grid[i+1].z*u+grid[k].z*v)
            : -(grid[k+1].z*(u+v-1)+grid[i+1].z*(1-v)+grid[k].z*(1-u));
    }
    internal void Dispose() {
        UnityEngine.Object.Destroy(coverage);
        UnityEngine.Object.Destroy(surfaceMaterial);
    }
    Vector2 Pixel(Vector2 p) => new Vector2((p.x-area.xMin)*Resolution/area.width,(p.y-area.yMin)*Resolution/area.height);
    Vector2 World(int x,int y) => new Vector2(area.xMin+(x+.5f)*area.width/Resolution,area.yMin+(y+.5f)*area.height/Resolution);
    void Raster(MonsterShadowRig.Outline outline) {
        // Even-odd scan conversion preserves concave antlers and the gaps between hooks.
        int count=outline.Points.Length;
        float minY=float.MaxValue,maxY=float.MinValue;
        for(int i=0;i<count;i++) {
            var p=Pixel(outline.Points[i]);pixelContour[i]=p;
            minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);
        }
        int bottom=Mathf.Max(0,Mathf.FloorToInt(minY));
        int top=Mathf.Min(Resolution-1,Mathf.CeilToInt(maxY)-1);
        for(int y=bottom;y<=top;y++) {
            int first=Resolution,last=-1;
            for(int sub=0;sub<2;sub++) {
                float row=y+(sub==0?.25f:.75f);int crossings=0;
                Vector2 a=pixelContour[count-1];
                for(int i=0;i<count;i++) {
                    Vector2 b=pixelContour[i];
                    if((a.y>row)!=(b.y>row))
                        intersections[crossings++]=a.x+(b.x-a.x)*(row-a.y)/(b.y-a.y);
                    a=b;
                }
                Array.Sort(intersections,0,crossings);
                for(int i=0;i+1<crossings;i+=2) {
                    float l=Mathf.Max(0,intersections[i]),r=Mathf.Min(Resolution,intersections[i+1]);
                    if(r<=l)continue;
                    int start=(int)l,end=Mathf.CeilToInt(r)-1;
                    first=Mathf.Min(first,start);last=Mathf.Max(last,end);
                    if(start==end) {
                        rowCoverage[start]+=(ushort)Mathf.RoundToInt((r-l)*128);
                    } else {
                        rowCoverage[start]+=(ushort)Mathf.RoundToInt((start+1-l)*128);
                        for(int x=start+1;x<end;x++)rowCoverage[x]+=128;
                        rowCoverage[end]+=(ushort)Mathf.RoundToInt((r-end)*128);
                    }
                }
            }
            // Union coverage before uploading. An eye socket subtracts only its
            // own coverage, retaining a soft subpixel boundary on narrow horns.
            for(int x=first;x<=last;x++) {
                int k=y*Resolution+x,covered=Mathf.Min(255,(int)rowCoverage[x]);
                mask[k]=(byte)(outline.Cutout?Mathf.Min(mask[k],255-covered):Mathf.Max(mask[k],covered));
                rowCoverage[x]=0;
            }
        }
    }
    public bool Contains(Vector2 p) {
        if(!area.Contains(p))return false;
        Vector2 xy=Pixel(p);
        int x=Mathf.Clamp((int)xy.x,0,Resolution-1),y=Mathf.Clamp((int)xy.y,0,Resolution-1);
        return mask[y*Resolution+x]>=128;
    }
    public bool Touches(Vector2 p,float radius) {
        if(Contains(p))return true;
        Vector2 low=Pixel(p-Vector2.one*radius),high=Pixel(p+Vector2.one*radius);
        for(int y=Mathf.Max(0,(int)low.y);y<=Mathf.Min(Resolution-1,(int)high.y);y++)
        for(int x=Mathf.Max(0,(int)low.x);x<=Mathf.Min(Resolution-1,(int)high.x);x++)
            if(mask[y*Resolution+x]>=128 && (World(x,y)-p).sqrMagnitude<=radius*radius)return true;
        return false;
    }
    public float HitDistance(Ray ray) {
        // Sample the terrain tessellation used for the visual decal, then test its coverage.
        Vector3 o=new Vector3(ray.origin.x,ray.origin.z,-ray.origin.y),d=new Vector3(ray.direction.x,ray.direction.z,-ray.direction.y);
        float nearest=float.PositiveInfinity;
        for(int i=0;i<grid.Length-Divisions-1;i++) {
            if(i%(Divisions+1)==Divisions)continue;
            int k=i+Divisions+1;
            Test(grid[i],grid[k],grid[i+1]);Test(grid[i+1],grid[k],grid[k+1]);
        }
        return nearest;
        void Test(Vector3 a,Vector3 b,Vector3 c) {
            Vector3 ab=b-a,ac=c-a,h=Vector3.Cross(d,ac);
            float det=Vector3.Dot(ab,h);
            if(Mathf.Abs(det)<1e-7f)return;
            Vector3 s=o-a,q=Vector3.Cross(s,ab);
            float u=Vector3.Dot(s,h)/det,v=Vector3.Dot(d,q)/det,t=Vector3.Dot(ac,q)/det;
            if(u<0||v<0||u+v>1||t<0||t>=nearest)return;
            Vector3 point=ray.GetPoint(t);
            if(Contains(new Vector2(point.x,point.z)))nearest=t;
        }
    }
    public void SetFireRim(float strength,float angle,float spread) {
        surfaceMaterial.SetFloat("_FireHeat",strength);
        surfaceMaterial.SetVector("_FireOrigin",new Vector4(Aim.x,Aim.y,angle,spread));
    }
    public void ReadBorder(List<Vector3> result) {
        result.Clear();
        for(int y=1;y<Resolution-1;y++) for(int x=1;x<Resolution-1;x++) {
            int i=y*Resolution+x;
            if(mask[i]<128 || (mask[i-1]>=128 && mask[i+1]>=128 && mask[i-Resolution]>=128 && mask[i+Resolution]>=128))continue;
            Vector2 p=World(x,y);
            result.Add(new Vector3(p.x,rig==null ? ReceiverHeight(x,y) : Surface(p.x,p.y),p.y));
        }
    }
}
