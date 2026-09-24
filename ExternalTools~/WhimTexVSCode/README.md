# WhimTex FX Language Support

WhimTex FX source uses HLSL with `// @param` metadata directives. This bundled extension adds directive highlighting, completions and validation to VS Code's HLSL language support. Types, modifiers, parameter names, numbers, colors, enum options, ranges and operators are highlighted too; tooltip comments retain comment styling. It does not register a separate language mode or replace the HLSL grammar.

Diagnostics cover optional defaults and tooltips, color/gradient defaults, curves, transforms, texture-reference syntax, numeric bounds, enum options, compatible repeated controls, conditions, groups and former-name aliases. The catalog marker is recognized. Asset existence, generated shader identifiers, includes and shader compilation are still checked by Unity.

Type `// @if` or `// @group` and press **Tab** to insert a block with its closing directive. `//@if` and `//@group` work too. Tab moves through the condition's parameter, comparison and value, or the group's title, then into the block; **Shift+Tab** moves back through placeholders. A group title can also contain `; _Parameter` to bind its header control. These are ordinary HLSL snippets, also available through **Insert Snippet** (insert after `// `). The extension defaults HLSL's `editor.tabCompletion` to `onlySnippets`, without writing user settings. If your settings override it, choose `onlySnippets` for direct Tab expansion, or select the snippet through **Ctrl+Space**.

Saving a working file opened from WhimTex requests Apply by writing a neighboring `.apply` signal; ordinary HLSL files do not send this signal. Save the TIFF separately in WhimTex. The extension supports Restricted Mode and never executes workspace code.

WhimTex checks the bundled VSIX contents when opening code and reinstalls changed bundles in its isolated profile. If that VS Code window is already running, use **Developer: Reload Window** after installation to load the updated extension.

For package development, rebuild the bundled archive with `node ExternalTools~/WhimTexVSCode/build-vsix.mjs` from the package root. Add `--check` to verify it without writing. Tests share metadata cases with the Unity parser and validate all built-in FX; the optional TextMate integration test uses an existing VS Code installation via `WHIMTEX_VSCODE_APP`.
