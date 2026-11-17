import fs from 'node:fs';
import path from 'node:path';
const [project, ...ids] = process.argv.slice(2);
if (!project || !ids.length) throw Error('Usage: analysis.mjs PROJECT RUN_IDS...');
const root = path.join(project, 'Temp/WhimTex/filled-preview');
const reports = ids.map(id => JSON.parse(fs.readFileSync(path.join(root, id + '.json'), 'utf8')));
const stats = values => {
  const a = values.filter(Number.isFinite).sort((a,b) => a-b);
  return { n:a.length, median:(a[Math.floor((a.length-1)/2)]+a[Math.floor(a.length/2)])/2,
    p95:a[Math.ceil(a.length*.95)-1], min:a[0], max:a.at(-1) };
};
const summaries = [], hashes = new Map();
for (const r of reports) {
  if (r.status !== 'passed') throw Error(r.id + ': incomplete/failed');
  if (r.fixtureHash !== reports[0].fixtureHash) throw Error('Fixture changed: ' + r.id);
  for (const row of r.rows.filter(x => x.hash)) {
    const key = row.workload + '/' + row.view;
    if (hashes.has(key) && hashes.get(key) !== row.hash) throw Error('Pixel difference: '+r.id+' '+key);
    hashes.set(key,row.hash);
  }
  const groups = Map.groupBy(r.rows.filter(x=>x.frame>=0), x=>x.workload+'/'+x.view+(x.workload==='gpu-control'?'/'+x.passes:''));
  for (const [key, rows] of groups) {
    const item = { run:r.id, mode:r.mode, key, stats:{} };
    for (const field of ['viewportMs','paintMs','renderMs','validateMs','configureMs','collectMs','cullMs','bindMs','recordMs','executeMs','cleanupMs','syncMs','draws','candidates','visible','poolBytes'])
      item.stats[field] = stats(rows.map(x=>x[field]));
    if (r.mode==='sync') item.stats.readyMs=stats(rows.map(x=>x.viewportMs+x.syncMs));
    if (r.mode==='gpu') {
      item.gpuStatuses = Object.fromEntries([...Map.groupBy(rows,x=>x.gpuStatus)].map(([s,a])=>[s,a.length]));
      item.stats.gpuMs = stats(rows.filter(x=>x.gpuStatus==='ready').map(x=>x.gpuMs));
    }
    item.gcRows=rows.filter(x=>x.gc0>0).length;
    summaries.push(item);
  }
}
const result = { runs:ids, matchingPixelStates:hashes.size, summaries };
fs.writeFileSync(path.join(root,'summary.json'),JSON.stringify(result,null,2));
for(const s of summaries) console.log([s.run,s.key,'CPU '+s.stats.viewportMs.median.toFixed(3),'bind '+s.stats.bindMs.median.toFixed(3),'execute '+s.stats.executeMs.median.toFixed(3),s.stats.gpuMs?'GPU '+s.stats.gpuMs.median.toFixed(3)+' '+JSON.stringify(s.gpuStatuses):''].join(' | '));
console.log('Matching whole-preview pixel states:',hashes.size);
