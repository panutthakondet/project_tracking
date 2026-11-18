(()=>{
    if(window.ProjectTrackingLoading)return;
    const active=new Map(),forms=new Map();
    let overlay,messageNode,timer,sequence=0;
    const ensure=()=>{
        if(overlay)return true;
        if(!document.body)return false;
        overlay=document.createElement('div');overlay.className='pt-global-loading';overlay.id='ptGlobalLoading';overlay.hidden=true;
        overlay.setAttribute('role','status');overlay.setAttribute('aria-live','polite');overlay.setAttribute('aria-atomic','true');
        const card=document.createElement('div');card.className='pt-global-loading__card';
        const ring=document.createElement('span');ring.className='pt-global-loading__spinner';ring.setAttribute('aria-hidden','true');
        messageNode=document.createElement('strong');messageNode.className='pt-global-loading__message';
        card.append(ring,messageNode);overlay.append(card);document.body.append(overlay);return true;
    };
    const paint=()=>{
        timer=undefined;
        if(!ensure())return;
        const rows=[...active.values()];
        if(!rows.length){overlay.hidden=true;return;}
        const latest=rows.filter(r=>r.blocking).at(-1)||rows.at(-1);
        overlay.classList.toggle('is-nonblocking',!latest.blocking);messageNode.textContent=latest.message;overlay.hidden=false;
    };
    const show=(message='กำลังดำเนินการ…',options={})=>{
        const id=++sequence;active.set(id,{message,blocking:options.blocking!==false});
        if(overlay&&!overlay.hidden)paint();
        else if(timer===undefined)timer=setTimeout(paint,options.delay??150);
        let ended=false;
        return ()=>{if(ended)return;ended=true;active.delete(id);if(!active.size&&timer!==undefined){clearTimeout(timer);timer=undefined;}if(overlay&&!overlay.hidden)paint();};
    };
    const reset=()=>{
        active.clear();if(timer!==undefined)clearTimeout(timer);timer=undefined;
        if(overlay)overlay.hidden=true;
        forms.forEach(({busy,buttons},form)=>{form.classList.remove('pt-form-saving');if(busy===null)form.removeAttribute('aria-busy');else form.setAttribute('aria-busy',busy);buttons.forEach(([button,aria])=>{if(aria===null)button.removeAttribute('aria-disabled');else button.setAttribute('aria-disabled',aria);});});
        forms.clear();
    };
    const eligible=(form,submitter)=>{
        if(!form||form.dataset.loading==='off')return false;
        const target=submitter?.formTarget||form.target;
        if(target&&target.toLowerCase()!=='_self')return false;
        if((submitter?.formMethod||form.method).toLowerCase()==='dialog')return false;
        const action=submitter?.formAction||form.action||location.href;
        return !/\/(?:download|export|print)[^/?#]*(?:[/?#]|$)|[?&](?:handler|action)=(?:download|export|print)/i.test(action);
    };
    const beginForm=(form,submitter)=>{
        if(!eligible(form,submitter)||forms.has(form))return;
        const buttons=[...form.querySelectorAll('button[type="submit"],input[type="submit"]')].map(b=>[b,b.getAttribute('aria-disabled')]);
        forms.set(form,{busy:form.getAttribute('aria-busy'),buttons});
        form.setAttribute('aria-busy','true');form.classList.add('pt-form-saving');buttons.forEach(([b])=>b.setAttribute('aria-disabled','true'));
        // Do not disable controls: named submit buttons and form values must still be posted.
        show(form.dataset.loadingMessage||((submitter?.formMethod||form.method).toLowerCase()==='get'?'กำลังโหลดข้อมูล…':'กำลังบันทึกข้อมูล…'));
    };
    const run=async(task,message,options)=>{const end=show(message,options);try{return await task();}finally{end();}};
    const fetchWithLoading=(input,init={},options={})=>{
        const method=String(init.method||input?.method||'GET').toUpperCase();
        const url=String(input?.url||input);
        const foreground=options.show??(!['GET','HEAD'].includes(method)&&!/(?:KeepAlive|Heartbeat|PushKey|Token|Poll)(?:[/?#]|$)/i.test(url));
        if(!foreground)return window.fetch(input,init);
        const blocking=options.blocking??!/(?:MeetingRoom|Reorder|UpdateSort|MoveCalendarEvent)(?:[/?#]|$)/i.test(url);
        return run(()=>window.fetch(input,init),options.message||'กำลังบันทึกข้อมูล…',{blocking});
    };
    const isWrite=(form,submitter)=>String(submitter?.formMethod||form?.method||'get').toLowerCase()!=='get';
    window.ProjectTrackingLoading={show,run,fetch:fetchWithLoading,reset,submit(form){if(forms.has(form)&&isWrite(form))return;beginForm(form);try{HTMLFormElement.prototype.submit.call(form);}catch(error){reset();throw error;}}};
    document.addEventListener('submit',event=>{if(forms.has(event.target)&&isWrite(event.target,event.submitter)){event.preventDefault();event.stopImmediatePropagation();}},true);
    window.addEventListener('submit',event=>{
        const form=event.target;
        if(!(form instanceof HTMLFormElement)||event.defaultPrevented)return;
        if(!form.noValidate&&!event.submitter?.formNoValidate&&!form.checkValidity())return;
        beginForm(form,event.submitter);
    });
    window.addEventListener('pageshow',event=>{if(event.persisted)reset();});
})();
