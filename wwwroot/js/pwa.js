(()=>{
    const workerUrl=document.currentScript?.dataset.serviceWorkerUrl;
    let installEvent;
    const standalone=()=>matchMedia('(display-mode: fullscreen)').matches||matchMedia('(display-mode: standalone)').matches||navigator.standalone===true;
    const buttons=()=>document.querySelectorAll('[data-pwa-install]');
    const update=()=>buttons().forEach(button=>button.hidden=standalone()||!installEvent&&!isIos);
    const isIos=/iPad|iPhone|iPod/.test(navigator.userAgent)||(navigator.platform==='MacIntel'&&navigator.maxTouchPoints>1);
    window.addEventListener('beforeinstallprompt',event=>{event.preventDefault();installEvent=event;update();});
    window.addEventListener('appinstalled',()=>{installEvent=null;update();});
    ['standalone','fullscreen'].forEach(mode=>matchMedia(`(display-mode: ${mode})`).addEventListener('change',update));
    const init=()=>{
        buttons().forEach(button=>button.addEventListener('click',async()=>{
            if(standalone())return;
            if(installEvent){const prompt=installEvent;installEvent=null;update();try{await prompt.prompt();await prompt.userChoice;}catch(error){console.warn('PWA install prompt unavailable.',error);}}
            else if(isIos)window.alert('ติดตั้ง ProjectTracking: เปิดใน Safari แล้วกดปุ่มแชร์ จากนั้นเลือก “เพิ่มไปยังหน้าจอโฮม” และกด “เพิ่ม”');
        }));
        update();
        if(workerUrl&&window.isSecureContext&&'serviceWorker' in navigator){navigator.serviceWorker.register(workerUrl,{updateViaCache:'none'}).catch(error=>console.warn('PWA service worker registration failed.',error));}
    };
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',init,{once:true});else init();
})();
