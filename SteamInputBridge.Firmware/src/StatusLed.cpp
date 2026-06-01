#include "StatusLed.h"

namespace SteamInputBridge {

StatusLed::StatusLed(uint8_t pin, uint32_t inputDurationMs)
    : _pin(pin), _inputDurationMs(inputDurationMs) {}

void StatusLed::begin() {
  pinMode(_pin, OUTPUT);
  set(false);
}

void StatusLed::showInput(bool hasInput, uint32_t now) {
  if (hasInput) {
    _inputActive = true;
    _lastInputAt = now;
    return;
  }

  _inputActive = false;
  set(false);
}

void StatusLed::update(uint32_t now) {
  const bool active = _inputActive && now - _lastInputAt < _inputDurationMs;
  set(active);
  _inputActive = active;
}

void StatusLed::set(bool active) { digitalWrite(_pin, active ? HIGH : LOW); }

}  // namespace SteamInputBridge
