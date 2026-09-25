#if WHIMTEX_PIPELINE
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace DCFApixels.WhimTex
{
    public static class WhimTexCommands
    {
        [CliCommand("whimtex_assistant_begin", "Create a named reservation and capture context in one call in the open WhimTex window. No automatic save.", MainThreadRequired = true)]
        public static JObject Begin(
            [CliArg("requestId", "Caller-generated UUID; reuse identical arguments for retries", Required = true)] string requestId,
            [CliArg("name", "Reserved layer name")] string name = "Generating…",
            [CliArg("source", "none, merged, or layer (active layer unless sourceLayerId is supplied)")] string source = "none",
            [CliArg("area", "canvas or selection")] string area = "canvas",
            [CliArg("sessionId", "Optional explicit session; otherwise the only open, currently focused, or last-focused document")] string sessionId = null,
            [CliArg("sourceLayerId", "Optional stable source layer ID")] string sourceLayerId = null,
            [CliArg("selectionMode", "strict clips to selection; guide allows output outside it within the captured crop")] string selectionMode = "strict",
            [CliArg("padding", "Selection context pixels, 0..4096; -1 uses default (strict: 32, guide: 128)")] int padding = -1)
            => JObject.Parse(WhimTexApi.LiveBegin(requestId, name, source, area, sessionId, sourceLayerId, selectionMode, padding));

        [CliCommand("whimtex_assistant_lock", "Reserve an existing layer for inline FX/settings editing in the open WhimTex window. No automatic save.", MainThreadRequired = true)]
        public static JObject Lock(
            [CliArg("requestId", "Caller-generated unique ID; reuse identical arguments for retries", Required = true)] string requestId,
            [CliArg("layerId", "Existing layer GUID", Required = true)] string layerId,
            [CliArg("sessionId", "Optional explicit open document")] string sessionId = null,
            [CliArg("expectedRevision", "Optional layer contentRevision from inspection")] string expectedRevision = null)
            => JObject.Parse(WhimTexApi.LiveLock(requestId, layerId, sessionId, expectedRevision));

        [CliCommand("whimtex_assistant_sessions", "List open WhimTex documents, including unsaved documents, with live session IDs.", MainThreadRequired = true)]
        public static JObject Sessions() => JObject.Parse(WhimTexApi.LiveSessions());

        [CliCommand("whimtex_assistant_live", "Reserve, preview or complete an agent layer in an open WhimTex document using a JSON request file.", MainThreadRequired = true)]
        public static JObject Live([CliArg("requestPath", "Absolute path to a live-edit JSON request file", Required = true)] string requestPath)
            => JObject.Parse(WhimTexApi.LiveFile(requestPath));

        [CliCommand("whimtex_describe", "Describe WhimTex's agent API, operations, enums and limits.", MainThreadRequired = true)]
        public static JObject Describe() => JObject.Parse(WhimTexApi.Describe());

        [CliCommand("whimtex_assistant_execute", "Apply a revision-checked operation batch to an open document, with Undo and no automatic save.", MainThreadRequired = true)]
        public static JObject AssistantExecute([CliArg("requestPath", "Absolute JSON request path", Required = true)] string requestPath)
            => JObject.Parse(WhimTexApi.AssistantExecuteFile(requestPath));

        [CliCommand("whimtex_fx_catalog", "Find installed FX presets in Assets, packages and the user library; inspect a preset's parameters by ID.", MainThreadRequired = true)]
        public static JObject FxCatalog([CliArg("query", "Optional category/name substring")] string query = null,
            [CliArg("presetId", "Optional exact ID returned by the catalog; includes parameter details")] string presetId = null)
            => JObject.Parse(WhimTexApi.FxCatalog(query, presetId));

        [CliCommand("whimtex_render_probe", "Render a document, layer or FX input/output, optionally isolate a channel and report linear pixel ranges. Does not edit the source.", MainThreadRequired = true)]
        public static JObject RenderProbe([CliArg("requestPath", "Absolute JSON request path", Required = true)] string requestPath)
            => JObject.Parse(WhimTexApi.RenderProbeFile(requestPath));

        [CliCommand("whimtex_document_inspect", "Read a WhimTex document's layer IDs, settings and revision before editing.", MainThreadRequired = true)]
        public static JObject Inspect([CliArg("assetPath", "Project-relative WhimTex .tiff path, or an existing legacy .asset for read-only inspection", Required = true)] string assetPath)
            => JObject.Parse(WhimTexApi.Inspect(assetPath));

        [CliCommand("whimtex_batch_execute", "Apply a JSON batch to an independent TIFF model. save=true writes TIFF; save=false discards edits after returning. Does not edit an open window.", MainThreadRequired = true)]
        public static JObject Execute([CliArg("requestPath", "Absolute path to a JSON request file", Required = true)] string requestPath)
            => JObject.Parse(WhimTexApi.ExecuteFile(requestPath));

        [CliCommand("whimtex_image_import", "Copy a generated PNG/JPEG into Assets and import it as a texture. Never overwrites.", MainThreadRequired = true)]
        public static JObject ImportImage(
            [CliArg("sourcePath", "Absolute local PNG/JPEG path", Required = true)] string sourcePath,
            [CliArg("assetPath", "New Assets/ texture path with the same extension", Required = true)] string assetPath)
            => JObject.Parse(WhimTexApi.ImportImage(sourcePath, assetPath));

        [CliCommand("whimtex_document_render", "Render an unfiltered WhimTex document PNG to Temp/WhimTex for visual inspection.", MainThreadRequired = true)]
        public static JObject Render(
            [CliArg("assetPath", "Project-relative WhimTex .tiff path; existing legacy .asset is read-only", Required = true)] string assetPath,
            [CliArg("outputPath", "Project-relative Temp/WhimTex/*.png path", Required = true)] string outputPath,
            [CliArg("maxSize", "Longest output side, 1..4096")] int maxSize = 1024,
            [CliArg("overwrite", "Explicitly replace an existing preview PNG")] bool overwrite = false)
            => JObject.Parse(WhimTexApi.Render(assetPath, outputPath, maxSize, overwrite));

        [CliCommand("whimtex_document_migrate", "Copy a legacy .asset document to a new TIFF without changing the source asset.", MainThreadRequired = true)]
        public static JObject Migrate(
            [CliArg("sourcePath", "Project-relative legacy .asset path", Required = true)] string sourcePath,
            [CliArg("destinationPath", "Project-relative new .tiff path", Required = true)] string destinationPath,
            [CliArg("overwrite", "Explicitly replace an existing TIFF")] bool overwrite = false)
            => JObject.Parse(WhimTexApi.Migrate(sourcePath, destinationPath, overwrite));

        [CliCommand("whimtex_storage_inspect", "Read TIFF block metadata without materializing the document.", MainThreadRequired = true)]
        public static JObject InspectStorage(
            [CliArg("assetPath", "Project-relative WhimTex .tiff path", Required = true)] string assetPath)
            => JObject.Parse(WhimTexApi.InspectStorage(assetPath));

        [CliCommand("whimtex_document_validate", "Validate a WhimTex document without saving it.", MainThreadRequired = true)]
        public static JObject Validate(
            [CliArg("assetPath", "Project-relative WhimTex .tiff path; existing legacy .asset is read-only", Required = true)] string assetPath,
            [CliArg("render", "Also render a preview when structural validation succeeds")] bool render = false)
            => JObject.Parse(WhimTexApi.Validate(assetPath, render));

        [CliCommand("whimtex_fx_compile", "Compile a Shader FX preset or raw ApplyFX HLSL in Unity and return diagnostics without changing a document.", MainThreadRequired = true)]
        public static JObject CompileFX(
            [CliArg("presetPath", "Project-relative Assets/... or Packages/... .hlsl path, or absolute path inside the configured Shader FX preset folder")] string presetPath = null,
            [CliArg("source", "Raw HLSL implementing float4 ApplyFX(float2 uv, float4 color)")] string source = null,
            [CliArg("includeBasePath", "Optional project-relative or absolute Assets/Packages directory, configured user preset directory, or HLSL file; establishes the base for relative includes")] string includeBasePath = null)
            => JObject.Parse(WhimTexApi.CompileFX(presetPath, source, includeBasePath));

        [CliCommand("whimtex_document_status", "Report document disk identity, import and editor session state.", MainThreadRequired = true)]
        public static JObject Status(
            [CliArg("assetPath", "Project-relative WhimTex .tiff path; existing legacy .asset is read-only", Required = true)] string assetPath)
            => JObject.Parse(WhimTexApi.Status(assetPath));

        [CliCommand("whimtex_document_compare", "Compare two WhimTex documents by model/storage, optionally including a rendered preview.", MainThreadRequired = true)]
        public static JObject Compare(
            [CliArg("leftPath", "Project-relative first WhimTex .asset or .tiff path", Required = true)] string leftPath,
            [CliArg("rightPath", "Project-relative second WhimTex .asset or .tiff path", Required = true)] string rightPath,
            [CliArg("render", "Also compare rendered preview pixels")] bool render = false,
            [CliArg("maxSize", "Preview longest side when render=true, 1..4096")] int maxSize = 1024)
            => JObject.Parse(WhimTexApi.Compare(leftPath, rightPath, render, maxSize));

        [CliCommand("whimtex_document_recover", "Validate a staged TIFF and recover it to a new TIFF without deleting the staged source.", MainThreadRequired = true)]
        public static JObject Recover(
            [CliArg("sourcePath", "Assets-relative or absolute *.whimtex-tmp path", Required = true)] string sourcePath,
            [CliArg("destinationPath", "New project-relative Assets/.../*.tiff path", Required = true)] string destinationPath)
            => JObject.Parse(WhimTexApi.Recover(sourcePath, destinationPath));

        [CliCommand("whimtex_document_export", "Export a flattened WhimTex document to PNG, JPEG, TGA or EXR in Temp/WhimTex.", MainThreadRequired = true)]
        public static JObject Export(
            [CliArg("assetPath", "Project-relative WhimTex .tiff path; existing legacy .asset is read-only", Required = true)] string assetPath,
            [CliArg("outputPath", "Project-relative Temp/WhimTex/*.png|jpg|tga|exr path", Required = true)] string outputPath,
            [CliArg("maxSize", "Longest output side; 0 keeps the document canvas size, 1..4096 resizes")] int maxSize = 0,
            [CliArg("overwrite", "Explicitly replace an existing output file")] bool overwrite = false)
            => JObject.Parse(WhimTexApi.Export(assetPath, outputPath, maxSize, overwrite));

        [CliCommand("whimtex_headless_live", "Run an in-memory TIFF session without a window (begin/list/status/preview/render/complete/cancel). Complete saves; active sessions do not survive script reload.", MainThreadRequired = true)]
        public static JObject TiffLive(
            [CliArg("requestPath", "Absolute path to a TIFF live JSON request file", Required = true)] string requestPath)
            => JObject.Parse(WhimTexApi.TiffLiveFile(requestPath));
    }
}
#endif
