(()=>{
    const form=document.querySelector('.dashboard-color-strip'),style=document.getElementById('pt-user-theme');
    if(!form||!style)return;
    const status=form.querySelector('[role="status"]');
    let stylesPromise,saveQueue=Promise.resolve(),selection=0;
    const loadStyles=()=>stylesPromise||=(fetch(form.dataset.stylesUrl,{credentials:'same-origin',cache:'no-store'})
        .then(r=>{if(!r.ok)throw new Error('styles');return r.json();}).catch(e=>{stylesPromise=null;throw e;}));
    // Preload after initial rendering; subsequent switches are entirely local.
    const preload=()=>loadStyles().catch(()=>{});
    if('requestIdleCallback' in window)requestIdleCallback(preload,{timeout:1500});else setTimeout(preload,300);
    form.addEventListener('submit',async event=>{
        if(!event.submitter?.value)return;
        event.preventDefault();
        const mode=event.submitter.value,id=++selection;
        status.textContent='';
        try{
            const styles=await loadStyles();
            if(id!==selection)return;
            if(typeof styles[mode]!=='string')throw new Error('styles');
            style.textContent=styles[mode];
            form.querySelectorAll('button[name="mode"]').forEach(b=>b.setAttribute('aria-pressed',String(b.value===mode)));
            window.dispatchEvent(new Event('pt:color-vision-change'));
            const data=new FormData(form);data.set('mode',mode);
            saveQueue=saveQueue.catch(()=>{}).then(async()=>{
                const response=await fetch(form.action,{method:'POST',body:data,credentials:'same-origin',headers:{'X-Requested-With':'XMLHttpRequest'}});
                if(response.status!==204)throw new Error('save');
            });
            await saveQueue;
        }catch(error){if(id===selection)status.textContent='บันทึกโหมดสีไม่สำเร็จ กรุณากดอีกครั้ง';}
    });
})();
