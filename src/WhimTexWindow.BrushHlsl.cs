using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private void BuildBrushSourceControls(VisualElement parent)
        {
            var source=WhimTexUI.ConfigureField(new EnumField("Source",paintSettings.dynamics.source));
            source.RegisterValueChangedCallback(e =>
            {
                try { ApplyPaintToolChange(()=>paintSettings.SetTipSource((BrushTipSource)e.newValue)); }
                catch(Exception error){ShowNotification(new GUIContent(error.Message));source.SetValueWithoutNotify(paintSettings.dynamics.source);}
            });
            brushSettingsBindings.Track(source,()=> (Enum)paintSettings.dynamics.source);
            parent.Add(source);
            var panel=new VisualElement();
            panel.Add(new Button(ShowBrushHlslCatalog){text="HLSL Presets ▾"});
            panel.Add(new Button(() =>
            {
                var settings=paintSettings;
                var dynamics=settings.dynamics;
                BrushHlslWindow.Open(settings.dynamics.hlslCode,code =>
                {
                    if(this==null || paintSettings!=settings || settings.dynamics!=dynamics || dynamics.source!=BrushTipSource.HLSL)
                        throw new InvalidOperationException("The brush has changed. Reopen the code editor.");
                    ApplyPaintToolChange(()=>settings.ApplyHlsl(code,settings.dynamics.hlslParameters,settings.dynamics.hlslResolution));
                },()=>settings.dynamics.hlslParameters);
            }){text="Edit Code…"});
            var resolution=new IntegerField("Tip Resolution"){value=paintSettings.dynamics.hlslResolution,isDelayed=true,
                tooltip="Cached square tip, 32–2048 pixels. Changing brush Size does not rebuild it."};
            resolution.RegisterValueChangedCallback(e =>
            {
                try{ApplyPaintToolChange(()=>paintSettings.ApplyHlsl(paintSettings.dynamics.hlslCode,paintSettings.dynamics.hlslParameters,e.newValue));}
                catch(Exception error){ShowNotification(new GUIContent(error.Message));resolution.SetValueWithoutNotify(paintSettings.dynamics.hlslResolution);}
            });
            brushSettingsBindings.Track(resolution,()=>paintSettings.dynamics.hlslResolution);
            panel.Add(resolution);
            var evaluation = new Label();
            panel.Add(evaluation);
            var sequence = new VisualElement();
            var seed = WhimTexUI.ConfigureField(new IntegerField("Seed") { value = paintSettings.dynamics.seed, isDelayed = true,
                tooltip = "Stable sequence seed. Changing it restarts stroke indices and total distance." });
            seed.RegisterValueChangedCallback(e => ApplyPaintToolChange(() =>
            {
                paintSettings.dynamics.seed = Mathf.Max(1, e.newValue);
                paintSettings.dynamics.ResetSequence();
            }));
            brushSettingsBindings.Track(seed, () => paintSettings.dynamics.seed);
            sequence.Add(seed);
            sequence.Add(new Button(() => ApplyPaintToolChange(() => paintSettings.dynamics.ResetSequence())) { text = "Reset Sequence" });
            panel.Add(sequence);
            var parameterSource = new BrushParameterViewSource(
                () => paintSettings, ApplyPaintToolChange, message => ShowNotification(new GUIContent(message)));
            var mainControl = new ShaderFXParameterView(parameterSource, true);
            panel.Add(mainControl);
            var parameters = new ShaderFXParameterView(parameterSource);
            panel.Add(parameters); parent.Add(panel);
            var diagnostics = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            panel.Add(diagnostics);
            brushSettingsBindings.Add(() =>
            {
                panel.EnableInClassList("whimtex-brush-setting--hidden", paintSettings.dynamics.source != BrushTipSource.HLSL);
                bool dynamic = paintSettings.dynamics.DynamicTip;
                evaluation.text = dynamic ? "Dynamic — evaluated while painting" : "Static — baked on Apply";
                resolution.EnableInClassList("whimtex-brush-setting--hidden", dynamic);
                sequence.EnableInClassList("whimtex-brush-setting--hidden", !dynamic);
                mainControl.Refresh(); parameters.Refresh();
                diagnostics.text = paintSettings.dynamics.hlslProgram?.Diagnostics ?? string.Empty;
                diagnostics.EnableInClassList("whimtex-shader-fx-hidden", string.IsNullOrEmpty(diagnostics.text));
            });
        }
        private void ShowBrushHlslCatalog()
        {
            var menu=new GenericMenu();int count=0;
            void Add(string file,string prefix)
            {
                try
                {
                    string physical=PresetLibraryPaths.PhysicalPath(file);
                    if(new FileInfo(physical).Length>65536)return;
                    using var reader=new StreamReader(physical);
                    string name=BrushTipProgram.ReadName(reader.ReadLine());
                    menu.AddItem(new GUIContent(prefix+name,physical),false,() =>
                    {
                        try
                        {
                            string code=File.ReadAllText(physical);
                            ApplyPaintToolChange(() =>
                            {
                                paintSettings.ApplyHlsl(code,new List<ShaderFXParameter>(),paintSettings.dynamics.hlslResolution);
                                paintSettings.dynamics.ResetSequence();
                            });
                        }
                        catch(Exception error){ShowNotification(new GUIContent(error.Message));}
                    });count++;
                }
                catch(IOException){}catch(UnauthorizedAccessException){}catch(FormatException){}
            }
            foreach(var path in PresetLibraryPaths.ProjectFiles("hlsl"))Add(path,"Project/");
            foreach(var path in PresetLibraryPaths.UserFiles(BrushTipProgram.Folder,"hlsl"))Add(path,"User/");
            if(count==0)menu.AddDisabledItem(new GUIContent("No HLSL brushes found"));
            menu.ShowAsContext();
        }
    }
}
