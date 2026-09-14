using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

var assign = typeof(FileLayerBehaviour).GetMethod("AssignSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
var layer = new FileLayerBehaviour();
var hdr = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true);
var otherHdr = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
var standard = new Texture2D(2, 2, TextureFormat.RGBA32, false);
int checks = 0;
void Assign(Texture2D texture) => assign.Invoke(layer, new object[] { texture, null, false });
void Check(LayerColorRange color, LayerBlendRange blend, string label)
{
    checks++;
    if (layer.colorRange != color || layer.blendRange != blend) throw new Exception(label);
}
try
{
    Assign(standard);
    Check(LayerColorRange.Standard, LayerBlendRange.Standard, "Ordinary texture keeps Standard defaults");
    Assign(hdr);
    Check(LayerColorRange.HDR, LayerBlendRange.HDR, "HDR assignment promotes both ranges");
    layer.colorRange = LayerColorRange.Standard;
    layer.blendRange = LayerBlendRange.Standard;
    Assign(hdr);
    Check(LayerColorRange.Standard, LayerBlendRange.Standard, "Same source preserves manual overrides");
    Assign(otherHdr);
    Check(LayerColorRange.HDR, LayerBlendRange.HDR, "Different HDR source promotes both ranges again");
    Assign(standard);
    Check(LayerColorRange.HDR, LayerBlendRange.HDR, "Ordinary source does not demote ranges");
    Assign(null);
    Check(LayerColorRange.HDR, LayerBlendRange.HDR, "Clearing source preserves ranges");
    layer.colorRange = LayerColorRange.Standard;
    layer.blendRange = LayerBlendRange.Standard;
    Assign(hdr);
    Check(LayerColorRange.HDR, LayerBlendRange.HDR, "First HDR source promotes empty layer");
    return $"File layer HDR assignment: {checks} checks passed.";
}
finally
{
    UnityEngine.Object.DestroyImmediate(hdr);
    UnityEngine.Object.DestroyImmediate(otherHdr);
    UnityEngine.Object.DestroyImmediate(standard);
}
