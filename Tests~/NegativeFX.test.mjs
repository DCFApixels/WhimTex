import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const source = readFileSync(new URL('../src/FXPresets/Negative.hlsl', import.meta.url), 'utf8').replace(/\r\n/g, '\n');

test('negative preset is catalogued with blend strength and optional alpha inversion', () => {
    assert.equal(source.split('\n')[0], '// @whimtex-effect Color/Negative');
    assert.match(source, /@param float _Amount = 1 \[0 \.\. 1\]/);
    assert.match(source, /@param bool _InvertAlpha = false/);
    assert.match(source, /color\.rgb = lerp\(color\.rgb, 1\.0 - color\.rgb, amount\)/);
    assert.match(source, /if \(_InvertAlpha > 0\.5\)[\s\S]*?color\.a = lerp\(color\.a, 1\.0 - color\.a, amount\)/);
});

test('negative math has identity, full-inversion, and partial-inversion behavior', () => {
    const negative = (color, amount, invertAlpha = false) => [
        ...color.slice(0, 3).map(channel => channel + ((1 - channel) - channel) * amount),
        invertAlpha ? color[3] + ((1 - color[3]) - color[3]) * amount : color[3],
    ];

    assert.deepEqual(negative([0.2, 0.5, 0.9, 0.35], 0), [0.2, 0.5, 0.9, 0.35]);
    assert.deepEqual(negative([0.2, 0.5, 0.9, 0.35], 1), [0.8, 0.5, 0.09999999999999998, 0.35]);
    assert.deepEqual(negative([0.2, 0.5, 0.9, 0.35], 1, true), [0.8, 0.5, 0.09999999999999998, 0.65]);
    assert.deepEqual(negative([0.2, 0.5, 0.9, 0.35], 0.5), [0.5, 0.5, 0.5, 0.35]);
});
