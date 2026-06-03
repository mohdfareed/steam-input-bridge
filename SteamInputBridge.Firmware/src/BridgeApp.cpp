#include "BridgeApp.h"

#include <Arduino.h>

namespace SteamInputBridge {
    BridgeApp::BridgeApp(uint8_t ledPin, uint32_t inputBlinkDurationMs) : _statusLed(ledPin, inputBlinkDurationMs) {}

    void BridgeApp::begin() {
        _mouse.begin();
        _statusLed.begin();
    }

    void BridgeApp::update(uint32_t now) {
        readSerialFrames(now);
        _statusLed.update(now);
    }

    void BridgeApp::readSerialFrames(uint32_t now) {
        BridgeMessage message;

        while (Serial.available() > 0) {
            const int value = Serial.read();
            if (value >= 0 && _protocol.read(static_cast<uint8_t>(value), message)) {
                handleMessage(message, now);
            }
        }
    }

    void BridgeApp::handleMessage(const BridgeMessage& message, uint32_t now) {
        if (message.type == BridgeMessageType::HandshakeProbe) {
            const uint8_t bytes =
                writeHandshakeResponse(message.sequence, _handshakeResponse, sizeof(_handshakeResponse));
            Serial.write(_handshakeResponse, bytes);
            return;
        }

        const bool hasInput = message.mouse.hasInput();
        _mouse.apply(message.mouse);
        _statusLed.showInput(hasInput, now);
    }
}  // namespace SteamInputBridge
