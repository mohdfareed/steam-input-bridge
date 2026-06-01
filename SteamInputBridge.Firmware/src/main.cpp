#include <Arduino.h>

#include "BridgeProtocol.h"
#include "MouseEmulator.h"
#include "StatusLed.h"

namespace {

constexpr uint32_t BaudRate = 115200;
constexpr uint32_t InputBlinkDurationMs = 100;

SteamInputBridge::BridgeProtocolReader protocol;
SteamInputBridge::MouseEmulator mouse;
SteamInputBridge::StatusLed statusLed(LED_BUILTIN, InputBlinkDurationMs);

}  // namespace

void setup() {
  Serial.begin(BaudRate);
  mouse.begin();
  statusLed.begin();
}

void loop() {
  SteamInputBridge::MouseReport report;

  while (Serial.available() > 0) {
    const int value = Serial.read();

    if (value >= 0 && protocol.read(static_cast<uint8_t>(value), report)) {
      mouse.apply(report);
      statusLed.showInput(report.hasInput(), millis());
    }
  }

  statusLed.update(millis());
}
