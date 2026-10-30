# SysPulse

A small Windows tray app that shows CPU/GPU temperature and load, switches CPU
turbo boost on and off, and points out apps worth closing to free memory or CPU.
It also draws a white, click-through readout in the top-right corner of the screen,
similar to an in-game FPS counter.

![main window](docs/main.png)

## Features

- **Overlay**: a white CPU / GPU / RAM / turbo readout in the top-right corner, always
  on top and click-through. Toggle it with **Ctrl+Shift+O**, the switch in the
  window, or the tray menu. Hold **Ctrl** over it to show a close button, or drag it
  somewhere else.
- **Overlay settings** (gear next to the Overlay switch, or the tray menu): stacked or
  one-line layout, small / medium / large, opacity 20–100%, optional dark backplate,
  and position. Pick any corner or edge, then fine-tune with the arrow buttons (hold to
  keep moving) or the arrow keys (1 px, Shift for 10 px). Changes apply live.
- **CPU**: load (Task Manager's "% Processor Utility"), live clock speed against
  the base clock, and package temperature and power (see below).
- **GPU**: load, temperature, VRAM, power and clock for NVIDIA GPUs (NVML, no admin
  needed). Load only for other GPUs. On hybrid laptops SysPulse checks the GPU's
  power state first and stops polling while it's idle, so it never keeps the dGPU awake.
- **Turbo boost**: one button that does the same as
  `powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0` (plus the DC
  value and `/setactive`). Turning it back on restores the previous boost mode
  (Aggressive by default). Also available from the tray menu and the command line:
  `SysPulse.exe --turbo off` / `--turbo on`.
- **Power mode**: the Windows 11 Efficiency / Balanced / Performance switch.
- **Free up memory and CPU**: apps grouped by program (all `chrome.exe` processes
  together) and sorted by memory or CPU. Open apps are closed normally, so they can
  still ask to save. Background apps are ended. Windows components, services and
  shell processes are never listed.
- Disk free space and activity, network speed, battery, power plan, uptime.
- **Start with Windows** switch. SysPulse starts in the tray with the overlay.
  Closing the window keeps it running in the tray; exit from the tray menu.

## CPU temperature

Windows has no API that lets regular apps read a modern CPU's temperature. SysPulse
reads it with [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
through the signed **PawnIO** driver, which needs:

1. PawnIO installed (one time): `winget install namazso.PawnIO`, or from https://pawnio.eu
2. SysPulse running as administrator ("Run as administrator" in the header).

Turn on **Start with Windows** *while running as administrator* to register a
logon task that runs elevated, so CPU temperature works after every sign-in without
a UAC prompt. Otherwise startup uses the normal `HKCU\...\Run` entry. Click
"Show CPU temp" in the CPU card for these steps.

## Build

Requires the .NET 8 SDK on Windows.

```bash
dotnet build -c Release
```

```bash
dotnet publish src/SysPulse -c Release -r win-x64 --self-contained false -o publish
```

Run `publish\SysPulse.exe`. If Start with Windows was turned on from another copy,
the startup entry moves to whichever copy you launch.

For UI checks without touching the desktop, `SysPulse.exe --snapshot <folder>` samples
the real sensors for a few seconds off-screen and saves `main.png`, `overlay.png`,
`overlay-ctrl.png` and the readings to that folder.

## Files

Settings live in `%LOCALAPPDATA%\SysPulse\settings.json`.
