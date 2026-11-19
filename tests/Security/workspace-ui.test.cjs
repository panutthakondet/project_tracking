const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
function node(extra={}){return {value:'',hidden:false,disabled:false,textContent:'',dataset:{},events:{},addEventListener(type,fn){this.events[type]=fn;},...extra};}
const rows=Array.from({length:31},(_,i)=>node({textContent:'System release #'+(i+1),dataset:{status:(i+1)%3===0?'Failed':'Completed'}}));
const search=node(),status=node(),prev=node(),next=node(),count=node(),controls=[node({hidden:true}),node({hidden:true})];
const selectors={'[data-history-search]':search,'[data-history-status]':status,'[data-history-prev]':prev,'[data-history-next]':next,'[data-history-count]':count};
const history=node({open:false,querySelector:s=>selectors[s],querySelectorAll:s=>s==='[data-history-row]'?rows:controls});
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
