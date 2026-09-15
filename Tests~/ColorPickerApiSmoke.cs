using System;
using System.Reflection;
using UnityEditor;
public static class ColorPickerApiSmoke
{
    public static string Main()
    {
        var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.ColorPicker");
        return string.Join("\n",Array.ConvertAll(Array.FindAll(type.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic),m=>m.Name=="Show"),m=>m+" : "+string.Join(",",Array.ConvertAll(m.GetParameters(),p=>p.Name))));
    }
}
