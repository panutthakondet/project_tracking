const vm=require('node:vm'),fs=require('node:fs'),assert=require('node:assert/strict');
let submit,prevented=0,posts=[],gets=0,events=0;
const buttons=['standard','red-green','blue-yellow','monochrome'].map(value=>({value,setAttribute(k,v){this[k]=v;}}));
const status={textContent:''},style={textContent:'original'};
const form={action:'/Settings/ColorVision',dataset:{stylesUrl:'/Settings/ColorVisionStyles'},querySelector:()=>status,querySelectorAll:()=>buttons,addEventListener(type,fn){submit=fn;}};
const styles=Object.fromEntries(buttons.map(b=>[b.value,'css-'+b.value]));
const context={document:{querySelector:()=>form,getElementById:()=>style},window:{dispatchEvent(){events++;}},Event:class{},FormData:class{set(k,v){this[k]=v;}},requestIdleCallback:fn=>fn(),setTimeout:fn=>fn(),fetch:async(url,opts)=>{if(opts.method==='POST'){posts.push(opts.body.mode);return{status:204};}gets++;return{ok:true,json:async()=>styles};}};
context.window.requestIdleCallback=context.requestIdleCallback;
vm.runInNewContext(fs.readFileSync('wwwroot/js/dashboard-color-switcher.js','utf8'),context);
(async()=>{
 await submit({submitter:buttons[1],preventDefault(){prevented++;}});
 assert.equal(style.textContent,'css-red-green');
 await Promise.all([submit({submitter:buttons[2],preventDefault(){prevented++;}}),submit({submitter:buttons[3],preventDefault(){prevented++;}})]);
 assert.equal(style.textContent,'css-monochrome');assert.equal(posts.at(-1),'monochrome');assert.equal(gets,1);assert.equal(prevented,3);assert.equal(buttons[3]['aria-pressed'],'true');assert(events>=2);
 console.log('PASS: cached instant switch, no navigation, latest selection persisted, active button updated');
})().catch(e=>{console.error(e);process.exitCode=1;});
