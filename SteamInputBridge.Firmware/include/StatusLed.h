#pragma once

#include <Arduino.h>

namespace SteamInputBridge {
    class StatusLed {
       public:
        StatusLed(uint8_t pin, uint32_t inputDurationMs);

        void begin();
        void showInput(bool hasInput, uint32_t now);
        void update(uint32_t now);

       private:
        uint8_t _pin;
        uint32_t _inputDurationMs;
        uint32_t _lastInputAt = 0;
        bool _inputActive = false;

        void set(bool active);
    };
}  // namespace SteamInputBridge
