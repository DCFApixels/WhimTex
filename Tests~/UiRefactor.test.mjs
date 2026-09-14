import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { snapshot } from './UssCascadeSnapshot.mjs';
const read = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
// New controls are not a restyle of the existing cascade.
const styles = read('src/WhimTexSplitView.uss')
    .replace(/\.whimtex-missing-thumbnail\s*\{[^}]*\}/g, '')
    .replace(/\.whimtex-view-field(?:\s*>\s*\.unity-base-field__label)?\s*\{[^}]*\}/g, '')
    .replace(/\.whimtex-guides?-[^{]+\{[^}]*\}/g, '')
    .replace(/\.whimtex-shape-(?:kind|color|picker[\w-]*|corners|corner[\w-]*)(?:\s*>\s*\.unity-base-field__label)?\s*\{[^}]*\}/g, '');
const baseline = JSON.parse(read('Tests~/UssCascadeBaseline.json'));
assert.deepEqual(snapshot(styles), baseline, 'Exact USS values, selectors/specificity and conflicting property order must remain unchanged');
assert.notDeepEqual(snapshot(styles.replace('--whimtex-surface: #383838', '--whimtex-surface: #393939')), baseline);
assert.notDeepEqual(snapshot(styles.replace('height: 26px', 'height: 27px')), baseline);
const shared = read('src/WhimTexUI.cs');
assert.match(shared, /root.AddToClassList\("whimtex-theme"\)/);
const target = read('src/EffectTargetSettingsView.cs');
for (const part of ['arrow','text'])
    assert.ok(target.includes(`target.Q(className: "unity-base-popup-field__${part}")?.AddToClassList("whimtex-effect-target-${part}")`));
assert.match(target, /if \(status.style.display.value != display\) status.style.display = display/);
assert.match(target, /if \(status.messageType != messageType\) status.messageType = messageType/);
assert.match(target, /else if \(source\?\.IsGroup == true\)/);
assert.match(target, /IsUsableEffectTarget\(effect, effect.TargetLayerId\)/, 'Status still validates the current dependency');
const inspector = read('src/TextureCompositorWindow.Inspector.cs');
assert.match(inspector, /if \(forceValues\) toolkitInspectorEffectTarget\?\.Invalidate\(\)/);
const window = read('src/TextureCompositorWindow.cs');
assert.match(window, /private void OnCompositorChanged\(TextureCompositor changedCompositor\)[\s\S]*?toolkitInspectorEffectTarget\?\.Invalidate\(\)/);
const ui = read('src/TextureCompositorWindow.UI.cs');
assert.match(ui, /toolkitLayerBindings.Clear\(\);\s*toolkitInspectorEffectTarget\?\.Invalidate\(\)/);
const utils = read('src/Utils.cs');
assert.match(utils, /compositor.MarkChanged\(\);\s*InvalidateEffectTargetOptions\(\)/);
assert.match(utils, /private void OnUndoRedo\(\)\s*\{\s*InvalidateEffectTargetOptions\(\)/);
const compositor = read('src/TextureCompositor.cs');
assert.match(compositor, /CollectEffectTargetOptions\(layers, consumer, 0, targetIds, labels, new HashSet<Layer>\(\)\)/);
assert.match(compositor, /visited.Clear\(\);\s*if \(!LayerDependsOn\(candidate, consumer, visited\)\)/);
assert.match(compositor, /depth \+ 1,\s*targetIds,\s*labels,\s*visited\)/);
// Reusing a visited set must not leak reachability state between candidates, including cycles.
function depends(graph, candidate, sought, visited) {
    if (candidate === sought) return true;
    if (visited.has(candidate)) return false;
    visited.add(candidate);
    return graph[candidate].some(next => depends(graph, next, sought, visited));
}
let checks = 0;
for (let seed = 1; seed < 100; seed++) {
    let state = seed;
    const random = () => {state = (Math.imul(state, 1664525) + 1013904223) >>> 0; return state;};
    const graph = Array.from({length: 16}, () => Array.from({length: random() % 4}, () => random() % 16));
    for (let sought = 0; sought < 16; sought++) {
        const scratch = new Set();
        for (let candidate = 0; candidate < 16; candidate++) {
            scratch.clear();
            assert.equal(depends(graph, candidate, sought, scratch), depends(graph, candidate, sought, new Set()));
            checks++;
        }
    }
}
console.log(`UI refactor: ${Object.keys(baseline).length} USS property cascade fingerprints, source contracts and ${checks} dependency scratch-reuse reference checks passed. Unity layout/profiling not executed.`);
