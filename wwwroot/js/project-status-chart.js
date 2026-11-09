(() => {
    const chart = document.querySelector('[data-project-status-donut]');
    if (!chart) return;
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 320 340');
    svg.setAttribute('aria-hidden', 'true');
    svg.classList.add('project-status-sculpture');
    chart.prepend(svg);
    const metrics = [...document.querySelectorAll('[data-project-status-metric]')];
    const element = (tag, attributes, parent) => {
        const node = document.createElementNS(ns, tag);
        Object.entries(attributes).forEach(([key, value]) => node.setAttribute(key, value));
        parent.append(node);
        return node;
    };
    const point = (radius, angle, offset) => {
        const radians = angle * Math.PI / 180;
        return `${160 + radius * Math.cos(radians)} ${150 + radius * .96 * Math.sin(radians) + offset}`;
    };
    const path = (start, end, offset = 0) => {
        // Two half-arcs also cover the single-status, full-circle case.
        const mid = (start + end) / 2;
        return `M ${point(139, start, offset)} A 139 133.44 0 0 1 ${point(139, mid, offset)} A 139 133.44 0 0 1 ${point(139, end, offset)} L ${point(89, end, offset)} A 89 85.44 0 0 0 ${point(89, mid, offset)} A 89 85.44 0 0 0 ${point(89, start, offset)} Z`;
    };
    const render = () => {
        const counts = new Map(chart.dataset.statusCounts ? JSON.parse(chart.dataset.statusCounts) : metrics.map(m => [m.dataset.projectStatusMetric, Number(m.dataset.projectStatusCount)]));
        let rows = metrics.map(m => ({ count: Math.max(0, Number(counts.get(m.dataset.projectStatusMetric)) || 0), color: m.dataset.projectStatusColor || '#64748b' })).filter(m => m.count > 0);
        if (!rows.length) rows = [{ count: 1, color: '#435e6b' }];
        const total = rows.reduce((sum, m) => sum + m.count, 0);
        svg.replaceChildren();
        const defs = element('defs', {}, svg);
        const bevel = element('filter', { id: 'projectStatusBevel', x: '-10%', y: '-10%', width: '120%', height: '120%', 'color-interpolation-filters': 'sRGB' }, defs);
        element('feGaussianBlur', { in: 'SourceAlpha', stdDeviation: '1.6', result: 'softEdge' }, bevel);
        const lighting = element('feSpecularLighting', { in: 'softEdge', surfaceScale: '5', specularConstant: '.85', specularExponent: '22', 'lighting-color': '#e3ffff', result: 'shine' }, bevel);
        element('fePointLight', { x: '-80', y: '-120', z: '260' }, lighting);
        element('feComposite', { in: 'shine', in2: 'SourceAlpha', operator: 'in', result: 'clippedShine' }, bevel);
        element('feComposite', { in: 'SourceGraphic', in2: 'clippedShine', operator: 'arithmetic', k1: '0', k2: '1', k3: '1', k4: '0' }, bevel);
        const gloss = element('linearGradient', { id: 'projectStatusGloss', x1: '0', y1: '0', x2: '.8', y2: '1' }, defs);
        [['0', '#fff', '.72'], ['.2', '#fff', '.24'], ['.55', '#fff', '0'], ['1', '#001b2a', '.18']].forEach(([offset, color, opacity]) => element('stop', { offset, 'stop-color': color, 'stop-opacity': opacity }, gloss));
        element('ellipse', { cx: 160, cy: 306, rx: 108, ry: 8, fill: '#00141d', opacity: '.25' }, svg);
        let angle = -90;
        const slices = rows.map(row => {
            const span = row.count / total * 360;
            const gap = rows.length > 1 ? Math.min(.7, span * .08) : 0;
            const slice = { ...row, start: angle + gap / 2, end: angle + span - gap / 2 };
            angle += span;
            return slice;
        });
        const depth = element('g', { class: 'project-status-sculpture-depth' }, svg);
        for (let offset = 14; offset >= 1; offset -= 1) {
            slices.forEach(s => element('path', { d: path(s.start, s.end, offset), fill: s.color }, depth));
        }
        slices.forEach(s => {
            const d = path(s.start, s.end);
            element('path', { d, fill: s.color, stroke: '#001a25', 'stroke-width': '1', 'stroke-linejoin': 'round', filter: 'url(#projectStatusBevel)' }, svg);
            element('path', { d, fill: 'url(#projectStatusGloss)', stroke: '#d8ffff', 'stroke-opacity': '.5', 'stroke-width': '1.2', 'stroke-linejoin': 'round' }, svg);
        });
    };
    render();
    new MutationObserver(render).observe(chart, { attributes: true, attributeFilter: ['data-status-counts'] });
})();
