(() => {
    'use strict';
    const prefix = 'PT-RICH-1:';
    ['ProjectDetail', 'TorDetail', 'ExtraTorDetail'].forEach(id => {
        const input = document.getElementById(id);
        if (!input) return;
        const wrapper = document.createElement('div');
        const toolbar = document.createElement('div');
        toolbar.className = 'd-flex flex-wrap gap-2 mb-2';
        toolbar.setAttribute('role', 'toolbar');
        toolbar.setAttribute('aria-label', 'จัดรูปแบบข้อความ');
        const editor = document.createElement('div');
        editor.className = 'form-control';
        editor.contentEditable = 'true';
        editor.setAttribute('role', 'textbox');
        editor.setAttribute('aria-multiline', 'true');
        editor.setAttribute('aria-label', input.placeholder);
        editor.style.cssText = 'min-height:280px;max-height:70vh;overflow:auto;white-space:pre-wrap;overflow-wrap:anywhere;resize:vertical;font-weight:400;';
        const initial = input.value;
        try {
            if (!initial.startsWith(prefix)) throw new Error('plain');
            const data = JSON.parse(initial.slice(prefix.length));
            if (data.version !== 2 || !Array.isArray(data.runs)) throw new Error('plain');
            data.runs.forEach(run => {
                const span = document.createElement('span');
                span.textContent = run.text;
                span.style.fontWeight = run.bold ? '700' : '400';
                span.style.fontStyle = run.italic ? 'italic' : 'normal';
                span.style.textDecoration = run.underline ? 'underline' : 'none';
                editor.append(span);
            });
        } catch { editor.textContent = initial; }
        const sync = () => {
            const runs = [];
            const visit = node => {
                if (node.nodeType === Node.TEXT_NODE) {
                    const style = getComputedStyle(node.parentElement);
                    runs.push({text:node.textContent, bold:parseInt(style.fontWeight,10)>=600, italic:style.fontStyle==='italic', underline:style.textDecorationLine.includes('underline')});
                } else if (node.nodeName === 'BR') runs.push({text:'\n'});
                else {
                    const block = ['DIV','P','LI'].includes(node.nodeName);
                    if (block && runs.length && !runs[runs.length-1].text.endsWith('\n')) runs.push({text:'\n'});
                    node.childNodes.forEach(visit);
                    if (block && node.nextSibling) runs.push({text:'\n'});
                }
            };
            editor.childNodes.forEach(visit);
            input.value = prefix + JSON.stringify({version:2,runs});
        };
        const command = (name,value) => { editor.focus(); document.execCommand(name,false,value); sync(); };
        [['ตัวหนา (Ctrl/⌘+B)','bold'],['ตัวปกติ','removeFormat'],['ตัวเอียง (Ctrl/⌘+I)','italic'],['ขีดเส้นใต้ (Ctrl/⌘+U)','underline'],['ย่อหน้า','indent']].forEach(([label,cmd]) => {
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-outline-secondary btn-sm'; button.textContent = label;
            button.addEventListener('mousedown',e=>e.preventDefault());
            button.addEventListener('click',()=> {
                if (cmd==='indent') command('insertText','    ');
                else command(cmd);
            });
            toolbar.append(button);
        });
        editor.addEventListener('input',sync);
        editor.addEventListener('keydown',e=> {
            if ((e.ctrlKey || e.metaKey) && ['b','i','u'].includes(e.key.toLowerCase())) {
                e.preventDefault(); command({b:'bold',i:'italic',u:'underline'}[e.key.toLowerCase()]);
            } else if (e.key==='Tab' && !e.shiftKey) { e.preventDefault(); command('insertText','    '); }
        });
        editor.addEventListener('paste',e=> { e.preventDefault(); command('insertText',e.clipboardData.getData('text/plain')); });
        input.before(wrapper); wrapper.append(toolbar,editor); input.hidden = true;
        input.form.addEventListener('submit',sync);
    });
})();
