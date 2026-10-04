import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(new URL('../src/TextureCompositorWindow.Selection.cs', import.meta.url), 'utf8');
const body = source.match(/private int FindAdjacentLayerIndex\(int direction\)\s*\{([\s\S]*?)\n        \}/)?.[1];
assert.ok(body, 'Find the actual navigation implementation');
const navigate = new Function('toolkitLayerTree', 'selectedLayerId', 'direction',
    body.replace(/\bint\b/g, 'let').replace(/toolkitLayerTree\.Count/g, 'toolkitLayerTree.length'));
const rows = (...ids) => ids.map((id, index) => ({ Layer: id === null ? null : { Id: id }, Index: id === null ? -1 : index }));

let checks = 0;
function check(tree, active, direction, expected) {
    assert.equal(navigate(tree, active, direction), expected);
    checks++;
}
const expanded = rows('a', 'group', 'child', null, 'b', null);
check(expanded, 'a', 1, 1);
check(expanded, 'group', 1, 2);
check(expanded, 'child', 1, 4);
check(expanded, 'b', -1, 2);
check(expanded, 'a', -1, -1);
check(expanded, 'b', 1, -1);
check(expanded, null, 1, 0);
check(expanded, null, -1, 4);
check(expanded, 'deleted', 1, 0);
const collapsed = rows('a', 'group', 'b', null);
check(collapsed, 'group', 1, 2);
check(collapsed, 'b', -1, 1);
for (const direction of [-1, 1]) {
    check([], null, direction, -1);
    check(rows(null, null), null, direction, -1);
    check(rows('a', null), 'a', direction, -1);
}
const container = {};
const broken = rows('a', null, 'b', null);
broken[1] = { Layer: { Id: 'broken', Behaviour: null }, Index: 1, Container: container };
check(broken, 'a', 1, 1);
check(broken, 'b', -1, 1);
assert.equal(navigate(broken, 'broken', 1), 2);
assert.equal(navigate(broken, 'broken', -1), 0);
console.log(`Layer navigation: ${checks + 2} checks passed, including missing rows.`);
