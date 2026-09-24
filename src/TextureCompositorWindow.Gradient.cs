using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private GradientLayerBehaviour gradientCanvasLayer => GetSelectedLayer()?.Behaviour as GradientLayerBehaviour;
        private VisualElement gradientCanvasOverlay;
        private GradientCanvasManipulator gradientCanvasManipulator;
        private bool IsGradientCanvasAvailable => compositor != null && gradientCanvasLayer != null &&
            gradientCanvasLayer.gradientType != GradientLayerBehaviour.GradientType.Circular &&
            !WhimTexApi.IsLayerContentLocked(compositor, gradientCanvasLayer.Owner) &&
            !WhimTexApi.ContainsReservation(gradientCanvasLayer.Owner);
        private bool IsGradientCanvasEnabled => previewTool == PreviewTool.GradientHandles && IsGradientCanvasAvailable;

        private void BuildGradientCanvasTool()
        {
            gradientCanvasOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            gradientCanvasOverlay.StretchToParentSize();
            toolkitPreviewCanvas.Add(gradientCanvasOverlay);
            gradientCanvasManipulator = new GradientCanvasManipulator(this);
            gradientCanvasOverlay.generateVisualContent += gradientCanvasManipulator.Draw;
            toolkitPreviewCanvas.AddManipulator(gradientCanvasManipulator);
        }

        private sealed class GradientCanvasManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            private GradientLayerBehaviour layer;
            private WhimTexGradient originalGradient;
            private TextureTransform originalTransform, originalLocal;
            private Vector2 pointerStart, start, end;
            private int pointer = -1, handle, undo = -1;
            private int selected = -1;
            private GradientLayerBehaviour selectionLayer;
            private bool pendingRemoval;
            private bool midpointSelected;
            public bool IsDragging => pointer >= 0;
            private Vector2 Size => new Vector2(owner.compositor.width, owner.compositor.height);
            public GradientCanvasManipulator(TextureCompositorWindow owner) => this.owner = owner;
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<DetachFromPanelEvent>(Detach);
                target.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                End(false);
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<DetachFromPanelEvent>(Detach);
                target.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            }
            private Vector2 View(Vector2 pixel)
            {
                Rect r = owner.toolkitPreviewCanvas.ImageRect;
                return owner.toolkitPreviewCanvas.ToView(new Vector2(r.x + pixel.x / Size.x * r.width, r.yMax - pixel.y / Size.y * r.height));
            }
            private Vector2 Document(Vector2 view)
            {
                Rect r = owner.toolkitPreviewCanvas.ImageRect;
                Vector2 p = owner.toolkitPreviewCanvas.ToCanvas(view);
                return new Vector2((p.x-r.x)/r.width*Size.x, (r.yMax-p.y)/r.height*Size.y);
            }
            private void Points(out Vector2 a, out Vector2 b)
            {
                owner.compositor.RefreshTransformHierarchy();
                GradientCanvasGeometry.UvEndpoints(owner.gradientCanvasLayer, out var ua, out var ub);
                var t = owner.gradientCanvasLayer.Owner.CanvasTransform;
                a = GradientCanvasGeometry.Point(ua, t, Size); b = GradientCanvasGeometry.Point(ub, t, Size);
                a=View(a); b=View(b);
            }
            private static Vector2 Offset(Vector2 a, Vector2 b)
            {
                Vector2 direction=(b-a).normalized;
                if(direction.sqrMagnitude<.5f)direction=Vector2.right;
                return new Vector2(-direction.y,direction.x)*18;
            }
            private float Projection(Vector2 p, Vector2 a, Vector2 b)
            {
                float screenTime=Mathf.Clamp01(Vector2.Dot(p-a,b-a)/Mathf.Max(.0001f,(b-a).sqrMagnitude));
                var g=owner.gradientCanvasLayer;
                if(g.Owner.CanvasTransform.storage==TransformStorage.TRS) return screenTime;
                GradientCanvasGeometry.UvEndpoints(g,out var ua,out var ub);
                var m=g.Owner.CanvasTransform.matrix;
                double wa=m.m20*ua.x+m.m21*ua.y+m.m22, wb=m.m20*ub.x+m.m21*ub.y+m.m22;
                return (float)(screenTime*wa/(wb*(1-screenTime)+screenTime*wa));
            }
            private Vector2 KeyPosition(Vector2 a,Vector2 b,float time)
            {
                var g=owner.gradientCanvasLayer;
                if(g.Owner.CanvasTransform.storage==TransformStorage.TRS) return Vector2.Lerp(a,b,time);
                GradientCanvasGeometry.UvEndpoints(g,out var ua,out var ub);
                return View(GradientCanvasGeometry.Point(Vector2.Lerp(ua,ub,time),g.Owner.CanvasTransform,Size));
            }
            private void Down(PointerDownEvent e)
            {
                if (!owner.IsGradientCanvasEnabled || pointer>=0 || e.button!=0 || e.altKey || owner.toolkitPreviewCanvas.ImageRect.width<=0) return;
                Points(out var a,out var b); Vector2 p=e.localPosition, offset=Offset(a,b);
                int hit = (p-a-offset).sqrMagnitude<=64 ? -2 : (p-b-offset).sqrMagnitude<=64 ? -3 : -1;
                var g=owner.gradientCanvasLayer.gradient;
                var keys=g.ColorKeys;
                int midpointHit=-1;
                if(g.Mode!=WhimTexGradientMode.Fixed && midpointSelected && selectionLayer==owner.gradientCanvasLayer &&
                    selected>=0 && selected<keys.Length-1 &&
                    (p-MidpointPosition(g,selected,a,b)).sqrMagnitude<=64)
                    midpointHit=selected;
                if(hit==-1 && midpointHit<0)
                {
                    float nearest=100;
                    for(int i=0;i<keys.Length;i++)
                    {
                        float distance=(p-KeyPosition(a,b,keys[i].time)).sqrMagnitude;
                        if(distance<=nearest){nearest=distance;hit=i;}
                    }
                }
                if(hit==-1 && midpointHit<0 && g.Mode!=WhimTexGradientMode.Fixed)
                    for(int i=0;i<keys.Length-1;i++)
                        if((p-MidpointPosition(g,i,a,b)).sqrMagnitude<=64){midpointHit=i;break;}
                float time=Projection(p,a,b);
                if(hit==-1 && midpointHit<0 && (p-KeyPosition(a,b,time)).sqrMagnitude>36) return;
                owner.Focus(); target.Focus();
                midpointSelected=midpointHit>=0;
                if(midpointSelected)hit=midpointHit;
                if(hit>=0 && !midpointSelected && e.clickCount==2)
                {
                    selected=hit; selectionLayer=owner.gradientCanvasLayer;
                    owner.gradientCanvasOverlay.MarkDirtyRepaint();
                    OpenColor(hit); e.StopImmediatePropagation(); return;
                }
                layer=owner.gradientCanvasLayer; originalGradient=g.Clone(); originalTransform=owner.compositor.GetCanvasTransform(layer); originalLocal=layer.transform;
                GradientCanvasGeometry.UvEndpoints(layer,out var startUv,out var endUv);
                start=GradientCanvasGeometry.Point(startUv,originalTransform,Size); end=GradientCanvasGeometry.Point(endUv,originalTransform,Size);
                pointerStart=Document(p); handle=hit; selected=hit>=0?hit:-1;
                selectionLayer=layer; pendingRemoval=false;
                owner.gradientCanvasOverlay.MarkDirtyRepaint();
                if(hit==-1)
                {
                    if(keys.Length>=64)return;
                    foreach(var key in keys) if(Mathf.Abs(key.time-time)<.00001f)return;
                    BeginUndo();
                    Array.Resize(ref keys,keys.Length+1);
                    keys[keys.Length-1]=new GradientColorKey(g.Evaluate(time),time);
                    Array.Sort(keys,(x,y)=>x.time.CompareTo(y.time));
                    g.SetKeys(keys,g.AlphaKeys);
                    selected=Array.FindIndex(keys,k=>k.time==time);
                    Commit(); e.StopImmediatePropagation(); return;
                }
                pointer=e.pointerId; target.CapturePointer(pointer); e.StopImmediatePropagation();
            }
            private void BeginUndo()
            {
                if(undo>=0)return;
                Undo.IncrementCurrentGroup(); undo=Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Edit Gradient on Canvas");
                Undo.RegisterCompleteObjectUndo(owner.compositor,"Edit Gradient on Canvas");
            }
            private void Move(PointerMoveEvent e)
            {
                if(pointer!=e.pointerId)return;
                Validate(); if(pointer<0)return;
                BeginUndo();
                if(midpointSelected && handle>=0)
                {
                    Points(out var a,out var b);
                    var keys=originalGradient.ColorKeys;
                    float time=Projection(e.localPosition,a,b);
                    layer.gradient=originalGradient.Clone();
                    layer.gradient.SetMidpoint(false,handle,Mathf.Clamp(
                        (time-keys[handle].time)/(keys[handle+1].time-keys[handle].time),.01f,.99f));
                }
                else if(handle>=0)
                {
                    Points(out var a,out var b);
                    float time=Projection(e.localPosition,a,b);
                    var keys=originalGradient.ColorKeys;
                    Vector2 direction=(b-a).normalized;
                    Vector2 distance=(Vector2)e.localPosition-a;
                    pendingRemoval=keys.Length>1 && Mathf.Abs(direction.x*distance.y-direction.y*distance.x)>32f;
                    if(pendingRemoval)
                    {
                        owner.gradientCanvasOverlay.MarkDirtyRepaint();
                        e.StopImmediatePropagation(); return;
                    }
                    for(int i=0;i<keys.Length;i++) if(i!=handle && Mathf.Abs(keys[i].time-time)<.00001f)return;
                    float midpoint=originalGradient.GetMidpoint(false,handle);
                    keys[handle].time=time;
                    Array.Sort(keys,(x,y)=>x.time.CompareTo(y.time));
                    layer.gradient=originalGradient.Clone();
                    layer.gradient.SetKeys(keys,originalGradient.AlphaKeys);
                    selected=Array.FindIndex(keys,k=>k.time==time);
                    layer.gradient.SetMidpoint(false,selected,midpoint);
                }
                else
                {
                    Vector2 delta=Document(e.localPosition)-pointerStart;
                    var next = GradientCanvasGeometry.MoveEndpointTransform(layer,Size,start,end,handle==-2,delta,originalTransform);
                    owner.compositor.SetCanvasTransform(layer,next);
                }
                owner.RequestTransformPreview(); owner.gradientCanvasOverlay.MarkDirtyRepaint();
                e.StopImmediatePropagation();
            }
            private void Up(PointerUpEvent e)
            {
                if(pointer!=e.pointerId || e.button!=0)return;
                if(pendingRemoval && handle>=0)
                {
                    BeginUndo();
                    layer.gradient=WithoutColorKey(originalGradient,handle);
                    selected=-1;
                }
                End(false); e.StopImmediatePropagation();
            }
            private void Lost(PointerCaptureOutEvent e){if(e.pointerId==pointer)End(false);}
            private void Detach(DetachFromPanelEvent e)=>End(false);
            public void Validate()
            {
                if(pointer>=0 && (!owner.IsGradientCanvasEnabled || owner.gradientCanvasLayer!=layer))End(false);
                if(selectionLayer!=owner.gradientCanvasLayer){selected=-1;selectionLayer=null;}
            }
            private static WhimTexGradient WithoutColorKey(WhimTexGradient source,int index)
            {
                var keys=source.ColorKeys;
                var remaining=new GradientColorKey[keys.Length-1];
                Array.Copy(keys,0,remaining,0,index);
                Array.Copy(keys,index+1,remaining,index,keys.Length-index-1);
                var result=source.Clone();
                result.SetKeys(remaining,source.AlphaKeys);
                for(int i=0;i<remaining.Length;i++)
                    result.SetMidpoint(false,i,source.GetMidpoint(false,i<index?i:i+1));
                return result;
            }
            public bool HandleDelete(KeyDownEvent e)
            {
                Validate();
                var element=e.target as VisualElement;
                if(e.keyCode!=KeyCode.Delete || !owner.IsGradientCanvasEnabled ||
                    selectionLayer!=owner.gradientCanvasLayer || selected<0 ||
                    (element!=target && (element==null || !target.Contains(element))))return false;
                if(!midpointSelected && pointer<0 && selected<selectionLayer.gradient.ColorKeys.Length &&
                    selectionLayer.gradient.ColorKeys.Length>1)
                {
                    BeginUndo();
                    selectionLayer.gradient=WithoutColorKey(selectionLayer.gradient,selected);
                    selected=-1; Commit();
                }
                e.StopImmediatePropagation(); return true;
            }
            private void Key(KeyDownEvent e)
            {
                if(!owner.IsGradientCanvasEnabled)return;
                if(e.keyCode==KeyCode.Escape && pointer>=0)
                {
                    End(true);
                    e.StopImmediatePropagation();
                }
            }
            public void End(bool cancel, bool commit = true)
            {
                if(pointer<0)return;
                int captured=pointer; pointer=-1;
                if(target.HasPointerCapture(captured))target.ReleasePointer(captured);
                if(cancel && layer!=null && undo>=0)
                {
                    layer.transform=originalLocal; layer.gradient=originalGradient;
                }
                if(undo>=0 && commit)Commit();
                undo=-1; layer=null;
                pendingRemoval=false;
                owner.gradientCanvasOverlay?.MarkDirtyRepaint();
            }
            private void Commit()
            {
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undo); Undo.IncrementCurrentGroup(); undo=-1;
                owner.applyingToolkitChange=true;
                try{owner.CommitModelChange();}finally{owner.applyingToolkitChange=false;}
                owner.RequestPreview(true); owner.toolkitRefreshRequested=true;
                owner.gradientCanvasOverlay?.MarkDirtyRepaint();
            }
            private void OpenColor(int index)
            {
                var edited=owner.gradientCanvasLayer;
                var document=owner.compositor;
                var expected=edited.gradient;
                Color initial=expected.ColorKeys[index].color;
                bool linear=expected.ColorSpace==ColorSpace.Linear;
                Color display=linear?HdrUtility.Encode(initial):initial;
                bool hdr=display.r>1 || display.g>1 || display.b>1 || display.r<0 || display.g<0 || display.b<0;
                if (!GradientKeyColorPicker.Show(display,hdr,value=>
                {
                    if(owner==null || owner.compositor!=document || owner.GetSelectedLayer()?.Behaviour!=edited ||
                        edited.gradient!=expected || WhimTexApi.IsLayerContentLocked(document,edited.Owner))return;
                    var keys=expected.ColorKeys;
                    Color color=linear?HdrUtility.Decode(value):value;
                    color.a=keys[index].color.a;
                    if(keys[index].color.Equals(color))return;
                    Undo.RegisterCompleteObjectUndo(document,"Change Gradient Color");
                    var next=expected.Clone(); keys[index].color=color;
                    next.SetKeys(keys,expected.AlphaKeys);
                    edited.gradient=expected=next;
                    owner.applyingToolkitChange=true;
                    try{owner.CommitModelChange();}finally{owner.applyingToolkitChange=false;}
                    owner.RequestPreview(true); owner.toolkitRefreshRequested=true;
                })) owner.ShowNotification(new GUIContent("Unity Color Picker is unavailable in this Editor version."));
            }
            public void Draw(MeshGenerationContext context)
            {
                if(!owner.IsGradientCanvasEnabled || owner.toolkitPreviewCanvas.ImageRect.width<=0)return;
                Points(out var a,out var b); Vector2 offset=Offset(a,b);
                var p=context.painter2D;
                for(int pass=0;pass<2;pass++)
                {
                    p.lineWidth=pass==0?5.5f:3.5f; p.strokeColor=pass==0?new Color(0,0,0,.2f):new Color(.4f,.85f,1,.5f);
                    p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
                }
                p.lineWidth=1; p.strokeColor=Color.white;
                p.BeginPath(); p.MoveTo(a); p.LineTo(a+offset); p.MoveTo(b); p.LineTo(b+offset); p.Stroke();
                Square(p,a+offset); Square(p,b+offset);
                var gradient=owner.gradientCanvasLayer.gradient;
                var keys=gradient.ColorKeys;
                int topMidpoint=midpointSelected && selectionLayer==owner.gradientCanvasLayer &&
                    selected>=0 && selected<keys.Length-1?selected:-1;
                if(gradient.Mode!=WhimTexGradientMode.Fixed)
                    for(int i=0;i<keys.Length-1;i++)
                        if(i!=topMidpoint)Diamond(p,MidpointPosition(gradient,i,a,b),false);
                int topKey=!midpointSelected && selectionLayer==owner.gradientCanvasLayer && selected>=0 && selected<keys.Length?selected:-1;
                for(int drawIndex=0;drawIndex<keys.Length;drawIndex++)
                {
                    int i=topKey<0?drawIndex:drawIndex==keys.Length-1?topKey:drawIndex>=topKey?drawIndex+1:drawIndex;
                    if(pendingRemoval && i==selected)continue;
                    var color=keys[i].color; if(gradient.ColorSpace==ColorSpace.Linear)color=HdrUtility.Encode(color);
                    float max=Mathf.Max(1,Mathf.Max(color.r,Mathf.Max(color.g,color.b))); color/=max; color.a=1;
                    Vector2 point=KeyPosition(a,b,keys[i].time);
                    bool highlighted=i==topKey;
                    p.fillColor=Color.black;
                    p.BeginPath(); p.Arc(point,highlighted?10.6f:9.1f,0,360); p.ClosePath(); p.Fill();
                    if(highlighted)
                    {
                        p.fillColor=new Color(.4f,.85f,1);
                        p.BeginPath(); p.Arc(point,9.6f,0,360); p.ClosePath(); p.Fill();
                    }
                    p.fillColor=Color.white;
                    p.BeginPath(); p.Arc(point,8.1f,0,360); p.ClosePath(); p.Fill();
                    p.fillColor=color;
                    p.BeginPath(); p.Arc(point,7.1f,0,360); p.ClosePath(); p.Fill();
                }
                if(topMidpoint>=0 && gradient.Mode!=WhimTexGradientMode.Fixed)
                    Diamond(p,MidpointPosition(gradient,topMidpoint,a,b),true);
            }
            private Vector2 MidpointPosition(WhimTexGradient gradient,int index,Vector2 a,Vector2 b)
            {
                var keys=gradient.ColorKeys;
                return KeyPosition(a,b,Mathf.Lerp(keys[index].time,keys[index+1].time,gradient.GetMidpoint(false,index)));
            }
            private static void Diamond(Painter2D p,Vector2 center,bool highlighted)
            {
                for(int pass=0;pass<(highlighted?3:2);pass++)
                {
                    float radius=highlighted?6-pass:5-pass;
                    p.fillColor=pass==0?Color.black:highlighted && pass==1?new Color(.4f,.85f,1):
                        Color.white;
                    p.BeginPath(); p.MoveTo(center+Vector2.up*radius); p.LineTo(center+Vector2.right*radius);
                    p.LineTo(center+Vector2.down*radius); p.LineTo(center+Vector2.left*radius); p.ClosePath(); p.Fill();
                }
            }
            private static void Square(Painter2D p,Vector2 c)
            {
                p.fillColor=Color.white; p.strokeColor=Color.black; p.lineWidth=1;
                p.BeginPath(); p.MoveTo(c+new Vector2(-4,-4)); p.LineTo(c+new Vector2(4,-4));
                p.LineTo(c+new Vector2(4,4)); p.LineTo(c+new Vector2(-4,4)); p.ClosePath(); p.Fill(); p.Stroke();
            }
        }
    }

    internal static class GradientCanvasGeometry
    {
        internal static Vector2 Rotate(Vector2 p,float degrees)
        {
            float a=degrees*Mathf.Deg2Rad,c=Mathf.Cos(a),s=Mathf.Sin(a);
            return new Vector2(c*p.x-s*p.y,s*p.x+c*p.y);
        }
        internal static Vector2 Point(Vector2 uv,TextureTransform t,Vector2 size)
        {
            return Vector2.Scale(t.Map(uv,size),size);
        }
        internal static void UvEndpoints(GradientLayerBehaviour g,out Vector2 uv,out Vector2 end)
        {
            bool horizontal=g.gradientType==GradientLayerBehaviour.GradientType.Horizontal;
            bool vertical=g.gradientType==GradientLayerBehaviour.GradientType.Vertical;
            uv=horizontal?new Vector2(0,.5f):vertical?new Vector2(.5f,0):GradientLayerBehaviour.BaseCenter;
            end=horizontal?new Vector2(1,.5f):vertical?new Vector2(.5f,1):uv+Vector2.right*GradientLayerBehaviour.BaseRadius;
        }
        internal static void Endpoints(GradientLayerBehaviour g,Vector2 size,out Vector2 a,out Vector2 b)
        {
            UvEndpoints(g,out var uv,out var end);
            a=Point(uv,g.transform,size); b=Point(end,g.transform,size);
        }
        internal static void MoveEndpoint(GradientLayerBehaviour g,Vector2 size,Vector2 a,Vector2 b,bool first,Vector2 delta)
        {
            g.transform = MoveEndpointTransform(g,size,a,b,first,delta,g.transform);
        }
        internal static TextureTransform MoveEndpointTransform(GradientLayerBehaviour g,Vector2 size,Vector2 a,Vector2 b,bool first,Vector2 delta,TextureTransform t)
        {
            bool linear=g.gradientType==GradientLayerBehaviour.GradientType.Horizontal || g.gradientType==GradientLayerBehaviour.GradientType.Vertical;
            if(!linear && first)
            {
                if(t.storage==TransformStorage.Projective)
                    t.TrySetMatrix(ProjectiveMatrix.Translate(delta.x/size.x,delta.y/size.y)*t.matrix);
                else t.position+=(Double2)delta;
                return t;
            }
            Vector2 old=b-a, next=first?b-a-delta:b-a+delta;
            if(old.sqrMagnitude<.000001f || next.sqrMagnitude<.01f)return t;
            float factor=next.magnitude/old.magnitude;
            float rotation=Mathf.Atan2(old.x*next.y-old.y*next.x,Vector2.Dot(old,next))*Mathf.Rad2Deg;
            Vector2 anchor=linear && first?b:a;
            if(t.storage==TransformStorage.Projective)
            {
                var op=ProjectiveMatrix.Translate(anchor.x/size.x,anchor.y/size.y)*ProjectiveMatrix.Scale(1d/size.x,1d/size.y)*
                    ProjectiveMatrix.Rotate(rotation)*ProjectiveMatrix.Scale(factor,factor)*ProjectiveMatrix.Scale(size.x,size.y)*
                    ProjectiveMatrix.Translate(-anchor.x/size.x,-anchor.y/size.y);
                t.TrySetMatrix(op*t.matrix);
            }
            else
            {
                Double2 pivot=new Double2(t.pivot.x*size.x,t.pivot.y*size.y);
                t.position=(Double2)anchor+ProjectiveMatrix.Rotate(rotation).Point((pivot+t.position-(Double2)anchor)*factor)-pivot;
                t.scale=t.scale*factor; t.rotation+=rotation;
            }
            return t;
        }
    }

    internal static class GradientKeyColorPicker
    {
        private static readonly MethodInfo show = typeof(EditorWindow).Assembly.GetType("UnityEditor.ColorPicker")?.GetMethod(
            "Show", BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic, null,
            new[]{typeof(Action<Color>),typeof(Color),typeof(bool),typeof(bool),typeof(bool)},null);
        internal static bool Show(Color color,bool hdr,Action<Color> changed)
        {
            if(show==null)return false;
            try { show.Invoke(null,new object[]{changed,color,false,hdr,false}); return true; }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); return false; }
        }
    }
}
