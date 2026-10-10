using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class TextToolTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static T Read<T>(object value, string name) => (T)Read(value, name);
    static void Write(object value, string name, object next) => value.GetType().GetField(name, Flags).SetValue(value, next);
    static object Call(object value, string name, params object[] args) => Array.Find(value.GetType().GetMethods(Flags), m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    static void Pointer(VisualElement target, EventType type, Vector2 point, bool control = false)
    {
        var input = new Event { type = type, button = 0, mousePosition = target.LocalToWorld(point), modifiers = control ? EventModifiers.Control : EventModifiers.None };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    static void Key(VisualElement target, KeyCode key, bool control = false)
    {
        var input = new Event { type = EventType.KeyDown, keyCode = key, modifiers = control ? EventModifiers.Control : EventModifiers.None };
        using var e = KeyDownEvent.GetPooled(input); e.target = target; target.SendEvent(e);
    }
    static Vector2 FrameCorner(WhimTexWindow window, WhimTexDocument document, Layer layer)
    {
        var canvas = (VisualElement)Read(window, "toolkitCanvas");
        var text = (TextLayerBehaviour)layer.Behaviour;
        var transform = (TextureTransform)Call(document, "GetCanvasTransform", layer);
        var dimensions = new Vector2(document.width, document.height);
        Vector2 uv = transform.Map(Vector2.one * .5f + Vector2.Scale(text.frameSize * .5f, new Vector2(1f / dimensions.x, 1f / dimensions.y)), dimensions);
        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        return (Vector2)Call(canvas, "ToView", new Vector2(image.x + uv.x * image.width, image.yMax - uv.y * image.height));
    }
    public static string Start(string id) => AsyncD.Start(id, Execute);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);
    static async Task Execute(TestContext t, CancellationToken token)
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        WhimTexWindow window = null;
        string pref = "DCFApixels.WhimTex.Canvas.Tool";
        bool hadPref = EditorPrefs.HasKey(pref); string oldPref = EditorPrefs.GetString(pref);
        try
        {
            document.hideFlags = HideFlags.HideAndDontSave; document.width = 512; document.height = 256;
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            Write(window, "activeDocument", document); window.ShowUtility(); window.position = new Rect(80,80,1400,800); window.CreateGUI();
            var root = window.rootVisualElement;
            for (int i=0;i<4;i++) await AsyncD.Tick(root,token);
            var canvas = (VisualElement)Read(window,"toolkitCanvas");
            var toolType = Read(window,"canvasTool").GetType();
            Call(window,"SetCanvasTool",Enum.Parse(toolType,"Text"));
            await AsyncD.Tick(root,token);
            t.True(root.Q<Button>("textTool") != null, "Text tool is in the common toolbar");
            var toolFont = root.Q<BaseField<string>>("textToolFont");
            t.True(toolFont != null && toolFont.ClassListContains("whimtex-system-font-field"), "Text tool uses the common system font selector");
            t.Near(160, toolFont.Q<VisualElement>(className: BaseField<string>.inputUssClassName).worldBound.width, .1,
                "Tool font selector has fixed input width");
            var toolFontPreview = toolFont.Q<Label>("fontName");
            t.True(toolFontPreview.text == toolFont.value && toolFontPreview.style.unityFontDefinition.value.fontAsset != null,
                "Tool font name previews its own font");
            var p = canvas.contentRect.center + new Vector2(-120,-40);
            Pointer(canvas,EventType.MouseDown,p); Pointer(canvas,EventType.MouseUp,p);
            t.Equal(1,document.layers.Count,"Single click creates exactly one text layer");
            var point = (TextLayerBehaviour)document.layers[0].Behaviour;
            var toolSettings = Read(window, "textToolSettings");
            var layerFont = root.Q<BaseField<string>>("textFont");
            t.Equal(toolFont.GetType(), layerFont.GetType(), "Tool and layer reuse exactly the same selector type");
            string originalFont = toolFont.value;
            string otherFont = Array.Find(Font.GetOSInstalledFontNames(), name => name != originalFont);
            if (otherFont != null)
            {
                toolFont.value = otherFont;
                t.True(Read<string>(toolSettings, "fontFamily") == otherFont && point.fontFamily == originalFont,
                    "Tool font changes defaults without changing the existing layer font");
                t.Near(160, toolFont.Q<VisualElement>(className: BaseField<string>.inputUssClassName).worldBound.width, .1,
                    "Font name length does not resize the tool selector");
                toolFont.value = originalFont;
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                layerFont.value = otherFont;
                t.True(point.fontFamily == otherFont && toolFont.value == originalFont,
                    "Layer font changes leave tool defaults independent");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await AsyncD.Tick(root, token);
                point = (TextLayerBehaviour)document.layers[0].Behaviour;
                t.True(point.fontFamily == originalFont && layerFont.value == originalFont,
                    "Undo restores the layer font selector and its preview");
            }
            t.True(!ReferenceEquals(point, toolSettings), "Created text owns a separate settings copy");
            t.True(!(toolSettings is TextLayerBehaviour) && Array.TrueForAll(
                new[] { "layoutMode", "frameSize", "wrapping", "overflow", "autoSize", "maxFontSize", "spacing", "characterHorizontalScale" },
                field => toolSettings.GetType().GetField(field, Flags) == null), "Tool data has no layer-layout or frame settings");
            t.Equal(TextLayoutMode.Point,point.layoutMode,"Click creates point text");
            t.True(root.Q<EnumField>("textToolLayout") == null, "Text creation has no Layout selector in the tool header");
            t.True(root.Q<Toggle>("textToolAutoSize") == null && root.Q<FloatField>("textToolMaxSize") == null &&
                root.Q<EnumField>("textToolWrapping") == null, "Frame controls are absent from the tool header, not hidden");
            var toolAlignment = root.Q<VisualElement>("textToolAlignment");
            var toolJustify = toolAlignment.Q<Button>("textAlignJustify");
            t.True(toolJustify.enabledInHierarchy, "Tool Justify remains available regardless of selected layer layout");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = toolJustify; toolJustify.SendEvent(submit); }
            t.True(Read<bool>(toolSettings, "justify") && !point.justify, "Tool Justify changes creation defaults, not the selected Point layer");
            var bold = root.Q<VisualElement>("textToolStyle").Q<Button>("textStyleBold");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = bold; bold.SendEvent(submit); }
            t.Equal(FontStyle.Bold, Read<FontStyle>(toolSettings, "fontStyle"), "Header style button updates new-text defaults");
            t.Equal(FontStyle.Normal, point.fontStyle, "Tool style changes leave the existing layer unchanged");
            var casing = root.Q<VisualElement>("textToolCasing").Q<Button>("textCasingUppercase");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = casing; casing.SendEvent(submit); }
            t.Equal(TextCasing.Uppercase, Read<TextCasing>(toolSettings, "casing"), "Header casing icon updates new-text defaults");
            t.Equal(TextCasing.Normal, point.casing, "Tool casing changes leave the existing layer unchanged");
            root.Q<FloatField>("textSize").value = 37;
            var layerItalic = root.Q<VisualElement>("textStyle").Q<Button>("textStyleItalic");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = layerItalic; layerItalic.SendEvent(submit); }
            var layerCasing = root.Q<VisualElement>("textCasing").Q<Button>("textCasingLowercase");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = layerCasing; layerCasing.SendEvent(submit); }
            t.Equal(37f, point.fontSize, "Properties changes size on the selected layer");
            t.Equal(FontStyle.Italic, point.fontStyle, "Properties changes style on the selected layer");
            t.Equal(TextCasing.Lowercase, point.casing, "Properties changes casing on the selected layer");
            t.Equal(64f, root.Q<FloatField>("textToolSize").value, "Layer size does not replace the tool size");
            t.Equal(FontStyle.Bold, Read<FontStyle>(toolSettings, "fontStyle"), "Layer style does not replace the tool style");
            t.Equal(TextCasing.Uppercase, Read<TextCasing>(toolSettings, "casing"), "Layer casing does not replace the tool casing");
            var input = canvas.Q<TextField>("canvasTextInput");
            await AsyncD.Tick(root,token);
            t.True(input != null && !input.ClassListContains("whimtex-hidden") && input.enabledInHierarchy,"Click enters a live Canvas View text field");
            string styledFont = Array.Find(Font.GetOSInstalledFontNames(), name => name == "Arial Bold Italic" || name == "Agency FB Bold");
            if (styledFont != null)
            {
                string sourceFont = point.fontFamily;
                var messages = new System.Collections.Generic.List<string>();
                Application.LogCallback capture = (message, stack, type) =>
                {
                    if ((type == LogType.Warning || type == LogType.Error || type == LogType.Exception) &&
                        (message.IndexOf("font face", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         message.IndexOf("font file", StringComparison.OrdinalIgnoreCase) >= 0 || message.Contains("Include Font Data")))
                        messages.Add(message);
                };
                Application.logMessageReceived += capture;
                try
                {
                    point.fontFamily = styledFont; Call(window, "RefreshTextEditing");
                    for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
                    var styledAsset = input.style.unityFontDefinition.value.fontAsset;
                    t.True(styledAsset != null && styledAsset.faceInfo.styleName.Contains("Bold"),
                        "Canvas input loads the actual non-Regular OS face without legacy Font conversion");
                    var atlases = styledAsset.atlasTextures; var material = styledAsset.material;
                    point.fontFamily = sourceFont; Call(window, "RefreshTextEditing");
                    await AsyncD.Tick(root, token);
                    t.True(styledAsset == null && material == null && Array.TrueForAll(atlases, atlas => atlas == null),
                        "Changing the inline font releases its owned font asset, material and atlases");
                    t.Equal(0, messages.Count, "Canvas text input and repaint emit no font-load warnings: " + string.Join("; ", messages));
                }
                finally
                {
                    Application.logMessageReceived -= capture;
                    point.fontFamily = sourceFont; Call(window, "RefreshTextEditing");
                }
            }
            var pointTransform = point.Owner.transform;
            var collapsed = pointTransform; collapsed.scale = new Vector2(0, 1); point.Owner.transform = collapsed;
            Call(window, "RefreshTextEditing");
            t.True(input.ClassListContains("whimtex-hidden"), "A singular transform hides inline input instead of leaving an overlay at its old position");
            point.Owner.transform = pointTransform; Call(window, "RefreshTextEditing");
            t.True(!input.ClassListContains("whimtex-hidden"), "Inline input returns after the transform becomes usable");
            var oldFont = Read<FontAsset>(window, "editorFont");
            typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.SystemFontCatalog").GetMethod("Refresh", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Call(window, "RefreshTextEditing");
            t.True(oldFont == null && Read<FontAsset>(window, "editorFont") != null, "Font catalog refresh replaces the owned inline font as well as render fonts");
            root.Q<FloatField>("textSpacingCharacter").value = .15f;
            root.Q<FloatField>("textSpacingWord").value = .3f;
            root.Q<FloatField>("textSpacingParagraph").value = .2f;
            Call(window, "RefreshTextEditing");
            t.Near(point.fontSize * .15f, input.style.letterSpacing.value.value, .01, "Inline editor converts Character em spacing to pixels");
            t.Near(point.fontSize * .3f, input.style.wordSpacing.value.value, .01, "Inline editor converts Word em spacing to pixels");
            float inlineScale = input.style.scale.value.value.x;
            root.Q<FloatField>("textCharacterHorizontalScale").value = .5f;
            Call(window, "RefreshTextEditing");
            t.Near(inlineScale * .5f, input.style.scale.value.value.x, .001, "Inline glyphs follow character horizontal scale");
            t.Near(point.fontSize * .15f, input.style.letterSpacing.value.value * point.characterHorizontalScale, .01,
                "Inline character spacing is not compressed with the glyphs");
            t.Near(point.fontSize * .3f, input.style.wordSpacing.value.value * point.characterHorizontalScale, .01,
                "Inline added word spacing is not compressed with the glyphs");
            input.value="Привет\nText";
            t.Equal("Привет\nText",point.text,"Canvas text input edits the actual layer, including line breaks");
            input.value="Cancelled"; Key(input,KeyCode.Escape);
            t.Equal("Text",point.text,"Escape restores the text from edit start");
            t.True(input.ClassListContains("whimtex-hidden"),"Escape closes only the inline editor");
            Call(window,"FocusTextInput",true);
            t.True(Read(window, "textFocusRequest") != null, "Inline focus has one owned scheduled request");
            Call(window,"EndTextEditing",false);
            t.True(Read(window, "textFocusRequest") == null && Read<FontAsset>(window, "editorFont") == null,
                "Ending an edit cancels its delayed focus and destroys its font");
            Call(window,"FocusTextInput",false); input.value="Committed"; Key(input,KeyCode.Return,true);
            t.Equal("Committed",point.text,"Ctrl+Enter commits the edited text");
            t.True(input.ClassListContains("whimtex-hidden"),"Ctrl+Enter ends editing");
            Call(window,"FocusTextInput",false); input.value="Click committed";
            Pointer(canvas,EventType.MouseDown,p+new Vector2(250,30)); Pointer(canvas,EventType.MouseUp,p+new Vector2(250,30));
            t.Equal(1,document.layers.Count,"A canvas click during editing finishes input without creating another text layer");
            t.Equal("Click committed",point.text,"Finishing with a canvas click retains edited text");
            t.True(input.ClassListContains("whimtex-hidden"),"A canvas click closes the inline editor");
            Call(window,"FocusTextInput",false); input.value="Drag committed";
            Pointer(canvas,EventType.MouseDown,p+new Vector2(250,30),true);
            Pointer(canvas,EventType.MouseDrag,p+new Vector2(390,130),true);
            Pointer(canvas,EventType.MouseUp,p+new Vector2(390,130),true);
            t.Equal(1,document.layers.Count,"Dragging during editing finishes input without creating a frame, even with Ctrl");
            t.Equal("Drag committed",point.text,"Finishing with a drag retains edited text");
            t.True(!canvas.HasPointerCapture(PointerId.mousePointerId),"Finishing input does not capture a new text gesture");
            var toolSize = root.Q<FloatField>("textToolSize"); toolSize.value = 12;
            t.True(toolSize.label == "Size", "The tool uses an ordinary fixed Size field");
            t.True(point.fontSize == 37 && !point.autoSize && point.layoutMode == TextLayoutMode.Point && point.wrapping == TextWrapping.Words,
                "Tool sizing does not change the selected Point layer or its layout");
            Pointer(canvas,EventType.MouseDown,p+new Vector2(220,20),true);
            Pointer(canvas,EventType.MouseDrag,p+new Vector2(390,130),true);
            Pointer(canvas,EventType.MouseUp,p+new Vector2(390,130),true);
            t.Equal(2,document.layers.Count,"Drag creates one additional layer");
            var framed=(TextLayerBehaviour)document.layers[0].Behaviour;
            t.Equal(TextLayoutMode.Frame,framed.layoutMode,"Rectangle drag creates frame text");
            t.True(!ReferenceEquals(framed, toolSettings), "Frame text owns a separate settings copy");
            t.Equal(default(TextSpacing), framed.spacing, "New text does not inherit the selected layer's spacing");
            t.Equal(1f, framed.characterHorizontalScale, "New text does not inherit the selected layer's character scale");
            t.Equal(Read<FontStyle>(toolSettings, "fontStyle"), framed.fontStyle, "New text copies the tool style, not the selected layer");
            t.Equal(Read<TextCasing>(toolSettings, "casing"), framed.casing, "New text copies the tool casing, not the selected layer");
            t.True(!framed.autoSize && framed.fontSize == 12 && framed.maxFontSize == 256 && framed.wrapping == TextWrapping.Words,
                "A new Frame uses ordinary layer defaults for frame-specific settings");
            t.True(framed.justify, "A dragged Frame inherits the tool Justify setting");
            t.True(framed.frameSize.x>1 && framed.frameSize.y>1,"Frame dimensions come from the dragged area");
            toolSize.value = 20;
            t.True(Read<float>(toolSettings, "fontSize") == 20 && framed.fontSize == 12,
                "Tool size changes do not alter an already-created Frame layer");
            root.Q<Toggle>("textAutoSize").value = true;
            root.Q<FloatField>("textSize").value = 18;
            root.Q<FloatField>("textMaxSize").value = 32;
            root.Q<EnumField>("textWrapping").value = TextWrapping.Characters;
            t.True(framed.autoSize && framed.fontSize == 18 && framed.maxFontSize == 32 && framed.wrapping == TextWrapping.Characters,
                "Properties changes limits and wrapping on the Frame layer");
            t.True(toolSize.value == 20 && toolSize.label == "Size" && root.Q<FloatField>("textToolMaxSize") == null &&
                root.Q<Toggle>("textToolAutoSize") == null, "Layer Auto Size does not add frame settings to the tool header");
            root.Q<Toggle>("textAutoSize").value = false;
            t.True(toolSize.value == 20 && toolSize.label == "Size", "Layer Auto Size visibility does not affect the tool header");
            root.Q<Toggle>("textAutoSize").value = true;
            root.Q<EnumField>("textWrapping").value = TextWrapping.Manual;
            t.True(!framed.justify && Read<bool>(toolSettings, "justify"), "Manual layer wrapping does not reset tool Justify");
            root.Q<EnumField>("textWrapping").value = TextWrapping.Words;
            framed.text = "Long framed text that must fit";
            Call(window, "RefreshTextEditing");
            t.Near((float)Call(framed, "GetFontSize", document.width, document.height), input.style.fontSize.value.value, .01, "Inline text editor uses the fitted size");
            float frameInlineWidth = input.style.width.value.value * input.style.scale.value.value.x;
            framed.characterHorizontalScale = 2; Call(window, "RefreshTextEditing");
            t.Near(frameInlineWidth, input.style.width.value.value * input.style.scale.value.value.x, .01,
                "Scaling inline characters preserves the displayed frame width");
            t.Near((float)Call(framed, "GetFontSize", document.width, document.height), input.style.fontSize.value.value, .01,
                "Inline Auto Size uses scaled character metrics");
            framed.characterHorizontalScale = 1;
            framed.text = "Text";
            Call(window,"EndTextEditing",false);
            int before=document.layers.Count;
            Pointer(canvas,EventType.MouseDown,p,true); Pointer(canvas,EventType.MouseDrag,p+new Vector2(80,50),true); Key(canvas,KeyCode.Escape); Pointer(canvas,EventType.MouseUp,p+new Vector2(80,50),true);
            t.Equal(before,document.layers.Count,"Cancelled drag creates no layer");
            t.True(!canvas.HasPointerCapture(PointerId.mousePointerId),"Cancelled drag releases capture");
            var frameLayer = framed.Owner;
            var frameSize = framed.frameSize;
            var frameTransform = frameLayer.transform;
            Vector2 handle = FrameCorner(window, document, frameLayer);
            Pointer(canvas, EventType.MouseDown, handle); Pointer(canvas, EventType.MouseDrag, handle + new Vector2(40, -20));
            t.True(canvas.HasPointerCapture(PointerId.mousePointerId), "Frame corner starts a captured resize gesture");
            Pointer(canvas, EventType.MouseUp, handle + new Vector2(40, -20));
            t.True(framed.frameSize.x > frameSize.x && framed.frameSize.y > frameSize.y, "Frame resize increases dimensions without changing font size");
            t.True(!canvas.HasPointerCapture(PointerId.mousePointerId), "Successful resize releases pointer capture");
            frameSize = framed.frameSize; frameTransform = frameLayer.transform;
            handle = FrameCorner(window, document, frameLayer);
            Pointer(canvas, EventType.MouseDown, handle);
            framed.frameSize = frameSize + new Vector2(13, 7);
            Pointer(canvas, EventType.MouseUp, handle + new Vector2(40, -20));
            t.True(framed.frameSize == frameSize + new Vector2(13, 7) && frameLayer.transform.Equals(frameTransform),
                "A stale resize never overwrites a frame changed by another action");
            t.True(!canvas.HasPointerCapture(PointerId.mousePointerId), "Stale resize releases pointer capture");
            framed.frameSize = frameSize;
            handle = FrameCorner(window, document, frameLayer);
            Pointer(canvas, EventType.MouseDown, handle);
            Call(window, "SelectOnlyLayer", point.Owner.Id); Call(window, "RefreshToolkitInterface", false);
            Pointer(canvas, EventType.MouseUp, handle + new Vector2(40, -20));
            t.True(framed.frameSize == frameSize && frameLayer.transform.Equals(frameTransform) && document.layers.Count == before,
                "Selection change cancels resize without changing or adding layers");
            Call(window, "SelectOnlyLayer", point.Owner.Id); Call(window, "RefreshToolkitInterface", false);
            t.True(toolSize.value == 20 && toolSize.label == "Size" && Read<bool>(toolSettings, "justify"),
                "Selecting another text layer preserves tool values and visibility");
            Call(window, "SelectOnlyLayer", framed.Owner.Id); Call(window, "RefreshToolkitInterface", false);
            Call(window,"FocusTextInput",false); input.value="Undo text"; Call(window,"EndTextEditing",false);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            t.Equal("Text",((TextLayerBehaviour)document.layers[0].Behaviour).text,"One document Undo restores an inline edit session");
            t.True(Read<float>(toolSettings, "fontSize") == 20 && Read<bool>(toolSettings, "justify"),
                "Document Undo leaves independent tool defaults unchanged");
            Pointer(canvas,EventType.MouseDown,p,true); Pointer(canvas,EventType.MouseUp,p,true);
            var newPoint = (TextLayerBehaviour)document.layers[0].Behaviour;
            t.True(newPoint.layoutMode == TextLayoutMode.Point && newPoint.fontSize == 20 && !newPoint.autoSize &&
                !newPoint.justify && newPoint.alignment == TextAnchor.UpperLeft && Read<bool>(toolSettings, "justify"),
                "A click creates left-aligned Point text without changing justified Frame defaults");
            Call(window,"SetCanvasTool",Enum.Parse(toolType,"None"));
            t.True(input.ClassListContains("whimtex-hidden"),"Switching tools leaves no text editor overlay");
            Call(window,"SetCanvasTool",Enum.Parse(toolType,"Text")); Call(window,"FocusTextInput",false);
            var detachedInput = input;
            detachedInput.RemoveFromHierarchy();
            t.True(detachedInput.ClassListContains("whimtex-hidden") && Read(window, "textEditingLayer") == null &&
                Read(window, "textFocusRequest") == null && Read<FontAsset>(window, "editorFont") == null,
                "Detaching the editor finishes its session and releases focus/font resources");
            window.DiscardChanges();
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if(window!=null) { window.DiscardChanges(); window.Close(); } },
                () => { if(document!=null) Undo.ClearUndo(document); },
                () => { if(document!=null) UnityEngine.Object.DestroyImmediate(document); },
                () => { if(hadPref) EditorPrefs.SetString(pref,oldPref); else EditorPrefs.DeleteKey(pref); });
        }
    }
}
