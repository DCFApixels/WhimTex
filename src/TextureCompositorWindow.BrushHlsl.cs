using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
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
            var parameters=new VisualElement();panel.Add(parameters);parent.Add(panel);
            string layout=null;
            var refresh=new List<Action>();
            brushSettingsBindings.Add(() =>
            {
                panel.EnableInClassList("whimtex-brush-setting--hidden",paintSettings.dynamics.source!=BrushTipSource.HLSL);
                string key=paintSettings.dynamics.hlslCode+"\n"+paintSettings.dynamics.hlslParameters?.Count;
                if(layout==key){foreach(var update in refresh)update();return;}
                layout=key;parameters.Clear();refresh.Clear();
                var definitions=paintSettings.dynamics.hlslParameters;
                if(definitions==null)return;
                foreach(var definition in definitions)
                {
                    string name=definition.name;
                    int firstField=parameters.childCount;
                    string label=ObjectNames.NicifyVariableName(name.TrimStart('_'));
                    ShaderFXParameter Current()=>paintSettings.dynamics.hlslParameters.Find(p=>p.name==name)??definition;
                    void Change(Action<ShaderFXParameter> write)
                    {
                        try
                        {
                            var values=new List<ShaderFXParameter>();
                            foreach(var p in paintSettings.dynamics.hlslParameters)values.Add(p.Copy());
                            var value=values.Find(p=>p.name==name);if(value==null)return;write(value);
                            ApplyPaintToolChange(()=>paintSettings.ApplyHlsl(paintSettings.dynamics.hlslCode,values,paintSettings.dynamics.hlslResolution));
                        }
                        catch(Exception error){ShowNotification(new GUIContent(error.Message));foreach(var update in refresh)update();}
                    }
                    if(definition.type==ShaderFXParameterType.Float)
                    {
                        if(definition.hasMinimum && definition.hasMaximum && definition.minimum<definition.maximum)
                        {
                            var field=new Slider(label,definition.minimum,definition.maximum){value=definition.floatValue,showInputField=true};
                            field.RegisterValueChangedCallback(e=>Change(p=>p.floatValue=p.Clamp(e.newValue)));parameters.Add(field);
                            refresh.Add(()=>field.SetValueWithoutNotify(Current().floatValue));
                        }
                        else
                        {
                            var field=new FloatField(label){value=definition.floatValue};
                            field.RegisterValueChangedCallback(e=>Change(p=>p.floatValue=p.Clamp(e.newValue)));parameters.Add(field);
                            refresh.Add(()=>field.SetValueWithoutNotify(Current().floatValue));
                        }
                    }
                    else if(definition.type==ShaderFXParameterType.Color)
                    {
                        var field=new ColorField(label){value=definition.colorValue,hdr=true};
                        field.RegisterValueChangedCallback(e=>Change(p=>p.colorValue=e.newValue));parameters.Add(field);
                        refresh.Add(()=>field.SetValueWithoutNotify(Current().colorValue));
                    }
                    else
                    {
                        var field=new Vector4Field(label){value=definition.vectorValue};
                        field.RegisterValueChangedCallback(e=>Change(p=>p.vectorValue=e.newValue));parameters.Add(field);
                        refresh.Add(()=>field.SetValueWithoutNotify(Current().vectorValue));
                    }
                    if(definition.controls.Count>0 && !string.IsNullOrEmpty(definition.controls[0].tooltip))
                        for(int i=firstField;i<parameters.childCount;i++)parameters[i].tooltip=definition.controls[0].tooltip;
                }
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
                            ApplyPaintToolChange(()=>paintSettings.ApplyHlsl(code,new List<ShaderFXParameter>(),paintSettings.dynamics.hlslResolution));
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
