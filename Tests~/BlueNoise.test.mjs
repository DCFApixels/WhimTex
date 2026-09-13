import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { ranks } from './GenerateBlueNoise.mjs';
const read = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const source = read('src/BlueNoiseData.cs');
function decode(name) {
    const text = source.match(new RegExp(`string ${name} =([\\s\\S]*?);`))[1];
    return Buffer.from([...text.matchAll(/"([^"]+)"/g)].map(m => m[1]).join(''), 'base64');
}
function lowFrequencyPower(data, w, h, channel, threshold) {
    const n = w * h, values = Array.from({ length: n }, (_, i) =>
        threshold == null ? data[i * 4 + channel] / 255 : +(data[i * 4 + channel] / 255 < threshold));
    const mean = values.reduce((a, b) => a + b, 0) / n;
    const variance = values.reduce((a, b) => a + (b - mean) ** 2, 0) / n;
    let power = 0, count = 0;
    for (let fy = h === 1 ? 0 : -5; fy <= (h === 1 ? 0 : 5); fy++) for (let fx = -5; fx <= 5; fx++) {
        if ((!fx && !fy) || fx * fx + fy * fy > 25) continue;
        let real = 0, imaginary = 0;
        for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
            const angle = 2 * Math.PI * (fx * x / w + fy * y / h), value = values[y * w + x] - mean;
            real += value * Math.cos(angle); imaginary += value * Math.sin(angle);
        }
        power += (real * real + imaginary * imaginary) / (n * variance); count++;
    }
    return power / count;
}
for (const [name, w, h] of [['TwoD', 128, 128], ['OneD', 256, 1]]) {
    const data = decode(name), n = w * h;
    assert.equal(data.length, n * 4);
    for (let c = 0; c < 3; c++) {
        const histogram = new Uint32Array(256);
        for (let i = 0; i < n; i++) { histogram[data[i * 4 + c]]++; assert.equal(data[i * 4 + 3], 255); }
        for (const count of histogram) assert.equal(count, n / 256, `${name}: uniform ranks`);
        for (const threshold of [null, .1, .25, .5, .75, .9]) {
            const power = lowFrequencyPower(data, w, h, c, threshold);
            assert.ok(power < .2, `${name} channel ${c}, threshold ${threshold}: low-frequency power ${power} (white expectation 1)`);
        }
    }
    for (let a = 0; a < 3; a++) for (let b = a + 1; b < 3; b++) {
        let covariance = 0;
        for (let i = 0; i < n; i++) covariance += (data[i * 4 + a] / 255 - .5) * (data[i * 4 + b] / 255 - .5);
        assert.ok(Math.abs(covariance / n) < .02, `${name}: separate color channels`);
    }
}
const small = ranks(16, 16, 1337);
assert.deepEqual(small, ranks(16, 16, 1337), 'Deterministic bake');
assert.equal(new Set(small).size, small.length, 'Each rank is used exactly once');
assert.notDeepEqual(small, ranks(16, 16, 1338), 'Independent seeded bakes');
const shader = read('src/Shaders/Noise.shader');
assert.match(shader, /_BlueNoise2D.Load/);
assert.match(shader, /_BlueNoise1D.Load/);
assert.match(shader, /float3\(r, BlueValue\(cell, 1u\), BlueValue\(cell, 2u\)\)/);
const textures = read('src/BlueNoiseTextures.cs');
assert.match(textures, /TextureFormat.RGBA32, false, true/);
assert.match(textures, /texture.Apply\(false, true\)/);
assert.match(textures, /beforeAssemblyReload \+= Release/);
assert.match(textures, /quitting \+= Release/);
console.log('Blue Noise: 1D/2D RGB uniform histograms, threshold spectra, deterministic rank generation and GPU resource contracts passed.');
