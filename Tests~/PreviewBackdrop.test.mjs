import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
const ui = read('src/TextureCompositorWindow.UI.cs');
const uss = read('src/WhimTexSplitView.uss');
assert.ok(!/toolkitPreviewCanvas\.style\.margin(?:Left|Right|Top|Bottom)\s*=/.test(ui));
assert.match(uss, /\.whimtex-layers-footer,\s*\.whimtex-preview-footer\s*\{\s*height: 26px;/);
const preview = ui.slice(ui.indexOf('private sealed class SpritePreviewElement'));
assert.ok(preview.indexOf('Add(backdrop);') < preview.indexOf('Add(insetShadow);'));
assert.ok(preview.indexOf('Add(insetShadow);') < preview.indexOf('Add(checker);'));
assert.ok(preview.indexOf('Add(insetShadow);') < preview.indexOf('RefreshBackdropVisibility();'));
for (const element of ['backdrop', 'insetShadow'])
    assert.ok(preview.includes(`${element}.EnableInClassList("whimtex-preview-backdrop--hidden", !WhimTexUserSettings.ShowManta);`));
assert.ok(preview.indexOf('Add(checker);') < preview.indexOf('Add(image);'));
const shadow = preview.split('private sealed class PreviewInsetShadow')[1].split('public void RefreshBackdropVisibility')[0];
assert.ok(shadow.includes('pickingMode = PickingMode.Ignore;'));
assert.ok(!shadow.includes('Texture2D') && !shadow.includes('schedule.'));
const steps = Number(shadow.match(/const int steps = (\d+);/)[1]);
const maxDepth = Number(shadow.match(/Mathf.Min\(([\d.]+)f,/)[1]);
for (const [width, height] of [[1, 1], [8, 240], [200, 96], [1920, 1080], [5120, 2880]]) {
    const depth = Math.min(maxDepth, Math.min(width, height) * .5);
    const vertices = [];
    for (let ring = 0; ring <= steps; ring++) {
        const t = ring / steps, inset = depth * t;
        const alpha = (1 - t) ** 3;
        assert.ok(alpha >= 0 && alpha <= 1);
        vertices.push([inset, inset], [width - inset, inset],
            [width - inset, height - inset], [inset, height - inset]);
    }
    let area = 0, indexCount = 0;
    for (let ring = 0; ring < steps; ring++) for (let side = 0; side < 4; side++) {
        const a = ring * 4 + side, b = ring * 4 + (side + 1) % 4;
        for (const triangle of [[a, b, b + 4], [b + 4, a + 4, a]]) {
            const [p, q, r] = triangle.map(i => vertices[i]);
            const signed = ((q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])) * .5;
            assert.ok(signed >= 0, 'Consistent front-facing triangles');
            area += signed;
            indexCount += 3;
        }
    }
    assert.equal(indexCount, steps * 24);
    assert.equal(vertices.length, (steps + 1) * 4);
    assert.ok(Math.abs(area - (width * height - (width - 2 * depth) * (height - 2 * depth))) < 1e-6,
        'Shadow covers only the inset border, with no corner overlap or center fill');
}
console.log('Preview background stacking, spacing, footer source and shadow geometry reference checks passed (Unity/UI not executed).');
