# Shared preset libraries

Run `SharedPresetLibrariesSmoke.cs` through Unity Pipeline `eval_file`, followed by
`ShaderFXCatalogSmoke.cs` and `node Tests~/BrushPresets.test.mjs`.
The native test creates only a unique Temp fixture, restores the preset-folder preference,
destroys transient textures/effects, and does not save project assets or user documents.
Project brush enumeration is exercised with an injected index, not a real Assets fixture.

Manual checks:

- Save a textured brush into Assets with **Save to Project…**. Reopen the brush selector,
  select it under Project, and confirm the preview stroke and settings match.
- Copy a `.sebrush` into a nested Assets folder. Confirm discovery after Unity imports it.
- Put identically named brushes in User and Project folders; both must remain selectable.
- In a Shader FX editor change several defaults, including HDR color and Transform 2D.
  Save HLSL in the user ShaderFX folder, then add it through **+ Preset → User**.
  Confirm the defaults, code, and rendering; the original instance should not be modified.
- Save to Assets instead: the preset must follow project source edits as before.
- Replace/delete a user HLSL file outside Unity: reopen the menu to see the updated list.
  Existing user-preset instances must retain their own code and values.
- Missing texture-default assets in another project fall back to white. Transfer their
  original assets and `.meta` files, or assign replacements before saving another preset.
