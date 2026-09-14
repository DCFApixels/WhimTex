using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal static class LayerTypeRegistry
    {
        internal sealed class Entry
        {
            internal readonly string ApiId, MenuName, NamePrefix, InsideMenuName;
            internal readonly Type BehaviourType;
            internal readonly int Section;
            private readonly Func<LayerBehaviour> create;

            internal Entry(string apiId, string menuName, string namePrefix, string insideMenuName,
                Type behaviourType, int section, Func<LayerBehaviour> create)
            {
                ApiId = apiId; MenuName = menuName; NamePrefix = namePrefix; InsideMenuName = insideMenuName;
                BehaviourType = behaviourType; Section = section; this.create = create;
            }

            internal LayerBehaviour CreateBehaviour() => create();
            internal Layer CreateLayer() => new Layer(create());
        }

        internal static readonly IReadOnlyList<Entry> Entries = Array.AsReadOnly(new[]
        {
            new Entry("drawing", "Drawing Layer", "Layer", "Drawing Layer", typeof(DrawingLayerBehaviour), 0, () => new DrawingLayerBehaviour()),
            new Entry("file", "File", "File", "File Layer", typeof(FileLayerBehaviour), 0, () => new FileLayerBehaviour()),
            new Entry("color", "Color Fill", "Color Fill", "Color Fill Layer", typeof(ColorFillLayerBehaviour), 0, () => new ColorFillLayerBehaviour()),
            new Entry("gradient", "Gradient", "Gradient", "Gradient Layer", typeof(GradientLayerBehaviour), 0, () => new GradientLayerBehaviour()),
            new Entry("noise", "Noise", "Noise", "Noise Layer", typeof(NoiseLayerBehaviour), 0, () => new NoiseLayerBehaviour()),
            new Entry("shape", "Shape", "Shape", "Shape", typeof(ShapeLayerBehaviour), 0, () => new ShapeLayerBehaviour()),
            new Entry("outline", "Outline", "Outline", "Outline Layer", typeof(OutlineLayerBehaviour), 1, () => new OutlineLayerBehaviour()),
            new Entry("sdf", "SDF", "SDF", "SDF Layer", typeof(SDFLayerBehaviour), 1, () => new SDFLayerBehaviour()),
            new Entry("normalMap", "Normal Map", "Normal Map", "Normal Map Layer", typeof(NormalMapLayerBehaviour), 1, () => new NormalMapLayerBehaviour()),
            new Entry("blur", "Blur", "Blur", "Blur", typeof(BlurLayerBehaviour), 1, () => new BlurLayerBehaviour()),
            new Entry("makeSeamless", "Make Seamless", "Make Seamless", "Make Seamless", typeof(MakeSeamlessLayerBehaviour), 1, () => new MakeSeamlessLayerBehaviour()),
            new Entry("shaderProcessor", "Shader Processor", "Shader Processor", "Shader Processor", typeof(ShaderProcessorLayerBehaviour), 1, () => new ShaderProcessorLayerBehaviour()),
            new Entry("group", "Group", "Group", "Group", typeof(GroupLayerBehaviour), 2, () => new GroupLayerBehaviour())
        });

        private static readonly Dictionary<string, Entry> ById = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, Entry> ByType = new Dictionary<Type, Entry>();

        static LayerTypeRegistry()
        {
            foreach (var entry in Entries) { ById.Add(entry.ApiId, entry); ByType.Add(entry.BehaviourType, entry); }
        }

        internal static Entry Find(string apiId) => apiId != null && ById.TryGetValue(apiId, out var entry) ? entry : null;
        internal static Entry Find(Type type) => type != null && ByType.TryGetValue(type, out var entry) ? entry : null;
    }
}
