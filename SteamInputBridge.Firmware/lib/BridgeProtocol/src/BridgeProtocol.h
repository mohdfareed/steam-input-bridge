#pragma once

#include <stdint.h>

namespace SteamInputBridge {

struct MouseReport {
  uint16_t buttons = 0;
  int16_t deltaX = 0;
  int16_t deltaY = 0;
  int16_t wheel = 0;

  bool hasInput() const;
};

enum class BridgeMessageType : uint8_t {
  HandshakeProbe,
  MouseReport,
};

struct BridgeMessage {
  BridgeMessageType type = BridgeMessageType::MouseReport;
  uint8_t sequence = 0;
  MouseReport mouse;
};

class BridgeProtocolReader {
 public:
  static constexpr uint8_t HeaderSize = 7;
  static constexpr uint8_t MousePayloadSize = 8;
  static constexpr uint8_t ChecksumSize = 2;
  static constexpr uint8_t FrameBufferSize =
      HeaderSize + MousePayloadSize + ChecksumSize;
  static constexpr uint8_t MouseFrameSize =
      HeaderSize + MousePayloadSize + ChecksumSize;
  static constexpr uint8_t HandshakeProbeFrameSize = HeaderSize + ChecksumSize;
  static constexpr uint8_t HandshakeResponsePayloadSize = 4;
  static constexpr uint8_t HandshakeResponseFrameSize =
      HeaderSize + HandshakeResponsePayloadSize + ChecksumSize;

  bool read(uint8_t value, BridgeMessage& message);

 private:
  uint8_t _frame[FrameBufferSize] = {};
  uint8_t _offset = 0;

  void reset(uint8_t firstByte);
  bool validate() const;
  bool hasExpectedPayloadSize() const;
  uint8_t expectedFrameSize() const;
  BridgeMessage readMessage() const;
  MouseReport readMouseReport() const;
};

uint8_t writeHandshakeResponse(uint8_t sequence, uint8_t* destination,
                               uint8_t length);

}  // namespace SteamInputBridge
