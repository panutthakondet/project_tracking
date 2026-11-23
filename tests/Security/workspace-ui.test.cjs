const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
function node(extra={}){return {value:'',hidden:false,disabled:false,textContent:'',dataset:{},events:{},addEventListener(type,fn){this.events[type]=fn;},...extra};}
const rows=Array.from({length:31},(_,i)=>node({textContent:'System release #'+(i+1),dataset:{status:(i+1)%3===0?'Failed':'Completed',kind:i%2?'WebAdmin':'Android'}}));
const search=node(),status=node(),prev=node(),next=node(),count=node(),controls=[node({hidden:true}),node({hidden:true})];
const selectors={'[data-history-search]':search,'[data-history-status]':status,'[data-history-prev]':prev,'[data-history-next]':next,'[data-history-count]':count};
const kinds=['','WebAdmin','Android','iOS'].map(kind=>node({dataset:{historyKind:kind},setAttribute(name,value){this[name]=value;}}));
const history=node({open:false,querySelector:s=>selectors[s],querySelectorAll:s=>s==='[data-history-row]'?rows:s==='[data-history-kind]'?kinds:controls});
const root={dataset:{projectId:'1'},querySelector:()=>history,querySelectorAll:()=>[]};
vm.runInNewContext(fs.readFileSync('wwwroot/js/security-workspace.js','utf8'),{document:{querySelector:()=>root},window:{location:{hash:''}},sessionStorage:{getItem:()=>null}});
const visible=()=>rows.filter(r=>!r.hidden);
assert.equal(visible().length,10);assert.equal(prev.disabled,true);assert.equal(next.disabled,false);assert.ok(controls.every(n=>!n.hidden));
next.events.click();assert.equal(visible()[0],rows[10]);assert.equal(visible().length,10);
search.value='#31';search.events.input();assert.deepEqual(visible(),[rows[30]]);assert.equal(prev.disabled,true);assert.equal(next.disabled,true);
status.value='Failed';status.events.change();assert.equal(visible().length,0);assert.ok(count.textContent.includes('ไม่พบ'));
search.value='';search.events.input();assert.equal(visible().length,10);assert.ok(visible().every(row=>row.dataset.status==='Failed'));assert.equal(next.disabled,true);
status.value='';status.events.change();next.events.click();next.events.click();next.events.click();assert.equal(visible().length,1);assert.equal(next.disabled,true);
console.log('PASS compact workspace: ten history rows per page, page navigation, search resets page, status filters, no results and last-page bounds');

kinds[1].events.click();assert.equal(visible().length,10);assert.ok(visible().every(row=>row.dataset.kind==='WebAdmin'));assert.equal(kinds[1]['aria-pressed'],'true');assert.equal(prev.disabled,true);
status.value='Failed';status.events.change();assert.ok(visible().every(row=>row.dataset.kind==='WebAdmin'&&row.dataset.status==='Failed'));
kinds[3].events.click();assert.equal(visible().length,0);assert.equal(next.disabled,true);
status.value='';status.events.change();kinds[0].events.click();assert.equal(visible().length,10);
console.log('PASS history category buttons combine with status, reset pagination, support empty categories and restore all rows');

const loginType=node({value:'Form'}),requestBody=node({value:'username={%username%}&password={%password%}'});
const formOnly=node({dataset:{websiteLoginKind:'Form'}}),jsonOnly=node({dataset:{websiteLoginKind:'JsonBearer'}});
const websiteForm={querySelector:s=>s==='[data-website-login-type]'?loginType:requestBody,querySelectorAll:()=>[formOnly,jsonOnly]};
const websiteRoot={dataset:{projectId:'1'},querySelector:()=>null,querySelectorAll:s=>s==='[data-website-login-form]'?[websiteForm]:[]};
vm.runInNewContext(fs.readFileSync('wwwroot/js/security-workspace.js','utf8'),{document:{querySelector:()=>websiteRoot},sessionStorage:{getItem:()=>null}});
assert.equal(formOnly.hidden,false);assert.equal(jsonOnly.hidden,true);
loginType.value='JsonBearer';loginType.events.change();assert.equal(formOnly.hidden,true);assert.equal(jsonOnly.hidden,false);assert.ok(requestBody.value.includes('membership_no'));
requestBody.value='{"custom":"{%username%}","pass":"{%password%}"}';loginType.value='Form';loginType.events.change();assert.ok(requestBody.value.includes('custom'));
console.log('PASS Website Login type toggles relevant fields and preserves customized request bodies');
