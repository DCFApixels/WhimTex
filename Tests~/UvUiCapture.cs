// Optional screenshot of only the temporary test window, after UvUiSetup.cs.
foreach(var window in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.SpriteEditor.TextureCompositorWindow>())
{
    if(window.name!="WhimTex UV smoke")continue;
    var rect=window.position;
    int width=(int)rect.width,height=(int)rect.height;
    var texture=new UnityEngine.Texture2D(width,height,UnityEngine.TextureFormat.RGBA32,false);
    try
    {
        texture.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position,width,height));
        texture.Apply();
        string path=System.IO.Path.GetFullPath("Temp/WhimTexUvPreview.png");
        System.IO.File.WriteAllBytes(path,texture.EncodeToPNG());
        return path;
    }
    finally{UnityEngine.Object.DestroyImmediate(texture);}
}
throw new System.Exception("Run UvUiSetup.cs first.");
