#include <Arduino.h>

#include "BridgeApp.h"

namespace {

constexpr uint32_t BaudRate = 115200;
constexpr uint32_t InputBlinkDurationMs = 100;

SteamInputBridge::BridgeApp app(LED_BUILTIN, InputBlinkDurationMs);

}  // namespace

void setup() {
  Serial.begin(BaudRate);
  app.begin();
}

void loop() { app.update(millis()); }
