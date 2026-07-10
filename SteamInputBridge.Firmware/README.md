# Steam Input Bridge Firmware

Teensy 4.0 firmware for the hardware mouse output.

- USB mode: `Serial + Keyboard + Mouse + Joystick`.
- Protocol: fixed binary frames with CRC16.
- LED: solid when the host serial connection is open, slow blink while waiting for the host.
- Build: run the repo build script; it packages `SteamInputBridge.Teensy.hex`.

## Credits

[Teensy CAD model](./teensy_4.0.step): https://grabcad.com/library/teensy-4-0-1
