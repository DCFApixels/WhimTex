import fs from 'node:fs';
import path from 'node:path';

// Usage: node HeightPreviewReport.mjs <output.html> <report.json>...
const [destination, ...inputs] = process.argv.slice(2);
if (!destination || !inputs.length) throw Error('Supply output HTML and explicit completed report paths.');
const reports = inputs.map(file => ({ file: path.resolve(file), ...JSON.parse(fs.readFileSync(file, 'utf8')) }));
for (const r of reports) if (r.rows.length !== 198 || r.preparation.length !== 6) throw Error('Incomplete report: ' + r.file);
const esc = s => String(s).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;');
const num = (n, digits = 3) => Number(n).toFixed(digits);
const median = values => { const sorted = [...values].sort((a, b) => a - b); return (sorted[Math.floor((sorted.length - 1) / 2)] + sorted[Math.floor(sorted.length / 2)]) / 2; };
const interval = values => `${num(Math.min(...values))}–${num(Math.max(...values))}`;
const labels = { flat: 'Плоскость', hills: 'Плавные холмы', steps: 'Ступени', 'thin-ridge': 'Гребень шириной 4 исходных пикселя', rough: 'Мелкий шумный рельеф', 'signed-hdr': 'Отрицательные высоты и HDR' };
const methodNames = { 'gpu-grid': 'Процедурная сетка', 'indexed-grid': 'Индексированная сетка', 'floating-horizon': 'Последовательный Horizon', 'parallel-horizon': 'Параллельный Horizon' };
const all = reports.flatMap(r => r.rows);
const selection = [['gpu-grid', 128], ['indexed-grid', 128], ['gpu-grid', 256], ['parallel-horizon', 512], ['parallel-horizon', 2048], ['parallel-horizon', 16384]];
const facts = [];
let sections = '';
for (const r of reports) {
    const first = r.rows[0], s = first.sourceSize, p = first.previewSize;
    let body = '';
    for (const [method, steps] of selection) {
        const rows = r.rows.filter(x => x.method === method && x.steps === steps && x.input === 'direct');
        if (!rows.length) continue;
        const fact = { source: s, preview: p, method, steps, medianMs: median(rows.map(x => x.medianMs)), minMs: Math.min(...rows.map(x => x.medianMs)), maxMs: Math.max(...rows.map(x => x.medianMs)), maxP95Ms: Math.max(...rows.map(x => x.p95Ms)), heightMae: median(rows.map(x => x.heightMae)), lightingMae: median(rows.map(x => x.lightingMae)), geometryUnityBytes: rows[0].geometryBytes, outputLogicalBytes: rows[0].outputLogicalBytes };
        facts.push(fact);
        body += `<tr><td>${methodNames[method]}</td><td>${steps}</td><td>${num(fact.medianMs)}</td><td>${interval(rows.map(x => x.medianMs))}</td><td>${num(fact.maxP95Ms)}</td><td>${num(fact.heightMae, 5)}</td><td>${num(fact.lightingMae, 5)}</td><td>${num(fact.geometryUnityBytes / 1048576, 2)}</td></tr>`;
    }
    const oracleCount = r.preparation.reduce((sum, x) => sum + x.oracleChecks, 0);
    sections += `<section><h2>Источник ${s}² → превью ${p}²</h2><p>Восемь проверяемых столбцов: ${oracleCount} лучей; максимальная ошибка геометрической высоты относительно CPU: ${num(Math.max(...r.preparation.map(x => x.oracleHeightMax)), 7)}. Уменьшение источника отдельным проходом: ${interval(r.preparation.map(x => x.reduceBatchMs))} мс.</p><div class="scroll"><table><thead><tr><th>Метод</th><th>Сегменты / шаги</th><th>Медиана, мс</th><th>Диапазон по картам</th><th>Худший p95</th><th>Ошибка высоты*</th><th>Ошибка света*</th><th>Память сетки**, МиБ</th></tr></thead><tbody>${body}</tbody></table></div></section>`;
}
const gallery = reports.find(r => r.rows[0].sourceSize === 8192 && r.rows[0].previewSize === 256);
let images = '';
if (gallery) for (const [pattern, label] of Object.entries(labels)) {
    const variants = [
        ['reference', 'Эталон: Horizon 16384'],
        ['direct-gpu-grid-128', 'Сетка 128²'],
        ['direct-gpu-grid-256', 'Сетка 256²'],
        ['direct-parallel-horizon-512', 'Horizon 512'],
        ['direct-parallel-horizon-2048', 'Horizon 2048'],
        ['reduced-parallel-horizon-512', 'Карта уменьшена до 256² → Horizon 512']
    ];
    images += `<h3>${label}</h3><div class="gallery">${variants.map(([suffix, name]) => {
        const file = path.join(path.dirname(gallery.file), `${pattern}-${suffix}.png`);
        const relative = path.relative(path.dirname(path.resolve(destination)), file).replaceAll('\\', '/');
        return `<figure><a href="${esc(relative)}"><img src="${esc(relative)}" width="256" height="256" alt="${esc(name)}"></a><figcaption>${name}</figcaption></figure>`;
    }).join('')}</div>`;
}
const html = `<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>WhimTex — Height preview benchmark</title><style>
body{background:#202124;color:#e4e4e4;font:15px/1.5 system-ui,sans-serif;margin:28px auto;max-width:1500px;padding:0 18px}h1{font-size:26px}h2{font-size:20px}h3{font-size:16px}p{max-width:1100px;color:#bbb}section{margin:24px 0}table{border-collapse:collapse;min-width:900px;width:100%;font-variant-numeric:tabular-nums}td,th{padding:7px 10px;border-bottom:1px solid #424449;text-align:right}th:first-child,td:first-child{text-align:left}th{background:#2c2e32;font-weight:600}.scroll{overflow:auto}.gallery{display:grid;grid-template-columns:repeat(6,minmax(100px,1fr));gap:12px}figure{margin:0}img{width:100%;height:auto;background:#111}figcaption{color:#aaa;font-size:13px}a{color:#87c2e9}code{color:#d4dcb4}details{margin:24px 0}@media(max-width:850px){.gallery{grid-template-columns:repeat(3,1fr)}}
</style><h1>GPU-сетка и Floating Horizon</h1><p>${esc(reports[0].gpu)} · ${esc(reports[0].api)} · Unity ${esc(reports[0].unity)}. Изолированный стенд, основное превью не изменено.</p>
<p>Время — прогретые пакеты по 32 рендера (8 для максимальной точности), с ожиданием завершения через отдельную текстуру 1×1. Девять замеров, порядок методов чередуется. Это амортизированное время отправки и завершения, а не чистые GPU timestamp или измерение FPS редактора. Подготовка команд и сохранение PNG исключены.</p>
<p>Источник RHalf, результат RGBAFloat. Ортографическая камера под 45°, без вращения вокруг вертикали. Одинаковое освещение и билинейное чтение высот. Нет теней, AO, MSAA, прозрачных дыр или боковых стенок. Уменьшение — обычный bilinear blit без усреднения всей площади; оно может терять узкие детали.</p>
${sections}<p>* Медиана ошибки по шести картам, среди пикселей поверхности, видимых в обоих изображениях. Высота сравнивается в исходных единицах, свет — в диапазоне 0–1. Покрытие и все отдельные результаты есть в CSV/JSON. Эталон Horizon проходит через каждый вертикальный узел билинейной карты; независимо проверены выбранные лучи на CPU, последовательная версия и сходимость плотной сетки.</p>
<p>** Память Mesh по Unity Profiler, включая собственный учёт Unity; это не независимое измерение физической VRAM. Процедурная сетка не хранит вершины/индексы. У Horizon нет геометрического буфера; параллельное ядро использует 520 байт shared memory на группу. Общий источник 8К RHalf занимает логически 128 МиБ, источник 512² — 0,5 МиБ. При 256² результат занимает 1 МиБ; сетке дополнительно нужен depth-буфер около 0,25 МиБ. В эти размеры не входят драйвер, код шейдеров и диагностические ресурсы.</p>
<h2>Качество · источник 8К, превью 256²</h2><p>Изображения можно открыть в исходном размере кликом. Нормали вычисляются одинаково из исходной карты; ошибка поверхности способна менять освещение даже при совпадении внешнего силуэта.</p>${images}
<details><summary>Полные отчёты</summary><ul>${reports.map(r => `<li><a href="${esc(path.relative(path.dirname(path.resolve(destination)), r.file).replaceAll('\\', '/'))}">${r.rows[0].sourceSize} → ${r.rows[0].previewSize}, JSON</a></li>`).join('')}</ul></details></html>`;
fs.mkdirSync(path.dirname(path.resolve(destination)), { recursive: true });
fs.writeFileSync(destination, html);
fs.writeFileSync(destination.replace(/\.html$/, '.json'), JSON.stringify({ reports: inputs, facts }, null, 2));
const fields = ['sourceSize','previewSize','pattern','input','method','steps','repeats','medianMs','p95Ms','submissionMedianMs','firstMs','geometrySetupMs','coverageIoU','heightMae','lightingMae','geometryBytes','outputLogicalBytes','outputUnityBytes'];
fs.writeFileSync(destination.replace(/\.html$/, '.csv'), fields.join(',')+'\n'+all.map(r=>fields.map(f=>r[f]).join(',')).join('\n')+'\n');
console.log(JSON.stringify({ destination: path.resolve(destination), rows: all.length, facts }, null, 2));
