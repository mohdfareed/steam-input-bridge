#include <Arduino.h>

#include "BridgeApp.h"

namespace {

constexpr uint32_t BaudRate = 115200;
constexpr uint32_t InputBlinkDurationMs = 100;
constexpr uint32_t DiagnosticIntervalMs = 1000;

SteamInputBridge::BridgeApp app(LED_BUILTIN, InputBlinkDurationMs,
                                DiagnosticIntervalMs);

}  // namespace

void setup() {
  Serial.begin(BaudRate);
  app.begin(millis());
}

void loop() {
  app.update(millis());
}
