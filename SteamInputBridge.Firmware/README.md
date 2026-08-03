# Steam Input Bridge Firmware

Teensy 4.0 firmware for the hardware mouse output.

- USB mode: `Serial + Keyboard + Mouse + Joystick`.
- Protocol: fixed binary frames with CRC16.
- LED: solid when the host serial connection is open, slow blink while waiting for the host.
- Build: run the repo build script; it packages `SteamInputBridge.Teensy.hex`.

## Resources

[Teensy CAD model](./enclosure/teensy-4.0-cad.zip): https://grabcad.com/library/teensy-4-0-1
[USB-A connector CAD model](./enclosure/usb-a-cad.zip): https://www.traceparts.com/goto?Product=90-29032021-052020
[Micro-USB connector CAD model](./enclosure/micro-usb-cad.zip): https://www.traceparts.com/goto?Product=90-26052023-026782
