(()=>{
    const panel=document.getElementById('notificationPanel');if(!panel)return;
    const bells=[...document.querySelectorAll('[data-notification-bell]')],list=panel.querySelector('[data-notification-list]'),status=panel.querySelector('[data-notification-status]');
    const more=panel.querySelector('[data-notification-more]'),enable=panel.querySelector('[data-push-enable]'),disable=panel.querySelector('[data-push-disable]');
    let beforeId=null,busy=false,queuedReload=false,opener=null,actionStatus=false;
    const pushMessage=message=>{actionStatus=true;status.textContent=message;};
    const csrf=()=>document.querySelector('#notificationsAntiforgery input[name="__RequestVerificationToken"]').value;
    const fetchOptions={credentials:'same-origin',cache:'no-store'};
    const errorFor=async response=>{
        if(response.status===401){counts(0);list.replaceChildren();return new Error('กรุณาเข้าสู่ระบบใหม่ (HTTP 401)');}
        let problem=null;try{problem=await response.clone().json();}catch{}
        const defaults={400:'คำขอไม่ผ่านการตรวจสอบ กรุณารีเฟรชหน้าแล้วลองใหม่',403:'ไม่มีสิทธิ์ดำเนินการนี้',404:'ไม่พบ API บนเซิร์ฟเวอร์ กรุณาตรวจสอบเวอร์ชันที่ Deploy',503:'ระบบแจ้งเตือนบนเซิร์ฟเวอร์ยังไม่พร้อม'};
        const message=typeof problem==='string'?problem:problem?.title||defaults[response.status]||'เซิร์ฟเวอร์ดำเนินการไม่สำเร็จ';
        const error=new Error(`${message.slice(0,250)} (HTTP ${response.status}${problem?.code?', '+problem.code:''}${problem?.traceId?', อ้างอิง '+problem.traceId:''})`);
        error.recovered=problem?.recovered===true;return error;
    };
    const freshToken=async()=>{
        if(!panel.dataset.tokenUrl)return csrf();
        const response=await fetch(panel.dataset.tokenUrl,fetchOptions);
        if(!response.ok)throw await errorFor(response);
        const data=await response.json();if(!data.requestToken)throw new Error('ไม่พบรหัสยืนยัน กรุณารีเฟรชหน้า');
        document.querySelector('#notificationsAntiforgery input[name="__RequestVerificationToken"]').value=data.requestToken;
        return data.requestToken;
    };
    const api=async(url,body,retry=true)=>{
        let response;
        try{response=await fetch(url,{...fetchOptions,...(body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':await freshToken()},body:JSON.stringify(body)})});}
        catch(error){if(error instanceof TypeError)throw new Error('ไม่สามารถติดต่อเซิร์ฟเวอร์แจ้งเตือนได้ กรุณาลองอีกครั้ง');throw error;}
        if(!response.ok){const error=await errorFor(response);if(retry&&error.recovered)return api(url,body,false);throw error;}
        if(body===undefined&&response.redirected&&new URL(response.url).pathname.toLowerCase().includes('/auth/login'))throw new Error('กรุณาเข้าสู่ระบบใหม่');
        return response;
    };
    const close=()=>{panel.hidden=true;bells.forEach(b=>b.setAttribute('aria-expanded','false'));};
    const counts=n=>document.querySelectorAll('[data-notification-count]').forEach(e=>{e.hidden=n===0;e.textContent=n>99?'99+':String(n);e.closest('[data-notification-bell]').setAttribute('aria-label',n?`การแจ้งเตือนที่ยังไม่อ่าน ${n} รายการ`:'เปิดการแจ้งเตือน');});
    const load=async append=>{
        if(busy){if(!append)queuedReload=true;return;}busy=true;more.disabled=true;
        try{
            const url=new URL(panel.dataset.listUrl,location.origin);if(append&&beforeId)url.searchParams.set('beforeId',beforeId);
            const data=await(await api(url)).json();counts(data.unread);
            if(!append)list.replaceChildren();
            for(const item of data.items){
                const a=document.createElement('a');a.className='notification-item'+(item.isRead?' is-read':'');a.href=item.url;
                const title=document.createElement('strong');title.textContent=item.title;
                const message=document.createElement('p');message.textContent=item.message;
                const time=document.createElement('time');time.dateTime=item.createdAt;time.textContent=new Date(item.createdAt).toLocaleString('th-TH',{dateStyle:'medium',timeStyle:'short'});
                a.append(title,message,time);a.addEventListener('click',async event=>{if(event.ctrlKey||event.metaKey||event.shiftKey||event.altKey||event.button!==0)return;event.preventDefault();try{await api(panel.dataset.readUrl+'?id='+encodeURIComponent(item.id),{});location.assign(a.href);}catch(error){status.textContent=error.message;}});list.append(a);
            }
            beforeId=data.items.at(-1)?.id||beforeId;more.hidden=!data.hasMore;
            if(!actionStatus)status.textContent=list.children.length?'':'ยังไม่มีการแจ้งเตือน';
        }catch(error){if(!actionStatus)status.textContent=error.message;}finally{busy=false;more.disabled=false;if(queuedReload){queuedReload=false;load(false);}}
    };
    bells.forEach(b=>b.addEventListener('click',()=>{if(!panel.hidden){close();return;}opener=b;actionStatus=false;status.textContent='';panel.hidden=false;bells.forEach(x=>x.setAttribute('aria-expanded','true'));panel.querySelector('[data-notification-close]').focus();load(false);pushState();}));
    panel.querySelector('[data-notification-close]').addEventListener('click',()=>{close();opener?.focus();});
    document.addEventListener('pointerdown',event=>{if(!panel.contains(event.target)&&!bells.some(b=>b.contains(event.target)))close();});
    document.addEventListener('keydown',event=>{if(event.key==='Escape'&&!panel.hidden){event.stopImmediatePropagation();close();opener?.focus();}},true);
    more.addEventListener('click',()=>load(true));
    panel.querySelector('[data-notification-read-all]').addEventListener('click',async()=>{try{await api(panel.dataset.readUrl+'?all=true',{});load(false);}catch(error){status.textContent=error.message;}});
    const supported=()=>isSecureContext&&'serviceWorker' in navigator&&'PushManager' in window&&'Notification' in window;
    const registration=()=>Promise.race([navigator.serviceWorker.ready,new Promise((_,reject)=>setTimeout(()=>reject(new Error('ระบบแจ้งเตือนยังไม่พร้อม กรุณาเปิดแอปใหม่แล้วลองอีกครั้ง')),10000))]);
    const pushState=async(saved=false)=>{
        if(!supported()){enable.disabled=true;enable.textContent='อุปกรณ์นี้ยังไม่รองรับ Push';disable.hidden=true;return;}
        let sub;
        try{
            sub=await(await registration()).pushManager.getSubscription();
            if(sub&&Notification.permission==='granted'){
                if(!saved)await save(sub);
                enable.hidden=true;disable.hidden=false;
            }else{enable.hidden=false;disable.hidden=true;}
        }catch(error){enable.hidden=false;disable.hidden=!sub;pushMessage('บันทึกอุปกรณ์: '+error.message);}
    };
    const keyBytes=value=>Uint8Array.from(atob(value.replace(/-/g,'+').replace(/_/g,'/').padEnd(Math.ceil(value.length/4)*4,'=')),c=>c.charCodeAt(0));
    const save=async sub=>api(panel.dataset.subscribeUrl,sub.toJSON());
    enable.addEventListener('click',async()=>{
        if(!supported())return;enable.disabled=true;
        let step='ขอสิทธิ์แจ้งเตือน';
        try{
            // Permission is requested only from this deliberate user action (including iOS Home Screen apps).
            if(await Notification.requestPermission()!=='granted')throw new Error('ยังไม่ได้อนุญาตแจ้งเตือน กรุณาเปิดสิทธิ์ในตั้งค่าของเบราว์เซอร์หรือแอป');
            step='ขอกุญแจ Push จากเซิร์ฟเวอร์';
            const key=await(await api(panel.dataset.pushKeyUrl)).json();
            step='เตรียมระบบแจ้งเตือนในแอป';
            const reg=await registration();
            step='สมัครรับ Push กับเบราว์เซอร์';
            let sub=await reg.pushManager.getSubscription();
            if(sub){const existing=sub.options.applicationServerKey;if(existing&&Array.from(new Uint8Array(existing)).join()!==Array.from(keyBytes(key.publicKey)).join()){await sub.unsubscribe();sub=null;}}
            sub ||= await reg.pushManager.subscribe({userVisibleOnly:true,applicationServerKey:keyBytes(key.publicKey)});
            step='บันทึกอุปกรณ์บนเซิร์ฟเวอร์';
            await save(sub);pushMessage('เปิดแจ้งเตือนบนอุปกรณ์นี้แล้ว');await pushState(true);
        }catch(error){pushMessage(step+': '+error.message);}finally{enable.disabled=false;}
    });
    disable.addEventListener('click',async()=>{disable.disabled=true;try{const sub=await(await registration()).pushManager.getSubscription();if(sub){await api(panel.dataset.unsubscribeUrl,{endpoint:sub.endpoint});await sub.unsubscribe();}pushMessage('ปิดแจ้งเตือนอุปกรณ์นี้แล้ว');await pushState();}catch(error){status.textContent=error.message;}finally{disable.disabled=false;}});
    // Refresh an already-consented subscription for the current account/session, never ask automatically.
    if(supported()&&Notification.permission==='granted')registration().then(r=>r.pushManager.getSubscription()).then(sub=>sub&&save(sub)).catch(()=>{});
    load(false);
    setInterval(()=>{if(document.visibilityState==='visible'&&panel.hidden)load(false);},30000);
    document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='visible')load(false);});
    navigator.serviceWorker?.addEventListener('message',event=>{if(event.data?.type==='notification:new')load(false);});
})();
