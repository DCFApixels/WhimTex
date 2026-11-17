using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class TiledDrawingHistoryTests
{
    public static string RunGpuUndoEdges() => TestContext.Run("Partial GPU history ownership, base, loss and branching", test =>
    {
        ReusePreflight(test); using var disk = new PaintTileHistoryTests.DiskScope();
        using var f = new ReuseFixture();
        var baseline = f.Capture();
        f.Stroke(new Color(3.5f, -.5f, .7f, .55f)); var first = f.Capture();
        f.Stroke(new Color(-.4f, 4.5f, .25f, .65f)); var second = f.Capture();
        Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
        Call(f.document, "ResetUndoTrackingAfterLoad");
        var surface = (TiledPaintSurface)Get(f.drawing, "tiledSurface");
        using (var lease = surface.AcquireSnapshot())
        {
            var snapshotBytes = GpuLeaseBytes(lease);
            f.Undo(); Exact(test, first, f.Capture(), "Partial Undo exact masked signed HDR");
            test.True(ReferenceEquals(surface, Get(f.drawing, "tiledSurface")), "Native Undo keeps runtime");
            ExactTiles(test, snapshotBytes, GpuLeaseBytes(lease), "Leased pre-Undo pixels remain immutable");
        }
        f.Undo(); Exact(test, baseline, f.Capture(), "First stroke Undo restores dense base and removes outside override");
        surface = (TiledPaintSurface)Get(f.drawing, "tiledSurface");
        test.Equal(1, surface.TileCount, "Only dense base remains after removing first stroke");
        f.Redo(); Exact(test, first, f.Capture(), "Recreated sparse overrides");
        f.Stroke(new Color(5.5f, -.6f, .4f, .45f)); var branch = f.Capture();
        f.Undo(); Exact(test, first, f.Capture(), "New stroke after partial restore keeps exact Before");
        f.Redo(); Exact(test, branch, f.Capture(), "Branch Redo");
        Undo.PerformRedo(); Exact(test, branch, f.Capture(), "Old future remains discarded");
        Call(f.drawing, "ReleaseTiledRuntime"); f.Undo(); Exact(test, first, f.Capture(), "Missing runtime rebuild fallback");
        f.Redo(); Exact(test, branch, f.Capture(), "Redo after runtime reconstruction");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        f.Stroke(new Color(2f, -.3f, 4f, .4f), false);
        f.Stroke(new Color(-.2f, 1.5f, 3f, .7f), false);
        Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
        Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
        var grouped = f.Capture();
        f.Undo(); Exact(test, branch, f.Capture(), "Grouped Undo keeps final values for repeated keys");
        f.Redo(); Exact(test, grouped, f.Capture(), "Grouped Redo keeps final values for repeated keys");
        surface = (TiledPaintSurface)Get(f.drawing, "tiledSurface");
        using (var lease = surface.AcquireSnapshot())
            foreach (var tile in lease.Tiles) { tile.Texture.Release(); break; }
        f.Undo(); Exact(test, branch, f.Capture(), "Lost GPU page falls back to authoritative CPU storage");
        test.True(!ReferenceEquals(surface, Get(f.drawing, "tiledSurface")), "Lost runtime replaced");
    });
    static Dictionary<Vector2Int, byte[]> GpuLeaseBytes(TiledPaintSnapshot snapshot)
    {
        var result = new Dictionary<Vector2Int, byte[]>(); var previous = RenderTexture.active;
        var rt = new RenderTexture(256,256,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
        var read = new Texture2D(256,256,TextureFormat.RGBAHalf,false,true);
        try
        {
            rt.Create();
            foreach (var tile in snapshot.Tiles)
            {
                var r=tile.PhysicalRect; RenderTexture.active=null;
                Graphics.CopyTexture(tile.Texture,0,0,r.x,r.y,256,256,rt,0,0,0,0);
                RenderTexture.active=rt; read.ReadPixels(new Rect(0,0,256,256),0,0,false);
                result.Add(tile.Key, read.GetRawTextureData<byte>().ToArray());
            }
            return result;
        }
        finally { RenderTexture.active=previous; Object.DestroyImmediate(rt); Object.DestroyImmediate(read); }
    }
    public static string RunGpuPatchBudget() => TestContext.Run("GPU tile patches respect budget and immutable snapshots", test =>
    {
        var seed = new Texture2D(256,256,TextureFormat.RGBAHalf,false,true);
        var values = new Color[256*256]; Array.Fill(values,new Color(-.5f,3f,.4f,.6f)); seed.SetPixels(values); seed.Apply(false,false);
        var frame = new TiledPaintFrame(256,256,256,256,ProjectiveMatrix.Identity);
        var shaders = new TiledPaintShaders(Shader.Find("Hidden/WhimTex/PaintBrush"), Shader.Find("Hidden/WhimTex/TiledPaintCommit"),
            AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintCommit.compute"));
        try
        {
            using var surface = new TiledPaintSurface(frame,shaders,new TiledPaintOptions { GpuBudgetBytes=16L*1024*1024, IdleBudgetBytes=0 });
            var a=new Vector2Int(-1,0); var b=new Vector2Int(0,0); var r=new RectInt(0,0,256,256);
            var imports=new List<TiledPaintImportTile>();
            for(int i=-1;i<31;i++) imports.Add(new TiledPaintImportTile(new Vector2Int(i,0),seed,r));
            using (surface.ImportTiles(imports)) {}
            using var before = surface.AcquireSnapshot(); var expected=GpuLeaseBytes(before);
            bool failed=false;
            try { using var delta=surface.ImportTiles(new[]{new TiledPaintImportTile(a,seed,r)},false,new[]{b}); }
            catch (TiledPaintMemoryLimitException) { failed=true; }
            test.True(failed,"Copy-on-write refuses to exceed existing GPU budget");
            using (var state=surface.AcquireSnapshot()) ExactTiles(test,expected,GpuLeaseBytes(state),"Budget failure is atomic");
            test.Equal(16L*1024*1024,surface.AllocatedBytes,"Budget is unchanged");
            using (surface.ImportTiles(Array.Empty<TiledPaintImportTile>(),false,new[]{a})) {}
            test.Equal(31,surface.TileCount,"Patch removes an absent tile");
            ExactTiles(test,expected,GpuLeaseBytes(before),"Removed tile remains in leased snapshot");
            failed=false;
            try { using var delta=surface.ImportTiles(new[]{new TiledPaintImportTile(b,seed,r)},false,new[]{b}); }
            catch (ArgumentException) { failed=true; }
            test.True(failed,"Conflicting patch rejected before mutation");
            test.Equal(31,surface.TileCount,"Invalid patch retains source");
            using var packed = new TiledPaintSurface(new TiledPaintFrame(1024,1024,1024,1024,ProjectiveMatrix.Identity),
                shaders,new TiledPaintOptions { GpuBudgetBytes=32L*1024*1024, IdleBudgetBytes=0 });
            using (packed.ImportTiles(imports)) {}
            var removed = new List<Vector2Int>();
            for(int i=-1;i<30;i++) removed.Add(new Vector2Int(i,0));
            using (packed.ImportTiles(Array.Empty<TiledPaintImportTile>(),false,removed)) {}
            // The first page is empty, the second has a surviving slot. A new
            // version belongs in that hole so the first page can be reclaimed.
            using (packed.ImportTiles(new[]{new TiledPaintImportTile(new Vector2Int(31,0),seed,r)})) {}
            packed.TrimIdle();
            test.Equal(8L*1024*1024,packed.AllocatedBytes,"Fill partial pages before consuming idle capacity");
            using var packedState = packed.AcquireSnapshot();
            var packedBytes=GpuLeaseBytes(packedState);
            test.Equal(2,packedBytes.Count,"Packing retains both source tiles");
            foreach(var tile in packedBytes) Exact(test,seed.GetRawTextureData<byte>().ToArray(),tile.Value,"Packed signed HDR pixels "+tile.Key);
        }
        finally { Object.DestroyImmediate(seed); }
    });
}
