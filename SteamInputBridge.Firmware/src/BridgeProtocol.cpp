#include "BridgeProtocol.h"

#ifndef STEAM_INPUT_BRIDGE_PROTOCOL_VERSION
#define STEAM_INPUT_BRIDGE_PROTOCOL_VERSION 1
#endif

namespace SteamInputBridge {

namespace {

constexpr uint8_t Magic0 = 'S';
constexpr uint8_t Magic1 = 'I';
constexpr uint8_t Magic2 = 'B';
constexpr uint8_t ProtocolVersion = STEAM_INPUT_BRIDGE_PROTOCOL_VERSION;
constexpr uint8_t MouseReportType = 0x01;

uint16_t crc16(const uint8_t* data, uint8_t length) {
  uint16_t crc = 0xFFFF;
  for (uint8_t i = 0; i < length; i++) {
    crc = static_cast<uint16_t>(crc ^ (static_cast<uint16_t>(data[i]) << 8));
    for (uint8_t bit = 0; bit < 8; bit++) {
      crc = (crc & 0x8000) != 0 ? static_cast<uint16_t>((crc << 1) ^ 0x1021)
                                : static_cast<uint16_t>(crc << 1);
    }
  }

  return crc;
}

int16_t readInt16LittleEndian(const uint8_t* data) {
  return static_cast<int16_t>(static_cast<uint16_t>(data[0]) |
                              (static_cast<uint16_t>(data[1]) << 8));
}

uint16_t readUInt16LittleEndian(const uint8_t* data) {
  return static_cast<uint16_t>(data[0]) | (static_cast<uint16_t>(data[1]) << 8);
}

}  // namespace

bool MouseReport::hasInput() const {
  return buttons != 0 || deltaX != 0 || deltaY != 0 || wheel != 0;
}

bool BridgeProtocolReader::read(uint8_t value, MouseReport& report) {
  if (_offset == 0 && value != Magic0) {
    return false;
  }

  _frame[_offset++] = value;

  if (_offset == 2 && _frame[1] != Magic1) {
    reset(value);
    return false;
  }

  if (_offset == 3 && _frame[2] != Magic2) {
    reset(value);
    return false;
  }

  if (_offset != FrameSize) {
    return false;
  }

  const bool valid = validate();
  if (valid) {
    report = readMouseReport();
  }

  _offset = 0;
  return valid;
}

void BridgeProtocolReader::reset(uint8_t firstByte) {
  _offset = 0;
  if (firstByte == Magic0) {
    _frame[_offset++] = firstByte;
  }
}

bool BridgeProtocolReader::validate() const {
  if (_frame[0] != Magic0 || _frame[1] != Magic1 || _frame[2] != Magic2) {
    return false;
  }

  if (_frame[3] != ProtocolVersion || _frame[4] != MouseReportType ||
      _frame[6] != PayloadSize) {
    return false;
  }

  const uint16_t expected = crc16(_frame, HeaderSize + PayloadSize);
  const uint16_t actual =
      readUInt16LittleEndian(_frame + HeaderSize + PayloadSize);
  return expected == actual;
}

MouseReport BridgeProtocolReader::readMouseReport() const {
  const uint8_t* payload = _frame + HeaderSize;
  return MouseReport{
      readUInt16LittleEndian(payload),
      readInt16LittleEndian(payload + 2),
      readInt16LittleEndian(payload + 4),
      readInt16LittleEndian(payload + 6),
  };
}

}  // namespace SteamInputBridge
