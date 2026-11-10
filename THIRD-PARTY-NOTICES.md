# Third-party notices

Ultra Light RGB Control is licensed under GPL-3.0-or-later (see `LICENSE`). It uses or builds on the
following work. Licence texts are in the `licenses` folder.

## Libraries shipped in the release

| Library (version) | Use | Licence | Source |
| :--- | :--- | :--- | :--- |
| HidSharp 2.6.4 (James F. Bellinger) | USB HID access to the LCD and the AURA controller | Apache-2.0 | <https://software.seekye.com/hidsharp> |
| LibreHardwareMonitorLib 0.9.6 | CPU and GPU temperature and load | MPL-2.0 | <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor> |
| BlackSharp.Core 1.0.7 | dependency of LibreHardwareMonitorLib | MPL-2.0 | <https://github.com/Blacktempel/BlackSharp> |
| DiskInfoToolkit 1.1.2 | dependency of LibreHardwareMonitorLib | MPL-2.0 | <https://github.com/Blacktempel/DiskInfoToolkit> |
| RAMSPDToolkit-NDD 1.4.2 | dependency of LibreHardwareMonitorLib | MPL-2.0 | <https://github.com/Blacktempel/RAMSPDToolkit> |
| Mono.Posix.NETStandard 1.0.0 (incl. MonoPosixHelper) | dependency of LibreHardwareMonitorLib | MIT (Mono class libraries, see `licenses/mono-LICENSE.txt`) | <https://github.com/mono/mono> |
| System.Drawing.Common, Microsoft.Win32.SystemEvents, System.CodeDom, System.IO.Ports, System.Management, System.Threading.AccessControl (Microsoft) | rendering, sensors | MIT | <https://github.com/dotnet/runtime>, <https://github.com/dotnet/winforms> |

The MPL-2.0 libraries are used unmodified; their source code is available at the links above.

## Protocol knowledge

No code was copied from these projects; register maps and packet layouts were re-implemented from
them and then tested on real hardware.

| Project | What came from it | Licence |
| :--- | :--- | :--- |
| [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) | ASUS AURA USB commands, MSI GPU v2 register map, SDK protocol (`Documentation/OpenRGBSDK.md`) | GPL-2.0-or-later |
| [rejeb/linux-thermalright-lcd-control](https://github.com/rejeb/linux-thermalright-lcd-control) | LCD handshake and frame format | Apache-2.0 |

## Trademarks

Thermalright, ASUS, AURA, ROG, MSI, NVIDIA and OpenRGB are names of their respective owners. They
are used here only to say which hardware and software this project works with. This project is not
affiliated with or endorsed by any of them.
