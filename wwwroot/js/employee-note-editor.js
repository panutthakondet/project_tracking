(() => {
    const editor = document.getElementById('employeeNoteRichEditor');
    const field = document.getElementById('employeeNoteContent');
    if (!editor || !field || editor.contentEditable !== 'true') return;
    let savedRange;
    const remember = () => {
        const selection = getSelection();
        if (selection.rangeCount && editor.contains(selection.anchorNode) && editor.contains(selection.focusNode)) savedRange = selection.getRangeAt(0).cloneRange();
    };
    document.addEventListener('selectionchange', remember);
    const restore = () => {
        editor.focus();
        const selection = getSelection();
        const range = savedRange && editor.contains(savedRange.startContainer) ? savedRange : document.createRange();
        if (range !== savedRange) { range.selectNodeContents(editor); range.collapse(false); }
        selection.removeAllRanges(); selection.addRange(range);
    };
    const hex = value => {
        if (!value || value === 'transparent' || value === 'rgba(0, 0, 0, 0)') return null;
        const values = value.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/);
        return values ? '#' + values.slice(1).map(n => Number(n).toString(16).padStart(2, '0')).join('') : /^#[\da-f]{6}$/i.test(value) ? value : null;
    };
    const sync = () => {
        const runs = [];
        const append = (text, element) => {
            if (!text) return;
            const s = getComputedStyle(element);
            const root = getComputedStyle(editor);
            let bg = element;
            while (bg !== editor && !hex(getComputedStyle(bg).backgroundColor)) bg = bg.parentElement;
            const run = {text, bold:parseInt(s.fontWeight) >= 600, italic:s.fontStyle === 'italic', underline:false, color:s.color === root.color ? null : hex(s.color), highlight:bg === editor ? null : hex(getComputedStyle(bg).backgroundColor), size:Math.min(48,Math.max(12,Math.round(parseFloat(s.fontSize))))};
            for (let node = element; node && node !== editor; node = node.parentElement) if (getComputedStyle(node).textDecorationLine.includes('underline')) run.underline = true;
            const prev = runs.at(-1);
            if (prev && Object.keys(run).filter(k=>k!=='text').every(k=>run[k]===prev[k])) prev.text += text;
            else runs.push(run);
        };
        const walk = node => {
            if (node.nodeType === Node.TEXT_NODE) { append(node.nodeValue,node.parentElement); return; }
            if (node.nodeType !== Node.ELEMENT_NODE) return;
            if (node.tagName === 'BR') { append('\n', node.parentElement); return; }
            const block = /^(DIV|P|LI)$/.test(node.tagName);
            if (block && runs.length && !runs.at(-1).text.endsWith('\n')) append('\n',node);
            [...node.childNodes].forEach(walk);
            if (block && node.nextSibling && !runs.at(-1)?.text.endsWith('\n')) append('\n',node);
        };
        [...editor.childNodes].forEach(walk);
        field.value = JSON.stringify({version:2,runs});
        field.dispatchEvent(new Event('input',{bubbles:true}));
    };
    const command = (name,value=null) => { restore(); document.execCommand(name,false,value); sync(); remember(); };
    editor.addEventListener('input',sync);
    // Paste only text. Imported HTML, scripts and external images never enter a note.
    editor.addEventListener('paste',event=>{event.preventDefault();command('insertText',event.clipboardData.getData('text/plain'));});
    editor.addEventListener('drop',event=>event.preventDefault());
    editor.addEventListener('keydown',event=>{
        if (event.key === 'Tab' && !event.isComposing && !event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey) {
            event.preventDefault();
            remember();
            command('insertText', '    ');
            return;
        }
        if (event.isComposing || !(event.ctrlKey || event.metaKey)) return;
        const key=/^Key[A-Z]$/.test(event.code) ? event.code.slice(3).toLowerCase() : event.key.toLowerCase();
        const simple={b:'bold',i:'italic',u:'underline'};
        if (!event.altKey && !event.shiftKey && simple[key]) { event.preventDefault();command(simple[key]);return; }
        if (!event.altKey && !event.shiftKey && key==='s') { event.preventDefault();field.form.requestSubmit();return; }
        if (!event.altKey || event.shiftKey || !['b','h','c','s'].includes(key)) return;
        event.preventDefault(); remember();
        if(key==='b') {
            restore();
            if(document.queryCommandIndeterm('bold')) command('bold');
            if(document.queryCommandState('bold')) command('bold');
            return;
        }
        if(key==='s') {
            const size=window.prompt('ขนาดตัวอักษร 12–48 px','16');
            if(size===null)return;
            const n=Number(size);if(!Number.isInteger(n)||n<12||n>48){window.alert('กรุณาใส่ขนาด 12–48');return;}
            command('fontSize','7');
            editor.querySelectorAll('font[size="7"]').forEach(node=>{node.removeAttribute('size');node.style.fontSize=n+'px';});sync();return;
        }
        const color=window.prompt(key==='h'?'สีไฮไลท์ เช่น #fff59d (พิมพ์ none เพื่อลบ)':'สีตัวอักษร เช่น #e53935',key==='h'?'#fff59d':'#e53935');
        if(color===null)return;
        if(key==='h'&&color.trim().toLowerCase()==='none'){command('hiliteColor','transparent');return;}
        if(!/^#[\da-f]{6}$/i.test(color.trim())){window.alert('กรุณาใส่รหัสสี 6 หลัก เช่น #e53935');return;}
        command(key==='h'?'hiliteColor':'foreColor',color.trim());
    });
    window.employeeNoteEditor={insertEmoji:emoji=>command('insertText',emoji)};
    sync();
})();
