# Third-party notices

SysPulse is MIT-licensed (see [LICENSE](LICENSE)). The released `SysPulse.exe`
bundles the libraries below, unchanged, under their own licenses. Their source
code is available at the links given.

| Component | Version | License | Source |
|---|---|---|---|
| LibreHardwareMonitorLib (includes its embedded PawnIO modules) | 0.9.6 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| HidSharp | 2.6.4 | Apache-2.0 | https://software.seekye.com/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | https://github.com/mono/mono |
| System.Management, System.IO.Ports, System.CodeDom, System.Threading.AccessControl | 10.0.x | MIT | https://github.com/dotnet/runtime |
| .NET runtime and Windows Desktop (Windows Forms) | 8.0.31 | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/winforms |

License texts online:

- MPL-2.0: https://mozilla.org/MPL/2.0/
- Apache-2.0: https://www.apache.org/licenses/LICENSE-2.0
- MIT: https://opensource.org/license/mit

## Not bundled

- **PawnIO** (the driver used to read the CPU temperature) is installed by the user
  separately from https://pawnio.eu and is not part of SysPulse.
- **NVIDIA Management Library** (`nvml.dll`) ships with the NVIDIA driver; SysPulse
  calls it if it is present.

## Full license texts

- HidSharp (Apache-2.0, with its copyright notice): [licenses/HidSharp.txt](licenses/HidSharp.txt)
- .NET (MIT): [licenses/dotnet.txt](licenses/dotnet.txt)
