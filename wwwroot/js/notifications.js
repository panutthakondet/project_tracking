(()=>{
    const panel=document.getElementById('notificationPanel');if(!panel)return;
    const bells=[...document.querySelectorAll('[data-notification-bell]')],list=panel.querySelector('[data-notification-list]'),status=panel.querySelector('[data-notification-status]');
    const more=panel.querySelector('[data-notification-more]'),enable=panel.querySelector('[data-push-enable]'),disable=panel.querySelector('[data-push-disable]');
    let beforeId=null,busy=false,queuedReload=false,opener=null;
    const csrf=()=>document.querySelector('#notificationsAntiforgery input[name="__RequestVerificationToken"]').value;
    const api=async(url,body)=>{const response=await fetch(url,{credentials:'same-origin',cache:'no-store',...(body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':csrf()},body:JSON.stringify(body)})});if(response.status===401){counts(0);list.replaceChildren();throw new Error('กรุณาเข้าสู่ระบบใหม่');}if(!response.ok)throw new Error('ดำเนินการไม่สำเร็จ กรุณาลองอีกครั้ง');return response;};
    const close=()=>{panel.hidden=true;bells.forEach(b=>b.setAttribute('aria-expanded','false'));};
    const counts=n=>document.querySelectorAll('[data-notification-count]').forEach(e=>{e.hidden=n===0;e.textContent=n>99?'99+':String(n);e.parentElement.setAttribute('aria-label',n?`การแจ้งเตือนที่ยังไม่อ่าน ${n} รายการ`:'เปิดการแจ้งเตือน');});
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
            status.textContent=list.children.length?'':'ยังไม่มีการแจ้งเตือน';
        }catch(error){status.textContent=error.message;}finally{busy=false;more.disabled=false;if(queuedReload){queuedReload=false;load(false);}}
    };
    bells.forEach(b=>b.addEventListener('click',()=>{if(!panel.hidden){close();return;}opener=b;panel.hidden=false;bells.forEach(x=>x.setAttribute('aria-expanded','true'));panel.querySelector('[data-notification-close]').focus();load(false);pushState();}));
    panel.querySelector('[data-notification-close]').addEventListener('click',()=>{close();opener?.focus();});
    document.addEventListener('pointerdown',event=>{if(!panel.contains(event.target)&&!bells.some(b=>b.contains(event.target)))close();});
    document.addEventListener('keydown',event=>{if(event.key==='Escape'&&!panel.hidden){event.stopImmediatePropagation();close();opener?.focus();}},true);
    more.addEventListener('click',()=>load(true));
    panel.querySelector('[data-notification-read-all]').addEventListener('click',async()=>{try{await api(panel.dataset.readUrl+'?all=true',{});load(false);}catch(error){status.textContent=error.message;}});
    const supported=()=>isSecureContext&&'serviceWorker' in navigator&&'PushManager' in window&&'Notification' in window;
    const registration=()=>Promise.race([navigator.serviceWorker.ready,new Promise((_,reject)=>setTimeout(()=>reject(new Error('ระบบแจ้งเตือนยังไม่พร้อม กรุณาเปิดแอปใหม่แล้วลองอีกครั้ง')),10000))]);
    const pushState=async()=>{if(!supported()){enable.disabled=true;enable.textContent='อุปกรณ์นี้ยังไม่รองรับ Push';disable.hidden=true;return;}try{const sub=await(await registration()).pushManager.getSubscription();enable.hidden=!!sub&&Notification.permission==='granted';disable.hidden=!enable.hidden;}catch{enable.hidden=false;disable.hidden=true;}};
    const keyBytes=value=>Uint8Array.from(atob(value.replace(/-/g,'+').replace(/_/g,'/').padEnd(Math.ceil(value.length/4)*4,'=')),c=>c.charCodeAt(0));
    const save=async sub=>api(panel.dataset.subscribeUrl,sub.toJSON());
    enable.addEventListener('click',async()=>{
        if(!supported())return;enable.disabled=true;
        try{
            // Permission is requested only from this deliberate user action (including iOS Home Screen apps).
            if(await Notification.requestPermission()!=='granted')throw new Error('ยังไม่ได้อนุญาตแจ้งเตือน กรุณาเปิดสิทธิ์ในตั้งค่าของเบราว์เซอร์หรือแอป');
            const key=await(await api(panel.dataset.pushKeyUrl)).json(),reg=await registration();
            let sub=await reg.pushManager.getSubscription();
            if(sub){const existing=sub.options.applicationServerKey;if(existing&&Array.from(new Uint8Array(existing)).join()!==Array.from(keyBytes(key.publicKey)).join()){await sub.unsubscribe();sub=null;}}
            sub ||= await reg.pushManager.subscribe({userVisibleOnly:true,applicationServerKey:keyBytes(key.publicKey)});
            await save(sub);status.textContent='เปิดแจ้งเตือนบนอุปกรณ์นี้แล้ว';await pushState();
        }catch(error){status.textContent=error.message;}finally{enable.disabled=false;}
    });
    disable.addEventListener('click',async()=>{disable.disabled=true;try{const sub=await(await registration()).pushManager.getSubscription();if(sub){await api(panel.dataset.unsubscribeUrl,{endpoint:sub.endpoint});await sub.unsubscribe();}status.textContent='ปิดแจ้งเตือนอุปกรณ์นี้แล้ว';await pushState();}catch(error){status.textContent=error.message;}finally{disable.disabled=false;}});
    // Refresh an already-consented subscription for the current account/session, never ask automatically.
    if(supported()&&Notification.permission==='granted')registration().then(r=>r.pushManager.getSubscription()).then(sub=>sub&&save(sub)).catch(()=>{});
    load(false);
    setInterval(()=>{if(document.visibilityState==='visible'&&panel.hidden)load(false);},30000);
    document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='visible')load(false);});
    navigator.serviceWorker?.addEventListener('message',event=>{if(event.data?.type==='notification:new')load(false);});
})();
