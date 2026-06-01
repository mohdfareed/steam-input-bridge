#include "MouseEmulator.h"

#include <Arduino.h>
#include <Mouse.h>

namespace SteamInputBridge {

namespace {

constexpr uint16_t MouseLeft = 1u << 0;
constexpr uint16_t MouseRight = 1u << 1;
constexpr uint16_t MouseMiddle = 1u << 2;
constexpr uint16_t MouseBack = 1u << 3;
constexpr uint16_t MouseForward = 1u << 4;

int8_t clampMouseStep(int16_t value) {
  if (value > 127) {
    return 127;
  }

  if (value < -127) {
    return -127;
  }

  return static_cast<int8_t>(value);
}

}  // namespace

void MouseEmulator::begin() { Mouse.begin(); }

void MouseEmulator::apply(const MouseReport& report) {
  applyButtons(report.buttons);
  emitMove(report.deltaX, report.deltaY, report.wheel);
}

void MouseEmulator::applyButtons(uint16_t buttons) {
  emitButton(buttons, _buttons, MouseLeft, MOUSE_LEFT);
  emitButton(buttons, _buttons, MouseRight, MOUSE_RIGHT);
  emitButton(buttons, _buttons, MouseMiddle, MOUSE_MIDDLE);

#if defined(MOUSE_BACK) && defined(MOUSE_FORWARD)
  emitButton(buttons, _buttons, MouseBack, MOUSE_BACK);
  emitButton(buttons, _buttons, MouseForward, MOUSE_FORWARD);
#endif

  _buttons = buttons;
}

void MouseEmulator::emitButton(uint16_t nextButtons, uint16_t previousButtons,
                               uint16_t bridgeButton, uint8_t teensyButton) {
  if ((nextButtons & bridgeButton) == (previousButtons & bridgeButton)) {
    return;
  }

  (nextButtons & bridgeButton) != 0 ? Mouse.press(teensyButton)
                                    : Mouse.release(teensyButton);
}

void MouseEmulator::emitMove(int16_t deltaX, int16_t deltaY, int16_t wheel) {
  while (deltaX != 0 || deltaY != 0 || wheel != 0) {
    const int8_t stepX = clampMouseStep(deltaX);
    const int8_t stepY = clampMouseStep(deltaY);
    const int8_t stepWheel = clampMouseStep(wheel);

    Mouse.move(stepX, stepY, stepWheel);

    deltaX = static_cast<int16_t>(deltaX - stepX);
    deltaY = static_cast<int16_t>(deltaY - stepY);
    wheel = static_cast<int16_t>(wheel - stepWheel);
  }
}

}  // namespace SteamInputBridge
