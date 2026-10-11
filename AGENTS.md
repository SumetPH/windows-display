# AGENTS.md

คู่มือสำหรับ AI agent ที่ทำงานใน repository นี้ (รายละเอียดการใช้งานสำหรับผู้ใช้อยู่ใน `README.md`)

## ภาพรวม

DisplayMode สลับ topology ของจอบน Windows 11 (resolution, refresh rate, rotation, position, wallpaper/slideshow) ผ่าน Windows CCD API โดยอ้างอิงจอจาก device path/hardware ID

| Project | Output | หน้าที่ |
|---|---|---|
| `DisplayMode/` | `DisplayMode.exe` (console) | เจ้าของ logic ทั้งหมด: อ่าน config, ค้นหาจอ, validate, apply topology, wallpaper, timing cache |
| `DisplayMode.Tray/` | `DisplayMode.App.exe` (WinForms) | UI บาง ๆ: tray menu, settings, startup, hotkey, slideshow timer, re-apply เมื่อจอเปลี่ยน |

**กฎสถาปัตยกรรม:** Tray ห้าม reference project CLI หรือเรียก CCD API เอง ทุกการเปลี่ยนจอต้องผ่านการรัน `DisplayMode.exe` ใน `DisplayModeClient.RunAsync` (`DisplayMode.Tray/DisplayModeClient.cs`) ถ้าต้องการความสามารถใหม่ใน Tray ให้เพิ่ม subcommand ใน CLI (`DisplayMode/Program.cs`) แล้วเรียกจาก client

## โครงสร้าง

```text
DisplayMode/
  Program.cs              parse argument และ subcommand ของ CLI
  Displays/               discovery, topology apply/rollback, wallpaper (IDesktopWallpaper), timing cache
  Models/                 config model และ ConfigurationLoader (validation)
  Native/                 P/Invoke, struct และ enum ของ CCD; ประกาศ native ใหม่ไว้ที่นี่เท่านั้น
  app.manifest            asInvoker, Win10/11, PerMonitorV2 DPI
DisplayMode.Tray/
  TrayApplicationContext  menu, startup registry, default profile, re-apply
  DisplayChangeWatcher    debounce WM_DISPLAYCHANGE
  SlideshowScheduler      ตั้งเวลาเรียก `slideshow-next`
  HotKeyManager / SettingsForm / TraySettings / AppIcon
  app.manifest            เหมือน CLI แต่ DPI กำหนดด้วย ApplicationHighDpiMode ใน csproj
Directory.Build.props     property ร่วม (TFM, RID, icon, manifest, version/company)
global.json               pin .NET SDK 8.0.424
assets/, tools/           icon และสคริปต์สร้าง ICO
```

## Build และตรวจสอบ

ใช้ local SDK ใน `.dotnet-sdk` ผ่านสคริปต์ (ไม่ต้องมี `dotnet` ใน PATH):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1                 # Release
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Configuration Debug
powershell -NoProfile -ExecutionPolicy Bypass -File publish.ps1 -OutputPath <โฟลเดอร์ชั่วคราว>
```

- `build.cmd`/`publish.cmd` จะ `pause` เมื่อ fail อย่าใช้ใน agent ให้เรียก `.ps1` ตรง ๆ
- สคริปต์ย้าย `APPDATA`/`LOCALAPPDATA` ไปที่ `.build-state` ระหว่าง build เท่านั้น
- `publish.ps1` แบบไม่ระบุ `-OutputPath` จะปิด Tray ที่รันจาก `artifacts\publish` ของผู้ใช้ ให้ publish ไปโฟลเดอร์ชั่วคราวแทน
- ยังไม่มี test project การตรวจสอบคือ build ให้ผ่านแบบ 0 warning แล้วรันคำสั่ง CLI แบบอ่านอย่างเดียว

## ความปลอดภัยบนเครื่องผู้ใช้

คำสั่ง CLI เปลี่ยนจอจริงของผู้ใช้ทันที

- รันได้: `list`, `status`, `config-path`, `profiles`, `config-check`, `active-profile`, `<profile> --dry-run`, `<profile> --validate-only`
- **ห้ามรันโดยไม่ได้รับอนุญาต:** `<profile>`/`apply <profile>` แบบไม่มี flag ข้างต้น, `slideshow-next`, `capture` (เขียน timing cache) และการเปิด `DisplayMode.App.exe` (อาจ apply default profile/เปลี่ยน wallpaper)
- ห้ามแก้ไฟล์ใน `%LOCALAPPDATA%\DisplayMode\` (`profiles.json`, `tray-settings.json`, `target-timings.json`) ซึ่งเป็นข้อมูลจริงของผู้ใช้ ถ้าต้องทดสอบ config ให้ใช้ `--config <path>` ชี้ไปไฟล์ชั่วคราว
- `profiles.json` เป็นไฟล์ส่วนตัวและถูก ignore ใน git ตัวอย่างที่ commit ได้คือ `DisplayMode/profiles.example.json`

## ความเข้ากันได้ย้อนหลัง

ห้ามเปลี่ยนค่าต่อไปนี้ เพราะผูกกับเครื่องที่ติดตั้งแล้ว:

- ชื่อ exe `DisplayMode.exe` และ `DisplayMode.App.exe` (Tray หา CLI จากโฟลเดอร์เดียวกัน, registry startup ชี้ path นี้)
- mutex `Local\DisplayMode.Tray.SingleInstance`, ค่า startup ใน `HKCU\...\Run` และ `LegacyStartupValue`
- ชื่อ field ใน `profiles.json`/`tray-settings.json` (`schemaVersion` = 1) ถ้าจำเป็นต้องเปลี่ยนให้รองรับรูปแบบเก่าด้วย
- ลำดับ profile ใน tray ต้องตามลำดับใน `profiles.json`

## แนวทางเขียนโค้ด

- C# / .NET 8, `net8.0-windows`, win-x64, Nullable เปิด, file-scoped namespace, ใช้ `internal` เป็นค่าเริ่มต้น
- property ที่ใช้ร่วมกันใส่ใน `Directory.Build.props` ไม่ใช่ csproj
- ไม่เพิ่ม NuGet package ถ้าไม่จำเป็นจริง (`NuGet.Config` มีไว้สำหรับ runtime pack ของ self-contained publish)
- ตรวจ HRESULT/Win32 error ของทุก native call และแสดง error ที่บอกชื่อจอ/profile
- comment ในโค้ดเป็นภาษาอังกฤษ อธิบายเหตุผล ไม่ใช่สิ่งที่โค้ดทำ
- ข้อความ CLI/UI เป็นภาษาอังกฤษ; `README.md` เป็นภาษาไทย ให้อัปเดตทุกครั้งที่เปลี่ยนคำสั่ง CLI, schema ของ config หรือพฤติกรรมของ Tray
- commit message ภาษาอังกฤษ ประโยคคำสั่งสั้น ๆ เช่น `Re-apply profile after ...`
