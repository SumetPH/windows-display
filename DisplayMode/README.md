# DisplayMode

`DisplayMode` is a Windows 11 x64 display-topology switcher built on the native CCD APIs. Monitor identities and display profiles are read from `profiles.json`, so ports, positions, resolutions, refresh rates, rotations, and profile names can be changed without recompiling.

The program deliberately does not identify monitors through `DISPLAY1`, `DISPLAY2`, connector order, adapter LUID, source ID, or target ID. It rediscovers every CCD path before each operation and matches persistent text in `DISPLAYCONFIG_TARGET_DEVICE_NAME`.

## Files and requirements

- Windows 11 build 22000 or later
- Interactive local console session
- `DisplayMode.exe`
- `profiles.json` beside the executable, unless `--config` specifies another file

The self-contained published executable does not require .NET to be installed.

## Commands

Apply a profile directly or with the explicit `apply` command:

```powershell
DisplayMode.exe 3
DisplayMode.exe 13
DisplayMode.exe apply 2
DisplayMode.exe apply work
```

Inspection and configuration commands:

```powershell
DisplayMode.exe list
DisplayMode.exe status
DisplayMode.exe profiles
DisplayMode.exe config-check
DisplayMode.exe config-path
DisplayMode.exe capture --verbose
```

Safe testing options:

```powershell
DisplayMode.exe apply 13 --dry-run
DisplayMode.exe apply 13 --validate-only --verbose
```

- `--dry-run` builds and prints the proposal without calling `SetDisplayConfig`.
- `--validate-only` calls `SetDisplayConfig` with `SDC_VALIDATE` but does not apply it.
- `--verbose` prints native structure and path details.
- `--config <path>` uses another configuration file with any command.

Example:

```powershell
DisplayMode.exe config-check --config D:\DisplayConfigs\office.json
DisplayMode.exe apply office --config D:\DisplayConfigs\office.json --validate-only
```

## Editing `profiles.json`

The supplied file starts with:

```json
{
  "schemaVersion": 1,
  "allowUnmanagedTargets": false,
  "monitors": {
    "gaming":  { "match": "DELA274" },
    "tv":      { "match": "HEC002F" },
    "portrait": { "match": "GSM5BD2" }
  },
  "profiles": {
    "13": {
      "monitors": [
        {
          "monitor": "gaming",
          "width": 3840,
          "height": 2160,
          "refreshHz": 120,
          "rotation": "landscape",
          "x": 0,
          "y": 0,
          "primary": true
        },
        {
          "monitor": "portrait",
          "width": 1440,
          "height": 2560,
          "refreshHz": 120,
          "rotation": "portrait",
          "x": -1440,
          "y": 0,
          "primary": false
        }
      ]
    }
  }
}
```

### Monitor definitions

The object key is a convenient alias used by profiles. `match` is a case-insensitive substring of the CCD monitor device path or friendly name.

```json
"monitors": {
  "main": { "match": "DELA274" },
  "left": { "match": "GSM5BD2" }
}
```

Moving a cable to another GPU port normally requires no configuration change: adapter, source, and target IDs are rediscovered. If the new link exposes different valid timings or bandwidth, validation can reject the old timing safely; activate the desired mode once and run `capture` again.

Each match string must identify exactly one physical target. Two identical monitor models need a more specific device-path substring or another persistent instance identifier.

When `allowUnmanagedTargets` is `false` (recommended), an apply operation fails if a connected monitor is not declared. This prevents an exclusive `SetDisplayConfig` call from unexpectedly disabling an unknown display. Set it to `true` only when disabling undeclared displays is intentional.

### Profile fields

| Field | Meaning |
|---|---|
| `monitor` | Alias from the root `monitors` object |
| `width`, `height` | Effective desktop resolution after rotation |
| `refreshHz` | Requested refresh; decimals such as `59.94` are accepted |
| `rotation` | `landscape`, `portrait`, `landscape-inverted`, or `portrait-inverted` |
| `x`, `y` | Upper-left desktop coordinate in physical pixels |
| `primary` | Exactly one active monitor must be `true` |

The primary monitor must be at `(0,0)`. Monitor rectangles cannot overlap or have gaps; every rectangle must touch the rest of the layout along an edge. `config-check` verifies these rules without querying or modifying displays.

Example profile with a portrait monitor to the right:

```json
"work": {
  "monitors": [
    { "monitor": "gaming", "width": 3840, "height": 2160, "refreshHz": 120, "rotation": "landscape", "x": 0, "y": 0, "primary": true },
    { "monitor": "portrait", "width": 1440, "height": 2560, "refreshHz": 120, "rotation": "portrait", "x": 3840, "y": 0, "primary": false }
  ]
}
```

After editing:

```powershell
DisplayMode.exe config-check
DisplayMode.exe apply work --dry-run
DisplayMode.exe apply work --validate-only --verbose
DisplayMode.exe apply work
```

## Native timing cache

`QDC_ALL_PATHS` does not return mode information for inactive paths. Preferred modes can be queried separately, but non-preferred modes such as 120 Hz may require their complete native signal timing to be captured while active.

The cache is stored at:

```text
%LOCALAPPDATA%\DisplayMode\target-timings.json
```

It supports multiple resolution/refresh combinations for every monitor. It is keyed by persistent monitor match, effective resolution, and actual refresh rational—not by port, source ID, or target ID.

To add another mode:

1. Activate the monitor and select the desired resolution/refresh once through Windows or NVIDIA Control Panel.
2. Run `DisplayMode.exe capture --verbose`.
3. Add or update the profile in `profiles.json`.
4. Run `config-check`, `--dry-run`, then `--validate-only`.

For the supplied GSM5BD2 setup, `prepare-13` activates the two-monitor layout with its preferred approximately-144-Hz timing. Change it to 120 Hz once and capture:

```powershell
DisplayMode.exe prepare-13
# Select 120 Hz for GSM5BD2 in Windows/NVIDIA settings.
DisplayMode.exe capture --verbose
DisplayMode.exe 13 --validate-only
DisplayMode.exe 13
```

The cache stores complete timings reported by Windows. It never invents pixel clocks or blanking values. Every cached timing is still passed through `SDC_VALIDATE` after a port or configuration change.

## Built-in configuration supplied in `profiles.json`

| Profile | Active displays |
|---|---|
| `3` | DELA274 at 3840x2160 120 Hz, primary |
| `13` | DELA274 primary plus GSM5BD2 portrait at 1440x2560 120 Hz |
| `2` | HEC002F at 3840x2160 60 Hz, primary |
| `prepare-13` | Calibration layout using GSM5BD2's approximately-144-Hz preferred timing |

## Build and publish

```powershell
dotnet build .\DisplayMode\DisplayMode.csproj -c Release
```

```powershell
dotnet publish .\DisplayMode\DisplayMode.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output:

```text
DisplayMode\bin\Release\net8.0-windows\win-x64\publish\
  DisplayMode.exe
  profiles.json
```

## Safety behavior

- All configured monitors must match uniquely unless the configuration explicitly permits unmanaged targets.
- At least one active monitor and exactly one primary monitor are required.
- The complete topology is submitted in one `SetDisplayConfig` operation.
- Validation occurs before apply.
- A failed validation leaves the current topology unchanged.
- The source at `(0,0)` is primary; CCD has no generic `Primary=true` flag.
- Portrait source dimensions are converted to CCD's pre-rotation coordinate convention.

The native layouts and flags follow Microsoft's documentation for [QueryDisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-querydisplayconfig), [SetDisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setdisplayconfig), and [DISPLAYCONFIG_TARGET_DEVICE_NAME](https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name).

## Troubleshooting

- `config-check` reports JSON, alias, primary, overlap, gap, and rotation errors.
- `list --verbose` shows persistent device paths and current CCD targets.
- If a requested non-preferred timing is missing, activate it once and run `capture --verbose`.
- `ERROR_ACCESS_DENIED` usually means the process is not running in the interactive local console session.
- `ERROR_BAD_CONFIGURATION` means the driver cannot satisfy the requested path/mode combination.
- After moving ports, run `list`, `config-check`, and `--validate-only` before the first apply.
