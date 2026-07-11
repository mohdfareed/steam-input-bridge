#include <Arduino.h>

#include "BridgeApp.h"

namespace {
    constexpr uint32_t BaudRate = 115200;
    constexpr uint32_t DisconnectedBlinkIntervalMs = 500;

    SteamInputBridge::BridgeApp app(LED_BUILTIN, DisconnectedBlinkIntervalMs);
}  // namespace

void setup() {
    Serial.begin(BaudRate);
    app.begin();
}

void loop() { app.update(millis()); }
