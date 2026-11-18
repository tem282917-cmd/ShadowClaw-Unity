using UnityEngine;
using UnityEngine.UI;

// Vertex colours give the hit flash a transparent centre and soft edges in one UI draw.
public sealed class HunterDamageBorder : MaskableGraphic {
    protected override void OnPopulateMesh(VertexHelper mesh) {
        mesh.Clear();
        var rect=rectTransform.rect;
        for(int ring=0;ring<2;ring++) {
            Vector2 inset=rect.size*(ring*.14f);
            var low=rect.min+inset;var high=rect.max-inset;
            Color tint=color;if(ring==1)tint.a=0;
            mesh.AddVert(new Vector3(low.x,low.y),tint,Vector2.zero);
            mesh.AddVert(new Vector3(low.x,high.y),tint,Vector2.zero);
            mesh.AddVert(new Vector3(high.x,high.y),tint,Vector2.zero);
            mesh.AddVert(new Vector3(high.x,low.y),tint,Vector2.zero);
        }
        for(int side=0;side<4;side++) {
            int next=(side+1)%4;
            mesh.AddTriangle(side,next,next+4);mesh.AddTriangle(side,next+4,side+4);
        }
    }
}
