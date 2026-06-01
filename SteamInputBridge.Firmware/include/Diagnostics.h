#pragma once

#include <stdint.h>

namespace SteamInputBridge {

class Diagnostics {
 public:
  Diagnostics(uint32_t inputActiveDurationMs, uint32_t diagnosticIntervalMs);

  void begin(uint32_t now);
  void updateSerialConnection(uint32_t now);
  void recordHandshake(uint8_t sequence, uint32_t now);
  void recordMouseReport(bool hasInput, uint32_t now);
  void update(uint32_t now);

 private:
  uint32_t _inputActiveDurationMs;
  uint32_t _diagnosticIntervalMs;
  uint32_t _lastDiagnosticAt = 0;
  uint32_t _lastInputAt = 0;
  uint32_t _mouseReportsReceived = 0;
  uint32_t _handshakeProbesReceived = 0;
  uint32_t _serialDisconnects = 0;
  uint32_t _lastDisconnectedAt = 0;
  bool _serialConnected = false;
  bool _pendingDisconnectedEvent = false;

  void writeSerialEvent(const char* event, uint32_t eventAt);
  void writeHandshakeEvent(uint8_t sequence, uint32_t now);
  static void writeLine(const char* line, int length, uint32_t capacity);
};

}  // namespace SteamInputBridge
