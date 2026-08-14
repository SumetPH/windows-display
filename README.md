# DisplayMode

โปรแกรมสลับ topology ของจอภาพบน Windows 11 พร้อม CLI และ Tray app รองรับ resolution, refresh rate, rotation, position และ wallpaper แยกตามจอ/โปรไฟล์ โดยอ้างอิงจอจาก device path แทนหมายเลข port ที่เปลี่ยนได้

## ส่วนประกอบ

| ไฟล์ | หน้าที่ |
|---|---|
| `DisplayMode.exe` | ค้นหาจอ ตรวจ config และสลับ profile ผ่าน Windows CCD API |
| `DisplayMode.Tray.exe` | ทำงานเบื้องหลัง มี tray menu, startup และ global hotkey |
| `profiles.json` | config ที่โปรแกรมใช้งานจริง |
| `profiles.example.json` | template สำหรับสร้าง config ใหม่ |

ต้องใช้ Windows 11 x64 และ local interactive session ตัว publish เป็น self-contained จึงไม่ต้องติดตั้ง .NET เพิ่ม

## เริ่มใช้งาน

เปิด PowerShell ใน `artifacts\publish` แล้วค้นหาจอที่เชื่อมต่อ:

```powershell
.\DisplayMode.exe list
```

นำ Hardware ID จากผลลัพธ์ เช่น `DELA274` ไปใส่ใน `profiles.json` จากนั้นตรวจและทดลองโดยยังไม่เปลี่ยนจอ:

```powershell
.\DisplayMode.exe config-check
.\DisplayMode.exe main-only --dry-run
.\DisplayMode.exe main-only --validate-only
```

เมื่อผลถูกต้องจึง apply หรือเปิด Tray:

```powershell
.\DisplayMode.exe main-only
.\DisplayMode.Tray.exe
```

## profiles.json

ชื่อ alias และชื่อ profile ตั้งได้อิสระ ส่วน `match` ต้องตรงกับจอเพียงจอเดียว:

```json
{
  "schemaVersion": 1,
  "allowUnmanagedTargets": false,
  "monitors": {
    "main": { "match": "DELA274" },
    "side": { "match": "GSM5BD2" }
  },
  "profiles": {
    "work": {
      "monitors": [
        {
          "monitor": "main",
          "width": 3840,
          "height": 2160,
          "refreshHz": 120,
          "rotation": "landscape",
          "x": 0,
          "y": 0,
          "primary": true,
          "wallpaper": "wallpapers\\main.jpg"
        },
        {
          "monitor": "side",
          "width": 1440,
          "height": 2560,
          "refreshHz": 120,
          "rotation": "portrait",
          "x": -1440,
          "y": 0,
          "primary": false,
          "wallpaper": "%USERPROFILE%\\Pictures\\side.png"
        }
      ]
    }
  }
}
```

กติกาสำคัญ:

- ต้องมี primary หนึ่งจอและอยู่ที่ `(0,0)`
- พื้นที่จอต้องไม่ซ้อนและต้องแตะกันตามขอบ
- `rotation` ใช้ `landscape`, `portrait`, `landscape-inverted` หรือ `portrait-inverted`
- `wallpaper` ไม่บังคับ พาธ relative จะอิงจากโฟลเดอร์ของ config และรองรับ environment variable
- โปรแกรมตั้ง wallpaper หลัง topology สำเร็จเท่านั้น; `--dry-run` และ `--validate-only` จะไม่เปลี่ยนภาพ
- Fill/Fit/Stretch เป็นค่ารวมของ Windows ไม่ใช่ค่ารายจอ โปรแกรมจึงคง style ปัจจุบันไว้
- `allowUnmanagedTargets: false` ป้องกันการปิดจอที่ไม่ได้ประกาศโดยไม่ตั้งใจ

ดูตัวอย่างเพิ่มเติมได้ที่ `profiles.example.json`

## คำสั่ง CLI

| คำสั่ง | การทำงาน |
|---|---|
| `DisplayMode.exe list` | แสดงจอทั้งหมด รวมจอ inactive |
| `DisplayMode.exe status` | แสดง topology ที่ active |
| `DisplayMode.exe profiles` | แสดง profile จาก config |
| `DisplayMode.exe config-check` | ตรวจ JSON, alias, layout และ wallpaper path |
| `DisplayMode.exe <profile>` | apply profile |
| `DisplayMode.exe apply <profile>` | apply แบบระบุคำสั่งเต็ม |
| `DisplayMode.exe capture --verbose` | บันทึก native timing ของจอที่ active |

ทุกคำสั่งรองรับ `--config <path>` และคำสั่ง apply รองรับ `--dry-run`, `--validate-only`, `--verbose`

## Tray, startup และ hotkey

เปิด `DisplayMode.Tray.exe` แล้วคลิกขวาที่ icon เพื่อเลือก profile, reload config, เปิด Settings หรือเปิด startup

ดับเบิลคลิก icon เพื่อแก้ path และ global hotkey เช่น `Ctrl+Alt+3` หรือ `Win+Alt+G` hotkey ต้องมี modifier อย่างน้อยหนึ่งตัว การสลับที่เริ่มจาก Tray จะเรียก CLI แบบซ่อน console และป้องกันการสลับซ้อนกัน

ค่าของ Tray เก็บที่:

```text
%LOCALAPPDATA%\DisplayMode\tray-settings.json
```

Startup ใช้ `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` จึงไม่ต้องใช้สิทธิ์ Administrator

## Native timing cache

จอ inactive อาจไม่มี timing ครบสำหรับโหมดที่ไม่ใช่ preferred เช่น 120 Hz หาก validation แจ้งว่าไม่มี complete native timing:

1. เปิดจอนั้นและตั้ง resolution/refresh ที่ต้องการใน Windows หรือ NVIDIA Control Panel
2. รัน `DisplayMode.exe capture --verbose`
3. รัน `<profile> --validate-only` แล้วจึง apply

cache เก็บที่ `%LOCALAPPDATA%\DisplayMode\target-timings.json` และรองรับหลาย resolution/refresh ต่อจอ

## Build และ publish

Build ทั้ง CLI และ Tray:

```powershell
dotnet build .\DisplayMode.sln -c Release
```

สร้าง self-contained release รวมไว้ที่ `artifacts\publish`:

```powershell
.\publish.ps1
```

ถ้า Tray กำลังรันอยู่ ให้ Exit ก่อน publish เพราะ Windows จะล็อก `DisplayMode.Tray.exe`

## โครงสร้างโปรเจกต์

```text
DisplayMode.sln
DisplayMode/             CLI, config models, CCD discovery/topology, timing และ wallpaper
DisplayMode.Tray/        WinForms tray UI, settings, startup และ hotkey
assets/                  PNG ต้นฉบับและ ICO ของโปรแกรม
tools/CreateIcon.ps1     สร้าง ICO หลายขนาดจาก PNG
publish.ps1              publish CLI และ Tray ไปยังโฟลเดอร์เดียว
artifacts/publish/       release ที่พร้อมใช้งาน
```

การแยกแบบนี้ตั้งใจให้ CLI เป็นเจ้าของ logic การสลับจอทั้งหมด ส่วน Tray เป็น UI บาง ๆ ที่เรียก CLI ทำให้ทดสอบและดูแลแต่ละส่วนได้แยกกัน

## Troubleshooting

- ย้าย port แล้ว: รัน `list`, `config-check`, `<profile> --validate-only`
- timing ไม่ครบ: เปิดโหมดนั้นครั้งหนึ่งแล้วรัน `capture --verbose`
- `ERROR_ACCESS_DENIED`: ต้องรันใน local interactive session
- `ERROR_BAD_CONFIGURATION`: driver หรือ bandwidth ไม่รองรับ path/mode ที่ขอ
- wallpaper ไม่เปลี่ยน: ตรวจว่าไฟล์มีอยู่และ path ใน config ถูกต้อง

Native implementation อ้างอิงเอกสาร Microsoft: [QueryDisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-querydisplayconfig), [SetDisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setdisplayconfig), [DISPLAYCONFIG_TARGET_DEVICE_NAME](https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name) และ [IDesktopWallpaper::SetWallpaper](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-setwallpaper)
