(function(){
    'use strict';
    const root=document.querySelector('.security-workspace');if(!root)return;
    root.querySelectorAll('.security-section-nav a').forEach(link=>link.addEventListener('click',()=>{
        const target=root.querySelector(link.getAttribute('href'));
        if(target&&target.tagName==='DETAILS')target.open=true;
    }));
    root.querySelectorAll('[data-login-form]').forEach(form=>{
        const enabled=form.querySelector('[data-login-enabled]');
        if(!enabled)return;
        const sync=()=>form.querySelectorAll('[data-login-required], [data-login-password], [name="requestData"]').forEach(field=>{
            field.disabled=!enabled.checked;
            field.required=enabled.checked&&field.hasAttribute('data-login-required')&&field.dataset.loginOptionalPassword!=='true';
        });
        enabled.addEventListener('change',sync);sync();
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
    let page=0;const pageSize=10;
    function render(){
        const query=search.value.trim().toLocaleLowerCase();
        const matches=indexed.filter(item=>(!query||item.text.includes(query))&&(!status.value||item.row.dataset.status===status.value));
        const pages=Math.max(1,Math.ceil(matches.length/pageSize));page=Math.min(page,pages-1);
        rows.forEach(row=>{row.hidden=true;});
        matches.slice(page*pageSize,(page+1)*pageSize).forEach(item=>{item.row.hidden=false;});
        prev.disabled=page===0;next.disabled=page>=pages-1;
        count.textContent=matches.length?'หน้า '+(page+1)+' / '+pages+' · '+matches.length+' รอบ':'ไม่พบรอบสแกนที่ตรงกับการค้นหา';
    }
    search.addEventListener('input',()=>{page=0;render();});status.addEventListener('change',()=>{page=0;render();});
    prev.addEventListener('click',()=>{if(page>0){page--;render();}});next.addEventListener('click',()=>{if(!next.disabled){page++;render();}});
    history.querySelectorAll('[data-history-controls]').forEach(node=>{node.hidden=false;});render();
}());
