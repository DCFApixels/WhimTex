// Optional independent reader check: node read-fixture.cjs /absolute/path/to/ag-psd fixture.psd
const fs = require('node:fs');
const assert = require('node:assert/strict');
const api = require(process.argv[2]);
api.initializeCanvas(() => { throw Error('Unexpected canvas access'); },
    (width, height) => ({ width, height, data: new Uint8ClampedArray(width * height * 4) }));
const psd = api.readPsd(fs.readFileSync(process.argv[3]), { useImageData: true, throwForMissingFeatures: true });
assert.equal(psd.width, 3);
assert.equal(psd.height, 2);
assert.equal(psd.channels, 4);
assert.equal(psd.imageResources.versionInfo.hasRealMergedData, true);
assert.deepEqual(psd.children.map(l => l.name), ['Bottom', 'Группа 💗', 'Fill', 'Stroke', 'Gradient']);
const [bottom, group, fill, stroke, gradient] = psd.children;
assert.equal(bottom.blendMode, 'multiply');
assert.equal(bottom.opacity, 128 / 255);
assert.equal(group.blendMode, 'pass through');
assert.equal(group.children[0].name, 'Nested');
assert.equal(group.children[0].blendMode, 'multiply');
assert.equal(group.children[0].opacity, 153 / 255);
assert.equal(group.children[0].children[0].hidden, true);
assert.deepEqual(fill.vectorFill, { type: 'color', color: { r: 255, g: 64, b: 32 } });
assert.equal(fill.mask.imageData.data[0], 20);
assert.equal(stroke.fillOpacity, 0);
assert.equal(stroke.effects.stroke[0].position, 'outside');
assert.equal(stroke.effects.stroke[0].size.value, 4);
assert.equal(stroke.effects.stroke[0].opacity, 0.75);
assert.equal(gradient.vectorFill.style, 'linear');
assert.equal(gradient.vectorFill.angle, 90);
assert.equal(gradient.vectorFill.colorStops.length, 2);
assert.equal(gradient.vectorFill.opacityStops[0].opacity, 0);
assert.equal(gradient.vectorFill.opacityStops[1].opacity, 1);
for (let y = 0; y < 2; y++) for (let x = 0; x < 3; x++) for (let c = 0; c < 4; c++) {
    const expected = 20 * (c === 3 ? 2 : c + 3) + 7 * y + x;
    const index = (y * 3 + x) * 4 + c;
    assert.equal(bottom.imageData.data[index], expected);
    // Unmatting 8-bit merged RGB introduces quantization at low alpha.
    assert.ok(Math.abs(psd.imageData.data[index] - expected) <= (c === 3 ? 0 : 4));
}
console.log('Independent PSD reader: hierarchy, Unicode, fills, gradient stops, stroke, mask, RGBA and merged matte passed.');
