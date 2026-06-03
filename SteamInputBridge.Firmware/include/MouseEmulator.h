#pragma once

#include "BridgeProtocol.h"

namespace SteamInputBridge {
    class MouseEmulator {
       public:
        void begin();
        void apply(const MouseReport& report);

       private:
        uint16_t _buttons = 0;

        void applyButtons(uint16_t buttons);
        static void emitButton(uint16_t nextButtons, uint16_t previousButtons, uint16_t bridgeButton,
                               uint8_t teensyButton);
        static void emitMove(int16_t deltaX, int16_t deltaY, int16_t wheel);
    };
}  // namespace SteamInputBridge
