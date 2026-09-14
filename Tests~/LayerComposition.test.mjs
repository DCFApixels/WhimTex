// Source contracts and extracted lifecycle control flow; does not compile or execute Unity.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../src/' + path, import.meta.url), 'utf8');
const source = read('Layers/Layer.cs');
const wrapper = source.split('public abstract class LayerBehaviour')[0];
const behaviour = source.split('public abstract class LayerBehaviour')[1];
assert.match(wrapper, /public sealed class Layer/);
assert.match(wrapper, /\[SerializeReference\] private LayerBehaviour behaviour/);
assert.match(wrapper, /\[SerializeReference\] public List<Layer> children/);
for (const field of ['layerName', 'enabled', 'opacity', 'blendMode', 'colorRange', 'blendRange', 'transform', 'swizzle', 'modifiers']) {
  assert.match(wrapper, new RegExp('public [^\\n]+ ' + field + '[ ;=]'));
  assert.doesNotMatch(behaviour, new RegExp('public \\w+(?:<[^>]+>)? ' + field + '\\s*[;=]'), field + ' has one serialized owner');
}
assert.match(behaviour, /\[NonSerialized\] private Layer owner/);
assert.match(behaviour, /\[SerializeField\] private string recoveryId/);
assert.match(wrapper, /\[SerializeField\] private string behaviourId/);
for (const name of ['Drawing', 'File', 'Group', 'ColorFill', 'Gradient', 'Noise', 'ShaderProcessor', 'Pending'])
  assert.match(read(`Layers/${name}LayerBehaviour.cs`), new RegExp(`class ${name}LayerBehaviour : LayerBehaviour`));
for (const name of ['Outline', 'SDF', 'NormalMap', 'Blur', 'MakeSeamless'])
  assert.match(read(`Layers/${name}LayerBehaviour.cs`), new RegExp(`class ${name}LayerBehaviour : TargetedLayerBehaviour`));

function body(signature) {
  const at = source.indexOf(signature);
  assert.ok(at >= 0, signature);
  const start = source.indexOf('{', at);
  let depth = 1, end = start + 1;
  while (depth && end < source.length) {
    if (source[end] === '{') depth++;
    if (source[end] === '}') depth--;
    end++;
  }
  assert.equal(depth, 0);
  return source.slice(start + 1, end - 1);
}
class GroupLayerBehaviour {}
const referenceEquals = (a,b) => a === b;
function extract(signature) {
  const code = body(signature)
    .replaceAll('value is GroupLayerBehaviour', 'value instanceof GroupLayerBehaviour')
    .replaceAll('new List<Layer>()', '[]')
    .replaceAll('new InvalidOperationException(', 'new Error(')
    .replaceAll('new ArgumentException(', 'new Error(')
    .replaceAll('nameof(prepared)', '"prepared"')
    .replace(/\b(string|bool|var) (retained\w+|result)/g, 'let $2');
  return new Function('value', 'prepared', 'ReferenceEquals', 'GroupLayerBehaviour', `with(this) { ${code} }`);
}
const set = extract('public void SetBehaviour(');
const detach = extract('internal LayerBehaviour DetachBehaviour(');
const adopt = extract('internal void AdoptContent(');
function layer(id) {
  const state = { id, layerName:id, enabled:true, behaviour:null, behaviourId:null, group:false, children:null, compositing:0,
    opacity:1, transform:{rotation:0},
    SetBehaviour(value) { set.call(this, value, null, referenceEquals, GroupLayerBehaviour); },
    DetachBehaviour() { return detach.call(this); },
    CopyRasterizedIdentityFrom(other) { this.id=other.id; this.layerName=other.layerName; this.enabled=other.enabled; this.opacity=other.opacity; },
  };
  return state;
}
function payload(id, group=false) {
  return Object.assign(group ? new GroupLayerBehaviour() : {}, {
    RecoveryId:id, owner:null, releases:0,
    ValidateOwner(next) { if(this.owner && this.owner !== next && this.owner.behaviour === this) throw Error('shared'); },
    Bind(next) { this.ValidateOwner(next); this.owner=next; },
    OnDetached() { this.ReleaseTransientResources(); },
    ReleaseTransientResources() { this.releases++; },
  });
}
const target = layer('stable'), old = payload('old');
target.SetBehaviour(old);
target.SetBehaviour(old);
assert.equal(old.releases, 0, 'Same behaviour is a no-op');
assert.throws(() => layer('other').SetBehaviour(old), /shared/);
target.SetBehaviour(null);
assert.equal(target.behaviourId, 'old', 'Missing data remains linked to its recovery payload');
assert.equal(target.layerName, 'stable');
assert.equal(old.releases, 1);
const guarded = layer('guarded'), failure = payload('failure'), candidate = payload('candidate');
guarded.SetBehaviour(failure);
failure.OnDetached = () => { throw Error('detach failed'); };
assert.throws(() => guarded.SetBehaviour(candidate), /detach failed/);
assert.equal(guarded.behaviour, failure, 'Failed cleanup must not commit the swap');
assert.equal(candidate.owner, null, 'Failed cleanup must not bind the replacement');
const other = layer('other');
other.SetBehaviour(candidate);
assert.throws(() => guarded.SetBehaviour(candidate), /shared/, 'Ownership validation happens before cleanup');
assert.equal(other.behaviour, candidate);
assert.match(read('Layers/DrawingLayerBehaviour.cs'), /internal override void OnDetached\(\) => ReleasePaintResources\(\)/);
const folder = layer('folder'), child = layer('child');
folder.SetBehaviour(payload('group', true));
folder.children.push(child);
folder.SetBehaviour(null);
assert.equal(folder.group, true);
assert.equal(folder.children[0], child, 'Missing group behaviour retains its children');
assert.throws(() => folder.SetBehaviour(payload('paint')), /children/);
folder.SetBehaviour(payload('repaired', true));
assert.equal(folder.children[0], child);
const prepared = layer('temporary'), pixels = payload('pixels');
prepared.SetBehaviour(pixels);
prepared.opacity=.4;
prepared.transform={rotation:45};
target.enabled=false;
adopt.call(target, null, prepared, referenceEquals, GroupLayerBehaviour);
assert.equal(target.id, 'stable');
assert.equal(target.layerName, 'stable');
assert.equal(target.enabled, false);
assert.equal(target.behaviour, pixels);
assert.equal(pixels.owner, target);
assert.equal(prepared.behaviour, null);
assert.equal(target.opacity, .4);
assert.equal(target.transform.rotation, 45);

const ui = read('TextureCompositorWindow.MissingLayers.cs');
const inspector = read('TextureCompositorWindow.Inspector.cs');
const recovery = read('MissingLayerRecovery.cs');
assert.match(ui, /FindRecord\(compositor, layer.BehaviourId\)/);
assert.match(ui, /layer.SetBehaviour\(draft\)/);
assert.match(ui, /layer.children.Count > 0 && descriptor.BehaviourType != typeof\(GroupLayerBehaviour\)/);
assert.match(inspector, /!ReferenceEquals\(toolkitInspectorBehaviour, selected\?\.Behaviour\)/);
assert.match(recovery, /if \(id != behaviourId\) continue/);
assert.match(recovery, /if \(found != null\) return new Record/);
assert.match(recovery, /property.Name == "recoveryId"\) continue/);
assert.match(recovery, /fields.TryGetValue\(property.Name/);
assert.match(recovery, /Enum.IsDefined\(type, value\)/);
assert.match(recovery, /float.MaxValue/);
assert.match(recovery, /TryGradient/);
assert.doesNotMatch(ui + recovery, /ClearAllManagedReferencesWithMissingTypes|FromJsonOverwrite|WriteAllText|SaveAssets|selectedMissingLayer/);
assert.match(read('TextureCompositorWindow.cs'), /layer.AdoptContent\(replacements\[i\]\)/);
assert.match(read('Automation/WhimTexApi.LiveCompletion.cs'), /pending.AdoptContent\(candidate\)/);
assert.match(read('Automation/WhimTexApi.Inspect.cs'), /entry\["behaviourMissing"\] = layer.Behaviour == null/);
assert.doesNotMatch(read('Layers/ColorFillLayerBehaviour.cs'), /FormerlySerializedAs|colorIsEncoded/);
assert.doesNotMatch(read('Layers/DrawingLayerBehaviour.cs'), /unifiedPatternMode/);
console.log('Layer composition: extracted ownership/swap lifecycle and source contracts passed. Unity serialization/GPU remain opt-in.');
