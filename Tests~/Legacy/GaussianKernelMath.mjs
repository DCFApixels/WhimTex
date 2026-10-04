// CPU-only verification of the paired-kernel/edge math; does not compile or run Unity.
import assert from 'node:assert/strict';
let checks = 0;
const near = (a, b, message, eps = 1e-10) => {
  assert.ok(Math.abs(a - b) <= eps, `${message}: ${a} vs ${b}`); checks++;
};
function at(data, i, edge) {
  const n = data.length;
  if (edge === 'Transparent' && (i < 0 || i >= n)) return 0;
  if (edge === 'Repeat') i = ((i % n) + n) % n;
  else if (edge === 'Mirror') {
    i = ((i % (2 * n)) + 2 * n) % (2 * n);
    i = Math.min(i, 2 * n - 1 - i);
  } else i = Math.min(n - 1, Math.max(0, i));
  return data[i];
}
function sample(data, x, edge) {
  const low = Math.floor(x), f = x - low;
  return at(data, low, edge) * (1 - f) + at(data, low + 1, edge) * f;
}
function blur(data, radius, edge, paired) {
  if (!radius) return [...data];
  const extent = Math.ceil(radius), sigma = Math.max(radius / 3, 1 / 3);
  const weight = i => Math.exp(-i * i / (2 * sigma * sigma));
  let total = 1;
  for (let i = 1; i <= extent; i++) total += 2 * weight(i);
  return data.map((_, x) => {
    let value = at(data, x, edge);
    if (paired) {
      for (let i = 1; i <= extent; i += 2) {
        const a = weight(i), b = i + 1 <= extent ? weight(i + 1) : 0;
        const w = a + b, offset = i + (w > 0 ? b / w : 0);
        value += w * (sample(data, x + offset, edge) + sample(data, x - offset, edge));
      }
    } else {
      for (let i = 1; i <= extent; i++) value += weight(i) * (at(data, x + i, edge) + at(data, x - i, edge));
    }
    return value / total;
  });
}
for (const size of [1, 2, 3, 17, 64, 65]) {
  const noise = Array.from({ length: size }, (_, i) => Math.sin(i * 3.79) * 15 + 4);
  for (const radius of [0, .1, .5, 1, 1.25, 4, 23.9, 24, 40, 128, 255.5, 256]) {
    for (const edge of ['Transparent', 'Clamp', 'Repeat', 'Mirror']) {
      const reference = blur(noise, radius, edge, false), pairs = blur(noise, radius, edge, true);
      pairs.forEach((value, i) => near(value, reference[i], `paired size=${size} r=${radius} ${edge}`));
      if (edge !== 'Transparent') blur(Array(size).fill(4), radius, edge, true)
        .forEach(value => near(value, 4, 'constant HDR normalization'));
    }
  }
}
for (const edge of ['Transparent', 'Clamp', 'Repeat', 'Mirror']) {
  const alpha = Array.from({ length: 65 }, (_, i) => i === 32 ? 1 : 0);
  const premultiplied = alpha.map(a => a * 8);
  const a = blur(alpha, 24, edge, true), rgb = blur(premultiplied, 24, edge, true);
  for (let i = 0; i < a.length; i++) if (a[i] > 0) near(rgb[i] / a[i], 8, 'unpremultiplied HDR intensity');
}
console.log(`Gaussian kernel math: ${checks} checks passed. CPU reference only; GPU validation is separate.`);
