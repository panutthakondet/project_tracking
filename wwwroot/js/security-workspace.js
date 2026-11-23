(function(){
    'use strict';
    const root=document.querySelector('.security-workspace');if(!root)return;
    root.querySelectorAll('.security-section-nav a').forEach(link=>link.addEventListener('click',()=>{
        const target=root.querySelector(link.getAttribute('href'));
        if(target&&target.tagName==='DETAILS')target.open=true;
    }));
    root.querySelectorAll('[data-login-form]').forEach(form=>{
        const enabled=form.querySelector('[data-login-enabled]'),type=form.querySelector('[data-login-type]');
        if(!enabled||!type)return;
        const body=form.querySelector('[name="requestData"]');
        const defaults={Form:'username={%username%}&password={%password%}',JsonBearer:'{"membership_no":"{%username%}","mem_password":"{%password%}"}'};
        let previous=type.value;
        const sync=()=>{
            form.querySelectorAll('[data-login-kind]').forEach(group=>{group.hidden=group.dataset.loginKind!==type.value;});
            form.querySelectorAll('input:not([type="hidden"]):not([data-login-enabled]),select,textarea').forEach(field=>{
                const group=field.closest('[data-login-kind]');
                field.disabled=!enabled.checked||!!group?.hidden;
                field.required=!field.disabled&&field.hasAttribute('data-login-required')&&field.dataset.loginOptionalPassword!=='true';
            });
            const preset=form.querySelector('[data-login-member-preset]');if(preset)preset.disabled=!enabled.checked;
        };
        enabled.addEventListener('change',sync);
        type.addEventListener('change',()=>{if(body&&body.value===defaults[previous])body.value=defaults[type.value];previous=type.value;sync();});
        form.querySelector('[data-login-member-preset]')?.addEventListener('click',()=>{
            let origin;try{origin=new URL(form.dataset.targetUrl).origin;}catch{return;}
            type.value='JsonBearer';previous='JsonBearer';
            form.querySelector('[name="loginUrl"]').value=origin+'/api/member/auth/login';
            form.querySelector('[name="scanStartUrl"]').value=origin+'/member/';
            form.querySelector('[name="tokenPath"]').value='token';body.value=defaults.JsonBearer;
            sync();
        });
        sync();
    });
    const storageKey='security-system-open:'+root.dataset.projectId;
    const systems=Array.from(root.querySelectorAll('[data-system-id]'));
    try{const saved=sessionStorage.getItem(storageKey);systems.forEach(system=>{if(system.dataset.systemId===saved)system.open=true;});}catch{}
    systems.forEach(system=>system.addEventListener('toggle',()=>{
        try{if(system.open)sessionStorage.setItem(storageKey,system.dataset.systemId);else if(sessionStorage.getItem(storageKey)===system.dataset.systemId)sessionStorage.removeItem(storageKey);}catch{}
    }));
    const history=root.querySelector('#security-history');if(!history)return;
    if(window.location.hash==='#security-history')history.open=true;
    const rows=Array.from(history.querySelectorAll('[data-history-row]'));
    const search=history.querySelector('[data-history-search]'),status=history.querySelector('[data-history-status]');
    const prev=history.querySelector('[data-history-prev]'),next=history.querySelector('[data-history-next]');
    const count=history.querySelector('[data-history-count]');if(!search||!status||!prev||!next||!count)return;
    const indexed=rows.map(row=>({row,text:row.textContent.toLocaleLowerCase()}));
    let page=0, selectedKind="";const pageSize=10;
    const kindButtons=Array.from(history.querySelectorAll("[data-history-kind]"));
    function render(){
        const query=search.value.trim().toLocaleLowerCase();
        const matches=indexed.filter(item=>(!query||item.text.includes(query))&&(!status.value||item.row.dataset.status===status.value)&&(!selectedKind||item.row.dataset.kind===selectedKind));
        const pages=Math.max(1,Math.ceil(matches.length/pageSize));page=Math.min(page,pages-1);
        rows.forEach(row=>{row.hidden=true;});
        matches.slice(page*pageSize,(page+1)*pageSize).forEach(item=>{item.row.hidden=false;});
        prev.disabled=page===0;next.disabled=page>=pages-1;
        count.textContent=matches.length?'หน้า '+(page+1)+' / '+pages+' · '+matches.length+' รอบ':'ไม่พบรอบสแกนที่ตรงกับการค้นหา';
    }
    kindButtons.forEach(button=>button.addEventListener('click',()=>{
        selectedKind=button.dataset.historyKind;
        kindButtons.forEach(item=>item.setAttribute('aria-pressed',String(item===button)));
        page=0;render();
    }));
    search.addEventListener('input',()=>{page=0;render();});status.addEventListener('change',()=>{page=0;render();});
    prev.addEventListener('click',()=>{if(page>0){page--;render();}});next.addEventListener('click',()=>{if(!next.disabled){page++;render();}});
    history.querySelectorAll('[data-history-controls]').forEach(node=>{node.hidden=false;});render();
}());
