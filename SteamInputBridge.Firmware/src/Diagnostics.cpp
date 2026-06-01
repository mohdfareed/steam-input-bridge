#include "Diagnostics.h"

#include <Arduino.h>
#include <stdio.h>

namespace SteamInputBridge {

Diagnostics::Diagnostics(uint32_t inputActiveDurationMs,
                         uint32_t diagnosticIntervalMs)
    : _inputActiveDurationMs(inputActiveDurationMs),
      _diagnosticIntervalMs(diagnosticIntervalMs) {}

void Diagnostics::begin(uint32_t now) { _lastDiagnosticAt = now; }

void Diagnostics::updateSerialConnection(uint32_t now) {
  const bool connected = static_cast<bool>(Serial);
  if (connected == _serialConnected) {
    return;
  }

  _serialConnected = connected;
  if (!connected) {
    _pendingDisconnectedEvent = true;
    _lastDisconnectedAt = now;
    _serialDisconnects++;
    return;
  }

  if (_pendingDisconnectedEvent) {
    writeSerialEvent("serial-disconnected", _lastDisconnectedAt);
    _pendingDisconnectedEvent = false;
  }

  writeSerialEvent("serial-connected", now);
}

void Diagnostics::recordHandshake(uint8_t sequence, uint32_t now) {
  _handshakeProbesReceived++;
  writeHandshakeEvent(sequence, now);
}

void Diagnostics::recordMouseReport(bool hasInput, uint32_t now) {
  _mouseReportsReceived++;
  if (hasInput) {
    _lastInputAt = now;
  }
}

void Diagnostics::update(uint32_t now) {
  if (now - _lastDiagnosticAt < _diagnosticIntervalMs) {
    return;
  }

  _lastDiagnosticAt = now;
  const bool active =
      _lastInputAt != 0 && now - _lastInputAt < _inputActiveDurationMs;
  const uint32_t idleMs = _lastInputAt == 0 ? now : now - _lastInputAt;

  char line[160];
  const int length = snprintf(
      line, sizeof(line),
      "ok uptime_ms=%lu reports=%lu handshakes=%lu serial_disconnects=%lu "
      "active=%u idle_ms=%lu",
      static_cast<unsigned long>(now),
      static_cast<unsigned long>(_mouseReportsReceived),
      static_cast<unsigned long>(_handshakeProbesReceived),
      static_cast<unsigned long>(_serialDisconnects), active ? 1u : 0u,
      static_cast<unsigned long>(idleMs));
  writeLine(line, length, sizeof(line));
}

void Diagnostics::writeSerialEvent(const char* event, uint32_t eventAt) {
  char line[96];
  const int length =
      snprintf(line, sizeof(line), "event=%s uptime_ms=%lu", event,
               static_cast<unsigned long>(eventAt));
  writeLine(line, length, sizeof(line));
}

void Diagnostics::writeHandshakeEvent(uint8_t sequence, uint32_t now) {
  char line[96];
  const int length = snprintf(
      line, sizeof(line), "event=handshake sequence=%u uptime_ms=%lu",
      static_cast<unsigned int>(sequence), static_cast<unsigned long>(now));
  writeLine(line, length, sizeof(line));
}

void Diagnostics::writeLine(const char* line, int length, uint32_t capacity) {
  if (length <= 0 || static_cast<uint32_t>(length) >= capacity || !Serial) {
    return;
  }

  if (Serial.availableForWrite() < length + 2) {
    return;
  }

  Serial.println(line);
}

}  // namespace SteamInputBridge
