// Runs in the head before dashboard markup can paint the legacy donut fallback.
(()=>{
    if(!('IntersectionObserver' in window))return;
    document.documentElement.classList.add('dashboard-donut-enhance');
    const selector='[data-project-status-donut], [data-overview-three], [data-support-three], [data-attendance-three]';
    const ready=root=>root.classList.contains('support-three-ready')||root.classList.contains('attendance-three-ready');
    const timers=new Map();
    const watch=()=>{
        const observer=new IntersectionObserver(entries=>entries.forEach(({target:root,isIntersecting})=>{
            if(!isIntersecting||ready(root)||root.hasAttribute('data-donut-fallback')||timers.has(root))return;
            // Failed module requests must not leave an empty chart indefinitely.
            timers.set(root,setTimeout(()=>{timers.delete(root);if(root.isConnected&&!ready(root))root.setAttribute('data-donut-fallback','');},12000));
        }),{rootMargin:'100px'});
        document.querySelectorAll(selector).forEach(root=>observer.observe(root));
        new MutationObserver(()=>{for(const[root,timer]of timers)if(ready(root)||!root.isConnected||root.hasAttribute('data-donut-fallback')){clearTimeout(timer);timers.delete(root);}}).observe(document.querySelector('.dashboard-view')||document.body,{subtree:true,attributes:true,attributeFilter:['class','data-donut-fallback'],childList:true});
        addEventListener('pageshow',()=>document.querySelectorAll(selector).forEach(root=>{observer.unobserve(root);observer.observe(root);}));
    };
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',watch,{once:true});else watch();
})();
