const vm=require('node:vm'),fs=require('node:fs'),assert=require('node:assert/strict');
class Element{
 constructor(){this.attributes={};this.children=[];this.dataset={};this.hidden=true;this.classes=new Set();this.classList={add:k=>this.classes.add(k),remove:k=>this.classes.delete(k),toggle:(k,v)=>v?this.classes.add(k):this.classes.delete(k)};}
 append(...nodes){this.children.push(...nodes);}setAttribute(k,v){this.attributes[k]=v;}getAttribute(k){return this.attributes[k]??null;}removeAttribute(k){delete this.attributes[k];}
}
class Form extends Element{
 constructor(){super();this.method='post';this.target='';this.action='/Meetings/Create';this.button=new Element();this.button.name='intent';this.button.value='save';this.valid=true;}
 querySelectorAll(){return[this.button];}checkValidity(){return this.valid;}submit(){this.submits=(this.submits||0)+1;}
}
const body=new Element(),handlers={},capture={},timers=new Map();let serial=0,calls=0;
const context={Map,HTMLFormElement:Form,location:{href:'https://example.test/Meetings/Create'},document:{body,createElement:()=>new Element(),addEventListener:(type,fn)=>capture[type]=fn},window:{addEventListener:(type,fn)=>handlers[type]=fn,fetch:async()=>{calls++;return{ok:true};}},setTimeout:fn=>{timers.set(++serial,fn);return serial;},clearTimeout:id=>timers.delete(id)};
vm.runInNewContext(fs.readFileSync('wwwroot/js/global-loading.js','utf8'),context);
const api=context.window.ProjectTrackingLoading,tick=()=>{const jobs=[...timers.values()];timers.clear();jobs.forEach(fn=>fn());},overlay=()=>body.children[0];
const submit=(form,extra={})=>{const event={target:form,defaultPrevented:false,preventDefault(){this.defaultPrevented=true;},stopImmediatePropagation(){this.stopped=true;},...extra};handlers.submit(event);return event;};
(async()=>{
 let end=api.show('first');handlers.pageshow({persisted:false});let end2=api.show('second',{blocking:false});tick();assert(!overlay().hidden);end();assert(!overlay().hidden);end2();assert(overlay().hidden);end2();
 assert.equal(body.children.length,1,'one shared overlay');
 await assert.rejects(api.run(async()=>{throw Error('failure');}));tick();assert(overlay().hidden,'failure cleans up');
 await api.fetch('/poll');tick();assert(overlay().hidden,'GET background request stays quiet');
 await api.fetch('/Auth/KeepAlive',{method:'POST'});tick();assert(overlay().hidden,'heartbeat stays quiet');
 let release;context.window.fetch=()=>new Promise(resolve=>release=resolve);const request=api.fetch('/Meetings/Save',{method:'POST'});tick();assert(!overlay().hidden);release({ok:true});await request;assert(overlay().hidden);
 context.window.fetch=async()=>{const e=Error('aborted');e.name='AbortError';throw e;};await assert.rejects(api.fetch('/Meetings/Save',{method:'POST'}));tick();assert(overlay().hidden,'aborted requests release the spinner');
 const cancelled=new Form();submit(cancelled,{defaultPrevented:true});tick();assert(overlay().hidden);
 const invalid=new Form();invalid.valid=false;submit(invalid);tick();assert(overlay().hidden);
 const form=new Form();submit(form);tick();assert(!overlay().hidden);assert.equal(form.getAttribute('aria-busy'),'true');assert.equal(form.button.disabled,undefined,'named submit buttons remain enabled');assert.equal(form.button.value,'save');
 const duplicate={target:form,preventDefault(){this.prevented=true;},stopImmediatePropagation(){this.stopped=true;}};capture.submit(duplicate);assert(duplicate.prevented&&duplicate.stopped);
 handlers.pageshow({persisted:true});assert(overlay().hidden);assert.equal(form.getAttribute('aria-busy'),null);assert.equal(form.button.getAttribute('aria-disabled'),null);
 for(const action of ['/Files/DownloadAttachment/1','/Report/ExportExcel','/Report/PrintTable']){const f=new Form();f.action=action;submit(f);tick();assert(overlay().hidden,action+' must not leave a spinner behind');}
 const filter=new Form();filter.method='get';api.submit(filter);api.submit(filter);assert.equal(filter.submits,2,'changing filters keeps the latest GET submission');handlers.pageshow({persisted:true});
 const blank=new Form();blank.target='_blank';submit(blank);tick();assert(overlay().hidden);
 const direct=new Form();api.submit(direct);tick();assert.equal(direct.submits,1);api.submit(direct);assert.equal(direct.submits,1);handlers.pageshow({persisted:true});assert(overlay().hidden);
 console.log('PASS: common spinner concurrency, cleanup, background exclusions, validation, duplicate submits, payload preservation, downloads and browser-back reset');
})().catch(e=>{console.error(e);process.exitCode=1;});
