import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(new URL('../src/FXPresets/Normalize.hlsl', import.meta.url), 'utf8').replace(/\r\n/g, '\n');
assert.ok(source.startsWith('// @whimtex-effect Normal Map/Normalize\n'));
assert.ok(source.includes('channels * 2.0 - 1.0'));
assert.ok(source.includes('SpriteDecode(channels) : channels, color.a'));
assert.ok(source.includes('// @param bool _PackedColor = true'));
assert.ok(source.includes('SpriteEncode(color.rgb)'));
function normalize(color) {
    const normal = color.slice(0, 3).map(c => c * 2 - 1);
    const largest = Math.max(...normal.map(Math.abs));
    if (largest <= 1e-6) return [.5, .5, 1, color[3]];
    const scaled = normal.map(c => c / largest);
    const length = Math.hypot(...scaled);
    return [...scaled.map(c => c / length * .5 + .5), color[3]];
}
for (const color of [[.5,.5,1,1], [1,.5,.5,.2], [.5,0,.5,.5], [.5,.5,.5,0], [1,1,1,.7], [.6,.8,.9,1]]) {
    const result = normalize(color);
    assert.ok(result.every(Number.isFinite));
    assert.equal(result[3], color[3]);
    assert.ok(Math.abs(Math.hypot(...result.slice(0,3).map(c => c * 2 - 1)) - 1) < 1e-12);
    normalize(result).forEach((v,i) => assert.ok(Math.abs(v-result[i]) < 1e-12));
}
assert.deepEqual(normalize([.5,.5,1,.3]), [.5,.5,1,.3]);
assert.deepEqual(normalize([.5,.5,.5,.3]), [.5,.5,1,.3]);
const decode = x => x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4;
const encode = x => x <= .0031308 ? x * 12.92 : 1.055 * x ** (1 / 2.4) - .055;
for (const normal of [[0,0,1], [.6,0,.8], [-.6,0,.8], [0,-1,0]]) {
    const packed = [...normal.map(n => decode(n * .5 + .5)), .3];
    const result = normalize([...packed.slice(0,3).map(encode), packed[3]]);
    result.slice(0,3).map(decode).forEach((v,i) => assert.ok(Math.abs(v-packed[i]) < 1e-12));
}
console.log('Normal map normalization source and CPU reference checks passed (GPU not executed).');
