from PIL import Image
import os

phone = Image.open("phone.png").convert("RGBA")

input_folder = "input"
output_folder = "output"

os.makedirs(output_folder, exist_ok=True)

for file in os.listdir(input_folder):
    if file.lower().endswith((".png", ".jpg", ".jpeg")):

        print("กำลังทำ:", file)

        screen = Image.open(f"{input_folder}/{file}").convert("RGBA")

        # 🔥 ย่อภาพให้เท่ากับมือถือ
        screen = screen.resize(phone.size)

        # 🔥 ใช้ alpha ของมือถือเป็น mask
        alpha = phone.split()[3]

        result = Image.composite(screen, phone, alpha)

        output_path = f"{output_folder}/{file}"
        result.save(output_path)

        print("✔ เสร็จ:", output_path)

print("🔥 เสร็จหมดแล้ว")