const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
function control(value=''){return {value,dataset:{},events:{},addEventListener(name,fn){this.events[name]=fn;},hasAttribute(){return true;},closest(){return null;}};}
const enabled=control();enabled.checked=true;
const type=control('Form'),preset=control();
const groups=['Form','JsonBearer'].map(kind=>({dataset:{loginKind:kind}}));
const fields={requestData:control('username={%username%}&password={%password%}'),loginUrl:control('https://uat.example.com/login'),scanStartUrl:control(),tokenPath:control('token'),username:control('saved-user'),password:control(),loggedInRegex:control('Logout')};
fields.password.dataset.loginOptionalPassword='true';
fields.loggedInRegex.closest=()=>groups[0];
fields.scanStartUrl.closest=fields.tokenPath.closest=()=>groups[1];
const form={dataset:{targetUrl:'https://uat.example.com/member/logon'},
 querySelector(selector){if(selector==='[data-login-enabled]')return enabled;if(selector==='[data-login-type]')return type;if(selector==='[data-login-member-preset]')return preset;return fields[selector.match(/name="([^"]+)"/)?.[1]];},
 querySelectorAll(selector){return selector==='[data-login-kind]'?groups:[type,...Object.values(fields)];}};
const root={dataset:{projectId:'1'},querySelector:()=>null,querySelectorAll:selector=>selector==='[data-login-form]'?[form]:[]};
vm.runInNewContext(fs.readFileSync('wwwroot/js/security-workspace.js','utf8'),{document:{querySelector:()=>root},URL,sessionStorage:{getItem:()=>null}});
assert.equal(fields.password.required,false);assert.equal(fields.tokenPath.disabled,true);
type.value='JsonBearer';type.events.change();
assert.equal(groups[0].hidden,true);assert.equal(fields.loggedInRegex.required,false);assert.equal(fields.tokenPath.required,true);
assert.equal(JSON.parse(fields.requestData.value).membership_no,'{%username%}');
fields.requestData.value='{"custom":"{%username%}"}';type.value='Form';type.events.change();assert(fields.requestData.value.includes('custom'));
preset.events.click();assert.equal(type.value,'JsonBearer');assert.equal(fields.loginUrl.value,'https://uat.example.com/api/member/auth/login');assert.equal(fields.scanStartUrl.value,'https://uat.example.com/member/');assert.equal(fields.username.value,'saved-user');assert.equal(fields.password.value,'');
enabled.checked=false;enabled.events.change();assert(Object.values(fields).every(field=>field.disabled&&!field.required));
enabled.checked=true;enabled.events.change();assert.equal(fields.password.required,false);assert.equal(fields.tokenPath.disabled,false);
console.log('PASS JSON/Form validation, Web Member preset, custom template preservation and optional saved password');
