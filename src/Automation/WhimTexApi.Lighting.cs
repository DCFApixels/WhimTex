using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void ApplyLightingOperation(WhimTexDocument document, JObject operation)
        {
            Keys(operation, "op", "settings");
            var settings = Obj(operation["settings"], "settings");
            Keys(settings, "ambientColor");
            if (settings["ambientColor"] != null)
            {
                var color = AgentJson.Color(settings["ambientColor"]);
                color.a = 1;
                document.ambientColor = color;
            }
        }

        private static JArray LightSnapshots(WhimTexDocument document)
        {
            var result = new JArray();
            if (document.lights == null) return result;
            for (int i = 0; i < document.lights.Count; i++)
            {
                var light = document.lights[i];
                var snapshot = WhimTexDocumentJson.LightSnapshot(light, document);
                snapshot["index"] = i;
                result.Add(snapshot);
            }
            return result;
        }

        private static void ApplyLightOperation(WhimTexDocument document, JObject operation)
        {
            Keys(operation, "op", "action", "id", "settings");
            string action = Text(operation, "action", "set");
            string id = Text(operation, "id");
            Require(action == "add" || action == "set" || action == "remove", "Light action must be add, set or remove.");
            WhimTexLight light;
            if (action == "add")
            {
                Require(document.lights.Count < WhimTexDocument.MaximumLights, "A document supports up to 64 lights.", "resource_limit");
                if (id != null) Require(!string.IsNullOrWhiteSpace(id) && id.Length <= 128 && !document.lights.Exists(item => item.id == id), "A new light ID must be nonempty, at most 128 characters and unique.");
                light = new WhimTexLight { id = id ?? Guid.NewGuid().ToString("N"), name = "Light " + (document.lights.Count + 1),
                    position = new Vector3(document.width * .5f, document.height * .5f, Mathf.Max(document.width, document.height) * .25f),
                    range = Mathf.Max(document.width, document.height), size = Vector2.one * Mathf.Max(document.width, document.height) * .25f };
            }
            else
            {
                light = document.lights.Find(item => item.id == id);
                Require(light != null, "Light not found: " + id, "invalid_target");
            }
            if (action == "remove")
            {
                Require(operation["settings"] == null, "Remove light does not accept settings.");
                WhimTexDocumentJson.RestoreMissingAssets(document);
                document.lights.Remove(light);
                WhimTexDocumentJson.CaptureMissingAssets(document);
                return;
            }
            var settings = operation["settings"] == null ? new JObject() : Obj(operation["settings"], "settings");
            Keys(settings, "name", "enabled", "type", "color", "intensity", "position", "direction", "range", "falloff", "angle", "softness", "areaShape", "size", "rotation", "cookie");
            // Validate first; batch planning and the existing transaction own rollback/Undo.
            string name = Text(settings, "name", light.name);
            Require(name != null && name.Length <= 256, "Light name must have at most 256 characters.");
            var type = Enum(settings, "type", light.type);
            var position = settings["position"] == null ? light.position : LightVector(settings["position"], "position");
            var direction = settings["direction"] == null ?
                (action == "add" && (type == WhimTexLightType.Spot || type == WhimTexLightType.Area) ? Vector3.back : light.DirectionForType(type)) :
                LightVector(settings["direction"], "direction");
            Require(direction.sqrMagnitude > 1e-12f, "Light direction cannot be zero.");
            var color = settings["color"] == null ? light.color : AgentJson.Color(settings["color"]);
            float intensity = Number(settings, "intensity", light.intensity, 0, 1000000);
            float range = Number(settings, "range", light.range, .001f, 10000000);
            float falloff = Number(settings, "falloff", light.falloff, .01f, 1000);
            float angle = Number(settings, "angle", light.angle, 1, 179);
            float softness = Number(settings, "softness", light.softness, 0, 1);
            var shape = Enum(settings, "areaShape", light.areaShape);
            Vector2 size = light.size;
            if (settings["size"] != null)
            {
                Require(settings["size"] is JArray a && a.Count == 2, "Light size must contain two numbers.");
                size = new Vector2(Number(settings["size"][0], "size.x", .001f, 10000000), Number(settings["size"][1], "size.y", .001f, 10000000));
            }
            float rotation = Number(settings, "rotation", light.rotation, -10000000, 10000000);
            var cookie = settings["cookie"] == null ? light.cookie : WhimTexDocumentJson.ReadLightCookie(settings["cookie"]);
            Require(!WhimTexDocumentService.IsOwnOutput(document, cookie), "A light Cookie cannot reference its own document output.");
            bool enabled = Bool(settings, "enabled", light.enabled);
            WhimTexDocumentJson.RestoreMissingAssets(document);
            light.name = name; light.type = type; light.enabled = enabled; light.position = position; light.direction = direction;
            light.color = color; light.intensity = intensity; light.range = range; light.falloff = falloff;
            light.angle = angle; light.softness = softness;
            light.areaShape = shape; light.size = size; light.rotation = rotation;
            if (settings["cookie"] != null) WhimTexDocumentJson.SetLightCookie(light, cookie, settings["cookie"]);
            if (action == "add") document.lights.Add(light);
            WhimTexDocumentJson.CaptureMissingAssets(document);
        }

        private static Vector3 LightVector(JToken value, string name)
        {
            Require(value is JArray array && array.Count == 3, name + " must contain three numbers.");
            return new Vector3(Number(value[0], name + ".x", -10000000, 10000000),
                Number(value[1], name + ".y", -10000000, 10000000), Number(value[2], name + ".z", -10000000, 10000000));
        }
    }
}
