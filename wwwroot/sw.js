// Cache only public PWA resources. Authenticated pages, API responses and uploads stay network-only.
const CACHE='projecttracking-pwa-v1';
const publicFiles=['offline.html','pwa/icon-192.png','pwa/icon-512.png','pwa/icon-maskable-512.png','pwa/apple-touch-icon.png'];
const scoped=path=>new URL(path,self.registration.scope).href;
self.addEventListener('install',event=>{
    event.waitUntil(caches.open(CACHE).then(cache=>cache.addAll(publicFiles.map(scoped))));
    // Do not skipWaiting: an update must not reload an unsaved note or form.
});
self.addEventListener('activate',event=>{
    event.waitUntil((async()=>{
        const keys=await caches.keys();
        await Promise.all(keys.filter(key=>key.startsWith('projecttracking-pwa-')&&key!==CACHE).map(key=>caches.delete(key)));
        await self.clients.claim();
    })());
});
self.addEventListener('fetch',event=>{
    const request=event.request,url=new URL(request.url);
    if(request.method!=='GET'||url.origin!==self.location.origin)return;
    if(request.mode==='navigate'){
        event.respondWith(fetch(request,{cache:'no-store'}).catch(async()=>{
            const cache=await caches.open(CACHE);
            return await cache.match(scoped('offline.html'))||Response.error();
        }));
        return;
    }
    if(publicFiles.some(path=>scoped(path)===url.href)){
        event.respondWith(caches.open(CACHE).then(async cache=>await cache.match(request)||fetch(request)));
    }
});
