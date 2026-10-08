// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import fs from 'node:fs';
const context = new TestContext("UvIslands source/reference tests");
const assert = context.assert;
context.case("UvIslands original assertion inputs and source contracts", async () => {
  const read = p => fs.readFileSync(new URL('../../../src/' + p, import.meta.url), 'utf8');
  const topology = read('UvIslandMap.cs');
  const uv = read('WhimTexWindow.Uv.cs');
  const area = read('WhimTexWindow.AreaSelectionView.cs');
  const tools = read('WhimTexWindow.Tools.cs');
  const picker = read('WhimTexWindow.ShapePicker.cs');
  assert.match(topology, /MeshUtility\.AcquireReadOnlyMeshData/);
  assert.match(topology, /position\.Equals\(other\.position\) && uv\.Equals\(other\.uv\)/);
  assert.match(topology, /edge\.count != 2/);
  assert.match(topology, /Cross\(edge\.b - edge\.a, edge\.otherOpposite - edge\.a\) >= 0/);
  assert.match(topology, /Check\(grid\[Cell\(uv\.y\)/);
  assert.match(topology, /ValidateSize\(\)/);
  assert.doesNotMatch(topology, /SetUVs|SetVertices|SetIndices|SaveAndReimport|Graphics\./);
  assert.match(uv, /pickingMode = PickingMode\.Ignore/);
  assert.match(uv, /s\.Set\(uvMap\.Rasterize\(island, s\.Width, s\.Height\), combine\)/);
  assert.match(uv, /toolkitCanvas\.ToCanvas\(point\)/);
  assert.match(uv, /toolkitCanvas\.ToView/);
  assert.match(uv, /ReferenceEquals\(mesh, uvCachedMesh\)/);
  assert.match(uv, /hash == uvCachedHash\) return;/);
  assert.match(uv, /schedule\.Execute\(RefreshUvReference\)\.Every\(750\)/);
  assert.match(uv, /\+\+batch == 512/);
  const draw = uv.slice(uv.indexOf('private void DrawUvOverlay'));
  assert.doesNotMatch(draw, /GetTriangles|AcquireReadOnlyMeshData|Rasterize|MarkDirtyRepaint|uvMap =[^=]/);
  assert.match(area, /if \(owner\.IsUvSelectionTool\)[\s\S]*?owner\.SelectUvIsland[\s\S]*?return;/);
  assert.match(uv, /canvasTool == CanvasTool\.UvIslandSelect/);
  assert.match(tools, /tool == CanvasTool\.UvIslandSelect/);
  assert.match(read('WhimTexWindow.ContextTools.cs'), /"uvIslandSelectTool", CanvasTool\.UvIslandSelect/);
  assert.doesNotMatch(picker, /"UV Island"/);
  assert.match(read('WhimTexDocument.cs'), /\[SerializeField, HideInInspector\] internal Mesh uvReferenceMesh/);
  assert.match(read('WhimTexWindow.cs'), /private void OnDisable\(\)[\s\S]*?uvMap = null/);

});
await finish(context);

