# DisplayMode

โปรแกรมสลับ topology ของจอภาพบน Windows 11 พร้อม CLI และ Tray app รองรับ resolution, refresh rate, rotation, position และ wallpaper แยกตามจอ/โปรไฟล์ โดยอ้างอิงจอจาก device path แทนหมายเลข port ที่เปลี่ยนได้

## ส่วนประกอบ

| ไฟล์ | หน้าที่ |
|---|---|
| `DisplayMode.exe` | ค้นหาจอ ตรวจ config และสลับ profile ผ่าน Windows CCD API |
| `DisplayMode.App.exe` | ทำงานเบื้องหลัง มี tray menu, startup และ global hotkey |
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
.\DisplayMode.exe "Main Only" --dry-run
.\DisplayMode.exe "Main Only" --validate-only
```

เมื่อผลถูกต้องจึง apply หรือเปิด Tray:

```powershell
.\DisplayMode.exe "Main Only"
.\DisplayMode.App.exe
```

## profiles.json

ชื่อ alias และชื่อ profile ตั้งได้อิสระ ส่วน `match` ต้องตรงกับจอเพียงจอเดียว:

```json
{
  "schemaVersion": 1,
  "allowUnmanagedTargets": false,
  "monitors": {
    "main": {
      "match": "DELA274",
      "width": 3840,
      "height": 2160,
      "refreshHz": 120,
      "rotation": "landscape",
      "x": 0,
      "y": 0
    },
    "side": {
      "match": "GSM5BD2",
      "width": 1440,
      "height": 2560,
      "refreshHz": 120,
      "rotation": "portrait",
      "x": -1440,
      "y": 0
    }
  },
  "profiles": {
    "Work": {
      "monitors": [
        {
          "monitor": "main",
          "primary": true,
          "wallpaperFolder": "%USERPROFILE%\\Pictures\\Wallpapers\\main",
          "slideshowInterval": 300,
          "slideshowOrder": "random"
        },
        {
          "monitor": "side",
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
- `width`, `height`, `refreshHz`, `rotation`, `x` และ `y` กำหนดครั้งเดียวใน `monitors`; ในแต่ละ profile ระบุเฉพาะจอที่เปิดใช้และค่าของ profile เช่น `primary` หรือ wallpaper
- `rotation` ใช้ `landscape`, `portrait`, `landscape-inverted` หรือ `portrait-inverted`
- `wallpaper` ไม่บังคับ พาธ relative จะอิงจากโฟลเดอร์ของ config และรองรับ environment variable
- `wallpaperFolder` ใช้ทำ slideshow แยกต่อจอ รองรับไฟล์ `.bmp`, `.jpeg`, `.jpg`, `.png` ในโฟลเดอร์โดยตรง
- `slideshowInterval` คือช่วงเปลี่ยนภาพหน่วยวินาที ค่าต่ำสุด 60 และค่าเริ่มต้น 300 วินาที
- `slideshowOrder` ใช้ `random` (ค่าเริ่มต้น) หรือ `sequential`; ห้ามกำหนด `wallpaper` และ `wallpaperFolder` พร้อมกันในจอเดียว
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

`--rollback-on-failure` จะจำ topology ที่ active ก่อน apply ไว้ ถ้า apply ไม่สำเร็จหลังจาก Windows เปลี่ยนจอไปแล้ว (เช่น จอไม่ active ครบ หรือตั้ง wallpaper ไม่ได้) จะคืนค่า topology เดิมให้

## Tray, startup และ hotkey

เปิด `DisplayMode.App.exe` แล้วคลิกขวาที่ icon เพื่อเลือก profile, reload config, เปิด Settings หรือเปิด startup

Slideshow จะทำงานเมื่อ apply profile ผ่าน Tray และเมื่อ Tray เริ่มจาก Windows startup จะตรวจชุดจอที่ active อยู่ก่อน แล้วเริ่ม slideshow เฉพาะ profile ที่ตรงกัน จึงไม่พยายามเปลี่ยนภาพของจอที่ถอดออกไปแล้ว ระบบจะหยุด/สลับชุดภาพอัตโนมัติเมื่อเลือก profile ใหม่ การ reload config จะหยุด slideshow เดิม ให้ apply profile อีกครั้งเพื่อเริ่มด้วยค่าที่ reload แล้ว ตัว Tray เก็บเฉพาะเวลาและรายชื่อจอ ส่วน CLI จะอ่านรายชื่อไฟล์จากโฟลเดอร์เมื่อถึงรอบ จึงไม่โหลดภาพทั้งหมดไว้ในหน่วยความจำ

เมนู **Default profile at startup** ใช้เลือก profile ที่จะ apply อัตโนมัติเมื่อ Tray เริ่มจาก Windows startup (registry จะเรียก `DisplayMode.App.exe --startup`; การเปิด Tray เองจะไม่ apply) โดย apply แบบ `--rollback-on-failure` ถ้าไม่สำเร็จจะกลับไปใช้ topology ล่าสุดก่อน apply แล้วเริ่ม slideshow ของ profile ที่ตรงกับจอที่ active อยู่ เลือก `(None)` เพื่อปิด ค่าที่เลือกเก็บใน `defaultProfile` ของ `tray-settings.json`

เมนู **Re-apply after display change** ใช้แก้กรณีจอติดดับหลัง Windows สลับ topology เอง เช่น เปิด TV แล้ว Windows ดึง layout "TV อย่างเดียว" ที่เคยบันทึกไว้กลับมาใช้ขณะที่ TV ยังไม่พร้อม ติ๊ก profile ที่ต้องการไว้ เมื่อจอเปลี่ยนแล้วนิ่งไปประมาณ 4 วินาที และ topology ที่ active ตรงกับ profile นั้น Tray จะ apply profile นั้นซ้ำให้หนึ่งครั้ง การเปลี่ยนจอที่เกิดจากการ apply ของ Tray เองจะไม่ถูกนับ จึงไม่วนซ้ำ ค่าที่เลือกเก็บใน `reapplyOnDisplayChange` ของ `tray-settings.json`

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

Repository นี้มี .NET SDK แบบ local อยู่ใน `.dotnet-sdk` จึง build ได้แม้เครื่องไม่มี SDK ใน `PATH` ดับเบิลคลิกไฟล์ `.cmd` หรือรันจาก PowerShell/CMD ได้เลย ตัว wrapper จะ bypass Execution Policy เฉพาะ process นี้ โดยไม่เปลี่ยนค่าของเครื่อง:

```powershell
.\build.cmd                        # Release build
.\build.cmd -Configuration Debug   # Debug build
.\publish.cmd                      # self-contained release ไปที่ artifacts\publish
.\publish.cmd -Run                 # publish แล้วเปิด Tray ให้ทันที
```

ถ้า Tray ที่รันจาก `artifacts\publish` เปิดอยู่ `publish.cmd` จะปิดให้ก่อน (Windows ล็อกไฟล์ exe ระหว่างรัน) แล้วเปิดกลับมาหลัง publish เสร็จ ถ้า build หรือ publish ล้มเหลว หน้าต่างจะค้างไว้ให้อ่าน error

หากติดตั้ง .NET 8 SDK ไว้ใน `PATH` แล้วจึงสามารถใช้ `dotnet build ...` แบบสั้นได้

## โครงสร้างโปรเจกต์

```text
DisplayMode.sln
Directory.Build.props    property ร่วมของทั้งสองโปรเจกต์ (TFM, RID, icon, manifest, version/company)
global.json              pin .NET SDK 8.0.424
DisplayMode/             CLI, config models, CCD discovery/topology, timing และ wallpaper
DisplayMode.Tray/        WinForms tray UI, settings, startup และ hotkey
*/app.manifest           asInvoker, รองรับ Windows 10/11, PerMonitorV2 DPI, long path
assets/                  PNG ต้นฉบับและ ICO ของโปรแกรม
tools/CreateIcon.ps1     สร้าง ICO หลายขนาดจาก PNG
publish.ps1/.cmd         publish CLI และ Tray ไปยังโฟลเดอร์เดียว
build.ps1/.cmd           restore และ build ทั้งสองโปรเจกต์ด้วย local SDK
artifacts/publish/       release ที่พร้อมใช้งาน
AGENTS.md                แนวทางสำหรับ AI agent (CLAUDE.md อ้างถึงไฟล์นี้)
```

`profiles.json` เป็น config ส่วนตัวจึงไม่เก็บใน repository (อยู่ใน `.gitignore`) ให้เริ่มจาก `DisplayMode/profiles.example.json`

การแยกแบบนี้ตั้งใจให้ CLI เป็นเจ้าของ logic การสลับจอทั้งหมด ส่วน Tray เป็น UI บาง ๆ ที่เรียก CLI ทำให้ทดสอบและดูแลแต่ละส่วนได้แยกกัน

## Troubleshooting

- ย้าย port แล้ว: รัน `list`, `config-check`, `<profile> --validate-only`
- timing ไม่ครบ: เปิดโหมดนั้นครั้งหนึ่งแล้วรัน `capture --verbose`
- `ERROR_ACCESS_DENIED`: ต้องรันใน local interactive session
- `ERROR_BAD_CONFIGURATION`: driver หรือ bandwidth ไม่รองรับ path/mode ที่ขอ
- wallpaper ไม่เปลี่ยน: ตรวจว่าไฟล์มีอยู่และ path ใน config ถูกต้อง

Native implementation อ้างอิงเอกสาร Microsoft: [QueryDisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-querydisplayconfig), [SetDisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setdisplayconfig), [DISPLAYCONFIG_TARGET_DEVICE_NAME](https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name) และ [IDesktopWallpaper::SetWallpaper](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-setwallpaper)
