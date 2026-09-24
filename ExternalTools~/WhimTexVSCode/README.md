# WhimTex FX Language Support

WhimTex FX source uses HLSL with `// @param` metadata directives. This bundled extension adds WhimTex directive highlighting, completions, and structural diagnostics to VS Code's built-in HLSL language support. It does not register a separate language mode or replace the HLSL grammar. Saving an FX document requests Apply in WhimTex. It can run in Restricted Mode because it only analyzes the open document and never executes workspace code.

Diagnostics cover directive spelling, supported parameter declaration shapes, duplicate parameter names, conditional blocks, and group boundaries. Unity remains the authority for shader compilation and parameter semantics.
