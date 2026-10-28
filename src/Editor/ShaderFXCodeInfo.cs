using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXCodeInfo : VisualElement
    {
        private readonly Label locality = new Label { name = "shaderFXPixelLocal", enableRichText = false };
        private readonly Label context = new Label { name = "shaderFXPixelLocalContext", enableRichText = false };
        private readonly Label editingState = new Label { name = "shaderFXReferenceState", enableRichText = false, pickingMode = PickingMode.Ignore };
        private readonly bool dark = EditorGUIUtility.isProSkin;
        private readonly ShaderFXCodeField code;
        private readonly List<string> snippets = new List<string>();
        internal VisualElement PixelLocalStatus { get; }

        internal ShaderFXCodeInfo(ShaderFXCodeField code)
        {
            this.code = code;
            AddToClassList("whimtex-fx-code-info");
            var row = PixelLocalStatus = new VisualElement();
            row.AddToClassList("whimtex-fx-pixel-local");
            row.EnableInClassList("whimtex-fx-pixel-local--light", !dark);
            var title = new Label("Pixel Local") { enableRichText = false };
            title.AddToClassList("whimtex-fx-pixel-local-title");
            locality.AddToClassList("whimtex-fx-pixel-local-value");
            context.AddToClassList("whimtex-fx-pixel-local-context");
            row.Add(title); row.Add(locality); row.Add(context);

            var reference = new Foldout { text = "Shader Inputs", name = "shaderFXInputs", value = false };
            reference.RegisterCallback<TooltipEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            reference.AddToClassList("whimtex-fx-reference");
            Add(reference);
            editingState.AddToClassList("whimtex-fx-reference-readonly");
            editingState.AddToClassList("whimtex-shader-fx-hidden");
            reference.Add(editingState);

            var entry = Section(reference, "Entry Points", true);
            Item(entry, Code("float4 ApplyFX(float4 color)") + "\nIncoming color without a coordinate parameter.");
            Item(entry, Code("float4 ApplyFX(PinnedUV uv, float4 color)") + "\nCoordinates of the current pixel, for local or spatial effects.");
            Text(entry, "Define exactly one entry point. " + Code("uv") + " is normalized; " + Code("color") +
                " is straight, linear RGBA at the current pixel. " + Code("PinnedUV") + " is immutable. Arithmetic, swizzles and conversion to " +
                Code("float2") + " produce numeric coordinates; conversion back is forbidden. Copy or pass an unchanged " + Code("PinnedUV") + " to retain locality.");

            var textures = Section(reference, "Texture Reads");
            Item(textures, Code("SampleInput(uv)") + " — primary read of the incoming image, including previous FX.");
            Item(textures, Code("SampleTexture(_Mask, uv)") + " — primary read of a declared texture, including Texture, Layer, Self and None masks. Self is the image before this FX; None is transparent black.");
            Text(textures, "Locality follows the coordinate type at each reachable image read: " + Code("PinnedUV") + " is local; " + Code("float2") +
                " or explicit tiling is spatial, including inside helpers. Numeric UV math without image reads remains local. Different texture resolutions use the same normalized coordinates.");
            Item(textures, Code("SampleInput(uv, tiling)") + " — spatial addressing: 0 Clamp, 1 Repeat, 2 Mirror, 3 Clip.");
            Item(textures, Code("SampleInputLod(uv, 0)") + " — incoming image at an explicit mip level; 0 is the supplied texture's base level, including reduced previews.");
            Item(textures, Code("SampleTextureLod(_Mask, uv, 0)") + " — explicit mip level of a declared texture, with its own UV mapping. Both Lod functions are spatial, including with PinnedUV or LOD 0, and work in dynamic loops without automatic LOD derivatives. They do not generate mipmaps.");
            Text(textures, Code("_MainTex") + " is the incoming " + Code("WhimTexTexture") + ". " + Code("SampleTexture(_MainTex, uv)") +
                " is equivalent to " + Code("SampleInput(uv)") + " and remains pixel-local when " + Code("uv") + " is " + Code("PinnedUV") + ".");
            var bypassNote = Text(textures, "Low-level bypass paths: " + Code("tex2D") + ", " + Code(".Sample") + ", " + Code(".Load") +
                ". Their use is not recommended; prefer " + Code("SampleInput") + " and " + Code("SampleTexture") + ".");
            bypassNote.AddToClassList("whimtex-fx-reference-note");

            var dimensions = Section(reference, "Coordinates & Size");
            Item(dimensions, Code("_InputSize") + " — render width, height, 1/width, 1/height.");
            Item(dimensions, Code("_MainTex_TexelSize") + " — complete incoming image at the render scale: 1/width, 1/height, width, height, regardless of the stored region. Reading this metadata is not a texture sample.");
            Item(dimensions, Code("_CanvasSize") + " — full-resolution canvas dimensions in the same format.");
            Item(dimensions, Code("_RenderScale") + " — full-size canvas pixels per preview pixel; 1 at full resolution.");
            Item(dimensions, Code("_PixelScale") + " — document logical-pixel multiplier; 1 by default. Numeric " + Code("@param px") + " values are already multiplied once.");
            Item(dimensions, Code("LayerToLocal(uv)") + " — convert canvas coordinates to the layer's local space; accepts and returns " + Code("float2") + ".");

            var parameters = Section(reference, "Parameters & Helpers");
            Item(parameters, "Declare controls with " + Code("// @param") + "; do not redeclare their generated uniforms. A " + Code("texture2D") +
                " parameter generates a " + Code("WhimTexTexture") + " and " + Code("_Name_TexelSize") + " (1/width, 1/height, width, height).");
            Item(parameters, Code("// @param rgba toggle(float4) _Channels = (1, 1, 1, 0)") +
                "\nFour switches with R/G/B/A labels; the HLSL value is float4 with 0/1 components.");
            Text(parameters, Code("toggle(type)") + ", " + Code("color(type)") + ", " + Code("enum(type)") + " and " + Code("seed(type)") +
                " separate the control from its numeric type. Defaults: float, float4, float and int respectively. Toggle/color support bool, int, uint, half and float with 1–4 components; enum accepts numeric scalars; seed accepts int, uint or float. " +
                Code("rgba") + " changes component labels only. Use an explicit component in vector conditions, for example " + Code("// @enableif _Channels.a == 1") + ".");
            Item(textures, Code("float4 ReadMask(WhimTexTexture source, PinnedUV uv) { return SampleTexture(source, uv); }") +
                "\nPass the texture view to your own helpers; its coordinate mapping travels with it.");
            Text(textures, Code("_Mask.texelSize") + " describes the complete input at its render scale, even when only a region is stored. " +
                Code("SampleTexture") + " maps image UV into that region automatically. Texture views are read-only: copy or pass them, but do not construct or modify them.");
            Item(parameters, Code("_Name_Sample(t)") + " — generated curve or gradient lookup. Curves return " + Code("float") +
                "; gradients return " + Code("float4") + ". These lookups are independent of image-neighbor reads.");
            Item(parameters, Code("_Name_ToLocal(uv)") + " / " + Code("_Name_ToInput(uv)") + " — generated " + Code("transform2D") +
                " helpers, accepting " + Code("float2") + ". Transformed image reads require the spatial entry point.");

            var libraries = Section(reference, "Libraries & Output");
            Item(libraries, Code("#include") + " accepts Assets/Packages paths or paths relative to the containing file. Inline FX resolve from the document folder, or Assets before the first save.");
            Item(libraries, Code("UnityCG.cginc") + ", FastNoiseLite and the shared HDR color helpers are included automatically. " +
                Code("SpriteEncode") + " / " + Code("SpriteDecode") + " convert between linear and display-encoded RGB; " +
                Code("WhimTexHue") + " / " + Code("WhimTexHueLinear") + " read the shared hue lookup.");
            Text(libraries, "Return straight RGBA, not premultiplied color. Layer blending and opacity are applied afterwards. Names beginning with " + Code("_WhimTex_") + " are reserved.");
        }

        internal void Refresh(ShaderFX effect, bool pendingChanges)
        {
            editingState.text = effect.IsCatalogLinked ? "Linked source is read-only. Choose Embed Copy to insert or edit code here." :
                code.isReadOnly ? "Code is read-only." : "";
            editingState.EnableInClassList("whimtex-shader-fx-hidden", editingState.text.Length == 0);
            bool usable = !effect.IsUnavailable;
            locality.text = effect.LastApplyFailed ? "Unavailable" : !effect.HasAppliedShader ? "Not compiled" : effect.IsPixelLocal ? "Yes" : "No";
            locality.EnableInClassList("whimtex-fx-pixel-local-value--yes", usable && effect.IsPixelLocal);
            context.text = usable ? pendingChanges ? "Compiled shader · draft changed" : "Compiled shader" : "";
            locality.tooltip = effect.LastApplyFailed ? "Compilation failed. No usable shader is available; this FX is skipped." :
                !effect.HasAppliedShader ? "Compile the FX to determine pixel locality." : effect.IsPixelLocal ?
                "The compiled shader has a confirmed pixel-local contract. Draft edits do not change this status until compilation." :
                "The compiled shader uses a spatial signature or its pixel-local contract could not be confirmed. This does not mean the shader is invalid.";
        }

        private string Code(string text)
        {
            int index = snippets.Count;
            snippets.Add(text);
            return "<link=\"whimtex-code-" + index + "\"><mark=" + (dark ? "#FFFFFF12" : "#0000000C") + "><color=" +
                (dark ? "#C1DDEB" : "#244F67") + "><b>" + text + "</b></color></mark></link>";
        }

        private static VisualElement Section(VisualElement parent, string title, bool first = false)
        {
            var section = new VisualElement();
            section.AddToClassList("whimtex-fx-reference-section");
            section.EnableInClassList("whimtex-fx-reference-section--first", first);
            var heading = new Label(title) { enableRichText = false };
            heading.AddToClassList("whimtex-fx-reference-heading");
            section.Add(heading); parent.Add(section);
            return section;
        }

        private void Item(VisualElement parent, string text)
        {
            var row = new VisualElement();
            row.AddToClassList("whimtex-fx-reference-item");
            var bullet = new Label("•") { enableRichText = false, pickingMode = PickingMode.Ignore };
            bullet.AddToClassList("whimtex-fx-reference-bullet");
            row.Add(bullet);
            Text(row, text);
            parent.Add(row);
        }

        private int SnippetIndex(string id)
        {
            const string prefix = "whimtex-code-";
            return id != null && id.StartsWith(prefix, System.StringComparison.Ordinal) &&
                int.TryParse(id.Substring(prefix.Length), out int index) && index >= 0 && index < snippets.Count ? index : -1;
        }

        private Label Text(VisualElement parent, string text)
        {
            var label = new ReferenceLabel(this, text);
            parent.Add(label);
            return label;
        }

        private sealed class ReferenceLabel : Label
        {
            private readonly ShaderFXCodeInfo owner;
            private readonly string original;
            private int hovered = -1, pressed = -1, released = -1, lastClicked = -1;
            private int pendingClickCount, previousClicked = -1;

            internal ReferenceLabel(ShaderFXCodeInfo owner, string text) : base(text)
            {
                this.owner = owner;
                original = text;
                enableRichText = true;
                focusable = false;
                AddToClassList("whimtex-fx-reference-text");
                RegisterCallback<PointerDownEvent>(evt =>
                {
                    pressed = released = -1;
                    pendingClickCount = 0;
                    owner.code.RememberSelection();
                    panel?.focusController?.IgnoreEvent(evt);
                }, TrickleDown.TrickleDown);
                RegisterCallback<MouseDownEvent>(evt => panel?.focusController?.IgnoreEvent(evt), TrickleDown.TrickleDown);
                RegisterCallback<PointerDownLinkTagEvent>(evt => { if (evt.button == 0) pressed = owner.SnippetIndex(evt.linkID); });
                RegisterCallback<PointerUpLinkTagEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    released = owner.SnippetIndex(evt.linkID);
                    if (ResolveClick()) evt.StopPropagation();
                });
                RegisterCallback<ClickEvent>(OnClick);
                RegisterCallback<PointerOverLinkTagEvent>(evt => Hover(owner.SnippetIndex(evt.linkID)));
                RegisterCallback<PointerMoveLinkTagEvent>(evt => Hover(owner.SnippetIndex(evt.linkID)));
                RegisterCallback<PointerOutLinkTagEvent>(_ => Hover(-1));
                RegisterCallback<PointerLeaveEvent>(_ => Hover(-1));
                RegisterCallback<DetachFromPanelEvent>(_ => Hover(-1));
            }

            private void OnClick(ClickEvent evt)
            {
                if (evt.button != 0)
                    return;
                previousClicked = lastClicked;
                lastClicked = -1;
                pendingClickCount = evt.clickCount;
                if (ResolveClick()) evt.StopPropagation();
            }

            private bool ResolveClick()
            {
                if (pendingClickCount == 0 || released < 0) return false;
                int index = pressed == released ? released : -1;
                bool activate = index >= 0 && index == previousClicked && pendingClickCount > 1 && (pendingClickCount & 1) == 0;
                lastClicked = index;
                pendingClickCount = 0;
                return activate && owner.code.UseReference(owner.snippets[index]);
            }

            private void Hover(int index)
            {
                if (!owner.code.CanUseReference) index = -1;
                if (hovered == index) return;
                hovered = index;
                EnableInClassList("whimtex-fx-reference-text--link-hover", index >= 0);
                string text = original;
                if (index >= 0)
                {
                    string prefix = "<link=\"whimtex-code-" + index + "\"><mark=";
                    text = text.Replace(prefix + (owner.dark ? "#FFFFFF12" : "#0000000C"),
                        prefix + (owner.dark ? "#FFFFFF30" : "#00000020"));
                }
                ((INotifyValueChanged<string>)this).SetValueWithoutNotify(text);
            }
        }
    }
}
