import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const code = readFileSync(new URL('../src/FXPresets/DigitalGlitch.hlsl', import.meta.url), 'utf8').replace(/\r\n/g, '\n');

test('digital glitch preset exposes independent, conditionally displayed artifact controls', () => {
    assert.equal(code.split('\n')[0], '// @whimtex-effect Stylization/Digital Glitch');
    for (const parameter of ['_Blend', '_TearDensity', '_TearLineHeight', '_TearJitter', '_TearShift', '_BlockDensity', '_BlockWidth', '_BlockHeight', '_BlockSizeRandomness', '_BlockShiftX', '_BlockShiftY', '_Dropout', '_BlockVoidChance', '_ColorJitterDensity', '_BlockColorJitter', '_GradientDensity', '_GradientOpacity', '_RGBSplit', '_RGBAngle', '_ColorLoss', '_PosterizeAmount', '_PosterizeLevels', '_NoiseAmount', '_NoiseColor', '_AlphaJitter', '_AlphaJitterFlip', '_AlphaNoise', '_Seed', '_EffectTime', '_FrameRate'])
        assert.match(code, new RegExp(`@param float ${parameter}\\b`));
    assert.match(code, /@param hidden float _AlphaFollowChance\b/);
    assert.match(code, /@if _TearDensity != 0[\s\S]*?@endif/);
    assert.match(code, /@if _BlockDensity != 0[\s\S]*?@endif/);
    assert.match(code, /@param enum _BlockOrder = RowsFirst \{RowsFirst: 0, ColumnsFirst: 1\} \/\/ RowsFirst jitters line heights, then gives each line an independent width layout/);
    assert.match(code, /float3 FindBlockCell\([\s\S]*?return float3\(index, start, max\(end - start, 1\.0\)\);/);
    assert.match(code, /blockRow = FindBlockCell\(pixel\.y[\s\S]*?blockColumn = FindBlockCell\(pixel\.x/);
    assert.match(code, /blockColumn = FindBlockCell\(pixel\.x[\s\S]*?blockRow = FindBlockCell\(pixel\.y/);
    assert.match(code, /blockRow\.x \+ frameIndex \* 13\.3/);
    assert.match(code, /blockColumn\.x \+ frameIndex \* 13\.3/);
    assert.match(code, /@if _RGBSplit != 0[\s\S]*?@param float _RGBAngle/);
    assert.match(code, /@if _PosterizeAmount != 0[\s\S]*?@param float _PosterizeLevels/);
    assert.match(code, /@if _NoiseAmount != 0[\s\S]*?@param float _NoiseColor/);
    assert.match(code, /@param float _BlockVoidChance = 0[\s\S]*?@endif/);
    assert.match(code, /@if _ColorJitterDensity != 0[\s\S]*?@param float _BlockColorJitter/);
    assert.match(code, /@param float _GradientDensity = 0[\s\S]*?@if _GradientDensity != 0[\s\S]*?@param enum _GradientBlendMode = Overlay \{Add: 0, Multiply: 1, Overlay: 2, Overwrite: 3\}/);
    assert.match(code, /@param float _GradientOpacity = 1 \[0 \.\. 1\][\s\S]*?@param gradient _BlockTintGradient[\s\S]*?@param enum _GradientMapping = RandomPerBlock/);
    assert.match(code, /gradientSample = _BlockTintGradient_Sample\(gradientT\)/);
    assert.match(code, /float gradientOpacity = saturate\(_GradientOpacity \* gradientSample\.a\)/);
    assert.match(code, /gradientT = saturate\(\(blockRow\.y \+ blockRow\.z \* 0\.5\) \/ max\(_CanvasSize\.y, 1\.0\)\)/);
    assert.match(code, /gradientT = saturate\(\(blockColumn\.y \+ blockColumn\.z \* 0\.5\) \/ max\(_CanvasSize\.x, 1\.0\)\)/);
    assert.match(code, /float colorJitterMask = step\(1\.0 - _ColorJitterDensity, colorJitterRoll\)/);
    assert.match(code, /gradientMask = step\(1\.0 - _GradientDensity, gradientRoll\)/);
    assert.match(code, /video = jitteredVideo \+ gradientColor \* gradientOpacity/);
    assert.match(code, /jitteredVideo \* gradientColor/);
    assert.match(code, /video = lerp\(jitteredVideo, gradientColor, gradientOpacity\)/);
    assert.match(code, /@param hidden float _AlphaFollowChance = 0[\s\S]*?@param float _AlphaJitter = 0[\s\S]*?@param float _AlphaNoise = 0/);
    assert.match(code, /@param float _AlphaJitter = 0 \[0 \.\. ~32\]/);
    assert.match(code, /@param float _AlphaJitterFlip = 0\.5 \[0 \.\. 1\]/);
});

test('random corruption is seeded and advances only through the explicit time parameter', () => {
    assert.match(code, /HashNoise\(float2\(lineKey, frameIndex \+ _Seed/);
    assert.match(code, /HashNoise\(blockKey/);
    assert.match(code, /floor\(max\(_EffectTime, 0\.0\) \* max\(_FrameRate, 1\.0\)\)/);
    assert.match(code, /alpha = lerp\(color\.a, shiftedAlpha, alphaFollows\)/);
    assert.match(code, /alpha \*= 1\.0 - blockVoidMask/);
    assert.match(code, /if \(_AlphaFollowChance > 0\.0\)[\s\S]*?SampleInput\(alphaUV\)\.a/);
    assert.match(code, /float jitterSign = 1\.0 - 2\.0 \* step\(flipRoll, _AlphaJitterFlip\)/);
    assert.doesNotMatch(code, /\b_Time\b/, 'Never depend on Unity’s implicit time uniform');
});
