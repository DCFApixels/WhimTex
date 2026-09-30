using System;
using System.Reflection;
using UnityEditor;
using DCFApixels.WhimTex;
public static class ColorPickerApiSmoke
{
    public static string Main()
    {
        var type=typeof(WhimTexColorPicker);
        return string.Join("\n",Array.ConvertAll(Array.FindAll(type.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic),m=>m.Name=="Open"),m=>m+" : "+string.Join(",",Array.ConvertAll(m.GetParameters(),p=>p.Name))));
    }
}
