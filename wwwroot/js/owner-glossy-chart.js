// Enhance the existing HTML chart without changing its data, ordering or shared scroll container.
(()=>{
    const root=document.getElementById('ownerOverview');if(!root)return;
    root.querySelectorAll('.owner-overview-item').forEach(item=>{
        const name=item.querySelector('.owner-overview-member b')?.textContent.trim()||item.title;
        item.querySelectorAll('.owner-bar').forEach(bar=>{
            bar.title=`${name} · ${bar.title}`;
            bar.tabIndex=0;bar.setAttribute('role','img');bar.setAttribute('aria-label',bar.title);
        });
    });
    if('IntersectionObserver' in window){const observer=new IntersectionObserver(entries=>{if(entries.some(e=>e.isIntersecting)){root.classList.add('is-visible');observer.disconnect();}},{threshold:.08});observer.observe(root);}else root.classList.add('is-visible');
})();
