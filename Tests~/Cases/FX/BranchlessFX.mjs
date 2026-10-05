// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('BranchlessFX: source/scalar contracts');
const assert = context.assert;

context.case('simple FX toggles use arithmetic channel masks', async () => {
    const source = name => readFileSync(new URL(`../../../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
    {
        const normalLighting = source('NormalLighting');
        assert.match(normalLighting, /n\.y \*= lerp\(1\.0, -1\.0, step\(0\.5001, _FlipY\)\)/);
        assert.doesNotMatch(normalLighting, /if \(_FlipY/);
    
        const pixelate = source('Pixelate');
        assert.match(pixelate, /float alphaClip = step\(0\.5001, _AlphaClip\)/);
        assert.match(pixelate, /source\.a = lerp\(source\.a, step\(_AlphaCutoff, source\.a\), alphaClip\)/);
        assert.doesNotMatch(pixelate, /if \(_AlphaClip/);
    }
});

context.case('halftone pattern inversion reuses one blend path', async () => {
    const source = name => readFileSync(new URL(`../../../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
    {
        const halftone = source('Halftone');
        assert.match(halftone, /float invertPattern = step\(0\.5001, _InvertPattern\)/);
        assert.match(halftone, /float inkAmount = lerp\(1\.0 - luminance, luminance, invertPattern\)/);
        assert.match(halftone, /float dotBlend = lerp\(dots, 1\.0 - dots, invertPattern\)/);
        assert.match(halftone, /result\.rgb = lerp\(_PaperColor\.rgb, _InkColor\.rgb, dotBlend\)/);
        assert.doesNotMatch(halftone, /bool invertPattern|result\.rgb = invertPattern\s*\?/);
    }
});

await finish(context);
