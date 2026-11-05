window.EmployeeNoteEditor = {
    attach(editor, field, toolbar) {
        let savedRange;
        let pendingFontSize = 16;
        const base = { bold: false, italic: false, underline: false, color: null, highlight: null, fontSize: null };
        const sizes = [12, 14, 16, 18, 20, 24, 28, 32, 36, 48];
        const color = value => {
            if (!value || value === 'transparent') return null;
            if (/^#[0-9a-f]{6}$/i.test(value)) return value.toLowerCase();
            const rgb = value.match(/^rgba?\(\s*(\d+)[, ]+\s*(\d+)[, ]+\s*(\d+)(?:,\s*([\d.]+))?\s*\)$/);
            if (!rgb || (rgb[4] !== undefined && Number(rgb[4]) === 0)) return null;
            return '#' + rgb.slice(1, 4).map(x => Number(x).toString(16).padStart(2, '0')).join('');
        };
        const getValue = () => {
            const runs = [];
            const emit = (text, style) => {
                if (!text) return;
                const previous = runs[runs.length - 1];
                if (previous && Object.keys(base).every(key => previous[key] === style[key])) previous.text += text;
                else runs.push({ text, ...style });
            };
            const newline = style => {
                if (runs.length && !runs[runs.length - 1].text.endsWith('\n')) emit('\n', style);
            };
            const visit = (node, inherited) => {
                if (node.nodeType === Node.TEXT_NODE) { emit(node.textContent, inherited); return; }
                if (node.nodeType !== Node.ELEMENT_NODE) return;
                const tag = node.tagName;
                if (['SCRIPT', 'STYLE', 'IFRAME', 'OBJECT'].includes(tag)) return;
                if (tag === 'BR') { emit('\n', inherited); return; }
                const style = { ...inherited };
                if (tag === 'B' || tag === 'STRONG') style.bold = true;
                if (tag === 'I' || tag === 'EM') style.italic = true;
                if (tag === 'U') style.underline = true;
                if (node.style.fontWeight) style.bold = node.style.fontWeight === 'bold' || Number(node.style.fontWeight) >= 600;
                if (node.style.fontStyle) style.italic = node.style.fontStyle === 'italic';
                const decoration = node.style.textDecorationLine || node.style.textDecoration;
                if (decoration) style.underline = decoration.includes('underline');
                if (node.style.color || node.getAttribute('color')) style.color = color(node.style.color || node.getAttribute('color'));
                if (node.style.backgroundColor) style.highlight = color(node.style.backgroundColor);
                const size = parseInt(node.style.fontSize, 10);
                if (sizes.includes(size)) style.fontSize = size;
                const block = ['DIV', 'P', 'LI', 'H1', 'H2', 'H3', 'PRE'].includes(tag);
                if (block && node.previousSibling) newline(inherited);
                node.childNodes.forEach(child => visit(child, style));
                if (block && node.nextSibling) newline(inherited);
            };
            editor.childNodes.forEach(node => visit(node, base));
            return JSON.stringify({ version: 1, runs });
        };
        const sync = () => field.value = getValue();
        const capture = () => {
            const selection = window.getSelection();
            if (selection.rangeCount && editor.contains(selection.anchorNode) && editor.contains(selection.focusNode)) {
                savedRange = selection.getRangeAt(0).cloneRange();
            }
        };
        const restore = () => {
            editor.focus({ preventScroll: true });
            const selection = window.getSelection();
            selection.removeAllRanges();
            if (savedRange && editor.contains(savedRange.startContainer) && editor.contains(savedRange.endContainer)) selection.addRange(savedRange);
            else {
                const range = document.createRange();
                range.selectNodeContents(editor);
                range.collapse(false);
                selection.addRange(range);
            }
        };
        const command = (name, value = null) => {
            if (!editor.isContentEditable) return;
            restore();
            // Native editing commands retain the browser's undo history.
            document.execCommand('styleWithCSS', false, true);
            if (name === 'normal') {
                if (document.queryCommandIndeterm('bold')) document.execCommand('bold', false);
                if (document.queryCommandState('bold')) document.execCommand('bold', false);
            } else document.execCommand(name, false, value);
            capture();
            sync();
            updateButtons();
        };
        const updateButtons = () => {
            if (!toolbar || !editor.isContentEditable) return;
            ['bold', 'italic', 'underline'].forEach(name => {
                toolbar.querySelector(`[data-format="${name}"]`)?.setAttribute('aria-pressed', document.queryCommandState(name) ? 'true' : 'false');
            });
        };
        document.addEventListener('selectionchange', () => { capture(); updateButtons(); });
        const normalizeFontSizes = () => {
            editor.querySelectorAll('font[size="7"]').forEach(font => {
                font.style.fontSize = `${pendingFontSize}px`;
                font.removeAttribute('size');
            });
            editor.querySelectorAll('[style]').forEach(node => {
                if (node.style.fontSize === 'xxx-large') node.style.fontSize = `${pendingFontSize}px`;
            });
        };
        editor.addEventListener('input', () => { normalizeFontSizes(); capture(); sync(); });
        editor.addEventListener('paste', event => {
            if (!editor.isContentEditable) return;
            event.preventDefault();
            capture();
            command('insertText', event.clipboardData.getData('text/plain'));
        });
        editor.addEventListener('drop', event => {
            if (!editor.isContentEditable) return;
            event.preventDefault();
            capture();
            command('insertText', event.dataTransfer.getData('text/plain'));
        });
        toolbar?.querySelectorAll('button[data-format]').forEach(button => {
            button.addEventListener('mousedown', event => { event.preventDefault(); capture(); });
            button.addEventListener('click', () => command(button.dataset.format));
        });
        toolbar?.querySelectorAll('[data-format-color]').forEach(input => {
            input.addEventListener('pointerdown', capture);
            input.addEventListener('input', () => command(input.dataset.formatColor, input.value));
        });
        const fontSize = toolbar?.querySelector('[data-format-size]');
        fontSize?.addEventListener('pointerdown', capture);
        fontSize?.addEventListener('change', () => {
            pendingFontSize = Number(fontSize.value);
            command('fontSize', '7');
            normalizeFontSizes();
            capture(); sync();
        });
        sync();
        return { getValue, sync, capture, insertText: text => command('insertText', text) };
    }
};
