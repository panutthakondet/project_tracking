using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace ProjectTracking.Services.Security;

public static class SecurityLoginGuide
{
    public static byte[] Create(string section,string fontPath)
    {
        using(var font=File.OpenRead(fontPath))QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("SecurityGuideThai",font);
        var admin=section=="WebAdmin";var info=section=="WebInfo";
        return Document.Create(document=>document.Page(page=>{
            page.Size(PageSizes.A4);page.Margin(30);page.DefaultTextStyle(x=>x.FontFamily("SecurityGuideThai").FontSize(15).FontColor("17324D"));
            page.Header().PaddingBottom(12).Column(c=>{
                c.Item().Text("PROJECTTRACKING  /  SECURITY CENTER").FontSize(12).FontColor("53758A");
                c.Item().Text("คู่มือ Login - "+SecurityValues.SectionLabel(section)).FontSize(28).Bold();
                c.Item().Text("ตั้งค่าบัญชีทดสอบก่อนสแกนเว็บไซต์").FontColor("53758A");
            });
            page.Content().Column(c=>{
                void Heading(string title)=>c.Item().PaddingTop(8).PaddingBottom(3).Text(title).FontSize(20).Bold();
                void Line(string text)=>c.Item().PaddingBottom(3).Text(text);
                void Code(string text)=>c.Item().Background("EEF4F8").Padding(9).Text(text).FontSize(13);
                Heading("1. เลือกวิธี Login ให้ตรงกับระบบ");
                Line(info?"Web Info ที่เปิดอ่านได้ทั่วไป: ไม่ต้องเปิด Login ก่อนสแกน หากมีพื้นที่จำกัดสิทธิ์ ให้เลือกวิธีตามการทำงานจริงของเว็บไซต์":"เปิด Login ก่อนสแกน แล้วกรอก Username และ Password ของบัญชีทดสอบแยกสำหรับส่วนนี้");
                Line("Form Login: ส่งฟอร์มและใช้ Cookie/Session; JSON + Cookie/Session + CSRF: ส่ง JSON แต่ใช้ Session; JSON + Bearer Token: API ส่ง Token กลับมา");
                Heading("2. ตัวอย่างการตั้งค่า "+(admin?"Web Admin":"Web Member"));
                Line("URL หลัก: https://uat.example.com/ (ใช้โดเมนจริงของโครงการ)");
                Line(admin?"วิธี Login: Form Login | เส้นทางรับ Login: /adminlogon":"วิธี Login: JSON + Cookie/Session + CSRF | เส้นทางรับ Login: /member/login");
                Line(admin?"เส้นทางเริ่มตรวจหลัง Login: /control":"เส้นทางเริ่มตรวจหลัง Login: หน้าสมาชิกที่ต้อง Login จริง เช่น /member/profile");
                Code(admin?"username={%username%}&password={%password%}&submit=Login":"{\"membership_no\":\"{%username%}\",\"password\":\"{%password%}\",\"des\":\"\",\"remember\":false}");
                Line("ตัวอย่างนี้ไม่ใช่ค่าที่ใช้ได้กับทุกเว็บ ตรวจชื่อฟิลด์และเส้นทางจริงผ่าน DevTools > Network > คำขอ POST Login > Payload");
                Heading("3. CSRF และข้อความยืนยัน");
                Line(admin?"Worker ตั้งค่า ZAP ให้รับ _token ใหม่จากหน้า Login ทุกครั้ง พร้อม Cookie ของ Session ไม่ต้องคัดลอกค่า _token ลง Request Body และไม่ใช้ค่าคงที่":"JSON Cookie: รับ XSRF-TOKEN จากหน้า Login แล้วส่ง X-XSRF-TOKEN กลับให้อัตโนมัติ ไม่ต้องคัดลอก Cookie จาก Browser");
                Line("ข้อความยืนยันหลัง Login: ใช้ข้อความหรือ regex ที่พบเฉพาะหน้าที่เข้าได้หลัง Login เช่น ข้อความเมนูออกจากระบบที่มีอยู่จริง ห้ามใช้คำที่ปรากฏบนหน้า Login ด้วย");
                Line("JSON Bearer ใช้ชื่อฟิลด์ Token เช่น token หรือ data.access_token แทนข้อความยืนยัน ต้องเป็น API ที่คืน Token จริง");
                Heading("4. บันทึกและตรวจผล");
                Line("บันทึกการตั้งค่าแต่ละส่วน อนุมัติโดเมน แล้วเพิ่มเข้าคิวสแกน Website ทุกส่วนที่เปิดไว้ ตรวจขั้นตอนเข้าสู่ระบบและขอบเขตการตรวจในผลสแกน");
                Line("เว้น Password ว่างเพื่อใช้รหัสที่บันทึกไว้เดิม บัญชีเก็บแบบเข้ารหัส คู่มือนี้ไม่มีรหัสผ่านหรือ Cookie จริง");
                Line("พบ 419: ตรวจ CSRF; 401/403 หรือกลับหน้า Login: ตรวจบัญชีและสิทธิ์; ยืนยันไม่ผ่าน: ตรวจเส้นทางหลัง Login และข้อความยืนยัน ไม่แก้ด้วยการข้ามการตรวจ Login");
                Line("OTP, CAPTCHA, MFA และหน้าที่ต้องรัน JavaScript อาจต้องเตรียมวิธีเชื่อมต่อเพิ่มเติม การสแกนไม่รับประกันว่าค้นพบทุกหน้า");
                c.Item().PaddingTop(12).Text("อ้างอิง ZAP: www.zaproxy.org/docs/desktop/start/features/authmethods/").FontSize(11).FontColor("53758A");
            });
            page.Footer().PaddingTop(10).AlignRight().Text(t=>{t.Span("คู่มือ Login  |  ");t.CurrentPageNumber();t.Span(" / ");t.TotalPages();});
        })).GeneratePdf();
    }
}
