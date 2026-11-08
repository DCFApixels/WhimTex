using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        ShaderParameter[] imageParameters = Array.Empty<ShaderParameter>();
        ShaderParameterBindings emptyInput;
        int inputSlot = -1;
        bool HasImages => HasBoundaries || imageParameters.Length != 0;
        internal bool NeedsInput
        {
            get { if (inputSlot >= 0 || HasBoundaries) return true;
                foreach (var p in imageParameters) if (p?.textureSource == ShaderTextureSource.Self) return true;
                return false; }
        }
        static readonly int[] ImageIds = new int[8], ImageSizes = new int[8], ImageMaps = new int[8], ImageLogicalSizes = new int[8];
        static ShaderGraphPreviewVM()
        {
            for(int i=0;i<8;i++) {
                string name="_VMImage"+i;
                ImageIds[i]=Shader.PropertyToID(name);ImageSizes[i]=Shader.PropertyToID("_VMImageSize"+i);
                ImageMaps[i]=Shader.PropertyToID(ShaderTextureViews.Prefix(name)+"_UvMap");
                ImageLogicalSizes[i]=Shader.PropertyToID(ShaderTextureViews.Prefix(name)+"_Size");
            }
        }
        void SetImage(int slot,Texture texture)
        {
            material.SetTexture(ImageIds[slot],texture);
            material.SetVector(ImageSizes[slot],new Vector4(texture.width,texture.height,0,0));
            ShaderTextureView.BindFull(material,ImageMaps[slot],ImageLogicalSizes[slot]);
        }
        bool RenderImages(RenderTexture target,in LayerRenderContext context,in ShaderLayerInput input)
        {
            using var bindings=context.activeDocument.BindShaderTextureLayers(imageParameters,hybridLayer,context,
                hybridEffect?input.Texture as RenderTexture:null);
            try {
                for(int i=0;i<imageParameters.Length;i++) {
                    int slot=7-i;var p=imageParameters[i];
                    if(p!=null) SetImage(slot,p.textureValue!=null?p.textureValue:Texture2D.whiteTexture);
                    else {
                        var view=input.GetView(input.Texture!=null?null:(emptyInput??=new ShaderParameterBindings()).Transparent);
                        SetImage(slot,view.texture);view.BindMetadata(material,ImageMaps[slot],ImageLogicalSizes[slot]);
                    }
                }
                bindings?.Apply(material);
                return Render(target,context.shaderContext.Time);
            }
            finally { for(int i=0;i<imageParameters.Length;i++) material.SetTexture(ImageIds[7-i],null); }
        }
        internal bool RenderEffectPreview(RenderTexture target,in LayerRenderContext context,RenderTexture input)
        {
            if (!Ready || HasBoundaries) return false;
            PrepareImageContext(context);
            ShaderParameterBindings.BindContext(material,context,effectContext:true);
            material.SetVector("_VMViewport",material.GetVector("_WhimTex_Viewport"));
            using var borrowed=new ShaderLayerInput(input,context,owned:false);
            return RenderImages(target,context,borrowed);
        }
        sealed partial class Builder
        {
            internal readonly List<ShaderParameter> images=new();
            readonly List<ShaderParameter> imageSources=new();
            internal int inputSlot=-1;
            Value Image(ShaderParameter parameter,bool input=false)
            {
                if(input && inputSlot>=0) return new Value(inputSlot,ShaderGraphValueType.Texture);
                if(!input) {
                    parameter??=ShaderGraphInput.DefaultResource(ShaderGraphValueType.Texture);
                    int index=imageSources.IndexOf(parameter);
                    if(index>=0) return new Value(7-index,ShaderGraphValueType.Texture);
                }
                if(resources.Count+images.Count>=8) Unsupported("Preview image input limit");
                int slot=7-images.Count;
                imageSources.Add(parameter);
                images.Add(input?null:new ShaderParameter {type=ShaderParameterType.Texture2D,name="_VMImage"+slot,
                    textureSource=parameter.textureSource,textureValue=parameter.textureValue,textureLayerId=parameter.textureLayerId});
                if(input) inputSlot=slot;
                return new Value(slot,ShaderGraphValueType.Texture);
            }
            Value SampleImage(ShaderGraphNode node,string port)
            {
                int slot=node.kind==ShaderGraphNodeKind.SampleInput?Image(null,true).register:Input(node,"Texture").register;
                var sample=Add(new Instruction {op=41,a=slot,b=Input(node,"UV").register,c=(int)node.wrap,d=(int)node.filter},ShaderGraphValueType.Color);
                int component="RGBA".IndexOf(port,StringComparison.Ordinal);
                return component>=0?Component(sample,component):sample;
            }
        }
    }
}
