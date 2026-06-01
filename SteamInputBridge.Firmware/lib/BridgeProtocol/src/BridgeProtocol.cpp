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
constexpr uint8_t HandshakeProbeType = 0x00;
constexpr uint8_t MouseReportType = 0x01;
constexpr uint8_t HandshakeResponseType = 0x80;
constexpr uint8_t HandshakeResponsePayload[] = {'T', 'N', 'S', 'Y'};

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

void writeUInt16LittleEndian(uint8_t* data, uint16_t value) {
  data[0] = static_cast<uint8_t>(value & 0xFF);
  data[1] = static_cast<uint8_t>(value >> 8);
}

}  // namespace

bool MouseReport::hasInput() const {
  return buttons != 0 || deltaX != 0 || deltaY != 0 || wheel != 0;
}

bool BridgeProtocolReader::read(uint8_t value, BridgeMessage& message) {
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

  if (_offset == 4 && _frame[3] != ProtocolVersion) {
    reset(value);
    return false;
  }

  if (_offset == 5 && _frame[4] != HandshakeProbeType &&
      _frame[4] != MouseReportType) {
    reset(value);
    return false;
  }

  if (_offset == HeaderSize && !hasExpectedPayloadSize()) {
    reset(value);
    return false;
  }

  if (_offset != expectedFrameSize()) {
    return false;
  }

  const bool valid = validate();
  if (valid) {
    message = readMessage();
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

  if (_frame[3] != ProtocolVersion || !hasExpectedPayloadSize()) {
    return false;
  }

  const uint8_t payloadSize = _frame[6];
  const uint16_t expected = crc16(_frame, HeaderSize + payloadSize);
  const uint16_t actual =
      readUInt16LittleEndian(_frame + HeaderSize + payloadSize);
  return expected == actual;
}

bool BridgeProtocolReader::hasExpectedPayloadSize() const {
  return (_frame[4] == HandshakeProbeType && _frame[6] == 0) ||
         (_frame[4] == MouseReportType && _frame[6] == MousePayloadSize);
}

uint8_t BridgeProtocolReader::expectedFrameSize() const {
  return _offset < HeaderSize
             ? HeaderSize
             : static_cast<uint8_t>(HeaderSize + _frame[6] + ChecksumSize);
}

BridgeMessage BridgeProtocolReader::readMessage() const {
  if (_frame[4] == HandshakeProbeType) {
    return BridgeMessage{BridgeMessageType::HandshakeProbe, _frame[5], {}};
  }

  return BridgeMessage{BridgeMessageType::MouseReport, _frame[5],
                       readMouseReport()};
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

uint8_t writeHandshakeResponse(uint8_t sequence, uint8_t* destination,
                               uint8_t length) {
  if (length < BridgeProtocolReader::HandshakeResponseFrameSize) {
    return 0;
  }

  destination[0] = Magic0;
  destination[1] = Magic1;
  destination[2] = Magic2;
  destination[3] = ProtocolVersion;
  destination[4] = HandshakeResponseType;
  destination[5] = sequence;
  destination[6] = BridgeProtocolReader::HandshakeResponsePayloadSize;

  for (uint8_t i = 0; i < BridgeProtocolReader::HandshakeResponsePayloadSize;
       i++) {
    destination[BridgeProtocolReader::HeaderSize + i] =
        HandshakeResponsePayload[i];
  }

  const uint16_t checksum = crc16(
      destination, BridgeProtocolReader::HeaderSize +
                       BridgeProtocolReader::HandshakeResponsePayloadSize);
  writeUInt16LittleEndian(
      destination + BridgeProtocolReader::HeaderSize +
          BridgeProtocolReader::HandshakeResponsePayloadSize,
      checksum);
  return BridgeProtocolReader::HandshakeResponseFrameSize;
}

}  // namespace SteamInputBridge
