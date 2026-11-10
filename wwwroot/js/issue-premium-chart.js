(() => {
    const chartSelector = '[data-premium-issue-donut], [data-premium-support-donut]';
    const ns = 'http://www.w3.org/2000/svg';
    const ready = new WeakSet();
    const add = (parent, tag, attrs) => {
        const node = document.createElementNS(ns, tag);
        Object.entries(attrs).forEach(([key,value]) => node.setAttribute(key,value));
        parent.append(node); return node;
    };
    const palette = tone => {
        if (['green','lime','success'].includes(tone)) return ['#70E000','#A5FF55','#348C12'];
        if (['purple','violet'].includes(tone)) return ['#9945FF','#C18AFF','#5822A0'];
        if (['blue','primary'].includes(tone)) return ['#2996F3','#86CDFF','#135CA5'];
        if (['orange','warning'].includes(tone)) return ['#FF9F1C','#FFD48A','#AE5B0A'];
        if (['cyan','info'].includes(tone)) return ['#13D6C9','#8EF5EA','#087E80'];
        if (['pink','danger'].includes(tone)) return ['#F55262','#FFADB6','#9F2338'];
        return ['#64748B','#B4C0CF','#334155'];
    };
    const enhance = figure => {
        if (ready.has(figure)) return;
        ready.add(figure);
        const prefix = figure.hasAttribute('data-premium-support-donut') ? 'support' : 'issue';
        const name = prefix === 'support' ? 'Support' : 'Issues';
        figure.style.setProperty('--overview-bevel', `url(#${prefix}PremiumBevel)`);
        const svg = figure.querySelector('svg');
        const top = svg?.querySelector('.overview-pie3d-top');
        const depth = svg?.querySelector('.overview-pie3d-depth');
        const defs = svg?.querySelector('defs');
        if (!top || !depth || !defs) return;
        const sources = [...top.querySelectorAll('path')].map(p => ({ d:p.getAttribute('d'), tone:p.dataset.tone, label:p.dataset.label || name, count:p.dataset.count || '0' }));
        const gradient = (id, colors, side = false) => {
            const g = add(defs,'linearGradient',{ id,x1:'12',y1:side?'66':'28',x2:side?'12':'132',y2:side?'104':'98',gradientUnits:'userSpaceOnUse' });
            (side ? [[0,colors[0]],[.35,colors[2]],[1,colors[2]]] : [[0,colors[1]],[.32,colors[0]],[.7,colors[0]],[1,colors[2]]]).forEach(([offset,color])=>add(g,'stop',{offset,'stop-color':color}));
        };
        const bevel = add(defs,'filter',{id:`${prefix}PremiumBevel`,x:'-5%',y:'-10%',width:'110%',height:'120%','color-interpolation-filters':'sRGB'});
        add(bevel,'feGaussianBlur',{in:'SourceAlpha',stdDeviation:'.55',result:'softEdge'});
        const light = add(bevel,'feSpecularLighting',{in:'softEdge',surfaceScale:'1.7',specularConstant:'.45',specularExponent:'20','lighting-color':'#e5f4ff',result:'shine'});
        add(light,'fePointLight',{x:'-40',y:'-70',z:'140'});
        add(bevel,'feComposite',{in:'shine',in2:'SourceAlpha',operator:'in',result:'clippedShine'});
        add(bevel,'feComposite',{in:'SourceGraphic',in2:'clippedShine',operator:'arithmetic',k1:0,k2:1,k3:1,k4:0});
        const gloss = svg.querySelector(`#${prefix}PieGloss`);
        if (gloss) {
            gloss.replaceChildren();
            [[0,.3],[.3,.12],[.6,0],[1,.04]].forEach(([offset,opacity])=>add(gloss,'stop',{offset,'stop-color':'#fff','stop-opacity':opacity}));
        }
        // Extrude the exact same backend paths; no second angle calculation or minimum slice size.
        depth.replaceChildren(); top.replaceChildren();
        sources.forEach((s,index)=>{gradient(`${prefix}PremiumTop${index}`,palette(s.tone));gradient(`${prefix}PremiumSide${index}`,palette(s.tone),true);});
        for (let y=15;y>=.5;y-=.5) sources.forEach((s,index)=>add(depth,'path',{d:s.d,transform:`translate(0 ${y})`,fill:`url(#${prefix}PremiumSide${index})`}));
        if (!sources.length) {
            // Neutral, continuous ring for a genuine zero-data state.
            const d = 'M12 66 A66 36 0 1 1 144 66 A66 36 0 1 1 12 66 L39 66 A39 21 0 1 0 117 66 A39 21 0 1 0 39 66 Z';
            for(let y=15;y>=.5;y-=.5) add(depth,'path',{d,transform:`translate(0 ${y})`,fill:'#283849'});
            const empty = add(top,'path',{d,fill:'#43546A'});
            empty.setAttribute('aria-label',`ไม่มีข้อมูล ${name}`);
        }
        sources.forEach((s,index)=>{
            const group = add(top,'g',{class:'issue-premium-surface',tabindex:0,role:'img','aria-label':`${s.label}: ${s.count} ${name}`});
            add(group,'title',{}).textContent=`${s.label}: ${s.count} ${name}`;
            add(group,'path',{d:s.d,fill:`url(#${prefix}PremiumTop${index})`,stroke:palette(s.tone)[0],'stroke-width':'.12',filter:`url(#${prefix}PremiumBevel)`});
        });
        const hole = svg.querySelector(`#${prefix}PieHoleShade`);
        if(hole) {
            hole.replaceChildren();
            [[0,'#102637'],[.72,'#091A29'],[1,'#020B14']].forEach(([offset,color])=>add(hole,'stop',{offset,'stop-color':color}));
        }
        const total = svg.querySelector('.overview-pie3d-total');
        if(total) {
            total.style.fontSize = total.textContent.trim().length > 4 ? '14px' : '18px';
            total.setAttribute('y','65');
        }
    };
    document.querySelectorAll(chartSelector).forEach(enhance);
    const dashboard = document.querySelector('.dashboard-view');
    if(dashboard) new MutationObserver(records=>records.forEach(record=>record.addedNodes.forEach(node=>{
        if(!(node instanceof Element))return;
        if(node.matches(chartSelector))enhance(node);
        node.querySelectorAll(chartSelector).forEach(enhance);
    }))).observe(dashboard,{childList:true,subtree:true});
})();
