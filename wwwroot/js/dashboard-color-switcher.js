(()=>{
    const form=document.querySelector('.dashboard-color-strip'),style=document.getElementById('pt-user-theme');
    if(!form||!style)return;
    const status=form.querySelector('[role="status"]');
    let stylesPromise,saveQueue=Promise.resolve(),selection=0;
    const loadStyles=()=>stylesPromise||=(fetch(form.dataset.stylesUrl,{credentials:'same-origin',cache:'no-store',signal:typeof AbortSignal!=='undefined'&&AbortSignal.timeout?AbortSignal.timeout(12000):undefined})
        .then(r=>{if(!r.ok)throw new Error('styles');return r.json();}).catch(e=>{stylesPromise=null;throw e;}));
    // Preload after initial rendering; subsequent switches are entirely local.
    const preload=()=>loadStyles().catch(()=>{});
    if('requestIdleCallback' in window)requestIdleCallback(preload,{timeout:1500});else setTimeout(preload,300);
    form.addEventListener('submit',async event=>{
        if(!event.submitter?.value)return;
        event.preventDefault();
        const mode=event.submitter.value;
        const id=++selection;
        status.textContent='กำลังเตรียมสี…';
        try{
            const styles=await loadStyles();
            if(id!==selection)return;
            if(typeof styles[mode]!=='string')throw new Error('styles');
            style.textContent=styles[mode];
            status.textContent='';
            form.querySelectorAll('button[name="mode"]').forEach(button=>{
                const selected=button.value===mode;
                button.setAttribute('aria-pressed',String(selected));
                const mark=button.querySelector('span');
                if(mark)mark.textContent=selected?'✓':'';
            });
            window.dispatchEvent(new Event('pt:color-vision-change'));
            const data=new FormData(form);data.set('mode',mode);
            saveQueue=saveQueue.catch(()=>{}).then(async()=>{
                const response=await fetch(form.action,{method:'POST',body:data,credentials:'same-origin',signal:typeof AbortSignal!=='undefined'&&AbortSignal.timeout?AbortSignal.timeout(12000):undefined,headers:{'X-Requested-With':'XMLHttpRequest'}});
                if(response.status!==204)throw new Error('save');
            });
            await saveQueue;
        }catch(error){if(id===selection)status.textContent='บันทึกโหมดสีไม่สำเร็จ กรุณากดอีกครั้ง';}
    });
})();
