# Devices

Every native driver in this project was written for, and tested on, one specific piece of hardware.
Anything else goes through OpenRGB, which maintains support for a large number of devices.

## Verified

| Device | Id | Driver | What was tested |
| :--- | :--- | :--- | :--- |
| LCD of a Thermalright AIO ("USBDISPLAY", 320x240 panel) | USB HID `0416:5302`, screen code PM 51 | built in, `"lcd": true` | all themes, frame rate and CPU load per theme |
| ASUS AURA USB controller on a ROG STRIX B650E-F GAMING WIFI, firmware `AULA3-AR32-0309` | USB HID `0B05:19AF` | `aura` | header 0 (cooler and fan chain) and board LEDs, starting from the power-on rainbow |
| MSI GeForce RTX 5070 Gaming Trio | PCI `10DE:2F04`, subsystem `1462:5322` | `msi-gpu` | static colours, theme changes, brightness down to off |
| Nuvoton NCT6799D Super I/O on the same board (read only) | through LibreHardwareMonitor | sensors | fan speeds; the AIO pump of the test system is on `Fan #7` (about 3100 RPM) |

Not yet verified on any device: standby and resume, unplugging during operation.

### Finding the pump header

Run `UltraLightRgb.exe --sensors-all` from an elevated PowerShell. The `<SuperIO>` block lists every
board fan header with its speed; headers without a fan read 0. An AIO pump usually runs at a fixed,
high speed (around 2500-3500 RPM) while case fans follow the temperature. Pick that header on the
Display page of the switcher.

### Notes from testing

- **AURA channel mapping.** OpenRGB documents the effect channels of `AULA3-AR32` firmware as starting
  at 0 for the addressable headers. On the board above, effect channel 0 is the board itself and the
  addressable headers start at 1, while the Direct data channels are 0 for header 0 and 4 for the
  board. A wrong channel produces no error: the controller accepts the command and the power-on
  rainbow just keeps running. If the LEDs ignore this tool on your board, that is the first suspect.
- **MSI GPU after a cold start.** The card ignored the static colour until it had been switched to
  mode "off" once. The driver now does that on every start, so the card may blink dark for a moment.
- **LCD.** If the display stays dark and `--devices` shows the LCD as "not found", check whether the
  USB hub it sits behind is visible to Windows at all. A full power cut (PSU switch off for 30 s)
  brought it back in one case.

## Through OpenRGB

The `openrgb` driver talks to a running OpenRGB instance over its SDK protocol (TCP port 6742, local
machine only). It works with OpenRGB 0.9 (protocol 4) and 1.0rc1 (protocol 5). The client was tested
against a protocol-conforming test server, not yet against every OpenRGB device.

1. Start OpenRGB with its SDK server, for example `OpenRGB.exe --server --startminimized`.
2. `UltraLightRgb.exe --openrgb-list` shows the controllers and their LED counts.
3. Add an `openrgb` output to `devices.json`; `match` selects controllers by part of their name.

Never let OpenRGB and a native driver of this project drive the same device.

## Adding a device

A new native driver is only accepted with evidence from the real hardware:

1. Output of `UltraLightRgb.exe --devices` and of the vendor's or OpenRGB's identification of the device.
2. A description of how each command was verified by eye (which colour appeared where).
3. Writes must be **volatile**: no commit, save or EEPROM register may ever be written.
4. Writes must be **gated on the exact device id**. Many RGB chips share I2C addresses with unrelated
   chips; a write to the wrong one can damage hardware.

Until then, OpenRGB is the way to use a device that is not on the verified list.
