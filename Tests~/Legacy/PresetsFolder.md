# Shared preset folder preference

Run `node Tests~/PresetsFolder.test.mjs` for source contracts only; it does not execute Unity.
Run `UserSettingsCleanupSmoke.Run` through the connected Unity Editor for reload/reset,
folder validation and preservation of existing preset bytes. For manual checks in User Settings:

1. Default path is the user's local application-data `DCFApixels/WhimTex/Presets` directory.
   Historical directories are not searched automatically; select an existing library explicitly.
2. Choose a folder or type an absolute path (including spaces/non-ASCII characters).
   Close/reopen settings and recompile manually: the preference survives.
3. Open settings in another project: the same path is read from EditorPrefs.
4. Cancel Browse: the value stays unchanged. Relative paths and existing files are rejected;
   a not-yet-created absolute directory is allowed, but no directory is created by this setting.
5. Clear the field or press ↺: default path returns without moving/deleting files.
6. Reset Canvas View Appearance preserves the folder; Reset WhimTex Settings restores
   the default folder and leaves existing files untouched.
