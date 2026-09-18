using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class UvMeshSourceSmoke
{
    public static string Main()
    {
        var resolve = typeof(TextureCompositorWindow).GetMethod("ResolveUvMesh", BindingFlags.Static | BindingFlags.NonPublic);
        Mesh Resolve(UnityEngine.Object value) => (Mesh)resolve.Invoke(null, new object[] { value });
        void Check(bool value) { if (!value) throw new Exception("UV mesh resolution failed"); }
        var root = new GameObject("UV resolution test") { hideFlags = HideFlags.HideAndDontSave };
        var a = new Mesh(); var b = new Mesh();
        try
        {
            Check(Resolve(null) == null && Resolve(a) == a && Resolve(root) == null);
            var first = new GameObject("First"); first.transform.SetParent(root.transform); first.SetActive(false);
            first.AddComponent<SkinnedMeshRenderer>().sharedMesh = a;
            var second = new GameObject("Second"); second.transform.SetParent(root.transform);
            second.AddComponent<MeshFilter>().sharedMesh = b;
            Check(Resolve(root) == a);
            first.GetComponent<SkinnedMeshRenderer>().sharedMesh = null;
            Check(Resolve(root) == b);
            root.AddComponent<MeshFilter>().sharedMesh = a;
            Check(Resolve(root) == a);
            return "PASS: direct mesh, empty model, inactive/skinned child, null mesh skip and hierarchy order.";
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
    }
}
