using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class BrushHlslWindow : EditorWindow
    {
        private Action<string> apply;
        private Func<List<ShaderFXParameter>> values;
        [SerializeField] private string draft;
        internal static void Open(string code,Action<string> apply,Func<List<ShaderFXParameter>> values)
        {
            var window=CreateInstance<BrushHlslWindow>();
            window.titleContent=new GUIContent("HLSL Brush");window.draft=code;window.apply=apply;window.values=values;
            window.minSize=new Vector2(420,300);window.ShowUtility();
        }
        private void CreateGUI()
        {
            WhimTexUI.ApplyWindowStyles(rootVisualElement);
            var code=new TextField{multiline=true,value=draft,selectAllOnFocus=false,selectAllOnMouseUp=false};
            code.AddToClassList("whimtex-brush-hlsl-code");
            code.verticalScrollerVisibility=ScrollerVisibility.Auto;
            code.RegisterValueChangedCallback(e=>draft=e.newValue);
            rootVisualElement.Add(code);
            var status=new HelpBox("BrushTip(float2 uv) returns an RGBA tip. Apply rebuilds the cached texture.",HelpBoxMessageType.Info);
            rootVisualElement.Add(status);
            void Apply()
            {
                try
                {
                    if(apply==null)throw new InvalidOperationException("Reopen this editor after script reload.");
                    apply(draft);status.text="Applied.";status.messageType=HelpBoxMessageType.Info;
                }
                catch(Exception error){status.text=error.Message;status.messageType=HelpBoxMessageType.Error;}
            }
            rootVisualElement.Add(new Button(Apply){text="Apply"});
            void Save(bool project)
            {
                try
                {
                    if(apply==null)throw new InvalidOperationException("Reopen this editor after script reload.");
                    apply(draft);
                    string folder=project?Application.dataPath:BrushTipProgram.Folder;
                    Directory.CreateDirectory(folder);
                    string path=EditorUtility.SaveFilePanel("Save HLSL Brush",folder,"Brush","hlsl");
                    if(string.IsNullOrEmpty(path))return;
                    path=PresetLibraryPaths.ValidateDestination(path,BrushTipProgram.Folder,"hlsl");
                    bool exists=File.Exists(path);
                    if(exists && !EditorUtility.DisplayDialog("Overwrite HLSL Brush","Replace "+Path.GetFileName(path)+"?","Replace","Cancel"))return;
                    string text=BrushTipProgram.Export(draft,values());
                    string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                    try
                    {
                        File.WriteAllText(temp,text,new UTF8Encoding(false));
                        if(exists)File.Replace(temp,path,path+".bak");else File.Move(temp,path);
                    }
                    finally{if(File.Exists(temp))File.Delete(temp);}
                    PresetLibraryPaths.ImportSavedFile(path);status.text="Saved.";
                }
                catch(Exception error){status.text=error.Message;status.messageType=HelpBoxMessageType.Error;}
            }
            rootVisualElement.Add(new Button(()=>Save(false)){text="Save HLSL Preset…"});
            rootVisualElement.Add(new Button(()=>Save(true)){text="Save to Project…"});
        }
    }
}
