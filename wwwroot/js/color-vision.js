(()=>{
    const resolvedColors=new Map();
    window.addEventListener('pt:color-vision-change',()=>resolvedColors.clear());
    const tone=value=>({green:'success',lime:'success',blue:'primary',orange:'warning',pink:'danger',red:'danger',cyan:'info',purple:'alt',violet:'alt',DONE:'success',WARNING:'warning',DANGER:'danger',OPEN:'primary',WIP:'warning',FIXED:'info',FAIL:'danger',PASS:'success',REJECT:'alt'})[value]||value;
    window.ptColorVisionEnabled=()=>getComputedStyle(document.documentElement).getPropertyValue('--pt-colorblind-enabled').trim()==='1';
    window.ptChartColor=(value,fallback)=>{
        if(!window.ptColorVisionEnabled())return fallback;
        // Resolve CSS color-mix into concrete RGB for Canvas/WebGL libraries.
        const suffix=String(value).match(/-(light|dark|soft)$/)?.[0]||'';
        const token=tone(suffix?String(value).slice(0,-suffix.length):value)+suffix;
        if(resolvedColors.has(token))return resolvedColors.get(token);
        const probe=document.createElement('span');probe.style.color=`var(--pt-chart-${token}, ${fallback})`;
        (document.body||document.documentElement).append(probe);const result=getComputedStyle(probe).color;probe.remove();
        const canvas=document.createElement('canvas');canvas.width=canvas.height=1;const ctx=canvas.getContext('2d');
        if(!ctx)return fallback;ctx.fillStyle=result;ctx.fillRect(0,0,1,1);
        const hex='#'+Array.from(ctx.getImageData(0,0,1,1).data).slice(0,3).map(c=>c.toString(16).padStart(2,'0')).join('');
        resolvedColors.set(token,hex);return hex;
    };
})();
