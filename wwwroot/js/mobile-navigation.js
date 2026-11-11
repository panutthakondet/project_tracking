(()=>{
    document.documentElement.classList.add('mobile-menu-js');
    const init=()=>{
        const sidebar=document.querySelector('.v2-sidebar'),toggle=document.getElementById('mobileMenuToggle');
        if(!sidebar||!toggle)return;
        const mobile=matchMedia('(max-width:720px)');
        const setOpen=open=>{
            sidebar.classList.toggle('mobile-menu-open',open);
            toggle.setAttribute('aria-expanded',String(open));
            toggle.setAttribute('aria-label',open?'ปิดเมนูหลัก':'เปิดเมนูหลัก');
            if(!open)sidebar.querySelectorAll('[data-bs-toggle="dropdown"]').forEach(button=>window.bootstrap?.Dropdown.getInstance(button)?.hide());
    };
    toggle.addEventListener('click',()=>setOpen(!sidebar.classList.contains('mobile-menu-open')));
    document.addEventListener('keydown',event=>{if(event.key==='Escape'&&mobile.matches&&sidebar.classList.contains('mobile-menu-open')){event.preventDefault();event.stopPropagation();setOpen(false);toggle.focus();}},true);
    document.addEventListener('pointerdown',event=>{if(mobile.matches&&!sidebar.contains(event.target))setOpen(false);});
    mobile.addEventListener('change',()=>setOpen(false));
    setOpen(false);
    };
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',init,{once:true});else init();
})();
