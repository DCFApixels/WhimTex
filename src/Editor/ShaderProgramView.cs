using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderProgramView : Foldout
    {
        internal sealed class Actions
        {
            internal Action<ShaderProgramMode> changeMode;
            internal Action editGraph, compile, savePreset, embed, clearPreset, reloadPreset, openCode, openInVsCode;
            internal Action<Rect> choosePreset;
#if WHIMTEX_DEEP_DEBUG
            internal Action createDebugGraph;
#endif
        }

        const string GraphTitle = "Shader Graph";
        const string CodeTitle = "Code";
        readonly Actions actions;
        readonly DropdownField mode;
        readonly ShaderFXPresetField preset = new ShaderFXPresetField();
        readonly VisualElement codeArea = new VisualElement();
        readonly ShaderCodeField code;
        readonly ShaderCodeInfo codeInfo;
        readonly Button choosePreset, presetMenu, editGraph, compile, openCode, openInVsCode;
#if WHIMTEX_DEEP_DEBUG
        readonly Button createDebugGraph;
#endif
        readonly Image indicator = new Image { name = "shaderProgramStatus", pickingMode = PickingMode.Ignore };
        readonly HelpBox status = new HelpBox();
        readonly TextField diagnostics = new TextField
        {
            name = "shaderFXDiagnostics", multiline = true, isReadOnly = true,
            verticalScrollerVisibility = ScrollerVisibility.Auto
        };
        readonly ShaderDiagnosticPassDetails passDetails = new ShaderDiagnosticPassDetails();
        bool editable, linked;

        internal ShaderProgramView(ShaderCodeField code, Actions actions, bool effectContext)
        {
            this.code = code;
            this.actions = actions;
            name = "shaderFXCodeFoldout";
            text = "Shader";
            value = false;
            AddToClassList("whimtex-shader-program");
            contentContainer.AddToClassList("whimtex-shader-program-content");
            indicator.AddToClassList("whimtex-fx-warning");
            indicator.AddToClassList("whimtex-hidden");
            this.Q<Toggle>().Add(indicator);

            choosePreset = new Button(ChoosePreset) { text = "▾", name = "shaderProgramChoosePreset" };
            choosePreset.AddToClassList("whimtex-shader-program-preset-menu");
            preset.AddButton(choosePreset);
            presetMenu = new Button(ShowPresetMenu) { text = "⋯", name = "shaderProgramPresetMenu" };
            presetMenu.AddToClassList("whimtex-shader-program-preset-menu");
            preset.AddButton(presetMenu);
            preset.AddToClassList("whimtex-shader-field");
            preset.AddManipulator(new ContextualMenuManipulator(e => { BuildPresetMenu(e.menu); e.StopPropagation(); }));
            Add(preset);

            mode = new DropdownField("Mode", new List<string> { GraphTitle, CodeTitle }, 1) { name = "shaderProgramMode" };
            mode.AddToClassList("whimtex-shader-field");
            mode.RegisterValueChangedCallback(e =>
            {
                if (editable && !linked)
                    actions.changeMode(e.newValue == CodeTitle ? ShaderProgramMode.Code : ShaderProgramMode.Graph);
            });
            Add(mode);

            editGraph = new Button(actions.editGraph) { text = "Edit Graph", name = "shaderProgramEditGraph" };
#if WHIMTEX_DEEP_DEBUG
            var graphActions = new VisualElement();
            graphActions.AddToClassList("whimtex-layer-fx-toolbar");
            editGraph.style.flexGrow = 1;
            graphActions.Add(editGraph);
            createDebugGraph = new Button(actions.createDebugGraph)
                { text = "Create Debug Graph", name = "shaderProgramCreateDebugGraph" };
            graphActions.Add(createDebugGraph);
            Add(graphActions);
#else
            Add(editGraph);
#endif
            codeInfo = new ShaderCodeInfo(code, effectContext);
            if (effectContext) codeArea.Add(codeInfo.PixelLocalStatus);
            codeArea.Add(code);
            Add(codeArea);

            var codeActions = new VisualElement();
            codeActions.AddToClassList("whimtex-layer-fx-toolbar");
            compile = new Button(actions.compile) { text = "Compile", name = "shaderFXCompile",
                tooltip = "Compile the current shader program. Does not bake pixels." };
            codeActions.Add(compile);
            openCode = new Button(actions.openCode) { text = "Open Code", name = "shaderProgramOpenCode" };
            openInVsCode = new Button(actions.openInVsCode) { text = "Open in VS Code", name = "shaderProgramOpenInVsCode" };
            codeActions.Add(openCode);
            codeActions.Add(openInVsCode);
            Add(codeActions);
            Add(codeInfo);

            Add(status);
            diagnostics.AddToClassList("whimtex-shader-fx-diagnostics");
            Add(diagnostics);
            Add(passDetails);
        }

        void ChoosePreset()
        {
            if (editable)
                actions.choosePreset?.Invoke((linked ? presetMenu : choosePreset).worldBound);
        }

        void ShowPresetMenu()
        {
            var items = new DropdownMenu();
            BuildPresetMenu(items);
            items.PrepareForDisplay(null);
            var menu = new GenericMenu();
            foreach (var item in items.MenuItems())
            {
                if (item is not DropdownMenuAction action || action.status == DropdownMenuAction.Status.Hidden) continue;
                var label = new GUIContent(action.name);
                if (action.status == DropdownMenuAction.Status.Disabled) menu.AddDisabledItem(label);
                else menu.AddItem(label, false, action.Execute);
            }
            menu.DropDown(presetMenu.worldBound);
        }

        void BuildPresetMenu(DropdownMenu menu)
        {
            DropdownMenuAction.Status EditStatus(DropdownMenuAction _) =>
                editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            DropdownMenuAction.Status LinkedStatus(DropdownMenuAction action) =>
                linked ? EditStatus(action) : DropdownMenuAction.Status.Hidden;
            if (actions.choosePreset != null)
            {
                menu.AppendAction("Choose Preset…", _ => ChoosePreset(),
                    action => linked ? DropdownMenuAction.Status.Hidden : EditStatus(action));
                menu.AppendAction("Change Preset…", _ => ChoosePreset(), LinkedStatus);
            }
            menu.AppendAction("Save Preset…", _ => actions.savePreset(), EditStatus);
            preset.AddSourceActions(menu);
            if (actions.reloadPreset != null)
                menu.AppendAction("Reload Preset", _ => actions.reloadPreset(), LinkedStatus);
            menu.AppendAction("Embed Copy", _ => actions.embed(), LinkedStatus);
            menu.AppendAction("Clear Preset", _ => { if (linked && editable) actions.clearPreset(); }, LinkedStatus);
        }

        internal void Refresh(ShaderProgram program, ShaderCompilationState compilation, bool canEdit,
            bool pending, bool fallback, string sourcePath, string presetError = null, ShaderFX effect = null)
        {
            editable = canEdit;
            linked = ShaderPresetLink.IsLinked(program);
            bool manual = program.mode == ShaderProgramMode.Code;
            bool editableSource = editable && !linked;
            mode.SetValueWithoutNotify(manual ? CodeTitle : GraphTitle);
            mode.SetEnabled(editableSource);
            preset.Refresh(linked ? sourcePath : null, linked ? "Source unavailable" : string.Empty);
            Visible(choosePreset, !linked && actions.choosePreset != null);
            choosePreset.SetEnabled(editable);
            Visible(presetMenu, linked || actions.choosePreset == null);
            editGraph.text = linked ? "Open Graph" : "Edit Graph";
            editGraph.SetEnabled(actions.editGraph != null);
            Visible(editGraph, !manual);
#if WHIMTEX_DEEP_DEBUG
            Visible(createDebugGraph, !manual && !linked && actions.createDebugGraph != null);
            createDebugGraph.SetEnabled(editableSource);
#endif
            Visible(codeArea, manual);
            Visible(codeInfo, manual);
            code.SetEnabled(editableSource);
            if (manual)
            {
                code.SyncFromModel();
                if (effect != null) codeInfo.Refresh(effect, pending);
                else codeInfo.RefreshEditingState(linked);
            }
            Visible(compile, manual || actions.editGraph == null);
            compile.SetEnabled(editableSource);
            Visible(openCode, manual && actions.openCode != null);
            Visible(openInVsCode, manual && actions.openInVsCode != null);
            openCode.SetEnabled(editableSource);
            openInVsCode.SetEnabled(editableSource);

            var severity = ShaderDiagnostics.Severity(compilation.DiagnosticMessages);
            bool hasError = compilation.HasError || severity == ShaderDiagnosticSeverity.Error;
            bool warning = severity == ShaderDiagnosticSeverity.Warning || fallback || !string.IsNullOrEmpty(presetError);
            string message = fallback || !string.IsNullOrEmpty(presetError) ? "Using embedded copy." :
                hasError ? (effect != null && effect.IsUnavailable || !program.HasWorkingProgram(program.mode)
                    ? "Compilation failed. No working shader is available."
                    : "Compilation failed. Showing the last working shader.") :
                pending ? "Uncompiled shader changes." : "";
            status.text = message;
            status.messageType = hasError ? HelpBoxMessageType.Error : warning ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
            Visible(status, message.Length != 0);
            string details = compilation.DiagnosticMessages.Count != 0 || hasError ? compilation.Diagnostics : null;
            if (!string.IsNullOrEmpty(presetError)) details = string.IsNullOrEmpty(details) ? presetError : presetError + "\n" + details;
            diagnostics.SetValueWithoutNotify(details ?? "");
            Visible(diagnostics, !string.IsNullOrEmpty(details));
            passDetails.Refresh(compilation.DiagnosticMessages);
            string icon = hasError ? "console.erroricon.sml" : warning ? "console.warnicon.sml" :
                pending || compilation.IsPending ? "console.infoicon.sml" : null;
            indicator.image = icon != null ? EditorGUIUtility.IconContent(icon).image : null;
            Visible(indicator, icon != null);
        }

        static void Visible(VisualElement element, bool show) => element.EnableInClassList("whimtex-hidden", !show);

        internal static void SavePreset(ShaderProgram program, string title, Action<string, bool> save)
        {
            try
            {
                Directory.CreateDirectory(ShaderPresetCatalog.Folder);
                string path = EditorUtility.SaveFilePanel("Save Shader Preset", ShaderPresetCatalog.Folder, title, ShaderPresetIO.ExtensionFor(program));
                if (string.IsNullOrEmpty(path)) return;
                bool overwrite = File.Exists(path);
                if (overwrite && !EditorUtility.DisplayDialog("Overwrite Shader Preset", "Replace this preset? A .bak copy will be kept.", "Overwrite", "Cancel")) return;
                save(path, overwrite);
            }
            catch (Exception error) { EditorUtility.DisplayDialog("Shader Preset", error.Message, "OK"); }
        }
    }
}
