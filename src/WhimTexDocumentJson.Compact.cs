using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexDocumentJson
    {
        // Default omission runs after lossless export selection. These snapshots describe v1.
        // Future contracts keep explicit fields until their own defaults are available.
        private static void CompactVersionOne(JObject root)
        {
            void Omit(JObject fields, Type type)
            {
                foreach (var property in fields.Properties().ToArray())
                {
                    if (type == typeof(WhimTexDocument) && (property.Name == "width" || property.Name == "height")) continue;
                    if (IsDefault(type, property.Name, property.Value)) property.Remove();
                }
            }
            void Visit(JArray layers)
            {
                foreach (JObject layer in layers)
                {
                    if (layer["children"] is JArray children) Visit(children);
                    var behaviour = (JObject)layer["behaviour"];
                    var contract = WhimTexDocumentTypes.Default.Resolve((string)behaviour["type"]);
                    if ((int)behaviour["behaviourVersion"] == 1 && contract.CurrentVersion == 1)
                        Omit((JObject)behaviour["data"], contract.Type);
                    if ((int)layer["layerVersion"] != 1 || WhimTexDocumentTypes.Default.Layer.CurrentVersion != 1) continue;
                    Omit(layer, typeof(Layer));
                    if (layer["rendering"] is JObject rendering) Omit(rendering, typeof(Layer));
                }
            }
            if ((int)root["documentVersion"] == 1) Omit((JObject)root["document"], typeof(WhimTexDocument));
            Visit((JArray)root["layers"]);
        }
    }
}
