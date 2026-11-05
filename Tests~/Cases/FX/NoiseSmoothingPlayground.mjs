// Prepare explicit opt-in workloads or summarize retained measurements; no Unity invocation.
import fs from 'node:fs';
import path from 'node:path';
const root=path.resolve(process.cwd(),'Temp/WhimTex/noise-smoothing');
fs.mkdirSync(root,{recursive:true});
const phase=process.argv[2]??'smoke';
if(phase==='summary') {
  const rows=[];
  for(const file of fs.readdirSync(root).filter(x=>x.endsWith('-report.json'))) {
    const r=JSON.parse(fs.readFileSync(path.join(root,file),'utf8'));
    console.log(file,r.state,r.completed+'/'+r.total,'checks='+r.checks,r.message);
    for(const c of r.cases) {
      const a=r.samples.filter(x=>x.name===c.name).map(x=>x.totalMs).sort((a,b)=>a-b);
      if(!a.length)continue;
      const median=a.length%2?a[a.length>>1]:(a[a.length/2-1]+a[a.length/2])/2;
      rows.push({phase:r.phase,name:c.name,n:a.length,medianMs:median,minMs:a[0],maxMs:a.at(-1)});
    }
  }
  fs.writeFileSync(path.join(root,'summary.json'),JSON.stringify(rows,null,2));
  console.table(rows.map(x=>({...x,medianMs:+x.medianMs.toFixed(3),minMs:+x.minMs.toFixed(3),maxMs:+x.maxMs.toFixed(3)})));
} else {
  const cases=[];
  function add(label,kind,dimensions,metric,output=1,smoothness=.5,side=1024,extra={}) {
    cases.push({name:`${dimensions}D-m${metric}-${label}-s${smoothness}`,kind,dimensions,metric,output,smoothness,side,evolution:0,jitter:1,...extra});
  }
  function family(d,m,side,levels=false) {
    add('value',1,d,m,0,0,side); add('f1',1,d,m,1,0,side); add('f2',1,d,m,2,0,side);
    add('smooth-f1-current',1,d,m,7,.5,side); add('subtract',1,d,m,4,0,side);
    add('blend',3,d,m,0,.5,side); add('soft-f1',4,d,m,1,.5,side);
    add('soft-f2',4,d,m,2,.5,side); add('soft-subtract',4,d,m,4,.5,side);
    if(levels) for(const s of [.25,1]) { add('blend',3,d,m,0,s,side); add('soft-f2',4,d,m,2,s,side); }
    if(m===0) { add('voronoise-current',2,d,m,0,.5,side); add('edge',5,d,m,1,0,side); add('soft-edge',6,d,m,1,.5,side); }
  }
  if(phase==='unified' || /^unified-[123]d$/.test(phase) || phase==='unified-periodic' || phase==='unified-8k') {
    const periodic=phase==='unified-periodic', big=phase==='unified-8k';
    for(const d of (/^unified-[123]d$/.test(phase)?[Number(phase.at(-2))]:big?[2,3]:[1,2,3])) for(const m of (big?[0]:[0,1,2,3]))
    for(const output of (big?[0,2,8]:[0,1,2,3,4,5,6,8])) {
      if(output===8 && m!==0) continue;
      if(periodic && d!==2 && (m!==0 || ![0,2,8].includes(output))) continue;
      const extra=periodic?{distribution:1,evolution:1.7,jitter:.6,period:4,offset:-2.7,fractal:2,footprint:.04}:big?{staticDomain:true}:{};
      const side=big?8192:128;
      add('baseline-o'+output,output===8?5:1,d,m,output===8?1:output,0,side,extra);
      if(!periodic) add('prototype-o'+output,output===0?3:output===8?6:4,d,m,output===8?1:output,.5,side,extra);
      if(big) add('unified-o'+output,7,d,m,output,0,side,extra);
      add('unified-o'+output,7,d,m,output,.5,side,extra);
    }
  } else if(phase==='smoke') { family(2,0,128); family(3,2,128); add('control',0,2,0,1,0,128); }
  else if(phase==='matrix') {
    for(const d of [2,3]) { add('control',0,d,0,1,0); for(const m of [0,1,2,3]) family(d,m,1024,m===0||m===2); }
  } else if(phase==='8k-2d'||phase==='8k-3d') {
    const d=phase==='8k-2d'?2:3; add('control',0,d,0,1,0,8192); family(d,0,8192,true);
    for(const m of [1,2,3]) { add('f2',1,d,m,2,0,8192); add('soft-f2',4,d,m,2,.5,8192); add('blend',3,d,m,0,.5,8192); }
  } else if(phase==='8k-full') {
    for(const d of [2,3]) {
      add('control',0,d,0,1,0,8192); family(d,0,8192);
      add('f2',1,d,2,2,0,8192); add('soft-f2',4,d,2,2,.5,8192); add('blend',3,d,2,0,.5,8192);
    }
  } else if(phase==='8k-metrics') {
    for(const d of [2,3]) for(const m of [1,3]) {
      add('f2',1,d,m,2,0,8192); add('soft-f2',4,d,m,2,.5,8192); add('blend',3,d,m,0,.5,8192);
    }
  } else if(phase==='extras') {
    for(const d of [2,3]) for(const m of [0,1,2,3]) for(const output of [3,5,6]) {
      add('op'+output,1,d,m,output,0,256); add('soft-op'+output,4,d,m,output,.5,256);
    }
    for(const d of [2,3]) {
      family(d,0,512);
      for(const distribution of [0,1]) for(const output of [0,2])
        add('animated-baseline-op'+output+'-dist'+distribution,1,d,0,output,0,512,{evolution:1.7,distribution,jitter:.6});
      for(const kind of [1,2,3,4,5,6]) for(const distribution of [0,1])
        add('animated-k'+kind+'-dist'+distribution,kind,d,0,kind===2||kind===3?0:1,.5,512,{evolution:1.7,distribution,jitter:.6});
    }
  } else throw Error('Unknown phase');
  const request={phase,cases,warmups:phase==='smoke'||phase==='extras'?0:1,repetitions:(phase.startsWith('8k')||phase==='unified-8k')?5:phase==='matrix'?7:2,maxPixelsPerDraw:['8k-full','8k-metrics','unified-8k'].includes(phase)?67108864:1048576};
  fs.writeFileSync(path.join(root,'request.json'),JSON.stringify(request,null,2));
  console.log(JSON.stringify({phase,cases:cases.length,shaders:new Set(cases.map(c=>[c.kind,c.dimensions,c.metric,c.output,c.distribution].join('/'))).size,repetitions:request.repetitions}));
}
