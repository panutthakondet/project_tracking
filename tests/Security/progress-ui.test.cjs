// Isolated DOM and HTTP fixtures; never contacts a server or scanner.
const fs=require('node:fs'), vm=require('node:vm'), assert=require('node:assert/strict');
const source=fs.readFileSync('wwwroot/js/security-scan-progress.js','utf8');
async function check(status,stage,percent,initial='Running',fail=false){
 const nodes=new Map(), node=()=>({textContent:'',hidden:false,style:{},attrs:{},classList:{toggle(){}},setAttribute(k,v){this.attrs[k]=v;},removeAttribute(k){delete this.attrs[k];}});
 const get=s=>{if(!nodes.has(s))nodes.set(s,node());return nodes.get(s);};
 const root={dataset:{state:initial,initialState:initial,url:'/fixture',startedAt:new Date().toISOString()},classList:{toggle(){}},querySelector:get,querySelectorAll:()=>[]};
 let calls=0,reloads=0;const scheduled=[];
 const data={status,stage,percent,statusLabel:status,label:'fixture',stepIndex:4,steps:Array(7).fill({}),bytesDone:null,updatedAt:new Date().toISOString(),startedAt:new Date().toISOString(),finishedAt:status==='Completed'?new Date().toISOString():null,error:status==='Failed'?'failure':null};
 vm.runInNewContext(source,{document:{hidden:false,querySelectorAll:s=>s==='[data-scan-progress]'?[root]:[],querySelector:get,addEventListener(){}},window:{addEventListener(){},location:{reload(){reloads++;}}},AbortController,Date,Set,Number,String,Math,fetch:async()=>{calls++;if(fail)throw Error('offline');return {ok:true,headers:{get:()=> 'application/json'},json:async()=>data};},setInterval:()=>1,clearInterval(){},setTimeout(fn,ms){scheduled.push({fn,ms});return scheduled.length;},clearTimeout(){}});
 await new Promise(resolve=>setImmediate(resolve));
 if(initial==='Completed'){assert.equal(calls,0);return;}
 if(fail){assert.equal(get('[data-progress-connection]').hidden,false);assert.ok(scheduled.some(x=>x.ms===3000));return;}
 const track=get('[role="progressbar"]');
 assert.equal(track.attrs['aria-valuenow'],percent===null?undefined:String(percent));
 assert.equal(get('[data-progress-percent]').textContent,percent===null?(status==='Failed'?status:'กำลังทำงาน'):percent+'%');
 if(status==='Completed'){const reload=scheduled.filter(x=>x.ms===800);assert.equal(reload.length,1);reload[0].fn();assert.equal(reloads,1);assert.ok(!scheduled.some(x=>x.ms===3000));}
 if(status==='Failed'){assert.equal(get('[data-scan-error]').textContent,'failure');assert.ok(!scheduled.some(x=>x.ms===3000));}
}
module.exports=(async()=>{await check('Running','Uploading',43);await check('Running','Analyzing',null);await check('Completed','Completed',100);await check('Failed','Analyzing',null);await check('Completed','Completed',100,'Completed');await check('Running','Analyzing',null,'Running',true);console.log('PASS progress UI: measured percent, indeterminate analysis, terminal states, one completion reload, no reload loop, retry after network failure');})().catch(e=>{throw e;});
