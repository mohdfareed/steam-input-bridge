#pragma once

#include <stdint.h>

#include "BridgeProtocol.h"
#include "MouseEmulator.h"
#include "StatusLed.h"

namespace SteamInputBridge {
    class BridgeApp {
       public:
        BridgeApp(uint8_t ledPin, uint32_t disconnectedBlinkIntervalMs);

        void begin();
        void update(uint32_t now);

       private:
        BridgeProtocolReader _protocol;
        MouseEmulator _mouse;
        StatusLed _statusLed;
        uint8_t _handshakeResponse[BridgeProtocolReader::HandshakeResponseFrameSize] = {};

        void readSerialFrames();
        void handleMessage(const BridgeMessage& message);
        bool isConnected() const;
    };
}  // namespace SteamInputBridge
