using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class TextLayerUiTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    public static string Start(string id) => AsyncD.Start(id, Execute);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);
    static void Click(Button button)
    {
        using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit);
    }
    static void AssertFormatIcons(TestContext t, VisualElement row)
    {
        foreach (var button in row.Query<Button>().ToList())
        {
            var input = button.parent;
            var icon = button.hierarchy[0];
            t.True(button.worldBound.width >= 22 && button.worldBound.height >= 18 &&
                icon.worldBound.width >= 12 && icon.worldBound.height >= 12,
                button.name + " has a visible button and icon: " + button.worldBound + ", " + icon.worldBound);
            for (var owner = input; owner != row; owner = owner.parent)
                t.True(owner.worldBound.xMin <= button.worldBound.xMin + 1 &&
                    owner.worldBound.xMax >= button.worldBound.xMax - 1 &&
                    owner.worldBound.yMin <= button.worldBound.yMin + 1 &&
                    owner.worldBound.yMax >= button.worldBound.yMax - 1,
                    button.name + " fits inside its formatting containers: " + owner.worldBound + ", " + button.worldBound);
        }
    }
    static async Task Execute(TestContext t, CancellationToken token)
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        var window = ScriptableObject.CreateInstance<TextLayerEditorWindow>();
        PopupWindowContent picker = null;
        try
        {
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = 384; document.height = 192;
            var text = new TextLayerBehaviour { text = "Editable", fontSize = 32 };
            var layer = new Layer(text); document.layers.Add(layer);
            typeof(Layer).GetMethod("AssignNewId", Flags).Invoke(layer, null);
            typeof(LayerEditorWindowBase).GetMethod("Initialize", Flags).Invoke(window, new object[] { layer, document });
            window.titleContent = new GUIContent("Text layer test");
            window.ShowUtility(); window.position = new Rect(100, 100, 480, 640);
            var root = window.rootVisualElement;
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            t.True(root.Q<TextField>("textContent") != null, "Text settings are built in the common layer inspector");
            var font = root.Q<TextField>("textFont");
            var choose = font.Q<Button>();
            t.True(font.isReadOnly && choose != null, "Font identity is read-only and has a picker");
            t.True(choose.worldBound.width >= 20 && choose.worldBound.xMin >= font.Q("unity-text-input").worldBound.xMax - 1,
                "Font picker button is visible and does not overlap the name field");
            var size = root.Q<FloatField>("textSize");
            var sizeRange = root.Q<Vector2Field>("textSizeRange");
            size.value = 48;
            t.Equal(48f, ((TextLayerBehaviour)document.layers[0].Behaviour).fontSize, "Size field updates actual layer through the common apply transaction");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            await AsyncD.Tick(root, token);
            text = (TextLayerBehaviour)document.layers[0].Behaviour;
            t.Equal(32f, text.fontSize, "Undo restores text size");
            t.Equal(32f, root.Q<FloatField>("textSize").value, "Undo refreshes the inspector binding");
            root.Q<TextField>("textContent").value = "Edited\nТекст";
            t.Equal("Edited\nТекст", text.text, "Multiline text field updates source");
            var characterSpacing = root.Q<FloatField>("textSpacingCharacter"); var wordSpacing = root.Q<FloatField>("textSpacingWord");
            var lineSpacing = root.Q<FloatField>("textSpacingLine"); var paragraphSpacing = root.Q<FloatField>("textSpacingParagraph");
            t.True(characterSpacing != null && wordSpacing != null && lineSpacing != null && paragraphSpacing != null, "Spacing block exposes all four em fields");
            t.True(Mathf.Abs(characterSpacing.worldBound.yMin - wordSpacing.worldBound.yMin) < 1 && lineSpacing.worldBound.yMin > characterSpacing.worldBound.yMin &&
                Mathf.Abs(wordSpacing.worldBound.xMin - paragraphSpacing.worldBound.xMin) < 1, "Spacing uses a compact aligned two-column grid");
            t.True(characterSpacing.Q("unity-text-input").worldBound.width >= 32 &&
                wordSpacing.worldBound.xMin >= characterSpacing.worldBound.xMax - 1, "Spacing number fields have usable width and do not overlap");
            var horizontalScale = root.Q<FloatField>("textCharacterHorizontalScale");
            var spacingRow = root.Q("textSpacing");
            t.True(horizontalScale != null && horizontalScale.value == 1 && horizontalScale.parent == spacingRow.parent &&
                spacingRow.parent.IndexOf(horizontalScale) == spacingRow.parent.IndexOf(spacingRow) + 1,
                "Character horizontal scale is a neutral multiplier directly below Spacing Options");
            t.True(horizontalScale.worldBound.yMin >= spacingRow.worldBound.yMax - 1,
                "Horizontal scale has its own standard field row without overlapping Spacing Options");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            horizontalScale.value = .5f;
            t.Equal(.5f, text.characterHorizontalScale, "Horizontal scale edits the layer through the shared apply transaction");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await AsyncD.Tick(root, token);
            text = (TextLayerBehaviour)document.layers[0].Behaviour;
            t.True(text.characterHorizontalScale == 1 && horizontalScale.value == 1,
                "Undo restores character horizontal scale and its binding");
            var styleRow = root.Q("textStyleRow"); var alignmentRow = root.Q("textAlignmentRow");
            var autoRow = root.Q("textAutoSize");
            t.True(autoRow.parent == sizeRange.parent && styleRow.parent == sizeRange.parent &&
                sizeRange.parent.IndexOf(autoRow) == sizeRange.parent.IndexOf(sizeRange) + 1 &&
                sizeRange.parent.IndexOf(styleRow) == sizeRange.parent.IndexOf(autoRow) + 1 &&
                styleRow.worldBound.yMin >= sizeRange.worldBound.yMax - 1, "Formatting row order is Size, Auto Size, Style without overlap");
            var styleGroup = root.Q("textStyle"); var casingGroup = root.Q("textCasing");
            var horizontalGroup = root.Q("textAlignment"); var verticalGroup = root.Q("textVerticalAlignment");
            t.True(styleGroup.parent == styleRow && casingGroup.parent == styleRow &&
                Mathf.Abs(styleGroup.worldBound.center.y - casingGroup.worldBound.center.y) < 1,
                "Style and Casing share one aligned formatting row");
            t.True(horizontalGroup.parent == alignmentRow && verticalGroup.parent == alignmentRow &&
                Mathf.Abs(horizontalGroup.worldBound.center.y - verticalGroup.worldBound.center.y) < 1,
                "Horizontal and vertical alignment share one formatting row");
            t.True(styleRow.Query<Label>(className: BaseField<string>.labelUssClassName).ToList().Count == 1 &&
                alignmentRow.Query<Label>(className: BaseField<string>.labelUssClassName).ToList().Count == 1,
                "Each formatting row has one shared label without duplicate subgroup labels");
            AssertFormatIcons(t, styleRow); AssertFormatIcons(t, alignmentRow);
            window.position = new Rect(100,100,320,640);
            for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
            t.True(wordSpacing.worldBound.yMin >= characterSpacing.worldBound.yMax - 1 &&
                characterSpacing.Q("unity-text-input").worldBound.xMax <= root.worldBound.xMax + 1, "Narrow settings stack spacing fields without overlap or horizontal overflow");
            t.True(casingGroup.worldBound.xMin >= styleGroup.worldBound.xMax &&
                verticalGroup.worldBound.xMin >= horizontalGroup.worldBound.xMax &&
                Mathf.Abs(horizontalGroup.worldBound.center.y - verticalGroup.worldBound.center.y) < 1 &&
                verticalGroup.worldBound.xMax <= root.worldBound.xMax + 1,
                "Formatting groups stay on one line without overlap or overflow in narrow settings");
            AssertFormatIcons(t, styleRow); AssertFormatIcons(t, alignmentRow);
            window.position = new Rect(100,100,480,640);
            for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
            lineSpacing.value = 100; t.Equal(10f, text.spacing.line, "UI clamps Line spacing to its supported em range");
            lineSpacing.value = 0;
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            characterSpacing.value = .15f; wordSpacing.value = .3f; paragraphSpacing.value = .5f;
            t.Equal(.15f, text.spacing.character, "Character field changes the actual layer");
            t.Equal(.3f, text.spacing.word, "Word field changes independently");
            t.Equal(.5f, text.spacing.paragraph, "Paragraph field changes independently");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await AsyncD.Tick(root, token);
            text = (TextLayerBehaviour)document.layers[0].Behaviour;
            t.Equal(default(TextSpacing), text.spacing, "Undo restores all spacing values from one transaction group");
            t.Equal(0f, characterSpacing.value, "Undo refreshes the nested Character field");
            t.Equal(0f, paragraphSpacing.value, "Undo refreshes the nested Paragraph field");
            var style = root.Q<VisualElement>("textStyle");
            var bold = style.Q<Button>("textStyleBold"); var italic = style.Q<Button>("textStyleItalic");
            t.True(bold.Q<Label>().text == "B" && italic.Q<Label>().text == "I", "Text style uses English letter buttons");
            Click(bold); t.Equal(FontStyle.Bold, text.fontStyle, "Bold button enables bold independently");
            Click(italic); t.Equal(FontStyle.BoldAndItalic, text.fontStyle, "Bold and italic can be combined");
            t.True(bold.ClassListContains("whimtex-text-format-button--selected") && italic.ClassListContains("whimtex-text-format-button--selected"), "Both active styles have selection outlines");
            await AsyncD.Tick(root, token);
            t.True(bold.resolvedStyle.borderTopColor.a > 0 && bold.resolvedStyle.borderTopWidth == 1, "Selected style outline resolves through the shared stylesheet");
            float buttonWidth = bold.worldBound.width;
            Click(bold); t.Equal(FontStyle.Italic, text.fontStyle, "Bold can be disabled without disabling italic");
            Click(italic); t.Equal(FontStyle.Normal, text.fontStyle, "Disabling both styles returns to regular text");
            await AsyncD.Tick(root, token);
            t.True(bold.resolvedStyle.borderTopColor.a == 0 && Mathf.Abs(buttonWidth - bold.worldBound.width) < .01f, "Inactive outline is invisible without changing button layout");
            var casing = root.Q<VisualElement>("textCasing"); string originalText = text.text;
            Click(casing.Q<Button>("textCasingLowercase")); t.Equal(TextCasing.Lowercase, text.casing, "Lowercase icon selects its display mode");
            Click(casing.Q<Button>("textCasingUppercase")); t.Equal(TextCasing.Uppercase, text.casing, "Uppercase replaces the previous casing mode");
            t.True(!casing.Q<Button>("textCasingLowercase").ClassListContains("whimtex-text-format-button--selected"), "Casing modes are mutually exclusive");
            Click(casing.Q<Button>("textCasingSmallCaps")); t.Equal(TextCasing.SmallCaps, text.casing, "Small Caps icon selects its display mode");
            t.Equal(originalText, text.text, "Casing controls do not replace source text");
            Click(casing.Q<Button>("textCasingSmallCaps")); t.Equal(TextCasing.Normal, text.casing, "Clicking the active casing icon restores original casing");
            var alignment = root.Q<VisualElement>("textAlignment");
            var vertical = root.Q<VisualElement>("textVerticalAlignment");
            t.Equal(4, alignment.Query<Button>().ToList().Count, "Horizontal alignment has four icon buttons");
            t.Equal(3, vertical.Query<Button>().ToList().Count, "Vertical alignment has three icon buttons");
            t.True(alignment.Q<Button>("textAlignJustify").enabledInHierarchy, "Justify stays available for point text");
            Click(alignment.Q<Button>("textAlignRight")); Click(alignment.Q<Button>("textAlignJustify"));
            t.True(text.alignment == TextAnchor.MiddleLeft && !text.justify &&
                alignment.Q<Button>("textAlignLeft").ClassListContains("whimtex-text-format-button--selected"),
                "Point text falls back to left alignment without changing vertical alignment");
            Click(alignment.Q<Button>("textAlignCenter"));
            var auto = root.Q<Toggle>("textAutoSize");
            var maxSize = root.Q<FloatField>("textMaxSize");
            t.True(auto.ClassListContains("whimtex-hidden"), "Auto Size is hidden for point text");
            var overflow = root.Q<EnumField>("textOverflow");
            t.True(overflow.ClassListContains("whimtex-hidden"), "Frame overflow is not exposed for Point text");
            t.True(maxSize.ClassListContains("whimtex-hidden") && sizeRange.label == "Size" && size.label == "",
                "Point text uses one Size field without a component label or an empty Max slot");
            root.Q<EnumField>("textLayoutMode").value = TextLayoutMode.Frame;
            t.True(!overflow.ClassListContains("whimtex-hidden") && (TextOverflowMode)overflow.value == TextOverflowMode.None,
                "Frame text exposes Overflow with None as its default");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            overflow.value = TextOverflowMode.Ellipsis;
            t.Equal(TextOverflowMode.Ellipsis, text.overflow, "Overflow changes the actual layer through the shared apply transaction");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await AsyncD.Tick(root, token);
            text = (TextLayerBehaviour)document.layers[0].Behaviour;
            t.True(text.overflow == TextOverflowMode.None && (TextOverflowMode)overflow.value == TextOverflowMode.None, "Undo restores overflow and its binding");
            t.True(!auto.ClassListContains("whimtex-hidden"), "Frame text exposes Auto Size");
            auto.value = true;
            t.True(text.autoSize && sizeRange.label == "Size" && size.label == "Min" && maxSize.label == "Max" &&
                !maxSize.ClassListContains("whimtex-hidden") && size.parent == maxSize.parent,
                "Auto Size exposes Min and Max components of one Size field");
            window.position = new Rect(100, 100, 320, 640);
            for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
            t.True(Mathf.Abs(size.worldBound.center.y - maxSize.worldBound.center.y) < 1 &&
                Mathf.Abs(size.worldBound.width - maxSize.worldBound.width) <= 1 &&
                maxSize.worldBound.xMin >= size.worldBound.xMax - 1 && maxSize.worldBound.xMax <= root.worldBound.xMax + 1,
                "Min and Max share an aligned, evenly divided row without overlap or overflow in narrow Properties: " +
                size.worldBound + ", " + maxSize.worldBound + ", " + root.worldBound);
            t.True(size.Q("unity-text-input").worldBound.width >= 32 && maxSize.Q("unity-text-input").worldBound.width >= 32 &&
                size.labelElement.worldBound.width >= size.labelElement.MeasureTextSize("Min", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x &&
                maxSize.labelElement.worldBound.width >= maxSize.labelElement.MeasureTextSize("Max", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x,
                "Min and Max labels and number inputs remain readable");
            window.position = new Rect(100, 100, 480, 640);
            for (int i = 0; i < 2; i++) await AsyncD.Tick(root, token);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            maxSize.value = 150; t.Equal(150f, text.maxFontSize, "Max Size field updates the actual layer");
            size.value = 200;
            t.True(text.fontSize == 200 && text.maxFontSize == 200 && maxSize.value == 200, "Raising Min Size above Max Size updates both bound fields");
            maxSize.value = 60;
            t.True(text.fontSize == 60 && size.value == 60, "Lowering Max Size below Min Size updates both bound fields");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await AsyncD.Tick(root, token);
            text = (TextLayerBehaviour)document.layers[0].Behaviour;
            t.True(text.fontSize == 32 && text.maxFontSize == 256 && size.value == 32 && maxSize.value == 256, "Undo restores both size limits and their bindings");
            auto.value = false;
            t.True(size.label == "" && maxSize.ClassListContains("whimtex-hidden"), "Disabling Auto Size restores a single fixed Size field");
            await AsyncD.Tick(root, token);
            t.True(size.worldBound.xMax >= sizeRange.worldBound.xMax - 4, "Fixed Size fills the input width without space reserved for Max");
            auto.value = true;
            Click(alignment.Q<Button>("textAlignJustify"));
            t.True(text.justify, "Justify button updates the layer");
            Click(vertical.Q<Button>("textAlignBottom"));
            t.True(text.alignment == TextAnchor.LowerLeft && text.justify, "Vertical alignment preserves justification and its left-aligned paragraph endings");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Click(alignment.Q<Button>("textAlignRight"));
            t.True(text.alignment == TextAnchor.LowerRight && !text.justify, "Horizontal alignment preserves vertical alignment and disables justification");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await AsyncD.Tick(root, token);
            text = (TextLayerBehaviour)document.layers[0].Behaviour;
            t.True(text.alignment == TextAnchor.LowerLeft && text.justify && alignment.Q<Button>("textAlignJustify").ClassListContains("whimtex-text-format-button--selected"), "Undo restores text alignment and selected icons together");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            root.Q<EnumField>("textWrapping").value = TextWrapping.Manual;
            t.True(alignment.Q<Button>("textAlignJustify").enabledInHierarchy && !text.justify && text.alignment == TextAnchor.LowerLeft &&
                alignment.Q<Button>("textAlignLeft").ClassListContains("whimtex-text-format-button--selected"),
                "Manual wrapping resets justification to left alignment without disabling the button");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await AsyncD.Tick(root, token);
            text = (TextLayerBehaviour)document.layers[0].Behaviour;
            t.True(text.wrapping == TextWrapping.Words && text.justify &&
                alignment.Q<Button>("textAlignJustify").ClassListContains("whimtex-text-format-button--selected"),
                "Undo restores wrapping and justification in one transaction");
            root.Q<EnumField>("textWrapping").value = TextWrapping.Manual;
            Click(alignment.Q<Button>("textAlignRight")); Click(alignment.Q<Button>("textAlignJustify"));
            t.True(text.alignment == TextAnchor.LowerLeft && !text.justify, "Choosing Justify with Manual wrapping falls back to the left edge");
            root.Q<EnumField>("textWrapping").value = TextWrapping.Words;
            Click(alignment.Q<Button>("textAlignJustify"));
            root.Q<EnumField>("textLayoutMode").value = TextLayoutMode.Point;
            t.True(text.alignment == TextAnchor.LowerLeft && !text.justify && alignment.Q<Button>("textAlignJustify").enabledInHierarchy,
                "Changing a justified Frame to Point resets justification and keeps its button available");
            string selected = text.fontFamily;
            Type pickerType = typeof(TextLayerEditorWindow).GetNestedType("FontPicker", BindingFlags.NonPublic);
            string chosen = null;
            picker = (PopupWindowContent)Activator.CreateInstance(pickerType, Flags, null,
                new object[] { selected, (Action<string>)(name => chosen = name) }, null);
            window.Focus(); await AsyncD.Tick(root, token);
            UnityEditor.PopupWindow.Show(root.Q<TextField>("textFont").Q<Button>().worldBound, picker);
            t.True(picker.editorWindow != null, "Font picker opens an owned popup window");
            var pickerRoot = picker.editorWindow.rootVisualElement;
            var search = pickerRoot.Q<TextField>("fontSearch");
            var list = pickerRoot.Q<ListView>();
            t.True(list.itemsSource.Count > 0, "Font picker displays the installed font catalog");
            search.value = selected;
            t.True(list.itemsSource.Count > 0 && list.itemsSource.Cast<string>().All(name => name.IndexOf(selected, StringComparison.OrdinalIgnoreCase) >= 0),
                "Font search filters names");
            list.SetSelection(0);
            t.True((Font)pickerType.GetField("previewFont", Flags).GetValue(picker) != null, "Selected font creates an owned sample font");
            var select = pickerRoot.Query<Button>().ToList().Single(button => button.text == "Select");
            t.True(list.selectedItem is string, "Picker has a selected font before confirmation");
            select.Focus();
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = select; select.SendEvent(submit); }
            if (chosen == null)
            {
                var enter = new Event { type = EventType.KeyDown, keyCode = KeyCode.Return };
                using (var key = KeyDownEvent.GetPooled(enter)) { key.target = select; select.SendEvent(key); }
            }
            t.True(!string.IsNullOrEmpty(chosen), "Picker Select button applies selected font");
            if (picker.editorWindow != null) picker.editorWindow.Close(); picker = null;
            text.fontFamily = "WhimTexMissingFont_" + Guid.NewGuid().ToString("N");
            var bindings = typeof(LayerEditorWindowBase).GetField("SettingsBindings", Flags).GetValue(window);
            bindings.GetType().GetMethod("Refresh").Invoke(bindings, new object[] { true });
            t.True(!root.Q<TextField>("textContent").enabledInHierarchy, "Missing font disables source layout editing");
            t.True(root.Q<TextField>("textFont").enabledInHierarchy && root.Q<VisualElement>("textColor").enabledInHierarchy,
                "Missing font keeps replacement and tint controls enabled");
            var notice = root.Q<HelpBox>("textFontNotice");
            t.True(!notice.ClassListContains("whimtex-hidden") && notice.text.Contains("unavailable"), "Missing font has a visible inspector warning");
            text.fontFamily = selected;
            bindings.GetType().GetMethod("Refresh").Invoke(bindings, new object[] { true });
            t.True(root.Q<TextField>("textContent").enabledInHierarchy && notice.ClassListContains("whimtex-hidden"),
                "Restoring a font reenables text editing and clears the warning");
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (picker?.editorWindow != null) picker.editorWindow.Close(); },
                () => { if (window != null) window.Close(); },
                () => { if (document != null) Undo.ClearUndo(document); },
                () => { if (document != null) UnityEngine.Object.DestroyImmediate(document); });
        }
    }
}
