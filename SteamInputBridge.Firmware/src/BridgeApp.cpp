#include "BridgeApp.h"

#include <Arduino.h>

namespace SteamInputBridge {

BridgeApp::BridgeApp(uint8_t ledPin, uint32_t inputBlinkDurationMs,
                     uint32_t diagnosticIntervalMs)
    : _statusLed(ledPin, inputBlinkDurationMs),
      _diagnostics(inputBlinkDurationMs, diagnosticIntervalMs) {}

void BridgeApp::begin(uint32_t now) {
  _mouse.begin();
  _statusLed.begin();
  _diagnostics.begin(now);
}

void BridgeApp::update(uint32_t now) {
  _diagnostics.updateSerialConnection(now);
  readSerialFrames(now);
  _diagnostics.update(now);
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
    const uint8_t bytes = writeHandshakeResponse(
        message.sequence, _handshakeResponse, sizeof(_handshakeResponse));
    Serial.write(_handshakeResponse, bytes);
    _diagnostics.recordHandshake(message.sequence, now);
    return;
  }

  const bool hasInput = message.mouse.hasInput();
  _diagnostics.recordMouseReport(hasInput, now);
  _mouse.apply(message.mouse);
  _statusLed.showInput(hasInput, now);
}

}  // namespace SteamInputBridge
