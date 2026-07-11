#include "BridgeApp.h"

#include <Arduino.h>

namespace SteamInputBridge {
    BridgeApp::BridgeApp(uint8_t ledPin, uint32_t disconnectedBlinkIntervalMs)
        : _statusLed(ledPin, disconnectedBlinkIntervalMs) {}

    void BridgeApp::begin() {
        _mouse.begin();
        _statusLed.begin();
    }

    void BridgeApp::update(uint32_t now) {
        readSerialFrames();
        _statusLed.update(isConnected(), now);
    }

    void BridgeApp::readSerialFrames() {
        BridgeMessage message;

        while (Serial.available() > 0) {
            const int value = Serial.read();
            if (value >= 0 && _protocol.read(static_cast<uint8_t>(value), message)) {
                handleMessage(message);
            }
        }
    }

    void BridgeApp::handleMessage(const BridgeMessage& message) {
        if (message.type == BridgeMessageType::HandshakeProbe) {
            const uint8_t bytes =
                writeHandshakeResponse(message.sequence, _handshakeResponse, sizeof(_handshakeResponse));
            Serial.write(_handshakeResponse, bytes);
            return;
        }

        _mouse.apply(message.mouse);
    }

    bool BridgeApp::isConnected() const { return static_cast<bool>(Serial); }
}  // namespace SteamInputBridge
