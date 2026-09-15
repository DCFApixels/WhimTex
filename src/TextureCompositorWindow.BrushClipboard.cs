using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private bool TryPasteBrushClipboard(string text)
        {
            string start=text?.TrimStart('\uFEFF',' ','\r','\n','\t');
            if(start==null || !(start.StartsWith("{") || start.StartsWith("```")) ||
                text.IndexOf("whimtex.brush",StringComparison.Ordinal)<0)return false;
            PaintToolSettings generated=null;
            try
            {
                generated=WhimTexApi.ReadBrushClipboard(text,out string url);
                var incoming=generated;
                if(url!=null)
                {
                    if(HasPendingImageUrl){ShowNotification(new GUIContent("Wait for the current image download."));return true;}
                    if(!EditorUtility.DisplayDialog("Download Brush Tip","Download a brush texture from "+new Uri(url).Host+"?","Download","Cancel"))return true;
                    string snapshot=JsonUtility.ToJson(paintSettings);
                    bool adopted=false;
                    BeginImageUrlBatch(compositor,new List<(string,Func<Texture2D,bool>)>{(url,texture=>
                    {
                        if(snapshot!=JsonUtility.ToJson(paintSettings))
                            throw new InvalidOperationException("The brush changed during download. Paste again to replace it.");
                        if((long)texture.width*texture.height>16*1024*1024)
                            throw new InvalidOperationException("Brush texture exceeds 16 megapixels.");
                        incoming.AdoptClipboardTip(texture);
                        ApplyGeneratedBrush(incoming);adopted=true;return true;
                    })},ok=>{if(!adopted)incoming.ReleasePresetTip();});
                    generated=null;
                }
                else
                {
                    if(generated.dynamics.source==BrushTipSource.HLSL)
                        generated.ApplyHlsl(generated.dynamics.hlslCode,generated.dynamics.hlslParameters,generated.dynamics.hlslResolution);
                    ApplyGeneratedBrush(generated);generated=null;
                }
            }
            catch(Exception error){ReportClipboardPasteError("Brush JSON paste failed", error);}
            finally{generated?.ReleasePresetTip();}
            return true;
        }
        private void ApplyGeneratedBrush(PaintToolSettings settings)
        {
            ApplyPaintToolChange(() =>
            {
                settings.brushColor=paintSettings.brushColor;settings.secondaryBrushColor=paintSettings.secondaryBrushColor;
                paintSettings.ReleasePresetTip();paintSettings=settings;
                selectedBrushPreset=selectedBrushPresetSnapshot=null;
            });
            ShowNotification(new GUIContent("Brush pasted."));
        }
    }
}
