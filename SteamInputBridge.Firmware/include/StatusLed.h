#pragma once

#include <Arduino.h>

namespace SteamInputBridge {
    class StatusLed {
       public:
        StatusLed(uint8_t pin, uint32_t disconnectedBlinkIntervalMs);

        void begin();
        void update(bool connected, uint32_t now);

       private:
        uint8_t _pin;
        uint32_t _disconnectedBlinkIntervalMs;
        bool _active = false;

        void set(bool active);
    };
}  // namespace SteamInputBridge
