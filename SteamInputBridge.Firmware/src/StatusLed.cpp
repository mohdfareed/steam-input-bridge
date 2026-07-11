#include "StatusLed.h"

namespace SteamInputBridge {
    StatusLed::StatusLed(uint8_t pin, uint32_t disconnectedBlinkIntervalMs)
        : _pin(pin), _disconnectedBlinkIntervalMs(disconnectedBlinkIntervalMs) {}

    void StatusLed::begin() {
        pinMode(_pin, OUTPUT);
        digitalWrite(_pin, LOW);
        _active = false;
    }

    void StatusLed::update(bool connected, uint32_t now) {
        if (connected) {
            set(true);
            return;
        }

        const bool active = ((now / _disconnectedBlinkIntervalMs) % 2) == 0;
        set(active);
    }

    void StatusLed::set(bool active) {
        if (_active == active) {
            return;
        }

        digitalWrite(_pin, active ? HIGH : LOW);
        _active = active;
    }
}  // namespace SteamInputBridge
