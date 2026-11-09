using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderExecutableCache
    {
        internal sealed partial class Entry
        {
            internal readonly bool borrowedShader;
            internal readonly string[] keywords;
            internal readonly (UnityObjectID shader, int revision, string keywords) variantKey;

            internal Entry(Material material, string[] keywords,
                (UnityObjectID shader, int revision, string keywords) key)
            {
                shader = material.shader; borrowedShader = true; this.keywords = keywords;
                variantKey = key; revision = key.revision;
                // A variant already available in the Editor needs no queue slot or submission.
                started = ready = ShaderUtil.IsPassCompiled(material, 0);
            }
        }

        static readonly Dictionary<(UnityObjectID shader, int revision, string keywords), Entry> variants = new();
        internal static long VariantPassCompilationRequests { get; private set; }
        internal static long VariantCacheHits { get; private set; }

        internal static Entry AcquireVariant(Material material, ShaderGraphPreparation.Priority request)
        {
            CheckRevision();
            var keywords = material.shaderKeywords;
            Array.Sort(keywords, StringComparer.Ordinal);
            var key = (UnityObjectID.FromObject(material.shader), revision, string.Join("\n", keywords));
            if (variants.TryGetValue(key, out var entry)) VariantCacheHits++;
            else
            {
                entry = new Entry(material, keywords, key);
                variants.Add(key, entry); entries.Add(entry);
            }
            entry.requests.Add(request); entry.Retain();
            Trim();
            return entry;
        }

        static void CheckRevision()
        {
            if (revision == ShaderProgramSourceRevision.Current) return;
            revision = ShaderProgramSourceRevision.Current;
            cache.Clear(); variants.Clear(); sourceBytes = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var old = entries[i]; old.cached = false;
                if (old.owners == 0 && (!old.started || old.ready)) Remove(old);
            }
        }
    }
}
