// Run via Unity Pipeline eval_file. Transient meshes/documents only.
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var assembly = typeof(DCFApixels.SpriteEditor.TextureCompositor).Assembly;
var mapType = assembly.GetType("DCFApixels.SpriteEditor.UvIslandMap", true);
var meshes = new System.Collections.Generic.List<UnityEngine.Mesh>();
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
UnityEngine.Vector2 V(float x, float y) => new UnityEngine.Vector2(x, y);
UnityEngine.Mesh Mesh(UnityEngine.Vector2[] uv, int[] indices, UnityEngine.Vector3[] positions = null)
{
    var mesh = new UnityEngine.Mesh { hideFlags = UnityEngine.HideFlags.HideAndDontSave, name = "UV regression" };
    meshes.Add(mesh);
    if (positions == null)
    { positions = new UnityEngine.Vector3[uv.Length]; for (int i = 0; i < uv.Length; i++) positions[i] = new UnityEngine.Vector3(uv[i].x, uv[i].y, 0); }
    mesh.vertices = positions; mesh.uv = uv; mesh.triangles = indices;
    return mesh;
}
object Build(UnityEngine.Mesh mesh, int channel = 0, int submesh = -1) => mapType.GetMethod("Build", flags).Invoke(null, new object[] { mesh, channel, submesh });
System.Collections.IList Islands(object map) => (System.Collections.IList)mapType.GetField("islands", flags).GetValue(map);
int Edges(object map)
{
    int count = 0;
    foreach (object island in Islands(map)) count += ((System.Collections.IList)island.GetType().GetField("edges", flags).GetValue(island)).Count;
    return count;
}
int Pick(object map, float x, float y) => (int)mapType.GetMethod("Pick", flags).Invoke(map, new object[] { V(x, y) });
byte[] Raster(object map, int island = 0, int w = 32, int h = 32) => (byte[])mapType.GetMethod("Rasterize", flags).Invoke(map, new object[] { island, w, h });
void ExpectError(System.Action action, string label)
{
    bool thrown = false;
    try { action(); } catch (System.Reflection.TargetInvocationException) { thrown = true; }
    Check(thrown, label);
}
try
{
    var squareUv = new[] { V(0,0), V(1,0), V(1,1), V(0,1) };
    var square = Mesh(squareUv, new[] {0,1,2,0,2,3});
    object map = Build(square);
    Check(Islands(map).Count == 1 && Edges(map) == 4, "Quad island hides diagonal");
    foreach (byte value in Raster(map)) Check(value == 255, "Square fully selected, no diagonal cracks");
    Check(Pick(map,.2f,.8f) == 0 && Pick(map,.8f,.2f) == 0, "Pick either triangle");
    Check(Pick(map,-.1f,.5f) == -1 && Pick(map,float.NaN,.5f) == -1, "Outside/NaN miss");
    var marker = (UnityEngine.Vector2)Islands(map)[0].GetType().GetField("marker", flags).GetValue(Islands(map)[0]);
    Check((marker - V(.5f,.5f)).sqrMagnitude < .00001f, "Marker at island center");
    var split = Mesh(new[] {V(0,0),V(1,0),V(1,1),V(0,0),V(1,1),V(0,1)}, new[] {0,1,2,3,4,5});
    Check(Islands(Build(split)).Count == 1 && Edges(Build(split)) == 4, "Hard-normal split vertices stitched by position + UV");
    var seam = split.uv; seam[3] += V(.1f,0); seam[4] += V(.1f,0); seam[5] += V(.1f,0); split.uv = seam;
    Check(Islands(Build(split)).Count == 2 && Edges(Build(split)) == 6, "UV discontinuity keeps seam");
    var overlapUv = new[] {V(0,0),V(1,0),V(0,1),V(0,0),V(1,0),V(0,1)};
    var positions = new UnityEngine.Vector3[6];
    for(int i=0;i<6;i++) positions[i] = new UnityEngine.Vector3(overlapUv[i].x, overlapUv[i].y, i<3?0:1);
    var overlap = Mesh(overlapUv, new[] {0,1,2,3,4,5}, positions);
    var overlapMap = Build(overlap);
    Check(Islands(overlapMap).Count == 2, "Overlapping disconnected surfaces stay independent");
    Check(Pick(overlapMap,.2f,.2f) == 0, "Overlapping hit has stable first-island ordering");
    var ra = Raster(overlapMap,0); var rb = Raster(overlapMap,1);
    for(int i=0;i<ra.Length;i++) Check(ra[i] == rb[i], "Coincident islands necessarily share selected pixels");
    var folded = Mesh(new[]{V(0,0),V(1,0),V(.5f,1),V(.5f,.5f)}, new[]{0,1,2,1,0,3});
    Check(Islands(Build(folded)).Count == 2, "Folded UV faces not joined across overlapping edge");
    square.subMeshCount = 2; square.SetTriangles(new[]{0,1,2},0); square.SetTriangles(new[]{0,2,3},1);
    Check(Islands(Build(square)).Count == 1 && Edges(Build(square)) == 4, "Continuous island crosses material boundary");
    Check(Edges(Build(square,0,1)) == 3 && Pick(Build(square,0,1),.8f,.2f) == -1, "Submesh filter");
    ExpectError(()=>Build(square,0,2), "Invalid submesh has diagnostic");
    ExpectError(()=>Build(square,1), "Missing UV channel has diagnostic");
    square.uv2 = squareUv;
    Check(Islands(Build(square,1)).Count == 1, "Alternate UV channel");
    square.UploadMeshData(true);
    Check(!square.isReadable && Edges(Build(square)) == 4, "Editor read-only access handles non-readable imported meshes");
    var quad = Mesh(squareUv, new[]{0,1,2});
    quad.SetIndices(new[]{0,1,2,3}, UnityEngine.MeshTopology.Quads,0);
    Check(Edges(Build(quad)) == 4, "Native quad topology");
    var ring = Mesh(new[]{V(0,0),V(1,0),V(1,1),V(0,1),V(.25f,.25f),V(.75f,.25f),V(.75f,.75f),V(.25f,.75f)},
        new[]{0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7});
    var ringMap=Build(ring); var ringPixels=Raster(ringMap);
    Check(Islands(ringMap).Count==1 && Edges(ringMap)==8,"Ring has outer and hole contours only");
    for(int y=0;y<32;y++) for(int x=0;x<32;x++) Check(ringPixels[y*32+x]==((x>=8&&x<24&&y>=8&&y<24)?0:255),"Hole preserved in selection");
    Check(Pick(ringMap,.5f,.5f)==-1 && Pick(ringMap,.1f,.5f)==0,"Hole is not clickable");
    marker=(UnityEngine.Vector2)Islands(ringMap)[0].GetType().GetField("marker",flags).GetValue(Islands(ringMap)[0]);
    Check(Pick(ringMap,marker.x,marker.y)==0,"Concave/hollow island marker remains inside surface");
    var nonmanifold=Mesh(new[]{V(0,0),V(1,0),V(.3f,1),V(.4f,-1),V(.6f,.8f)},new[]{0,1,2,1,0,3,0,1,4});
    Check(Islands(Build(nonmanifold)).Count==3 && Edges(Build(nonmanifold))==9,"Non-manifold edge retained for every face");
    var clipped=Mesh(new[]{V(-1,-1),V(2,-1),V(2,2),V(-1,2)},new[]{0,1,2,0,2,3});
    foreach(byte value in Raster(Build(clipped))) Check(value==255,"Out-of-tile geometry clipped to canvas");
    var empty=Mesh(new[]{V(0,0),V(0,0),V(0,0)},new[]{0,1,2});
    ExpectError(()=>Build(empty),"Degenerate UV diagnostic");
    ExpectError(()=>Raster(map,0,8192,8192),"Selection memory limit enforced");
    const int n=96;
    var gridUv=new UnityEngine.Vector2[(n+1)*(n+1)];
    for(int y=0;y<=n;y++) for(int x=0;x<=n;x++) gridUv[y*(n+1)+x]=V(x/(float)n,y/(float)n);
    var gridIndices=new int[n*n*6];
    for(int y=0,k=0;y<n;y++) for(int x=0;x<n;x++) {int a=y*(n+1)+x,b=a+1,c=a+n+2,d=a+n+1;gridIndices[k++]=a;gridIndices[k++]=b;gridIndices[k++]=c;gridIndices[k++]=a;gridIndices[k++]=c;gridIndices[k++]=d;}
    var dense=Mesh(gridUv,gridIndices);
    var timer=System.Diagnostics.Stopwatch.StartNew();
    object denseMap=Build(dense); timer.Stop(); double buildMs=timer.Elapsed.TotalMilliseconds;
    Check(Islands(denseMap).Count==1 && Edges(denseMap)==n*4,"Dense mesh keeps only island perimeter");
    timer.Restart();
    for(int i=0;i<1000;i++) Check(Pick(denseMap,(i%97+.123f)/97,(i%89+.456f)/89)==0,"Spatial picking on dense island");
    timer.Stop(); double pickMs=timer.Elapsed.TotalMilliseconds;
    foreach(byte value in Raster(denseMap,0,257,129)) Check(value==255,"Dense triangulation has no selection cracks");
    var doc=UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
    doc.hideFlags=UnityEngine.HideFlags.HideAndDontSave;
    DCFApixels.SpriteEditor.TextureCompositor copy=null;
    try
    {
        typeof(DCFApixels.SpriteEditor.TextureCompositor).GetField("uvReferenceMesh",flags).SetValue(doc,dense);
        typeof(DCFApixels.SpriteEditor.TextureCompositor).GetField("uvReferenceChannel",flags).SetValue(doc,1);
        copy=UnityEngine.Object.Instantiate(doc);
        Check(typeof(DCFApixels.SpriteEditor.TextureCompositor).GetField("uvReferenceMesh",flags).GetValue(copy)==dense,"Document clone keeps mesh reference");
        Check((int)typeof(DCFApixels.SpriteEditor.TextureCompositor).GetField("uvReferenceChannel",flags).GetValue(copy)==1,"Document clone keeps UV channel");
    }
    finally { if(copy!=null) UnityEngine.Object.DestroyImmediate(copy); UnityEngine.Object.DestroyImmediate(doc); }
    return $"UV islands: {checks} checks passed; dense 18,432 triangles built in {buildMs:0.##} ms, 1,000 reflected picks {pickMs:0.##} ms. No scene/asset writes.";
}
finally { foreach(var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh); }
