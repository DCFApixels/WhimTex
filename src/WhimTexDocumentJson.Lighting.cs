using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexDocumentJson
    {
        internal static JObject LightSnapshot(WhimTexLight light, WhimTexDocument document)
            => new Writer(new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }, document).ObjectFields(light);

        internal static Texture2D ReadLightCookie(JToken data)
        {
            if (data.Type != JTokenType.Null && !(data is JObject asset && asset["$asset"] is JObject))
                throw new WhimTexDocumentException("light.cookie requires an $asset identity or null.");
            return (Texture2D)new Reader().Value(data, typeof(Texture2D));
        }

        internal static void SetLightCookie(WhimTexLight light, Texture2D texture, JToken data = null)
        {
            light.cookie = texture;
            const string field = nameof(WhimTexLight.cookie);
            if (MissingAssets.TryGetValue(light, out var missing)) missing.Remove(field);
            if (texture == null && data != null && data.Type != JTokenType.Null)
                MissingAssets.GetOrCreateValue(light)[field] = data.DeepClone();
        }
    }
}
