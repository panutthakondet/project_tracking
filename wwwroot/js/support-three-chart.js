const charts=new Map(),pending=new WeakSet();let library;
const colors={OPEN:'#2996F3',WIP:'#FF9F1C',FIXED:'#14D6C0',FAIL:'#FF4560',PASS:'#80FF45',REJECT:'#9655FF'};
const geometrySettings=Object.freeze({outerRadius:2.5,innerRadius:1,depth:.65,bevelThickness:.045,bevelSize:.045,bevelSegments:4,curveSegments:64});
const reduced=matchMedia('(prefers-reduced-motion: reduce)');
const create=(THREE,root)=>{
    let dead=false,frame=0,animation=null,hover=null,renderCount=0;
    const host=root.closest('.support-three-overview');const label=root.querySelector('.support-three-label');
    const renderer=new THREE.WebGLRenderer({alpha:true,antialias:true,powerPreference:'low-power'});
    renderer.setClearColor(0,0);renderer.setPixelRatio(Math.min(devicePixelRatio||1,1.5));renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1;
    renderer.shadowMap.enabled=true;renderer.shadowMap.type=THREE.PCFSoftShadowMap;
    const canvas=renderer.domElement;canvas.className='support-three-canvas';canvas.tabIndex=0;canvas.setAttribute('role','img');canvas.setAttribute('aria-label','กราฟสถานะ Support ใช้ปุ่มลูกศรซ้ายขวาเพื่อดูแต่ละสถานะ');root.prepend(canvas);
    const tooltip=document.createElement('aside');tooltip.className='support-three-tooltip';tooltip.hidden=true;tooltip.setAttribute('role','tooltip');root.append(tooltip);
    const scene=new THREE.Scene(),group=new THREE.Group();scene.add(group);
    const camera=new THREE.PerspectiveCamera(35,1,.1,40);
    camera.zoom=1.2; // Match the visible ring width of the neighbouring Issues SVG.
    camera.up.set(0,0,1);camera.position.set(0,-7,7);camera.lookAt(0,0,0);
    scene.add(new THREE.AmbientLight(0xffffff,.4));
    const hemisphere=new THREE.HemisphereLight(0xe6f4ff,0x18212c,.65);hemisphere.position.set(0,0,1);scene.add(hemisphere);
    const key=new THREE.DirectionalLight(0xffffff,2.1);key.position.set(-5,-4,9);key.castShadow=true;key.shadow.mapSize.set(512,512);Object.assign(key.shadow.camera,{left:-4.5,right:4.5,top:4.5,bottom:-4.5,near:.5,far:25});key.shadow.bias=-.0005;key.shadow.normalBias=.015;key.shadow.radius=3;scene.add(key);
    const fill=new THREE.DirectionalLight(0x99caff,.35);fill.position.set(4,3,4);scene.add(fill);
    const floor=new THREE.Mesh(new THREE.PlaneGeometry(20,20),new THREE.ShadowMaterial({opacity:.25}));floor.position.z=-.06;floor.receiveShadow=true;scene.add(floor);
    const studio=new THREE.Scene();studio.background=new THREE.Color(0x333333);
    const panels=[new THREE.Mesh(new THREE.BoxGeometry(8,4,.1),new THREE.MeshBasicMaterial({color:new THREE.Color(3,3,3)})),new THREE.Mesh(new THREE.BoxGeometry(.1,4,5),new THREE.MeshBasicMaterial({color:0xddddff}))];panels[0].position.set(0,-1,7);panels[1].position.set(-6,-3,3);panels.forEach(p=>studio.add(p));
    const pmrem=new THREE.PMREMGenerator(renderer),environment=pmrem.fromScene(studio,.06,.1,20);scene.environment=environment.texture;pmrem.dispose();panels.forEach(p=>{p.geometry.dispose();p.material.dispose();});
    const meshes=[],pointer=new THREE.Vector2(),raycaster=new THREE.Raycaster();let statusSum=0,supportTotal=0;
    const draw=()=>{if(!dead){renderer.render(scene,camera);renderCount++;}};
    const stop=()=>{if(frame)cancelAnimationFrame(frame);frame=0;animation=null;group.scale.setScalar(1);};
    // Geometry lives in XY, with extrusion along Z. Radial separation is a Mesh translation.
    const target=mesh=>{const distance=mesh.userData.explode+(mesh===hover ? .1 : 0);return new THREE.Vector3(Math.cos(mesh.userData.mid)*distance,Math.sin(mesh.userData.mid)*distance,0);};
    const tick=now=>{
        frame=0;if(dead||!animation)return;
        const t=Math.min(1,(now-animation.start)/animation.duration),ease=1-Math.pow(1-t,3);
        if(animation.kind==='intro')group.scale.setScalar(.92+.08*ease);
        else meshes.forEach((mesh,index)=>mesh.position.lerpVectors(animation.from[index],target(mesh),ease));
        draw();if(t<1)frame=requestAnimationFrame(tick);else animation=null;
    };
    const select=mesh=>{
        if(mesh!==hover){stop();hover=mesh;if(reduced.matches){meshes.forEach(m=>m.position.copy(target(m)));draw();}else{animation={kind:'hover',start:performance.now(),duration:200,from:meshes.map(m=>m.position.clone())};frame=requestAnimationFrame(tick);}}
        tooltip.hidden=!mesh;
        if(mesh){const d=mesh.userData;tooltip.textContent=`${d.status}: ${d.count.toLocaleString('th-TH')} รายการ (${d.percent.toLocaleString('th-TH',{maximumFractionDigits:1})}%${statusSum!==supportTotal?' ของผลรวมสถานะ':''})${statusSum!==supportTotal?' · Support จริง '+supportTotal.toLocaleString('th-TH')+' รายการ สถานะอาจซ้อนกัน':''}`;}
    };
    const clear=()=>{meshes.forEach(mesh=>{group.remove(mesh);mesh.geometry.dispose();mesh.material.dispose();});meshes.length=0;};
    const build=()=>{
        stop();clear();hover=null;tooltip.hidden=true;
        supportTotal=Math.max(0,Number(root.dataset.supportTotal)||0);
        const rows=[...host.querySelectorAll('[data-support-metric]')].map(node=>({status:node.dataset.status,count:Math.max(0,Number(node.dataset.count)||0)})).filter(row=>row.count>0);
        statusSum=rows.reduce((sum,row)=>sum+row.count,0);let cursor=Math.PI/2;
        root.setAttribute('aria-label',`Support ทั้งหมด ${supportTotal} รายการ${statusSum!==supportTotal?' ผลรวมสถานะ '+statusSum+' รายการ เนื่องจากสถานะอาจซ้อนกัน':''}`);
        label.querySelector('strong').textContent=String(supportTotal);label.querySelector('strong').style.fontSize=supportTotal>=10000?'18px':'';
        // Rotate the entire distribution so a readable, smaller slice faces the camera.
        // This highlights an actual separate piece instead of pulling almost the whole ring
        // when one status dominates. Rotation never changes counts or sweep angles.
        if(rows.length>1){
            const candidate=rows.filter(row=>row.count/statusSum>=.05&&row.count/statusSum<=.5).sort((a,b)=>a.count-b.count)[0];
            if(candidate){const before=rows.slice(0,rows.indexOf(candidate)).reduce((sum,row)=>sum+row.count,0);cursor=-Math.PI/2-(before+candidate.count/2)/statusSum*Math.PI*2;}
        }
        const slices=rows.map(row=>{const span=row.count/statusSum*Math.PI*2,start=cursor,mid=start+span/2;cursor+=span;return {...row,start,end:start+span,span,mid};});
        // Camera faces the -Y side: pull the front-facing slice out as the emphasis.
        const front=rows.length>1?slices.reduce((best,slice)=>Math.sin(slice.mid)<Math.sin(best.mid)?slice:best,slices[0]):null;
        slices.forEach((row,index)=>{
            const {start,end,span,mid}=row,{outerRadius,innerRadius}=geometrySettings;
            const shape=new THREE.Shape();if(span>=Math.PI*2-1e-7){shape.absarc(0,0,outerRadius,0,Math.PI*2,false);const hole=new THREE.Path();hole.absarc(0,0,innerRadius,0,Math.PI*2,true);shape.holes.push(hole);}else{shape.absarc(0,0,outerRadius,start,end,false);shape.lineTo(innerRadius*Math.cos(end),innerRadius*Math.sin(end));shape.absarc(0,0,innerRadius,end,start,true);shape.closePath();}
            // Inward bevel keeps adjacent outlines from growing into each other. Only cap
            // its horizontal width for extremely narrow data slices; never enlarge their angles.
            const bevelSize=Math.min(geometrySettings.bevelSize,Math.sin(Math.min(span,Math.PI)/2)*innerRadius*.35);
            const geometry=new THREE.ExtrudeGeometry(shape,{depth:geometrySettings.depth,steps:1,bevelEnabled:true,bevelThickness:geometrySettings.bevelThickness,bevelSize,bevelOffset:-bevelSize,bevelSegments:geometrySettings.bevelSegments,curveSegments:geometrySettings.curveSegments});
            const material=new THREE.MeshPhysicalMaterial({color:colors[row.status]||[0x39b9ee,0xb17af3,0xe882b5][index%3],roughness:.35,metalness:.05,clearcoat:.25,clearcoatRoughness:.3,envMapIntensity:.6});
            const mesh=new THREE.Mesh(geometry,material);mesh.castShadow=true;mesh.receiveShadow=true;mesh.userData={...row,percent:row.count/statusSum*100,highlighted:row===front,explode:rows.length>1?(row===front ? .45 : .12):0};mesh.position.copy(target(mesh));group.add(mesh);meshes.push(mesh);
        });draw();
    };
    const resize=()=>{if(dead)return;const {width,height}=root.getBoundingClientRect();if(!width||!height)return;renderer.setSize(width,height,false);camera.aspect=width/height;camera.updateProjectionMatrix();const center=new THREE.Vector3(0,0,.2).project(camera);label.style.left=`${(center.x+1)*50}%`;label.style.top=`${(1-center.y)*50}%`;draw();};
    const move=event=>{const r=canvas.getBoundingClientRect();pointer.set((event.clientX-r.left)/r.width*2-1,-(event.clientY-r.top)/r.height*2+1);raycaster.setFromCamera(pointer,camera);select(raycaster.intersectObjects(meshes,false)[0]?.object||null);};
    const leave=()=>select(null);
    const keyboard=event=>{if(event.key==='Escape'){select(null);return;}if(!['ArrowLeft','ArrowRight'].includes(event.key)||!meshes.length)return;event.preventDefault();const index=meshes.indexOf(hover),next=index<0?(event.key==='ArrowRight'?0:meshes.length-1):(index+(event.key==='ArrowRight'?1:-1)+meshes.length)%meshes.length;select(meshes[next]);};
    canvas.addEventListener('pointermove',move);canvas.addEventListener('pointerleave',leave);canvas.addEventListener('keydown',keyboard);canvas.addEventListener('blur',leave);
    const resizeObserver=new ResizeObserver(resize);resizeObserver.observe(root);
    const dataObserver=new MutationObserver(build);dataObserver.observe(host,{subtree:true,attributes:true,attributeFilter:['data-count','data-status','data-support-total']});
    const destroy=()=>{if(dead)return;dead=true;stop();resizeObserver.disconnect();dataObserver.disconnect();canvas.removeEventListener('pointermove',move);canvas.removeEventListener('pointerleave',leave);canvas.removeEventListener('keydown',keyboard);canvas.removeEventListener('blur',leave);canvas.removeEventListener('webglcontextlost',lost);clear();floor.geometry.dispose();floor.material.dispose();environment.dispose();key.shadow.dispose();renderer.dispose();renderer.forceContextLoss();canvas.remove();tooltip.remove();root.classList.remove('support-three-ready');label.style.removeProperty('left');label.style.removeProperty('top');charts.delete(root);};
    const lost=event=>{event.preventDefault();destroy();};canvas.addEventListener('webglcontextlost',lost);
    try{build();resize();root.classList.add('support-three-ready');if(!reduced.matches){animation={kind:'intro',start:performance.now(),duration:700};frame=requestAnimationFrame(tick);}}catch(error){destroy();throw error;}
    return {destroy,diagnostics:()=>({supportTotal,statusSum,pixelRatio:renderer.getPixelRatio(),renderCount,animating:!!frame,camera:camera.position.toArray(),fov:camera.fov,geometrySettings,segments:meshes.map(m=>({status:m.userData.status,count:m.userData.count,percent:m.userData.percent,color:m.material.color.getHexString(),roughness:m.material.roughness,metalness:m.material.metalness,clearcoat:m.material.clearcoat,clearcoatRoughness:m.material.clearcoatRoughness,geometry:m.geometry.type,material:m.material.type,options:m.geometry.parameters.options,start:m.userData.start,sweep:m.userData.span,mid:m.userData.mid,position:m.position.toArray(),highlighted:m.userData.highlighted,radial:Math.hypot(m.position.x,m.position.y)}))})};
};
const init=async root=>{if(charts.has(root)||pending.has(root)||!root.isConnected)return;pending.add(root);try{const THREE=await(library ||= import('../lib/three/three.module.min.js'));if(root.isConnected&&!charts.has(root))charts.set(root,create(THREE,root));}catch(error){console.warn('Support 3D chart unavailable; keeping SVG fallback.',error);}finally{pending.delete(root);}};
const visible=new IntersectionObserver(entries=>entries.forEach(e=>{if(e.isIntersecting)init(e.target);} ),{rootMargin:'100px'});
const scan=node=>{if(node instanceof Element&&node.matches('[data-support-three]'))visible.observe(node);node.querySelectorAll?.('[data-support-three]').forEach(root=>visible.observe(root));};scan(document);
new MutationObserver(records=>{records.forEach(r=>r.addedNodes.forEach(scan));for(const[root,instance]of charts)if(!root.isConnected){visible.unobserve(root);instance.destroy();}}).observe(document.querySelector('.dashboard-view')||document.body,{childList:true,subtree:true});
addEventListener('pagehide',()=>[...charts.values()].forEach(chart=>chart.destroy()));
addEventListener('pageshow',()=>document.querySelectorAll('[data-support-three]').forEach(root=>{visible.unobserve(root);visible.observe(root);}));
