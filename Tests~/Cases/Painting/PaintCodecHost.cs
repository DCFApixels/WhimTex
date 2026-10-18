using System;
using WhimTex.Tests;

// Explicitly authorized temporary assembly imported through Unity for Burst processing.
public static class PaintCodecHost
{
    public static Type Target(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == "WhimTex.CodecDiagnostics") return assembly.GetType(name, true);
        throw new Exception("Import the authorized temporary codec diagnostic assembly first");
    }
    public static string Check() => (string)Target("PaintCodecPreflight").GetMethod("Check").Invoke(null, null);
}
