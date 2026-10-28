using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static JToken TypedFxValue(ShaderFXParameter parameter)
        {
            JToken Component(int i) => parameter.IsBoolean ? new JValue(parameter.GetNumber(i) != 0) :
                parameter.IsInteger ? new JValue((long)parameter.GetNumber(i)) : new JValue(parameter.GetNumber(i));
            if (parameter.Components == 1) return Component(0);
            var array = new JArray();
            for (int i = 0; i < parameter.Components; i++) array.Add(Component(i));
            return array;
        }

        private static void ReadTypedFxValue(ShaderFXParameter parameter, JToken token)
        {
            if (parameter.Components > 1) Require(token is JArray a && a.Count == parameter.Components, "Wrong vector component count: " + parameter.name);
            for (int i = 0; i < parameter.Components; i++)
            {
                var item = parameter.Components == 1 ? token : token[i];
                Require(item != null && (parameter.IsBoolean ? item.Type == JTokenType.Boolean : item.Type == JTokenType.Integer || item.Type == JTokenType.Float),
                    "Expected " + (parameter.IsBoolean ? "true/false" : "a numeric value") + " for " + parameter.name);
                double value = parameter.IsBoolean ? (bool)item ? 1 : 0 : (double)item;
                if (parameter.controls.All(c => c.type == ShaderFXParameterType.Toggle)) Require(value == 0 || value == 1, "Toggle values must be 0 or 1.");
                var seed = ShaderFXSeed.Control(parameter);
                if (seed != null) Require(value == Math.Truncate(value) && (seed.softMinimum || value >= seed.valueMinimum) &&
                    (seed.softMaximum || value <= seed.valueMaximum) && (parameter.ValueType != "float" || Math.Abs(value) <= ShaderFXSeed.Limit), "Seed is outside its integer range.");
                parameter.SetNumber(i, value);
            }
        }
    }
}
