using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Native uGUI primitives shared only by the Shiyin presentation layer.
internal static class ShiyinWidgets
{
    internal static readonly Color Ink = Hex("E8DFCA"), Silver = Hex("C7C5BC"), Muted = Hex("B9B5AB"),
        Surface = Hex("121719"), Danger = Hex("C7807E"), Health = Hex("B45E68"),
        Energy = Hex("9AAF9E"), Flame = Hex("E9BB72");
    static Font bodyFont, titleFont;
    static Sprite white;
    static readonly Dictionary<string,Sprite> sprites = new Dictionary<string,Sprite>();
    internal static Color Hex(string value, float alpha=1f) {
        Color c; ColorUtility.TryParseHtmlString("#"+value,out c); c.a=alpha; return c;
    }
    internal static Color Alpha(Color c,float a) { c.a=a; return c; }
    internal static Font BodyFont => bodyFont != null ? bodyFont : bodyFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Microsoft YaHei UI","SimSun","Arial"},32);
    internal static Font TitleFont => titleFont != null ? titleFont : titleFont=Font.CreateDynamicFontFromOSFont(new[]{"SimSun","NSimSun","Microsoft YaHei","Arial"},40);
    internal static Sprite White {
        get {
            if(white!=null) return white;
            var texture=new Texture2D(1,1,TextureFormat.RGBA32,false) {name="Shiyin UI White",filterMode=FilterMode.Point};
            texture.SetPixel(0,0,Color.white);texture.Apply(false,true);
            return white=Sprite.Create(texture,new Rect(0,0,1,1),new Vector2(.5f,.5f),1);
        }
    }
    internal static Sprite IconSprite(string name) {
        if(sprites.TryGetValue(name,out var cached))return cached;
        var sprite=Resources.Load<Sprite>("Shiyin/Icons/"+name);
        if(sprite==null){
            var texture=Resources.Load<Texture2D>("Shiyin/Icons/"+name);
            if(texture!=null)sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
        }
        sprites[name]=sprite;return sprite;
    }
    internal static RectTransform Rect(Transform parent,string name,float x,float y,float width,float height) {
        var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(parent,false);
        rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);
        rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(width,height);return rt;
    }
    internal static RectTransform Stretch(Transform parent,string name) {
        var rt=Rect(parent,name,0,0,0,0);rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;
        rt.offsetMin=rt.offsetMax=Vector2.zero;return rt;
    }
    internal static RectTransform Center(Transform parent,string name,float width,float height,float x=0,float y=0) {
        var rt=Rect(parent,name,0,0,width,height);rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);
        rt.anchoredPosition=new Vector2(x,y);return rt;
    }
    internal static RectTransform Anchor(Transform parent,string name,Vector2 anchor,Vector2 pivot,float x,float y,float width,float height) {
        var rt=Rect(parent,name,0,0,width,height);rt.anchorMin=rt.anchorMax=anchor;rt.pivot=pivot;
        rt.anchoredPosition=new Vector2(x,y);return rt;
    }
    internal static Image Image(RectTransform rt,Color color,bool raycast=false) {
        var i=rt.gameObject.AddComponent<Image>();i.sprite=White;i.color=color;i.raycastTarget=raycast;return i;
    }
    internal static Image Fill(Transform parent,string name,float x,float y,float width,float height,Color color) => Image(Rect(parent,name,x,y,width,height),color);
    internal static Image Icon(Transform parent,string name,float x,float y,float width,float height,Color color) {
        var image=Image(Rect(parent,"Icon "+name,x,y,width,height),color);image.sprite=IconSprite(name);image.preserveAspect=true;
        if(image.sprite==null)image.enabled=false;return image;
    }
    internal static Text Text(Transform parent,string text,float x,float y,float width,float height,int size,Color color,bool title=false,TextAnchor align=TextAnchor.UpperLeft) {
        var t=Rect(parent,"Text",x,y,width,height).gameObject.AddComponent<Text>();
        t.font=title?TitleFont:BodyFont;t.fontSize=size;t.color=color;t.text=text;t.alignment=align;
        t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Overflow;
        t.supportRichText=false;t.raycastTarget=false;t.lineSpacing=1.12f;return t;
    }
    internal static void Border(RectTransform target,Color color,float thickness=1f) {
        var top=Stretch(target,"Border top");top.anchorMin=new Vector2(0,1);top.offsetMin=new Vector2(0,-thickness);top.offsetMax=Vector2.zero;Image(top,color);
        var bottom=Stretch(target,"Border bottom");bottom.anchorMax=new Vector2(1,0);bottom.offsetMin=Vector2.zero;bottom.offsetMax=new Vector2(0,thickness);Image(bottom,color);
        var left=Stretch(target,"Border left");left.anchorMax=new Vector2(0,1);left.offsetMin=Vector2.zero;left.offsetMax=new Vector2(thickness,0);Image(left,color);
        var right=Stretch(target,"Border right");right.anchorMin=new Vector2(1,0);right.offsetMin=new Vector2(-thickness,0);right.offsetMax=Vector2.zero;Image(right,color);
    }
    internal static void Corners(RectTransform target,Color color) {
        foreach(bool right in new[]{false,true}) {
            var corner=Rect(target,"Silver corner",0,0,18,18);
            corner.anchorMin=corner.anchorMax=new Vector2(right?1:0,1);corner.pivot=new Vector2(right?1:0,1);
            Fill(corner,"Top",0,0,18,2,color);Fill(corner,"Side",right?16:0,0,2,18,color);
        }
    }
    internal static Image Diamond(Transform parent,string name,float x,float y,float size,Color color) {
        var mark=Fill(parent,name,x,y,size,size,color);mark.rectTransform.localRotation=Quaternion.Euler(0,0,45);return mark;
    }
    internal static void TextOutline(Text text) {
        var outline=text.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.015f,.02f,.025f,.85f);
        outline.effectDistance=new Vector2(.9f,-.9f);outline.useGraphicAlpha=true;
    }
    internal static Button Button(Transform parent,string action,string label,float x,float y,float width,float height,Action click,int size=20,bool primary=false,bool danger=false,bool quiet=false) {
        var rt=Rect(parent,action,x,y,width,height);
        var baseColor=danger?Hex("70272D",.12f):Alpha(Silver,quiet?0:primary?.045f:.018f);
        var bg=Image(rt,baseColor,true);
        if(!quiet)Fill(rt,"Engraved underline",0,height-1,width,1,danger?Alpha(Danger,.65f):Alpha(Silver,primary?.7f:.3f));
        if(primary)Diamond(rt,"Selection mark",12,height*.5f-3,6,Silver);
        var text=Text(rt,label,28,0,width-56,height,size,danger?Hex("F1B4AA"):quiet?Muted:Ink,false,TextAnchor.MiddleCenter);
        var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=bg;
        var colors=b.colors;colors.normalColor=Color.white;colors.highlightedColor=Hex("FFFFFF");colors.selectedColor=Hex("FFFFFF");colors.pressedColor=Hex("C8C6B5");colors.disabledColor=Hex("777C79");colors.fadeDuration=.12f;b.colors=colors;
        b.onClick.AddListener(()=>click?.Invoke());
        var focus=rt.gameObject.AddComponent<ShiyinFocusVisual>();focus.Setup(bg,baseColor);
        return b;
    }
    internal static Toggle Toggle(Transform parent,string action,bool value,float x,float y,float width,Action<bool> change,int size=18) {
        var rt=Rect(parent,action,x,y,width,54);
        var bg=Fill(rt,"Toggle box",0,10,54,30,Alpha(Silver,.04f));bg.raycastTarget=true;Border(bg.rectTransform,Alpha(Silver,.6f));
        var check=Diamond(bg.rectTransform,"Toggle check",22,10,10,Silver);
        var label=Text(rt,value?"开启":"关闭",73,0,width-73,54,size,Muted,false,TextAnchor.MiddleLeft);
        var toggle=rt.gameObject.AddComponent<Toggle>();toggle.targetGraphic=bg;toggle.graphic=check;toggle.isOn=value;
        toggle.onValueChanged.AddListener(v=>{label.text=v?"开启":"关闭";change?.Invoke(v);});
        var clickArea=Image(rt,Color.clear,true);toggle.targetGraphic=clickArea;
        rt.gameObject.AddComponent<ShiyinFocusVisual>().Setup(clickArea,Color.clear);
        return toggle;
    }
    internal static Slider Slider(Transform parent,string action,float value,float min,float max,float x,float y,float width,Action<float> change,bool whole=false) {
        var rt=Rect(parent,action,x,y,width,50);var bg=Image(rt,Color.clear,true);
        var track=Fill(rt,"Track",0,24,width,2,Alpha(Silver,.24f));
        var fillArea=Rect(rt,"Fill area",0,24,width,2);var fill=Image(Stretch(fillArea,"Fill"),Silver);
        var handleArea=Rect(rt,"Handle area",0,14,width,22);var handle=Image(Stretch(handleArea,"Handle"),Ink);
        handle.rectTransform.anchorMin=new Vector2(0,0);handle.rectTransform.anchorMax=new Vector2(0,1);
        handle.rectTransform.pivot=new Vector2(.5f,.5f);handle.rectTransform.sizeDelta=new Vector2(12,0);handle.rectTransform.anchoredPosition=Vector2.zero;
        var slider=rt.gameObject.AddComponent<Slider>();slider.fillRect=fill.rectTransform;slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;
        slider.minValue=min;slider.maxValue=max;slider.wholeNumbers=whole;slider.value=value;slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.onValueChanged.AddListener(v=>change?.Invoke(v));rt.gameObject.AddComponent<ShiyinFocusVisual>().Setup(bg,Color.clear);
        return slider;
    }
    internal static RectTransform Scroll(Transform parent,string name,float x,float y,float width,float height,float contentHeight) {
        var rt=Rect(parent,name,x,y,width,height);var scroll=rt.gameObject.AddComponent<ScrollRect>();
        var viewport=Rect(rt,"Viewport",0,0,width-18,height);Image(viewport,Color.clear,true);viewport.gameObject.AddComponent<RectMask2D>();
        var content=Rect(viewport,"Content",0,0,width-18,Mathf.Max(height,contentHeight));
        scroll.content=content;scroll.viewport=viewport;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=38;scroll.inertia=false;
        var barRoot=Rect(rt,"Scrollbar",width-6,0,6,height);var track=Image(barRoot,Hex("25312F"),true);
        var area=Stretch(barRoot,"Slide area");var thumb=Image(Stretch(area,"Handle"),Alpha(Silver,.6f));
        var bar=barRoot.gameObject.AddComponent<Scrollbar>();bar.targetGraphic=thumb;bar.handleRect=thumb.rectTransform;bar.direction=Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }
}

internal sealed class ShiyinFocusVisual : MonoBehaviour,ISelectHandler,IDeselectHandler,IPointerEnterHandler,IPointerExitHandler
{
    Image background;Color normal;GameObject outline;bool selected,hovered;
    internal void Setup(Image image,Color color) {
        background=image;normal=color;
        var rect=ShiyinWidgets.Stretch(transform,"Focus outline");rect.offsetMin=new Vector2(-5,-5);rect.offsetMax=new Vector2(5,5);
        ShiyinWidgets.Border(rect,ShiyinWidgets.Alpha(ShiyinWidgets.Silver,.75f),1);outline=rect.gameObject;outline.SetActive(false);
    }
    void Paint(){if(outline!=null)outline.SetActive(selected);if(background!=null)background.color=selected||hovered?ShiyinWidgets.Alpha(ShiyinWidgets.Silver,Mathf.Max(normal.a,.10f)):normal;}
    public void OnSelect(BaseEventData e){selected=true;Paint();}
    public void OnDeselect(BaseEventData e){selected=false;Paint();}
    public void OnPointerEnter(PointerEventData e){hovered=true;Paint();}
    public void OnPointerExit(PointerEventData e){hovered=false;Paint();}
    void OnDisable(){selected=hovered=false;if(outline!=null)outline.SetActive(false);if(background!=null)background.color=normal;}
}
