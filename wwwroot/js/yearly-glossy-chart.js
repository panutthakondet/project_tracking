// Presentation only: counts and bar heights remain the values supplied by the dashboard.
(()=>{
    const ns='http://www.w3.org/2000/svg';let serial=0;
    const node=(tag,attrs={},text)=>{const e=document.createElementNS(ns,tag);Object.entries(attrs).forEach(([k,v])=>e.setAttribute(k,String(v)));if(text!==undefined)e.textContent=text;return e;};
    document.querySelectorAll('[data-glossy-yearly-chart]').forEach(root=>{
        const id=`yearly-glossy-${++serial}`,stage=document.createElement('div');stage.className='yearly-glossy-stage';
        const tooltip=document.createElement('div');tooltip.className='yearly-glossy-tooltip';tooltip.hidden=true;tooltip.setAttribute('role','tooltip');tooltip.id=id+'-tooltip';
        root.append(stage);root.classList.add('glossy-yearly-ready');let previousWidth=0;
        const draw=()=>{
            const width=Math.round(stage.clientWidth);if(!width)return;
            const compact=width<600,height=compact?300:350,left=compact?30:44,right=12,top=compact?66:42,bottom=height-66,plotHeight=bottom-top;
            const rows=[...root.querySelectorAll('.bar-group[data-month]')].map(e=>({month:e.dataset.month,counts:[Number(e.dataset.completed)||0,Number(e.dataset.progress)||0],heights:[Number(e.dataset.completedHeight)||0,Number(e.dataset.progressHeight)||0]}));
            const svg=node('svg',{viewBox:`0 0 ${width} ${height}`,class:'yearly-glossy-svg',role:'group','aria-label':'ความคืบหน้าของงานรายเดือน Completed และ In Progress'}),defs=node('defs');svg.append(defs);
            const palettes=[['#A8FF80','#42E51C','#159B13','rgba(72,255,40,.25)'],['#8AD8FF','#2196F3','#0757A8','rgba(33,150,243,.25)']];
            palettes.forEach((p,i)=>{
                const grad=node('linearGradient',{id:`${id}-fill-${i}`,x1:0,y1:0,x2:0,y2:1});[p[0],p[1],p[2]].forEach((c,j)=>grad.append(node('stop',{offset:[0,.38,1][j],'stop-color':c})));defs.append(grad);
                const shade=node('linearGradient',{id:`${id}-shade-${i}`,x1:0,y1:0,x2:1,y2:0});[[0,'#fff',.38],[.22,'#fff',.04],[.65,'#000',0],[1,'#000',.3]].forEach(([o,c,a])=>shade.append(node('stop',{offset:o,'stop-color':c,'stop-opacity':a})));defs.append(shade);
                const glow=node('filter',{id:`${id}-glow-${i}`,x:'-100%',y:'-40%',width:'300%',height:'200%'});glow.append(node('feGaussianBlur',{stdDeviation:compact?2:4}));defs.append(glow);
            });
            const cap=node('linearGradient',{id:id+'-cap',x1:0,y1:0,x2:0,y2:1});cap.append(node('stop',{offset:0,'stop-color':'#fff','stop-opacity':.75}),node('stop',{offset:1,'stop-color':'#fff','stop-opacity':0}));defs.append(cap);
            svg.append(node('text',{x:2,y:15,fill:'#d5d5cf','font-size':compact?11:13},'จำนวนงาน'));
            for(let tick=0;tick<=30;tick+=5){const y=bottom-tick/30*plotHeight;svg.append(node('line',{x1:left,y1:y,x2:width-right,y2:y,stroke:tick===0?'#b8bab2':'#9a7c30','stroke-opacity':tick===0?.8:.26,'stroke-width':1}),node('text',{x:left-8,y:y+4,'text-anchor':'end',fill:'#bfc2c1','font-size':compact?10:12},tick));}
            const placedBadges=[];
            const slot=(width-left-right)/Math.max(1,rows.length),gap=Math.max(2,slot*.07),barWidth=Math.min(48,(slot*.76-gap)/2),font=compact?9:12;
            rows.forEach((row,index)=>{
                const center=left+slot*(index+.5),barTops=row.heights.map(h=>bottom-Math.max(0,Math.min(100,h))/100*plotHeight);
                const badgeY=barTops.map(y=>y-6);
                // On narrow screens stagger adjacent capsules rather than changing the data scale.
                if(row.counts.every(c=>c>0)&&Math.abs(badgeY[0]-badgeY[1])<20&&barWidth+gap<24)badgeY[1]=Math.min(badgeY[0],badgeY[1])-21;
                row.counts.forEach((count,i)=>{
                    if(count<=0)return;
                    const x=center+(i===0?-gap/2-barWidth:gap/2),y=barTops[i],h=bottom-y,r=Math.min(barWidth*.4,h*.4,10),label=`${row.month} · ${i===0?'Completed':'In Progress'}: ${count} งาน`;
                    const group=node('g',{class:'yearly-glossy-bar',tabindex:0,role:'img','aria-label':label,'aria-describedby':tooltip.id,'data-value':count});group.append(node('title',{},label));
                    const surface=node('g',{class:'yearly-glossy-surface'});
                    surface.append(node('ellipse',{cx:x+barWidth/2,cy:bottom+3,rx:barWidth*.7,ry:4,fill:'#000',opacity:.3}));
                    const path=`M ${x} ${bottom} V ${y+r} Q ${x} ${y} ${x+r} ${y} H ${x+barWidth-r} Q ${x+barWidth} ${y} ${x+barWidth} ${y+r} V ${bottom} Q ${x+barWidth/2} ${bottom+3} ${x} ${bottom} Z`;
                    surface.append(node('path',{d:path,fill:palettes[i][3],filter:`url(#${id}-glow-${i})`}),node('path',{d:path,fill:`url(#${id}-fill-${i})`}),node('path',{d:path,fill:`url(#${id}-shade-${i})`}));
                    surface.append(node('ellipse',{cx:x+barWidth/2,cy:y+r*.55,rx:barWidth*.39,ry:Math.max(1,r*.5),fill:`url(#${id}-cap)`}));
                    if(h>8)surface.append(node('path',{d:`M ${x+barWidth*.15} ${y+r+1} V ${bottom-3}`,stroke:'#fff','stroke-opacity':.28,'stroke-width':Math.max(.7,barWidth*.06),'stroke-linecap':'round'}));
                    group.append(surface);
                    const text=String(count),badgeWidth=Math.max(compact?16:22,text.length*font*.65+10),bx=x+barWidth/2;
                    let by=Math.max(21,badgeY[i]);
                    while(by>21&&placedBadges.some(b=>Math.abs(b.x-bx)<(b.w+badgeWidth)/2+2&&Math.abs(b.y-by)<21))by=Math.max(21,by-21);
                    placedBadges.push({x:bx,y:by,w:badgeWidth});
                    if(badgeY[i]-by>8)group.append(node('line',{x1:bx,y1:by+2,x2:bx,y2:y-3,stroke:palettes[i][0],'stroke-opacity':.3,'stroke-width':.7,'stroke-dasharray':'2 2'}));
                    group.append(node('rect',{class:'yearly-glossy-badge',x:bx-badgeWidth/2,y:by-18,width:badgeWidth,height:19,rx:9.5,fill:'#080c0e',stroke:'#35403b','stroke-width':.8}),node('text',{x:bx,y:by-5,'text-anchor':'middle',fill:'#fff','font-size':font},count));
                    const show=()=>{tooltip.textContent=label;tooltip.hidden=false;};const hide=()=>{tooltip.hidden=true;};group.addEventListener('pointerenter',show);group.addEventListener('pointerleave',hide);group.addEventListener('focus',show);group.addEventListener('blur',hide);group.addEventListener('keydown',e=>{if(e.key==='Escape')hide();});svg.append(group);
                });
                svg.append(node('text',{x:center,y:bottom+24,'text-anchor':'middle',fill:'#c5c9c8','font-size':compact?9:12,...(compact?{transform:`rotate(-40 ${center} ${bottom+24})`,'text-anchor':'end'}:{})},row.month));
            });
            const legendY=height-12,legendGap=compact?85:115;
            ['Completed','In Progress'].forEach((label,i)=>{const x=width/2-legendGap+(i*legendGap);svg.append(node('circle',{cx:x,cy:legendY-4,r:5,fill:palettes[i][1]}),node('text',{x:x+11,y:legendY,fill:'#f0f3ed','font-size':compact?10:13},label));});
            stage.replaceChildren(svg,tooltip);tooltip.hidden=true;
        };
        new ResizeObserver(()=>{const w=Math.round(stage.clientWidth);if(w!==previousWidth){previousWidth=w;draw();}}).observe(stage);
        new MutationObserver(draw).observe(root.querySelector('.bar-chart'),{subtree:true,attributes:true,attributeFilter:['data-month','data-completed','data-progress','data-completed-height','data-progress-height'],childList:true});
        draw();
    });
})();
