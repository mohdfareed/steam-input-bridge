#pragma once

#include <stdint.h>

#include "BridgeProtocol.h"
#include "MouseEmulator.h"
#include "StatusLed.h"

namespace SteamInputBridge {
    class BridgeApp {
       public:
        BridgeApp(uint8_t ledPin, uint32_t inputBlinkDurationMs);

        void begin();
        void update(uint32_t now);

       private:
        BridgeProtocolReader _protocol;
        MouseEmulator _mouse;
        StatusLed _statusLed;
        uint8_t _handshakeResponse[BridgeProtocolReader::HandshakeResponseFrameSize] = {};

        void readSerialFrames(uint32_t now);
        void handleMessage(const BridgeMessage& message, uint32_t now);
    };
}  // namespace SteamInputBridge
