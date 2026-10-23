// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
import { snapshot } from '../../Framework/Node/UssCascadeSnapshot.mjs';
const context = new TestContext("UiRefactor source/reference tests");
const assert = context.assert;
// The snapshot helper resolves only global palette variables. Preserve local
// custom-property declarations/references as a scoped cascade, not a global tint.
// Selector specificity, declaration order and each theme/state value still hash.
function scopedSnapshot(source) {
    const rules = [...source.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{([^{}]*)\}/g)];
    const global = new Set(), local = new Set();
    for (const [, selectors, body] of rules) {
        const names = selectors.trim() === '.whimtex-theme' ? global : local;
        for (const [, name] of body.matchAll(/(?:^|;)\s*(--whimtex-[\w-]+)\s*:/g)) names.add(name);
    }
    // Do not silently treat a genuinely undefined token as an inherited one.
    // An alias keeps the helper from resolving local variables at theme scope;
    // the original selector-qualified declarations remain in its property chains.
    const scoped = name => local.has(name) && !global.has(name);
    const canonical = source
        .replace(/var\((--whimtex-[\w-]+)\)/g, (value, name) =>
            scoped(name) ? `var(--scoped-${name.slice(2)})` : value)
        .replace(/(--whimtex-[\w-]+)(\s*:)/g, (value, name, colon) =>
            scoped(name) ? `--scoped-${name.slice(2)}${colon}` : value);
    return snapshot(canonical);
}
context.case("UiRefactor original assertion inputs and source contracts", async () => {
  const read = p => readFileSync(new URL('../../../' + p, import.meta.url), 'utf8');
  // Preserve the existing terminology normalization and exclusions. The original
  // fixture exactly matches 1269df5, before shared headers, navigation and comments.
  // Its successor records the reviewed 1269df5..d11522c changes and the explicit
  // cyclic-rail correction; the old fixture remains unchanged. Values, selectors,
  // scoped tokens and conflicting declaration order still compare exactly.
  const terminology = [
      ['.whimtex-canvas-view-', '.whimtex-preview-'],
      ['.whimtex-canvas-shadow', '.whimtex-preview-canvas-shadow'],
      ['.whimtex-canvas-cursor-', '.whimtex-preview-cursor-'],
      ['.whimtex-canvas-surface', '.whimtex-preview-surface'],
      ['.whimtex-layer-preview-channel', '.whimtex-mini-preview-channel'],
  ];
  let canonicalStyles = read('src/WhimTexSplitView.uss');
  for (const [current, previous] of terminology) canonicalStyles = canonicalStyles.replaceAll(current, previous);
  canonicalStyles = canonicalStyles.replace(/\.whimtex-canvas(?=--|[^\w-]|$)/g, '.whimtex-preview-canvas');
  const styles = canonicalStyles
      .replace(/\.whimtex-missing-thumbnail\s*\{[^}]*\}/g, '')
      .replace(/\.whimtex-view-field(?:\s*>\s*\.unity-base-field__label)?\s*\{[^}]*\}/g, '')
      .replace(/\.whimtex-guides?-[^{]+\{[^}]*\}/g, '')
      .replace(/\.whimtex-shape-(?:kind|color|picker[\w-]*|corners|corner[\w-]*)(?:\s*>\s*\.unity-base-field__label)?\s*\{[^}]*\}/g, '');
  const baseline = JSON.parse(read('Tests~/Fixtures/UssCascadeBaseline.current.json')).snapshot;
  assert.deepEqual(scopedSnapshot(styles), baseline, 'Exact USS values, selectors/specificity and conflicting property order must remain unchanged');
  assert.notDeepEqual(scopedSnapshot(styles.replace('--whimtex-surface: #383838', '--whimtex-surface: #393939')), baseline);
  assert.notDeepEqual(scopedSnapshot(styles.replace('height: 26px', 'height: 27px')), baseline);
  const shared = read('src/WhimTexUI.cs');
  assert.match(shared, /root.AddToClassList\("whimtex-theme"\)/);
  const target = read('src/EffectTargetSettingsView.cs');
  for (const part of ['arrow','text'])
      assert.ok(target.includes(`target.Q(className: "unity-base-popup-field__${part}")?.AddToClassList("whimtex-effect-target-${part}")`));
  assert.match(target, /if \(status.style.display.value != display\) status.style.display = display/);
  assert.match(target, /if \(status.messageType != messageType\) status.messageType = messageType/);
  assert.match(target, /else if \(source\?\.IsGroup == true\)/);
  assert.match(target, /IsUsableEffectTarget\(effect, effect.TargetLayerId\)/, 'Status still validates the current dependency');
  const inspector = read('src/WhimTexWindow.Inspector.cs');
  assert.match(inspector, /if \(forceValues\) toolkitInspectorEffectTarget\?\.Invalidate\(\)/);
  const window = read('src/WhimTexWindow.cs');
  assert.match(window, /private void OnDocumentChanged\(WhimTexDocument changedDocument\)[\s\S]*?toolkitInspectorEffectTarget\?\.Invalidate\(\)/);
  const ui = read('src/WhimTexWindow.UI.cs');
  assert.match(ui, /toolkitLayerBindings.Clear\(\);\s*toolkitInspectorEffectTarget\?\.Invalidate\(\)/);
  const utils = read('src/Utils.cs');
  assert.match(utils, /activeDocument.MarkChanged\(\);\s*InvalidateEffectTargetOptions\(\)/);
  assert.match(utils, /private void OnUndoRedo\(\)\s*\{\s*InvalidateEffectTargetOptions\(\)/);
  const activeDocument = read('src/WhimTexDocument.cs');
  assert.match(activeDocument, /CollectEffectTargetOptions\(layers, consumer, 0, targetIds, labels, new HashSet<Layer>\(\)\)/);
  assert.match(activeDocument, /visited.Clear\(\);\s*if \(!LayerDependsOn\(candidate, consumer, visited\)\)/);
  assert.match(activeDocument, /depth \+ 1,\s*targetIds,\s*labels,\s*visited\)/);
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

  context.facts.referenceChecks = checks;

});
await finish(context);

