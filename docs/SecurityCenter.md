# ศูนย์ตรวจความปลอดภัยตามโครงการ

เข้า **Projects → 🛡️ ตรวจความปลอดภัย** แล้วเพิ่มระบบ → เพิ่มเวอร์ชัน → เพิ่มเข้าคิว → เปิดผลสแกน

- Website รวม Web Info / Web Member / Web Admin ใช้ ZAP; Android APK / iOS IPA ใช้ MobSF Static Analysis
- เวอร์ชันเก็บ Version, Build, Git Commit, UAT/Production และรายละเอียดการเปลี่ยนแปลง
- ไฟล์ APK/IPA สูงสุด 250 MB เก็บนอก wwwroot พร้อม SHA-256; IIS ต้องอนุญาต request size อย่างน้อย 256 MB ด้วย
- ทุกครั้งที่สแกนมีรายงาน JSON ต้นฉบับแยกกัน ดาวน์โหลด PDF ที่สร้างจากผลรอบนั้นได้
- เปรียบเทียบรอบของระบบ สภาพแวดล้อม เครื่องมือ โหมด และเป้าหมายเดียวกัน: พบใหม่ / ยังพบ / ไม่พบในรอบล่าสุด
- ผลที่ไม่พบในรอบใหม่ไม่ได้แปลว่าแก้ไขแล้ว ต้องทบทวนขอบเขต/เวอร์ชันเครื่องมือและบันทึกหลักฐานยืนยัน
- เลือกประเด็นและผู้รับผิดชอบ/วันเริ่ม/กำหนดส่งเพื่อสร้าง ProjectIssues หรือเชื่อม Issue ในโครงการเดิม ประเด็นเดิมไม่สร้าง Issue ซ้ำ
- สถานะการติดตามและการตรวจยืนยันแยกจากการปิด Issue เสมอ

## 1. เตรียม IIS และฐานข้อมูล

สำรองฐานข้อมูลก่อน deploy แล้วรัน `deploy/security/schema.sql` กับฐานข้อมูลของ ProjectTracking ที่ใช้งานจริง ไฟล์นี้สร้างเฉพาะ 13 ตาราง `security_*` ไม่มี ALTER/DROP ตารางเดิม

ค่าเริ่มต้นไม่สร้างตารางอัตโนมัติ ตั้ง `SECURITY_AUTO_CREATE_SCHEMA=false` บน IIS หากติดตั้ง schema เอง หรือใช้ `true` เพื่อให้แอปสร้างตารางใหม่ตอนเริ่ม (ต้องมีสิทธิ์ CREATE TABLE) ไม่ควรใช้การสร้าง schema อัตโนมัติแทนกระบวนการ deploy ที่ตรวจสอบแล้ว

ตั้ง Environment Variables ของแอปใน IIS โดยไม่ commit ค่า secret:

```text
SECURITY_WORKER_KEY=<สุ่มอย่างน้อย 32 ตัวอักษร ต้องตรงกับ worker>
SECURITY_STORAGE_PATH=C:\inetpub\security-data\ProjectTracking
SECURITY_AUTO_CREATE_SCHEMA=false
```

### เก็บค่าบน Windows ให้รอดจากการ Deploy

แอปรุ่นใหม่อ่าน `C:\inetpub\security-data\ProjectTracking\server-settings.json` โดยอัตโนมัติ ไฟล์นี้อยู่นอกโฟลเดอร์ Publish และเก็บเฉพาะ `SECURITY_WORKER_KEY`, `SECURITY_STORAGE_PATH`, `SECURITY_AUTO_CREATE_SCHEMA` ค่า Environment Variables เดิมของ IIS ยังมีลำดับความสำคัญสูงกว่า จึงใช้งานกับการติดตั้งเดิมได้

ย้ายครั้งเดียว: คัดลอก `deploy/windows/Save-SecurityServerSettings.ps1` ไป Windows แล้วรัน PowerShell แบบ Administrator ก่อน Deploy ครั้งถัดไป:

```powershell
powershell -ExecutionPolicy Bypass -File .\Save-SecurityServerSettings.ps1
```

สคริปต์อ่านค่าจาก web.config เดิม หากไม่มี key จะใช้ไฟล์ `C:\inetpub\security-data\ProjectTracking\iis-worker-key.txt` เดิม ไม่สุ่มคีย์ใหม่ ไม่พิมพ์คีย์ และไม่เขียนทับ server-settings.json ที่มีอยู่ ตั้งสิทธิ์ไฟล์ให้ Administrators/SYSTEM และ App Pool ที่ระบุเท่านั้น (ค่าเริ่มต้น DefaultAppPool) จากนั้น Deploy เว็บรุ่นใหม่และ Recycle App Pool หนึ่งครั้ง ไม่ต้องเปลี่ยน Ubuntu หรือฐานข้อมูล

หลังจากนี้ Publish ทับ web.config ได้ตามปกติ แต่ต้องไม่ลบโฟลเดอร์ security-data และควรสำรองไฟล์ตั้งค่านี้ควบคู่ข้อมูลสแกน หากใช้เส้นทางอื่น ตั้ง `PROJECTTRACKING_SERVER_CONFIG_FILE` เป็น path แบบ absolute ของไฟล์บนเครื่องเซิร์ฟเวอร์ ไม่เก็บไฟล์จริงใน Git หรือส่งคีย์ในแชต

ให้ App Pool ของไซต์มีสิทธิ์ Modify เฉพาะโฟลเดอร์เก็บไฟล์นี้ เช่น `IIS AppPool\DefaultAppPool` ตาม App Pool ที่ไซต์ใช้จริง อย่าเพิ่ม Virtual Directory ชี้ไปยัง storage และอย่าเก็บใต้ wwwroot หรือโฟลเดอร์ uploads ที่เปิดผ่านเว็บ สำรองทั้งฐานข้อมูลและโฟลเดอร์ storage ควบคู่กัน

ผู้ใช้ต้อง Login และมี `SecurityScans.Index` พร้อมเป็น BA/PM/สมาชิกทีมของโครงการ หรือได้รับ `SecurityScans.ViewAllProjects`; ADMIN ผ่านทั้งหมด
เพิ่มระบบ/เวอร์ชัน/สแกน/ติดตามต้องมี `SecurityScans.Edit`; สร้างและเชื่อม Issues ต้องมี `SecurityScans.CreateIssues` และ `ProjectIssues.Create` ด้วย

## 2. ติดตั้งเครื่องสแกนด้วย Docker

แนะนำเครื่อง Linux/VM แยกจาก IIS ที่มี Docker Engine และ Compose v2, RAM อย่างน้อย 8 GB และพื้นที่สำหรับไฟล์แอป/รายงาน ใช้ Linux containers; บน Mac Apple Silicon ต้องตรวจว่า image/tag ของ MobSF ที่เลือกมีสถาปัตยกรรมที่รองรับ หรือใช้ Linux amd64 VM

จาก root ของ repository:

```bash
cp deploy/security/.env.example deploy/security/.env
# สุ่ม 3 ครั้ง สำหรับ WORKER, ZAP และ MobSF แยกกัน
openssl rand -hex 32
```

แก้ `.env` บนเครื่องสแกน:

- `SECURITY_APP_URL`: HTTPS URL ของ IIS (รวม path base ถ้ามี) ห้าม localhost ของ container
- `SECURITY_WORKER_KEY`: ค่าเดียวกับ IIS
- `ZAP_API_KEY`, `MOBSF_API_KEY`: key สุ่มแยกกัน
- `SECURITY_ALLOWED_HOSTS`: ค่าสำรองสำหรับ API รุ่นเก่าเท่านั้น ในรุ่นใหม่ให้อนุมัติโดเมนจาก SecurityScans แทน ไม่ต้องเพิ่มโดเมนใน `.env` และยังไม่ใช้ wildcard
- `SECURITY_ALLOW_PRIVATE_TARGETS=true` เฉพาะเมื่อ UAT อยู่เครือข่ายภายใน (ยังบล็อก loopback/link-local/metadata)
- `SECURITY_ALLOW_ACTIVE=true` เฉพาะเมื่อต้องการ Active Scan บน UAT; ฝั่งแอปและ worker บล็อก Production อยู่แล้ว
- Pin `ZAP_IMAGE` และ `MOBSF_IMAGE` เป็น tag/digest ที่ทดสอบแล้วก่อนใช้งานจริง ค่า stable/latest ในตัวอย่างใช้สำหรับเริ่มติดตั้ง

```bash
docker compose --env-file deploy/security/.env -f deploy/security/compose.yaml config --quiet
docker compose --env-file deploy/security/.env -f deploy/security/compose.yaml up -d --build
docker compose --env-file deploy/security/.env -f deploy/security/compose.yaml logs --tail=100 worker
```

ไม่มีการ publish port ZAP/MobSF ออกอินเทอร์เน็ต Worker ติดต่อ IIS ผ่าน HTTPS และเรียกเครื่องมือผ่านเครือข่าย Docker ภายใน รัน **หนึ่ง worker ต่อหนึ่ง ZAP instance** เพราะมีการ reset session ของ ZAP ก่อนแต่ละงาน

ตั้ง firewall/egress ของเครื่องสแกนให้เข้าถึงเฉพาะ IIS, เป้าหมาย UAT ที่อนุญาต และปลายทางจำเป็นของเครื่องมือ รายชื่อ host และ context ไม่ใช่ network sandbox: redirects, DNS changes และการวิเคราะห์ binary อาจทำให้ engine ติดต่อปลายทางอื่นได้ ต้องใช้ network policy ของเครื่องสแกนร่วมด้วย

MobSF เก็บข้อมูลใน volume `mobsf-data`; จำกัดสิทธิ์โฮสต์และสำรองตามนโยบาย อย่าใช้ public MobSF service อัปโหลด APK/IPA ของลูกค้า

## 3. Web Member ที่ต้อง Login

### ตั้งค่า Login จากหน้า SecurityScans (แนะนำ)

เปิดระบบเว็บไซต์ → ตั้งค่า Login → เลือก “Login ก่อนสแกน” แล้วกรอก URL ที่รับฟอร์ม Login, Username, Password และข้อความตรวจสอบหลัง Login สำเร็จ หากชื่อช่องไม่ใช่ username/password ให้เปิด “ตั้งค่าขั้นสูง” และเปลี่ยน requestData โดยคง {%username%}/{%password%} ไว้ ตั้งค่าได้เฉพาะผู้มี SecurityScans.Edit และสิทธิ์ในโครงการนั้น หน้าเว็บแสดง URL, Username, login indicator และ requestData เดิมให้ผู้มีสิทธิ์แก้ไข โดยไม่ส่งรหัสผ่านเดิมกลับมา หากเว้นช่อง Password ว่างจะใช้รหัสเดิมจากไฟล์เข้ารหัส การตั้งค่าครั้งแรกหรือเมื่ออ่านไฟล์เดิมไม่ได้ต้องกรอกรหัสใหม่ หรือเอาเครื่องหมาย Login ก่อนสแกนออกแล้วบันทึกเพื่อปิด

บัญชีถูกเข้ารหัสด้วย ASP.NET Data Protection แยก purpose ตาม system ID เก็บไฟล์ .login แบบ immutable ใน SECURITY_STORAGE_PATH นอก wwwroot ฐานข้อมูลเก็บเฉพาะชื่ออ้างอิง ไม่เก็บรหัสผ่าน plaintext ต้องสำรองทั้ง storage และ C:\inetpub\keys\ProjectTracking ที่เป็น Data Protection key ring ห้ามลบไฟล์ .login ของโปรไฟล์เก่าระหว่างมีงานในคิว เพราะแต่ละรอบอ้างอิงข้อมูลชุดเดิม

Worker รับข้อมูลผ่าน claim API ที่ตรวจคีย์แล้วทาง HTTPS (Cache-Control: no-store) และใช้ในหน่วยความจำโดยไม่ต้องสร้างไฟล์ JSON บน Ubuntu การแก้ไขมีผลต่อรอบที่เข้าคิวใหม่เท่านั้น งานที่เข้าไปแล้วคงชุดเดิม การอ่านข้อมูลไม่ได้จะทำให้งานล้มเหลว ไม่ลดเป็นการสแกนแบบไม่ Login อัตโนมัติ

ต้องอัปเดตทั้งเว็บ Windows และ Worker Ubuntu ครั้งแรก ไม่มีตารางใหม่และไม่เปลี่ยนคีย์ใน .env หลังจากนี้แก้บัญชีผ่านหน้าเว็บได้โดยไม่ต้อง rebuild/restart Worker รองรับ Form Login ตาม ZAP; SSO, MFA, OTP, JavaScript และ flow เฉพาะต้องเตรียม integration เพิ่ม การตั้งค่าหน้าเว็บไม่ได้ยืนยันว่าล็อกอินได้จนกว่า ZAP ตรวจพบ login indicator สำเร็จจริง

### โปรไฟล์ไฟล์เดิมบน Ubuntu (ยังรองรับ)

คัดลอก `deploy/security/profiles/login.example.json` เป็น `member-uat.json` (ไฟล์ส่วนตัวถูก gitignore) แล้วแก้ loginUrl, requestData, loggedInRegex, username/password ของบัญชี UAT สิทธิ์ต่ำที่เตรียมไว้

ในหน้าระบบระบุชื่อโปรไฟล์ `member-uat` และเลือก “ต้อง Login” Worker จะ mount profiles แบบอ่านอย่างเดียวและส่งให้ ZAP ไม่เก็บรหัสผ่านในฐานข้อมูล/หน้าจอ และต้องตรวจสอบ `authSuccessful` ก่อนสแกน

รุ่นนี้รองรับ form-based authentication ตาม ZAP เท่านั้น ระบบที่ใช้ SSO, MFA, JavaScript login หรือ flow พิเศษต้องเตรียม authentication integration เพิ่ม ไม่ควรรันแล้วถือว่าครอบคลุมหน้าที่ต้อง Login โดยไม่มีการยืนยัน

Passive mode ใช้ traditional spider เพื่อเข้าหน้าและตรวจ response; ยังเกิด HTTP requests จึงควรทดสอบบน UAT การ exclude logout/delete/remove เป็นเพียงตัวช่วย ไม่รับประกันว่าแอปที่เปลี่ยนข้อมูลผ่าน GET จะไม่มีผลข้างเคียง Active mode ยิง payload จริง ใช้เฉพาะ UAT ที่สำรองไว้

## 4. ตรวจสอบหลังติดตั้ง

1. Deploy เว็บ, schema และตั้ง worker key/storage ก่อนเริ่ม worker
2. ตรวจ worker/engine logs ว่าเริ่มทำงาน (logs ของ worker ไม่พิมพ์ secrets หรือ URL ที่อาจมีรหัสผ่าน)
3. เพิ่มระบบ UAT ที่อยู่ allowlist และเพิ่ม version แล้วสั่ง Passive หนึ่งรอบ
4. สถานะต้องเปลี่ยน Queued → Running → Completed; JSON/PDF ดาวน์โหลดได้เฉพาะผู้มีสิทธิ์ในโครงการ
5. ตรวจจำนวน/ตำแหน่งประเด็นเทียบ JSON ต้นฉบับก่อนใช้จริง สแกนซ้ำแล้วต้องยังเปิดรายงานเก่าได้
6. ทดสอบสร้าง Issue หนึ่งประเด็นแล้วกดซ้ำ ต้องได้ Issue เดิม และมีประวัติสถานะ OPEN ของ Issue
7. ทดสอบบัญชีที่ไม่ใช่ทีมต้องเข้ารายงานไม่ได้
8. ทดสอบ APK/IPA ของโครงการเอง แล้วตรวจผล MobSF และขอบเขต static analysis

Worker ต่ออายุ lease ทุก 30 วินาที; ขาดการติดต่อเกิน 3 นาที งานจะถูกทำเครื่องหมายไม่สำเร็จเมื่อ worker claim ครั้งถัดไป ต้องสั่งรอบใหม่ ไม่ส่งผลด้วย lease เก่า งานถูกยกเลิกจะส่งผลไม่ได้ จำกัดแต่ละงาน 30 นาที

หาก Failed: ตรวจ allowlist, DNS/เครือข่าย, key ตรงกัน, HTTPS certificate, engine พร้อมหรือไม่, auth profile, ขนาดไฟล์/รายงาน และรูปแบบ JSON หากเครื่องมือเปลี่ยน schema จน parser ไม่รู้จัก ระบบจะไม่สรุปว่าไม่มีช่องโหว่

MobSF แบบ API-only บางรุ่นอาจตอบ HTTP 500 จาก `/login/` ขณะที่ API ทำงานได้ Image เดิมใช้หน้า Login เป็น Health Check ชุด Compose นี้จึงตรวจ `GET /api/v1/scans` พร้อม API key และตรวจผล JSON แทน Worker ใช้ endpoint เดียวกันก่อนรับงาน ไม่ข้ามการตรวจสุขภาพหรือปิดการยืนยันตัวตน และ Compose รอทั้งสองเครื่องมือเป็น healthy ก่อนเริ่ม Worker

Worker ระบุ MIME ของ multipart file เป็น `application/vnd.android.package-archive` สำหรับ APK และ `application/octet-stream` สำหรับ IPA ตามชนิดที่ MobSF รองรับ หากใช้ Worker เก่าที่ไม่ระบุ MIME อาจถูกปฏิเสธด้วย HTTP 400 แม้ไฟล์และนามสกุลถูกต้อง ข้อผิดพลาด HTTP ของ Worker รุ่นใหม่ระบุขั้นตอน เช่น `MobSF upload`, `MobSF static analysis`, `MobSF JSON report`, `ProjectTracking artifact download` หรือ `ProjectTracking report completion` โดยไม่พิมพ์เนื้อหาตอบกลับหรือกุญแจ เมื่อแก้ Worker ต้อง build และ recreate เฉพาะ worker แล้วสั่งรอบใหม่จากเวอร์ชัน APK เดิม ไม่ต้องอัปโหลดไฟล์ซ้ำหรือแก้แถวรอบเก่าในฐานข้อมูล

เมื่ออัปเดต Health Check ให้ใช้ `up -d --no-deps mobsf` เพื่อสร้าง container MobSF ใหม่จาก configuration ปัจจุบัน โดยยังใช้ named volume เดิม ไม่ใช้ `down -v` จากนั้นรอให้ healthy ก่อนสั่ง build/start Worker รุ่นใหม่ การแก้ชุดนี้อยู่เฉพาะ Docker/Worker ไม่ต้อง deploy IIS หรือรัน schema ซ้ำ

## ตารางที่เพิ่ม

`security_systems`, `security_versions`, `security_artifacts`, `security_scans`, `security_findings`, `security_scan_findings`, `security_reports`, `security_finding_history`, `security_finding_issue_links`

ตรวจชื่อจริงใน schema.sql ก่อน query ตารางใช้ PascalCase columns ตาม Models/SecurityModels.cs finding แยกตามระบบ/engine/environment/rule/location; รายงานแต่ละรอบเก็บ snapshot ของรายละเอียดและแนวทางแก้ไข

## การทดสอบโค้ด

```bash
dotnet build --no-restore
dotnet build SecurityWorker/SecurityWorker.csproj
dotnet run --project tests/Security/SecurityTests.csproj
python3 tests/Security/mobsf-healthcheck.test.py
```

ชุดทดสอบใช้ SQLite ชั่วคราวและ HTTP จำลอง ไม่เชื่อมฐานข้อมูลจริง ไม่ยิงสแกนภายนอก และสร้าง schema.sql จาก EF MySQL metadata โดยไม่เปิด connection

การผ่าน test ไม่ได้แทนการทดสอบเครื่องมือจริงหลังติดตั้ง Docker: รอบสแกนจริงและการยืนยัน authenticated coverage ต้องทำใน UAT ของคุณ

เอกสาร API ที่ใช้อ้างอิง: [ZAP API](https://www.zaproxy.org/docs/api/), [MobSF Docker](https://github.com/MobSF/docs/blob/master/running_mobsf_docker.md), [MobSF Configuration](https://github.com/MobSF/docs/blob/master/configurations.md)


### อัปเดตระบบเดิมเพื่อแสดงความคืบหน้าสแกน

ระบบที่สร้าง 9 ตารางไว้แล้ว: สำรองฐานข้อมูล เลือกฐานข้อมูล ProjectTracking ใน MySQL Workbench แล้วรัน `deploy/security/progress-schema.sql` ก่อน deploy เว็บรุ่นนี้ สคริปต์สร้างเฉพาะ `security_scan_progress` ไม่แก้ตารางสแกนหรือรายงานเดิม ระบบใหม่ติดตั้งด้วย `schema.sql` ได้ตามปกติ

อัปเดตเว็บ IIS และ build Worker รุ่นนี้ด้วย หลังรอบที่กำลังสแกนอยู่สิ้นสุดแล้วจึง restart Worker เพื่อไม่ขัดจังหวะงานเดิม Worker เก่ายังต่ออายุ lease ได้ แต่จะยังไม่มีรายละเอียดขั้นตอน หน้าเว็บจะแสดงหลอดแบบไม่ระบุเปอร์เซ็นต์ระหว่างรอข้อมูล

หน้า Scan อัปเดตสถานะอัตโนมัติทุก 3 วินาที หยุด polling เมื่อซ่อนหน้า และแสดงเวลาที่ใช้กับการติดต่อล่าสุดจาก Worker การดาวน์โหลด/อัปโหลดแสดงเปอร์เซ็นต์และจำนวนไบต์จริง ส่วน ZAP แสดงเปอร์เซ็นต์สำรวจ/Active ที่เครื่องมือรายงาน MobSF ระหว่างวิเคราะห์ใช้หลอดเคลื่อนไหวโดยไม่สร้างเปอร์เซ็นต์รวมขึ้นเอง เมื่อบันทึกรายงานสำเร็จเท่านั้นจะแสดงครบ 100% และโหลดหน้ารายงานหนึ่งครั้ง

Progress ส่งผ่าน heartbeat ที่ตรวจ worker key และ lease เดิม Endpoint สำหรับหน้าเว็บตรวจสิทธิ์โครงการ และไม่ส่ง lease token หรือกุญแจของเครื่องสแกนให้เบราว์เซอร์


### สิทธิ์ผู้ใช้ SecurityScans

กำหนดใน UserManagement/Permissions หมวด “ตรวจความปลอดภัยโครงการ”:
- `SecurityScans.Index`: เข้าใช้งาน ดูความคืบหน้า และดาวน์โหลดรายงาน
- `SecurityScans.Edit`: จัดการระบบ/เวอร์ชัน สั่งสแกน ยกเลิก และติดตามผล ต้องเปิด Index ด้วย
- `SecurityScans.CreateIssues`: สร้าง/เชื่อม Issues ต้องเปิด Index และ `ProjectIssues.Create` ด้วย
- `SecurityScans.ViewAllProjects`: อนุญาตข้ามขอบเขตทีมโครงการ ต้องเปิด Index ด้วย ไม่เพิ่มสิทธิ์แก้ไขหรือสั่งสแกนเอง

หากไม่เปิด ViewAllProjects จะเข้าถึงได้เฉพาะโครงการที่เป็น BA, PM หรือสมาชิกทีมตามเดิม ADMIN ผ่านสิทธิ์ทั้งหมด การเพิ่มสิทธิ์ใหม่ไม่คัดลอกสิทธิ์ Projects.Edit อัตโนมัติ หลังบันทึกให้บัญชีเป้าหมายออกจากระบบและเข้าใหม่เพื่อโหลดสิทธิ์ใน session ไม่ต้องเพิ่มตารางฐานข้อมูล

### Worker ดาวน์โหลดไฟล์ไม่ได้: ARTIFACT_FILE_MISSING

หากฐานข้อมูลมี artifact แต่ไฟล์จริงหายจาก `SECURITY_STORAGE_PATH` เว็บจะตอบ 404 พร้อมรหัส `ARTIFACT_FILE_MISSING` เฉพาะ Worker ที่มี key และ lease ถูกต้อง ไม่ส่งเส้นทางไฟล์ภายในออกไป และไม่ Redirect เข้าหน้า Login Worker รุ่นใหม่แสดงสาเหตุไฟล์หายโดยตรง

กู้ไฟล์เดิมจาก backup/เครื่องที่อัปโหลด แล้วคืนตาม StorageKey ในโฟลเดอร์เก็บไฟล์ของ Server หรือเพิ่มเวอร์ชันใหม่พร้อมอัปโหลดไฟล์แอปอีกครั้ง อย่าแก้ SHA-256 หรือ StorageKey ในฐานข้อมูลเพื่อแทนไฟล์ต่างตัว และอย่าสั่งรอบเดิมซ้ำก่อนมีไฟล์จริง หากหลายเครื่องใช้ฐานข้อมูลเดียวกันแต่คนละ storage ต้องให้การอัปโหลดและ Worker API ใช้พื้นที่เก็บไฟล์เดียวกันด้วย


### อนุมัติโดเมนผ่านหน้าเว็บ (ไม่ต้องแก้ .env ทุกเว็บไซต์)

อัปเดตระบบเดิมครั้งเดียว: สำรองฐานข้อมูล เลือกฐานข้อมูล ProjectTracking แล้วรัน `deploy/security/host-approvals-schema.sql` ก่อน deploy เว็บ และอัปเดต/build Worker รุ่นเดียวกัน (รอไม่มีงานสแกนกำลังทำก่อน restart) ไม่เปลี่ยน `.env` หรือกุญแจเดิม และรักษาค่า `SECURITY_*` ใน web.config ของ IIS ตอน deploy

กำหนด `SecurityScans.Index` และ `SecurityScans.ApproveHosts` ใน UserManagement/Permissions แล้วให้ผู้ใช้ออก/เข้าใหม่ ผู้อนุมัติจะเห็น “อนุมัติโดเมนนี้” ใต้ระบบเว็บไซต์ การอนุมัติแยกตามโครงการและ hostname ที่แน่นอน เช่น `www.example.com` ไม่รวม `example.com` หรือ subdomain อื่น มีข้อมูลผู้แก้ไขและเวลาอนุมัติ/ถอนล่าสุด

เว็บตรวจการอนุมัติก่อนเข้าคิวและก่อนออกงานให้ Worker รายชื่อ `ApprovedHosts` ที่มากับงานมีเฉพาะ hostname เป้าหมายของงานนั้น Worker รุ่นใหม่ใช้รายการนี้แทน `.env` และไม่ fallback หากรายการเป็นค่าว่าง โหมดอ่าน `.env` เดิมใช้เฉพาะเมื่อคุยกับ API รุ่นเก่าที่ไม่มี field นี้ (เพื่อรองรับการอัปเดต) ดังนั้นต้องอัปเดต Worker ครั้งแรกก่อนใช้ความสามารถใหม่ หลังจากนั้นไม่ต้อง restart เมื่อเพิ่มหรือถอนโดเมน

โดเมนเดิมใน `.env` ไม่ถูกนำเข้าเป็นสิทธิ์บนเว็บอัตโนมัติ ผู้อนุมัติต้องอนุมัติจากหน้าเว็บด้วย ไฟล์ APK/IPA ไม่ต้องอนุมัติโดเมน การถอนสิทธิ์ป้องกันงานในคิวที่ยังไม่ได้ออกให้ Worker แต่ไม่หยุดงานที่ออกให้ Worker แล้ว การตรวจ private network/metadata/loopback และข้อกำหนด Active UAT ยังบังคับตามเดิม

### รายงาน ZAP จำนวนมาก

Worker ดึง alerts เป็นหน้า หน้าละ 500 รายการตาม ZAP API และเก็บทุกรายการใน JSON สำเร็จรูป ไม่หยุดที่ 10,000 รายการเหมือนรุ่นก่อน รองรับรายการดิบสูงสุด 100,000 รายการ ขนาด JSON ไม่เกิน 50 MB และประเด็นหลังรวมซ้ำไม่เกิน 50,000 รายการ หากเกินขอบเขตจะรายงานความล้มเหลวชัดเจน ไม่บันทึกผลเพียงบางส่วนเป็นรอบสำเร็จ ตรวจพบหน้า alerts ซ้ำจะหยุดแทนการวนซ้ำไม่สิ้นสุด

การรวมซ้ำยังใช้ fingerprint เดิม (เครื่องมือ/กฎ/ตำแหน่ง/ชื่อพารามิเตอร์) โดยทำก่อนตรวจเพดานประเด็นไม่ซ้ำ JSON เก็บรายการดิบครบตามที่เครื่องมือส่ง หน้ารายละเอียดแบ่งหน้าละ 50 ประเด็น ยอด severity และการเปรียบเทียบรอบคำนวณจากทั้งรอบ ไม่ใช่เฉพาะหน้าที่กำลังดู การดาวน์โหลดรายงานใช้ข้อมูลทั้งรอบตามเดิม

อัปเดตรุ่นนี้ให้ deploy เว็บ Windows ก่อน แล้ว build/recreate Worker ใหม่หลังไม่มีงานสแกนกำลังทำ ไม่ต้องแก้ตารางฐานข้อมูลหรือ `.env`

### Login แบบ JSON + Bearer Token (Web Member)

อัปเดตเว็บ Windows และ build/recreate Worker รุ่นนี้ทั้งคู่ ไม่ต้องเพิ่มตารางหรือเปลี่ยนกุญแจเดิม รอให้งานที่กำลังทำเสร็จก่อน recreate Worker

หากเว็บไซต์ส่ง JSON ไป API แล้วเก็บ token ให้เลือก “JSON Login + Bearer Token” แทน Form Login ในตั้งค่า Login กด “ใช้ค่าตัวอย่าง Web Member” เพื่อเติมค่าบน origin ของระบบ:
- URL Login: /api/member/auth/login
- ข้อมูล JSON: {"membership_no":"{%username%}","mem_password":"{%password%}"}
- ชื่อ Token: token (รองรับชื่อซ้อน เช่น data.access_token)
- URL เริ่มตรวจ: /member/

กรอกบัญชีทดสอบใน Username/Password แล้วบันทึก หากมีรหัสเดิมและไม่ได้เปลี่ยน ให้เว้น Password ว่างได้ ค่าตัวอย่างต้องปรับให้ตรง API ของระบบจริง ไม่ได้รองรับทุกเว็บไซต์โดยอัตโนมัติ ข้อมูลถูกเข้ารหัสใน storage เดิม และส่งให้ Worker เฉพาะเมื่อรับงานผ่าน API ที่ตรวจ key/lease

Worker ใช้ ZAP JSON authentication และ Header Based Session Management ภายใน context เดียวกัน ส่ง Authorization: Bearer จาก token ที่ได้จริง ตรวจ HTTP สำเร็จและ token ก่อนเริ่ม crawl หากไม่ได้ token จะหยุด ไม่สแกนแบบไม่ล็อกอินแทน คำขอตั้งค่า authentication/session/credentials ใช้ POST เพื่อไม่ให้รหัสผ่านอยู่ใน query string

ต้องมี ZAP Authentication Helper ที่รองรับ Header Based Session Management API (ZAP 2.16.1 ขึ้นไป) ดู https://www.zaproxy.org/docs/desktop/addons/authentication-helper/session-header/ หาก ZAP ปฏิเสธ method ต้องอัปเดต scanner ก่อนเริ่มรอบใหม่

ขอบเขต: การยืนยันว่าได้รับ token ยังไม่เท่ากับตรวจครบทุกหน้าที่ต้องล็อกอิน Traditional Spider ไม่รัน JavaScript ของ SPA และไม่อ่าน localStorage แบบ browser จึงอาจค้นพบหน้า/API ได้น้อย ต้องตรวจรายงาน coverage และใช้ browser-based scan เพิ่มสำหรับเส้นทางเหล่านั้น รุ่นนี้ไม่ข้าม CAPTCHA, OTP หรือ MFA

### ลบประวัติรอบสแกน

กำหนดสิทธิ์ `SecurityScans.Delete` ใน UserManagement/Permissions พร้อม `SecurityScans.Index` และให้ผู้ใช้เข้าใช้งานใหม่เพื่อโหลดสิทธิ์ ปุ่มลบอยู่ในประวัติรอบสแกน ลบได้เฉพาะ Completed / Failed / Cancelled พร้อมยืนยันก่อนลบ ฝั่งเซิร์ฟเวอร์ตรวจสิทธิ์และขอบเขตโครงการอีกครั้ง และบล็อก Queued / Running

การลบจะล้างรอบสแกน ความคืบหน้า ผลที่พบในรอบ และรายงานของรอบนั้น โดยเก็บระบบ เวอร์ชัน ไฟล์ APK/IPA และประเด็น/Issue/ประวัติการแก้ไขไว้ ลิงก์กลับผลสแกนจาก Issue จะเลือกจากรอบที่เสร็จแล้วที่ยังมีอยู่ หากไม่เหลือรอบดังกล่าวจะไม่แสดงลิงก์ รายงานในเครื่อง MobSF ไม่ถูกลบผ่านปุ่มนี้ หากลบไฟล์รายงานบน Windows ไม่สำเร็จ ระบบแจ้งผู้ดูแลและบันทึก Log


### Website รวม Web Info / Web Member / Web Admin

ประเภทหลักสำหรับระบบใหม่และแท็บประวัติคือ Website, Android และ iOS แต่ Website เดียวตั้งค่าได้สามส่วน: Web Info, Web Member และ Web Admin แต่ละส่วนมี URL, เปิด/ปิดการรวมสแกน และ Login แบบเข้ารหัสของตัวเอง บันทึก URL และ Username/Password ของ Member และ Admin ล่วงหน้าได้ เลือก Form หรือ JSON Bearer ให้ตรงกับระบบจริง ไม่มีการเดา URL หรือบัญชี Admin อัตโนมัติ URL Login และ URL เริ่มตรวจต้องอยู่ origin เดียวกับ URL ของส่วนที่ตั้งค่า

อัปเกรดระบบเดิม: สำรองฐานข้อมูลและ storage แล้วรัน `deploy/security/website-schema.sql` ในฐานข้อมูลที่ใช้งาน ก่อน Deploy เว็บ Windows สคริปต์เพิ่มเพียง `security_website_targets` และ `security_website_scan_parts` ไม่เปลี่ยนหรือลบข้อมูลเดิม ไม่ต้องเปลี่ยน `.env`, กุญแจ หรืออัปเดต Worker เพราะแต่ละส่วนยังส่งงาน ZAP ตามสัญญา API เดิม

ระบบเก่าประเภท WebInformation / WebMember / WebAdmin แสดงหมวด Website และยังเก็บระบบ เวอร์ชัน รายงานและ Issue เดิมแยกกัน ไม่รวมประวัติจากหลายระบบให้เอง เมื่อบันทึกส่วนใหม่ครั้งแรก ระบบรักษา URL และโปรไฟล์เดิมเป็นส่วนที่ตรงกับประเภทเดิม จากนั้นเพิ่ม Member/Admin ในระบบ Website ที่ต้องการใช้งานเป็นชุดได้

กด “สแกน Website ทุกส่วน” ครั้งเดียว จะตรวจการตั้งค่าและการอนุมัติโดเมนของทุกส่วนที่เปิด ก่อนบันทึกคิวทั้งหมดใน transaction เดียว หากส่วนหนึ่งตั้งค่าไม่ครบจะไม่สร้างคิวเพียงบางส่วน Member/Admin ที่เปิดต้องบันทึก Login ผ่านหน้าเว็บแล้ว หน้าเว็บรวมประวัติหนึ่งแถวต่อการสั่งสแกน และรวมผล จำนวนประเด็น ความคืบหน้า และรายงาน PDF/MD/JSON ในหน้าเดียว โดยระบุแหล่งที่มาของแต่ละประเด็น ภายในยังเก็บงานย่อยและ lease ตามส่วนเพื่อใช้บัญชี Login แยกกัน Worker ตรวจตามลำดับ ไม่รันหลาย ZAP sessions พร้อมกัน ส่วนหนึ่ง Login ไม่สำเร็จจะเป็น Failed โดยไม่แกล้งรายงานว่าสแกนผ่าน และส่วนอื่นยังดำเนินการตามคิว

การสแกนทุกส่วนหมายถึงเข้าแต่ละ URL ด้วยบัญชีที่ระบุ ไม่ใช่การข้ามสิทธิ์จากหน้า Info เข้า Member/Admin Traditional Spider ยังไม่รัน JavaScript ของ SPA จึงไม่รับประกันว่าค้นพบทุกหน้า/API ตรวจ coverage ของแต่ละรอบประกอบเสมอ

กรอก URL หลักเว็บไซต์ครั้งเดียว เช่น `https://uat.fishcorp.in.th/` แล้วระบุเส้นทางแต่ละส่วน เช่น `/`, `/member/login`, `/adminlogon` ระบบประกอบ URL ให้เอง ทั้งสามตัวอย่างเป็น origin เดียวกัน ไม่ใช่ subdomain คนละตัว ช่องรับข้อมูล Login ต้องระบุเส้นทาง Form/API ที่รับ POST จริง ซึ่งอาจต่างจากหน้าที่ใช้แสดง Login เช่น `/api/member/auth/login` และระบุเส้นทางเริ่มตรวจหลัง Login ตามระบบจริง ไม่ต้องกรอกโดเมนซ้ำ

### JSON + Cookie/Session + CSRF
เลือกโหมดนี้สำหรับเว็บที่ POST JSON แต่ใช้ Session Cookie แทน Bearer Token เช่น Inertia เว็บสมาชิก กำหนด Login path `/member/login`, Request Body `{"membership_no":"{%username%}","password":"{%password%}","des":"","remember":false}` และเส้นทางเริ่มตรวจเป็นหน้าที่ต้อง Login จริง พร้อม regex ข้อความที่มีเฉพาะหลัง Login เครื่องสแกน GET หน้า Login เพื่อรับ XSRF-TOKEN แล้ว POST พร้อม X-XSRF-TOKEN และเก็บ Cookie ตรวจยืนยันก่อนเริ่มและก่อนจบ ไม่รองรับ OTP/MFA หรือการ refresh session อัตโนมัติ ต้องเริ่มรอบใหม่เมื่อ session หมดอายุ Cookie ถูกจำกัดตาม URL ของรอบและลบกฎเมื่อจบ ตาม Replacer API: https://www.zaproxy.org/docs/desktop/addons/replacer/

การอัปเดตรุ่นนี้ต้อง Deploy เว็บ Windows และอัปเดต source/build/recreate Worker Ubuntu ไม่ต้องเปลี่ยนฐานข้อมูลหรือกุญแจ เลือกโหมดและบันทึก Login ใหม่ก่อนเพิ่มรอบสแกน ไม่ใช้ Cookie ที่คัดลอกจาก browser

### Form Login พร้อม CSRF และคู่มือ PDF
Worker ลงทะเบียน `_token` เป็น Anti-CSRF และกำหนด `loginPageUrl` ให้ ZAP อ่านค่าจากหน้า Login ใหม่ทุกครั้ง ถ้าไม่มีฟิลด์ `_token` ใน Request Body จะเพิ่มช่องว่างให้ ZAP เติมค่าใหม่ ไม่ใช้ Token คงที่ ตัวอย่าง Admin: `/adminlogon`, `username={%username%}&password={%password%}&submit=Login`, เริ่มตรวจ `/control` และใช้ข้อความยืนยันเฉพาะหลัง Login คู่มือ PDF อยู่ใน Web Info/Member/Admin แต่ละส่วน เปิดแท็บใหม่แบบ inline ผ่าน endpoint ที่ตรวจสิทธิ์และขอบเขตโครงการ ไม่มีข้อมูลบัญชีจริงใน PDF

### ผู้รับผิดชอบและ Issue อัตโนมัติ
ก่อน Deploy เว็บ Windows ให้รัน `deploy/security/auto-issues-schema.sql` ในฐานข้อมูลเดิม เพิ่มตาราง `security_issue_defaults` เพียงตารางเดียว ไม่แก้ไขข้อมูลเดิม ไม่ต้องอัปเดต Worker สำหรับฟีเจอร์นี้
เพิ่มระบบใหม่ต้องเลือกพนักงาน Active พร้อมวันเริ่ม/สิ้นสุดการแก้ไข และมีสิทธิ์ SecurityScans.Edit, SecurityScans.CreateIssues และ ProjectIssues.Create เมื่อรับผลสำเร็จ เว็บสร้าง Issue สำหรับ Critical/High/Medium/Low ยกเว้น Info ใน transaction เดียวกับรายงาน ใช้สถานะ OPEN / Dev WIP และการติดตามเดิม มี Issue เชื่อมประเด็นนั้นอยู่แล้วจะใช้รายการเดิม ไม่สร้างซ้ำ ไม่ทับผู้รับผิดชอบหรือวันที่ของ Issue เดิม ระบบเก่าที่ไม่มีค่าในตารางนี้ยังใช้การสร้าง Issue แบบเดิม ไม่สร้างย้อนหลัง

### แยกสแกนระหว่างพัฒนาและสแกนจริง
รัน `deploy/security/scan-workflow-schema.sql` ก่อน Deploy เว็บ Windows เพื่อเพิ่ม security_scans.Workflow ค่าเดิมทุกแถวเป็น Formal (สแกนจริง) ไม่ลบประวัติ เปิดแท็บ “สแกนระหว่างพัฒนา” เพื่อทดสอบวนระหว่างเขียนโปรแกรม ไม่ต้องกำหนดผู้รับผิดชอบ/วันที่ Issue ประวัติ ค่าตั้งต้นสำหรับเปรียบเทียบ และตัวตนประเด็นแยกจาก Formal เซิร์ฟเวอร์บล็อกทั้ง Issue อัตโนมัติและการสร้าง/เชื่อมด้วยมือจากรอบ Development
แท็บสแกนจริงใช้การทำงานเดิมและค่าผู้รับผิดชอบสำหรับ Issue การอนุมัติโดเมนยังเป็นสิทธิ์ของโครงการร่วมกัน ส่วนระบบ/เวอร์ชัน/ไฟล์แยกตาม Workflow ตามการอัปเกรดด้านล่าง กำหนดวัตถุประสงค์แยกจาก Passive/Active/Static: แม้เป็นรอบ Development การสแกน Active ยังใช้ได้เฉพาะ UAT ตามข้อจำกัดเดิม Worker ไม่ต้องเปลี่ยนสำหรับการแยก Workflow นี้

### แยกระบบในโครงการตาม Workflow
รัน `deploy/security/system-workflow-schema.sql` หลัง `scan-workflow-schema.sql` ก่อน Deploy เว็บ Windows เพิ่ม security_systems.Workflow ค่า Formal ให้ระบบเดิม ไม่มีการคัดลอก ลบ หรือย้ายระบบและไฟล์เดิม ระบบใหม่ในแท็บ Development จะเป็น Development พร้อมเวอร์ชัน ไฟล์ และ Login ที่ผูกกับ SystemId ของตัวเอง ส่วน Formal แยกอีกชุด สั่งสแกนจากเวอร์ชันข้าม Workflow ไม่ได้
ประวัติ Development เก่าที่เคยสแกนกับระบบ Formal ร่วมกันก่อนรุ่นนี้ยังอ่านได้ในประวัติ Development โดยโหลดข้อมูลอ้างอิงเฉพาะการแสดงประวัติ ไม่แสดงระบบเก่านั้นในรายการระบบ Development และไม่ให้สั่งงาน Development ใหม่ผ่านระบบ Formal การอนุมัติโดเมนยังเป็นระดับโครงการตามระบบเดิม ไม่ได้แยกสิทธิ์พนักงานระหว่างสองแท็บ ต้องกำหนดเมนูสิทธิ์ตามปกติ ไม่ต้องอัปเดต Worker เพิ่มสำหรับการแยกระบบ

### ผลรวม Website ต่อการสั่งสแกน

รวมเฉพาะงานที่มี BatchKey เดียวกันภายในเวอร์ชันและรูปแบบการทำงานเดียวกัน ไม่รวมคนละคลิกหรือคนละระบบ งานเก่าที่มี BatchKey อยู่แล้วแสดงรวมได้ทันที URL ผลสแกนของงานย่อยทุกตัวเปิดหน้าผลรวมเดียวกัน เลขรอบรวมใช้เลขงานแรกของชุด

สำเร็จทั้งชุดเมื่อทุกส่วน Completed เท่านั้น หากบางส่วน Failed หรือ Cancelled ยังเปิดดูและดาวน์โหลดผลจากส่วนที่สำเร็จได้ พร้อมเตือนว่าผลไม่ครบ JSON รวมเป็น envelope ที่มีสถานะและรายงานต้นฉบับของแต่ละส่วน PDF/MD รวมประเด็นพร้อมชื่อส่วน เปรียบเทียบได้เฉพาะชุดที่สำเร็จครบและชุดส่วน/URL/โหมด Login ตรงกัน เมื่อกดยกเลิกหรือลบจะมีผลกับทุกงานในชุด โดยลบไม่ได้หากยังมีงานที่ไม่สิ้นสุด Issue เดิมไม่ถูกลบ

การปรับหน้าผลรวมนี้ Deploy เฉพาะเว็บ Windows ไม่ต้องเปลี่ยน schema หรืออัปเดต Worker
