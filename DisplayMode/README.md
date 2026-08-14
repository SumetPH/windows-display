# DisplayMode

`DisplayMode` is a small Windows 11 x64 command-line utility that switches one specific three-monitor system between three display topologies. It uses the native CCD APIs (`QueryDisplayConfig`, `DisplayConfigGetDeviceInfo`, and `SetDisplayConfig`) and applies each topology as one operation.

It deliberately does **not** use `DISPLAY1`, `DISPLAY2`, or `DISPLAY3`: Windows can renumber those names after a topology change. Physical targets are rediscovered before every switch by matching these stable identifiers in the CCD monitor device path (or friendly name):

- `DELA274`
- `HEC002F`
- `GSM5BD2`

If all three targets cannot be identified uniquely, no change is attempted.

## Requirements

- Windows 11 (build 22000 or later)
- An interactive local console session (CCD cannot modify a disconnected/remote desktop)
- x64

## Commands

```powershell
DisplayMode.exe 3
DisplayMode.exe 13
DisplayMode.exe 2
DisplayMode.exe list
DisplayMode.exe status
DisplayMode.exe capture
DisplayMode.exe prepare-13
```

`list` shows every connected CCD target, including inactive monitors. `status` shows only active paths. Neither command modifies the display configuration.

Add `--dry-run` to a mode to discover, check identities, select paths/modes, and print the proposed configuration without validating or applying it:

```powershell
DisplayMode.exe 13 --dry-run
```

Use `--validate-only` to call `SetDisplayConfig` with `SDC_VALIDATE` but never apply the result:

```powershell
DisplayMode.exe 13 --validate-only --verbose
```

`capture` saves complete native CCD target timings for known monitors that are currently active at their requested refresh rates. This is useful because `QDC_ALL_PATHS` deliberately omits mode data for inactive paths. The cache is stored at `%LOCALAPPDATA%\DisplayMode\target-timings.json` and is never synthesized from partial timing data.

For first-time GSM5BD2 calibration, `prepare-13` safely enables the requested two-monitor layout using GSM5BD2's complete native preferred timing (approximately 144 Hz):

```powershell
DisplayMode.exe prepare-13
```

Then change GSM5BD2 to 120 Hz once in Windows Display Settings or NVIDIA Control Panel and run:

```powershell
DisplayMode.exe capture --verbose
```

Add `--verbose` for selected CCD path indexes, native structure sizes, and full exception details.

## Topologies

| Mode | Active displays | Layout and mode |
|---|---|---|
| `3` | DELA274 | Primary at `(0,0)`, 3840x2160, 120 Hz, landscape |
| `13` | GSM5BD2 + DELA274 | GSM5BD2 at `(-1440,0)`, 1440x2560, 120 Hz, portrait; DELA274 primary at `(0,0)`, 3840x2160, 120 Hz |
| `2` | HEC002F | Primary at `(0,0)`, 3840x2160, 60 Hz, landscape |

`prepare-13` is a calibration topology, not one of the three normal modes. It has the same layout as mode `13` but temporarily uses GSM5BD2's preferred approximately-144-Hz native timing so the panel can be activated safely before its 120-Hz timing is captured.

CCD defines the source surface at `(0,0)` as primary; there is no generic CCD `Primary` Boolean flag.

## Build

From the repository root:

```powershell
dotnet build .\DisplayMode\DisplayMode.csproj -c Release
```

## Publish a standalone executable

```powershell
dotnet publish .\DisplayMode\DisplayMode.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The standalone executable is written below `DisplayMode\bin\Release\net8.0-windows\win-x64\publish` and does not require a preinstalled .NET runtime.

## How mode selection works

The utility queries `QDC_ALL_PATHS`, retries the buffer-size race documented by Microsoft, groups duplicate possible paths by persistent adapter LUID and target ID, and resolves monitor names with `DISPLAYCONFIG_TARGET_DEVICE_NAME`. It never selects a monitor by source ID, target ID, connector order, or GDI display number.

For each active target it supplies an explicit source size, desktop position, rotation, and refresh rational. CCD source dimensions are pre-rotation, so GSM5BD2 uses a 2560x1440 source rotated 90 degrees to produce the requested effective 1440x2560 desktop extent. If the target is already running at the requested signal size and refresh, its complete native target timing is reused. When an inactive target has no timing in the query (normal for `QDC_ALL_PATHS`), its target mode index remains unspecified so CCD best-mode logic can select a valid signal timing; the requested path refresh is still supplied. An exact observed fractional rational such as `120000/1001` is retained instead of being replaced with `120/1`.

The proposal is first passed to `SetDisplayConfig` with `SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG`. NVIDIA can reject a caller-supplied source mode when a previously inactive target has no complete target timing. The utility never invents signal timing: it uses a current mode, the monitor's preferred mode when that exactly matches, or a previously captured native mode. If none is available and validation fails, it exits without changing the displays and explains how to capture the requested mode.

The native layouts and flag behavior follow Microsoft's documentation for [QueryDisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-querydisplayconfig), [SetDisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setdisplayconfig), and [DISPLAYCONFIG_TARGET_DEVICE_NAME](https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name). Expected x64 structure sizes are checked at startup before any CCD call.

## Troubleshooting

- Run `DisplayMode.exe list --verbose` and confirm each hardware ID appears exactly once in its device path or friendly name.
- Run the desired mode with `--dry-run` before the first real switch.
- If mode `13` reports that GSM5BD2 has no complete native timing, run `DisplayMode.exe prepare-13`, change GSM5BD2 to 120 Hz in Windows/NVIDIA settings, then run `DisplayMode.exe capture --verbose`. On the tested machine its preferred timing was approximately 144 Hz (`143933/1000`), which cannot safely be converted into a 120 Hz signal timing by arithmetic.
- `ERROR_ACCESS_DENIED` usually means the program is not running in the interactive console session (for example, it is running through Remote Desktop or a service).
- `ERROR_BAD_CONFIGURATION` means the display driver could not produce the exact supplied layout/mode combination. Confirm the requested resolutions and refresh rates are currently exposed by the NVIDIA driver.
- If a monitor is identified but reported unavailable, check its cable, input selection, power state, and NVIDIA Control Panel configuration. The utility fails closed rather than forcing an unavailable target.
- NVIDIA driver behavior and the three real panels cannot be exercised by an ordinary build/test environment. Verify the full `13 -> 3 -> 2 -> 13 -> 2 -> 3` sequence locally, first using `--dry-run` and then with real switches.
