var f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var fillType=typeof(DCFApixels.SpriteEditor.TextureCompositorWindow).GetNestedType("ContentFillWindow",f);
foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll(fillType))
{
    var owner=(DCFApixels.SpriteEditor.TextureCompositorWindow)fillType.GetField("owner",f).GetValue(item);
    if(owner==null||owner.name!="Content fill smoke")continue;
    var window=(UnityEditor.EditorWindow)item;var rect=window.position;
    int w=(int)rect.width,h=(int)rect.height;
    var texture=new UnityEngine.Texture2D(w,h,UnityEngine.TextureFormat.RGBA32,false);
    try
    {
        texture.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position,w,h));texture.Apply();
        string path="D:/DCFA/Projects/Test6.6/Temp/WhimTexContentFillUi.png";
        System.IO.File.WriteAllBytes(path,UnityEngine.ImageConversion.EncodeToPNG(texture));return path;
    }
    finally{UnityEngine.Object.DestroyImmediate(texture);}
}
throw new System.Exception("Run ContentFillUiSetup.cs first.");
