(function () {
    'use strict';
    const terminalStates = new Set(['Completed', 'Failed', 'Cancelled']);
    const stateLabels = {Queued:'รอสแกน',Running:'กำลังสแกน',Processing:'กำลังเก็บรายงาน',Completed:'เสร็จสิ้น',Failed:'ไม่สำเร็จ',Cancelled:'ยกเลิก'};
    document.querySelectorAll('[data-scan-progress]').forEach(root => {
        const text = (selector, value) => { const node=root.querySelector(selector); if(node)node.textContent=value; };
        const pageText = (selector, value) => { const node=document.querySelector(selector); if(node)node.textContent=value; };
        const connection=root.querySelector('[data-progress-connection]');
        const track=root.querySelector('[role="progressbar"]');
        const fill=root.querySelector('.security-progress-fill');
        let timer, clock, controller, inFlight=false, disposed=false, reloadScheduled=false;
        let state=root.dataset.state, startedAt=root.dataset.startedAt||null, finishedAt=root.dataset.finishedAt||null;
        const utc=value=>value ? new Date(value).toISOString().slice(0,16).replace('T',' ') : '—';
        const duration=()=>{
            if(!startedAt)return text('[data-progress-elapsed]','—');
            const seconds=Math.max(0,Math.floor(((finishedAt?Date.parse(finishedAt):Date.now())-Date.parse(startedAt))/1000));
            if(!Number.isFinite(seconds))return;
            const h=Math.floor(seconds/3600),m=Math.floor(seconds%3600/60),s=seconds%60;
            text('[data-progress-elapsed]',(h?h+' ชม. ':'')+m+' นาที '+s+' วินาที');
        };
        const mb=bytes=>(bytes/1048576).toLocaleString('th-TH',{maximumFractionDigits:1})+' MB';
        function render(data) {
            state=data.status;startedAt=data.startedAt;finishedAt=data.finishedAt;
            root.dataset.state=state;root.dataset.stage=data.stage;
            const terminal=terminalStates.has(state);
            const percent=Number.isInteger(data.percent)&&data.percent>=0&&data.percent<=100?data.percent:null;
            root.classList.toggle('is-terminal',terminal);root.classList.toggle('is-indeterminate',percent===null);
            text('[data-progress-label]',data.label);
            text('[data-progress-counter]',state==='Completed'?'เสร็จสิ้นครบทุกขั้นตอน':data.stepIndex>=0?'ขั้นตอน '+(data.stepIndex+1)+' จาก '+data.steps.length:'รอรายละเอียดขั้นตอน');
            text('[data-progress-percent]',percent!==null?percent+'%':terminal?data.statusLabel:data.stage==='Queued'?'รอคิว':'กำลังทำงาน');
            track.setAttribute('aria-valuetext',data.label);
            if(percent!==null)track.setAttribute('aria-valuenow',String(percent));else track.removeAttribute('aria-valuenow');
            fill.style.width=(percent===null?100:percent)+'%';
            let detail=data.stage==='Analyzing'?'MobSF กำลังวิเคราะห์ไฟล์ — เครื่องมือไม่รายงานเปอร์เซ็นต์รวมในขั้นตอนนี้':state==='Completed'?'เปิดรายงานผลสแกนด้านล่างได้แล้ว':state==='Queued'?'ระบบจะเริ่มเมื่อ Worker พร้อมและถึงคิวของรอบนี้':'เปอร์เซ็นต์นี้เป็นของขั้นตอนปัจจุบัน ไม่ใช่เปอร์เซ็นต์รวมทั้งงาน';
            if(data.bytesDone!==null&&['Downloading','Uploading'].includes(data.stage))detail=mb(data.bytesDone)+(data.bytesTotal?' / '+mb(data.bytesTotal):'')+' · '+detail;
            if(state==='Failed'||state==='Cancelled')detail=data.statusLabel+' · ขั้นตอนล่าสุด: '+data.label;
            text('[data-progress-detail]',detail);
            root.querySelectorAll('[data-progress-step]').forEach((step,index)=>{
                const done=state==='Completed'||data.stepIndex>index,current=data.stepIndex===index;
                step.classList.toggle('is-done',done);step.classList.toggle('is-current',current);
                if(current)step.setAttribute('aria-current','step');else step.removeAttribute('aria-current');
                step.querySelector('[data-step-mark]').textContent=done?'✓':String(index+1);
            });
            text('[data-progress-contact]',terminal?'สิ้นสุดรอบสแกนแล้ว':data.updatedAt?'Worker อัปเดต '+new Date(data.updatedAt).toLocaleTimeString('th-TH'):'รอรายละเอียดจาก Worker');
            pageText('[data-scan-status]',stateLabels[state]||data.statusLabel);
            pageText('[data-scan-started]',utc(data.startedAt));pageText('[data-scan-finished]',utc(data.finishedAt));
            const error=document.querySelector('[data-scan-error]');if(error){error.hidden=!data.error;error.textContent=data.error||'';}
            document.querySelectorAll('[data-scan-cancel],[data-scan-running-note]').forEach(node=>{node.hidden=terminal;});
            connection.hidden=true;
            if(!terminal&&data.updatedAt&&Date.now()-Date.parse(data.updatedAt)>45000){connection.textContent='ยังไม่ได้รับอัปเดตจาก Worker เมื่อเร็ว ๆ นี้ กำลังรอการติดต่อกลับ';connection.hidden=false;}
            duration();
            if(terminal){clearTimeout(timer);clearInterval(clock);}
            if(state==='Completed'&&root.dataset.initialState!=='Completed'&&!reloadScheduled){
                reloadScheduled=true;setTimeout(()=>{if(!disposed)window.location.reload();},800);
            }
        }
        async function poll() {
            if(disposed||document.hidden||inFlight||terminalStates.has(state))return;
            inFlight=true;controller=new AbortController();const timeout=setTimeout(()=>controller.abort(),12000);
            try{
                const response=await fetch(root.dataset.url,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'},signal:controller.signal});
                if(!response.ok||response.redirected||!(response.headers.get('content-type')||'').includes('application/json'))throw new Error('Progress unavailable');
                const data=await response.json();if(!stateLabels[data.status]||!Array.isArray(data.steps))throw new Error('Invalid progress');
                render(data);
            }catch(error){
                if(!disposed&&!document.hidden){connection.textContent='ยังอัปเดตสถานะไม่ได้ กำลังลองเชื่อมต่อใหม่ กรุณาตรวจการเข้าสู่ระบบหากยังไม่สำเร็จ';connection.hidden=false;}
            }finally{
                clearTimeout(timeout);inFlight=false;
                if(!disposed&&!document.hidden&&!terminalStates.has(state))timer=setTimeout(poll,3000);
            }
        }
        document.addEventListener('visibilitychange',()=>{
            clearTimeout(timer);if(document.hidden){if(controller)controller.abort();}else if(!disposed)poll();
        });
        window.addEventListener('pagehide',()=>{disposed=true;clearTimeout(timer);clearInterval(clock);if(controller)controller.abort();},{once:true});
        window.addEventListener('pageshow',event=>{if(event.persisted)window.location.reload();});
        duration();
        if(!terminalStates.has(state)){clock=setInterval(duration,1000);poll();}
    });
}());
