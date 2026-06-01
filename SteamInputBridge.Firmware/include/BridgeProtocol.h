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

class BridgeProtocolReader {
 public:
  static constexpr uint8_t HeaderSize = 7;
  static constexpr uint8_t PayloadSize = 8;
  static constexpr uint8_t ChecksumSize = 2;
  static constexpr uint8_t FrameSize = HeaderSize + PayloadSize + ChecksumSize;

  bool read(uint8_t value, MouseReport& report);

 private:
  uint8_t _frame[FrameSize] = {};
  uint8_t _offset = 0;

  void reset(uint8_t firstByte);
  bool validate() const;
  MouseReport readMouseReport() const;
};

}  // namespace SteamInputBridge
