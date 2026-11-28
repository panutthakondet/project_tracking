// Three.js is loaded only when this dashboard chart enters the viewport.
const instances=new Map();const loading=new WeakSet();let threePromise;
const loadThree=()=>threePromise ||= import('../lib/three/three.module.min.js');
const reduced=matchMedia('(prefers-reduced-motion: reduce)');
const createChart=(THREE,root)=>{
    let disposed=false,frame=0,hovered=null,animation=null,renderCount=0;
    const renderer=new THREE.WebGLRenderer({alpha:true,antialias:true,powerPreference:'low-power'});
    renderer.setClearColor(0x000000,0);renderer.setPixelRatio(Math.min(devicePixelRatio||1,1.5));
    renderer.shadowMap.enabled=true;renderer.shadowMap.type=THREE.PCFSoftShadowMap;
    renderer.toneMapping=THREE.NeutralToneMapping;renderer.toneMappingExposure=.95;
    renderer.domElement.className='attendance-three-canvas';renderer.domElement.setAttribute('aria-hidden','true');
    root.prepend(renderer.domElement);
    const tooltip=document.createElement('aside');tooltip.className='attendance-three-tooltip';tooltip.hidden=true;tooltip.setAttribute('role','tooltip');root.append(tooltip);
    const scene=new THREE.Scene();const group=new THREE.Group();scene.add(group);
    const camera=new THREE.PerspectiveCamera(34,1,.1,20);const elevation=40*Math.PI/180;
    camera.position.set(0,.12+Math.sin(elevation)*3.5,Math.cos(elevation)*3.5);camera.lookAt(0,.12,0);
    scene.add(new THREE.AmbientLight(0xffffff,.3));
    const key=new THREE.DirectionalLight(0xffffff,1.6);key.position.set(-3,5,3);key.castShadow=true;key.shadow.mapSize.set(512,512);key.shadow.camera.left=-2;key.shadow.camera.right=2;key.shadow.camera.top=2;key.shadow.camera.bottom=-2;key.shadow.camera.near=.5;key.shadow.camera.far=12;key.shadow.bias=-.0005;key.shadow.normalBias=.015;key.shadow.radius=3;scene.add(key);
    const fill=new THREE.DirectionalLight(0xffffff,.3);fill.position.set(3,2,-3);scene.add(fill);
    const floor=new THREE.Mesh(new THREE.PlaneGeometry(5,5),new THREE.ShadowMaterial({opacity:.25}));floor.rotation.x=-Math.PI/2;floor.position.y=-.035;floor.receiveShadow=true;scene.add(floor);
    // Procedural studio reflections: no image textures or background grid.
    const studio=new THREE.Scene();studio.background=new THREE.Color(0x333333);
    const panels=[new THREE.Mesh(new THREE.BoxGeometry(4,.1,3),new THREE.MeshBasicMaterial({color:new THREE.Color(3,3,3)})),new THREE.Mesh(new THREE.BoxGeometry(.1,3,2),new THREE.MeshBasicMaterial({color:0xddddff}))];
    panels[0].position.set(-1,3,-2);panels[1].position.set(3,1,-1);panels.forEach(p=>studio.add(p));
    const pmrem=new THREE.PMREMGenerator(renderer);const environment=pmrem.fromScene(studio,.06,.1,10);scene.environment=environment.texture;pmrem.dispose();panels.forEach(p=>{p.geometry.dispose();p.material.dispose();});
    const meshes=[];const label=root.querySelector(':scope > div');const raycaster=new THREE.Raycaster();const pointer=new THREE.Vector2();
    const cssColor=name=>{const probe=document.createElement('i');probe.style.color=`var(${name})`;root.append(probe);const color=getComputedStyle(probe).color;probe.remove();return color;};
    const draw=()=>{if(disposed)return;renderer.render(scene,camera);renderCount++;};
    const cancel=()=>{if(frame)cancelAnimationFrame(frame);frame=0;animation=null;group.scale.setScalar(1);};
    const tick=now=>{
        frame=0;if(disposed||!animation)return;
        const t=Math.min(1,(now-animation.start)/animation.duration);const ease=1-Math.pow(1-t,3);
        if(animation.kind==='intro')group.scale.setScalar(.92+.08*ease);
        else meshes.forEach((mesh,i)=>mesh.position.y=animation.from[i]+((mesh===hovered ? .09 : 0)-animation.from[i])*ease);
        draw();if(t<1)frame=requestAnimationFrame(tick);else{animation=null;group.scale.setScalar(1);}
    };
    const lift=mesh=>{
        if(mesh===hovered)return;cancel();hovered=mesh;
        if(reduced.matches){meshes.forEach(m=>m.position.y=m===mesh ? .09 : 0);draw();return;}
        animation={kind:'hover',start:performance.now(),duration:180,from:meshes.map(m=>m.position.y)};frame=requestAnimationFrame(tick);
    };
    const clearMeshes=()=>{for(const mesh of meshes){group.remove(mesh);mesh.geometry.dispose();mesh.material.dispose();}meshes.length=0;};
    const build=()=>{
        cancel();clearMeshes();hovered=null;tooltip.hidden=true;
        const recorded=Math.max(0,Number(root.dataset.recorded)||0),expected=Math.max(0,Number(root.dataset.expected)||0);
        const rows=[{label:'ลงเวลาแล้ว',count:recorded,color:cssColor('--pt-chart-success')},{label:'ยังไม่ลงเวลา',count:Math.max(0,expected-recorded),color:cssColor('--pt-chart-muted')}].filter(row=>row.count>0);
        const total=rows.reduce((sum,row)=>sum+row.count,0);let angle=Math.PI/2;
        const data=rows;
        data.forEach(row=>{
            const span=total?row.count/total*Math.PI*2:Math.PI*2;const gap=rows.length>1?Math.min(.085,span*.25):0;
            const start=angle+gap/2,end=angle+span-gap/2;angle+=span;
            const shape=new THREE.Shape();
            if(span>=Math.PI*2-1e-7){shape.absarc(0,0,1,0,Math.PI*2,false);const hole=new THREE.Path();hole.absarc(0,0,.62,0,Math.PI*2,true);shape.holes.push(hole);}
            else{shape.absarc(0,0,1,start,end,false);shape.lineTo(.62*Math.cos(end),.62*Math.sin(end));shape.absarc(0,0,.62,end,start,true);shape.closePath();}
            const bevel=Math.min(.014,span*.02);
            const geometry=new THREE.ExtrudeGeometry(shape,{depth:.24,steps:1,bevelEnabled:true,bevelThickness:bevel,bevelSize:bevel,bevelSegments:4,curveSegments:64});geometry.rotateX(-Math.PI/2);
            const material=new THREE.MeshPhysicalMaterial({color:row.color,roughness:.25,metalness:.1,clearcoat:.7,clearcoatRoughness:.18,envMapIntensity:.4});
            const mesh=new THREE.Mesh(geometry,material);mesh.castShadow=true;mesh.receiveShadow=true;mesh.userData={...row,percent:total?row.count/total*100:0};group.add(mesh);meshes.push(mesh);
        });
        draw();
    };
    const resize=()=>{
        if(disposed)return;const {width,height}=root.getBoundingClientRect();if(!width||!height)return;
        renderer.setSize(width,height,false);camera.aspect=width/height;camera.updateProjectionMatrix();
        if(label){const center=new THREE.Vector3(0,.25,0).project(camera);label.style.left=`${(center.x+1)*50}%`;label.style.top=`${(1-center.y)*50}%`;}
        draw();
    };
    const onMove=event=>{
        const r=renderer.domElement.getBoundingClientRect();pointer.set((event.clientX-r.left)/r.width*2-1,-(event.clientY-r.top)/r.height*2+1);raycaster.setFromCamera(pointer,camera);
        const mesh=raycaster.intersectObjects(meshes,false)[0]?.object||null;lift(mesh);
        tooltip.hidden=!mesh;if(mesh){const d=mesh.userData;tooltip.textContent=`${d.label}: ${d.count.toLocaleString('th-TH')} คน-วัน (${d.percent.toLocaleString('th-TH',{maximumFractionDigits:1})}%)`;}
    };
    const onLeave=()=>{tooltip.hidden=true;lift(null);};
    renderer.domElement.addEventListener('pointermove',onMove);renderer.domElement.addEventListener('pointerleave',onLeave);
    const resizeObserver=new ResizeObserver(resize);resizeObserver.observe(root);
    const dataObserver=new MutationObserver(build);dataObserver.observe(root,{attributes:true,attributeFilter:['data-recorded','data-expected']});
    const destroy=()=>{
        if(disposed)return;disposed=true;cancel();resizeObserver.disconnect();dataObserver.disconnect();
        renderer.domElement.removeEventListener('pointermove',onMove);renderer.domElement.removeEventListener('pointerleave',onLeave);renderer.domElement.removeEventListener('webglcontextlost',onLost);
        clearMeshes();floor.geometry.dispose();floor.material.dispose();environment.dispose();key.shadow.dispose();renderer.dispose();renderer.forceContextLoss();renderer.domElement.remove();tooltip.remove();root.classList.remove('attendance-three-ready');if(label){label.style.removeProperty('left');label.style.removeProperty('top');}instances.delete(root);
    };
    const onLost=event=>{event.preventDefault();root.setAttribute('data-donut-fallback','');destroy();};renderer.domElement.addEventListener('webglcontextlost',onLost);
    const refreshTheme=()=>{if(disposed)return;meshes.forEach(mesh=>mesh.material.color.set(cssColor(mesh.userData.label==='ลงเวลาแล้ว'?'--pt-chart-success':'--pt-chart-muted')));draw();};
    build();resize();root.classList.add('attendance-three-ready');
    if(!reduced.matches){animation={kind:'intro',start:performance.now(),duration:700};frame=requestAnimationFrame(tick);}
    return {destroy,refreshTheme,diagnostics:()=>({segments:meshes.map(m=>({count:m.userData.count,percent:m.userData.percent,type:m.geometry.type,material:m.material.type,depth:m.geometry.parameters.options.depth,roughness:m.material.roughness,metalness:m.material.metalness,lift:m.position.y})),pixelRatio:renderer.getPixelRatio(),renderCount,animating:!!frame})};
};
const init=async root=>{
    if(instances.has(root)||loading.has(root)||!root.isConnected||root.hasAttribute('data-donut-fallback'))return;loading.add(root);
    try{const THREE=await loadThree();if(root.isConnected&&!root.hasAttribute('data-donut-fallback')&&!instances.has(root))instances.set(root,createChart(THREE,root));}catch(error){root.setAttribute('data-donut-fallback','');console.warn('Attendance 3D chart unavailable; keeping original chart.',error);}finally{loading.delete(root);}
};
const visible=new IntersectionObserver(entries=>entries.forEach(e=>{if(e.isIntersecting)init(e.target);}),{rootMargin:'100px'});
const scan=node=>{if(node instanceof Element&&node.matches('[data-attendance-three]'))visible.observe(node);node.querySelectorAll?.('[data-attendance-three]').forEach(root=>visible.observe(root));};scan(document);
const mutations=new MutationObserver(records=>{
    records.forEach(record=>record.addedNodes.forEach(scan));
    for(const [root,instance]of instances)if(!root.isConnected){visible.unobserve(root);instance.destroy();}
});mutations.observe(document.querySelector('.dashboard-view')||document.body,{childList:true,subtree:true});
const refreshThemes=()=>instances.forEach(instance=>instance.refreshTheme());
new MutationObserver(records=>{if(records.some(r=>r.target.id==='pt-user-theme'||r.target.parentElement?.id==='pt-user-theme'||[...r.addedNodes].some(n=>n.id==='pt-user-theme')))refreshThemes();}).observe(document.head,{childList:true,subtree:true,characterData:true});
new MutationObserver(refreshThemes).observe(document.documentElement,{attributes:true,attributeFilter:['class','style','data-theme']});
addEventListener('pagehide',()=>[...instances.values()].forEach(instance=>instance.destroy()));
addEventListener('pageshow',()=>document.querySelectorAll('[data-attendance-three]').forEach(root=>{visible.unobserve(root);visible.observe(root);}));
