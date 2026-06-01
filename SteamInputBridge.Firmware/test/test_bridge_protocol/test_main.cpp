#include <unity.h>

#include "BridgeProtocol.h"

#ifdef ARDUINO
#include <Arduino.h>
#endif

using SteamInputBridge::BridgeProtocolReader;
using SteamInputBridge::MouseReport;

namespace {

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

void writeUInt16LittleEndian(uint8_t* data, uint16_t value) {
  data[0] = static_cast<uint8_t>(value & 0xFF);
  data[1] = static_cast<uint8_t>(value >> 8);
}

void writeInt16LittleEndian(uint8_t* data, int16_t value) {
  writeUInt16LittleEndian(data, static_cast<uint16_t>(value));
}

void writeMouseFrame(uint8_t* frame, uint8_t sequence,
                     const MouseReport& report) {
  frame[0] = 'S';
  frame[1] = 'I';
  frame[2] = 'B';
  frame[3] = 1;
  frame[4] = 1;
  frame[5] = sequence;
  frame[6] = BridgeProtocolReader::PayloadSize;

  writeUInt16LittleEndian(frame + 7, report.buttons);
  writeInt16LittleEndian(frame + 9, report.deltaX);
  writeInt16LittleEndian(frame + 11, report.deltaY);
  writeInt16LittleEndian(frame + 13, report.wheel);

  const uint16_t checksum = crc16(frame, BridgeProtocolReader::HeaderSize +
                                             BridgeProtocolReader::PayloadSize);
  writeUInt16LittleEndian(frame + 15, checksum);
}

bool readFrame(BridgeProtocolReader& reader, const uint8_t* frame,
               MouseReport& report) {
  bool parsed = false;
  for (uint8_t i = 0; i < BridgeProtocolReader::FrameSize; i++) {
    parsed = reader.read(frame[i], report);
  }

  return parsed;
}

}  // namespace

void parses_valid_mouse_frame() {
  BridgeProtocolReader reader;
  MouseReport input{0x0005, -42, 256, 120};
  MouseReport output;
  uint8_t frame[BridgeProtocolReader::FrameSize] = {};
  writeMouseFrame(frame, 7, input);

  TEST_ASSERT_TRUE(readFrame(reader, frame, output));
  TEST_ASSERT_EQUAL_UINT16(input.buttons, output.buttons);
  TEST_ASSERT_EQUAL_INT16(input.deltaX, output.deltaX);
  TEST_ASSERT_EQUAL_INT16(input.deltaY, output.deltaY);
  TEST_ASSERT_EQUAL_INT16(input.wheel, output.wheel);
  TEST_ASSERT_TRUE(output.hasInput());
}

void rejects_bad_checksum_and_resynchronizes() {
  BridgeProtocolReader reader;
  MouseReport report;
  uint8_t badFrame[BridgeProtocolReader::FrameSize] = {};
  writeMouseFrame(badFrame, 1, MouseReport{0x0001, 2, 3, 4});
  badFrame[15] ^= 0x7F;

  TEST_ASSERT_FALSE(readFrame(reader, badFrame, report));

  uint8_t goodFrame[BridgeProtocolReader::FrameSize] = {};
  writeMouseFrame(goodFrame, 2, MouseReport{0, 0, 0, 0});
  TEST_ASSERT_TRUE(readFrame(reader, goodFrame, report));
  TEST_ASSERT_FALSE(report.hasInput());
}

void ignores_noise_before_magic() {
  BridgeProtocolReader reader;
  MouseReport report;
  TEST_ASSERT_FALSE(reader.read(0x00, report));
  TEST_ASSERT_FALSE(reader.read('X', report));

  uint8_t frame[BridgeProtocolReader::FrameSize] = {};
  writeMouseFrame(frame, 3, MouseReport{0x0002, 0, 0, 0});
  TEST_ASSERT_TRUE(readFrame(reader, frame, report));
  TEST_ASSERT_EQUAL_UINT16(0x0002, report.buttons);
}

int runTests() {
  UNITY_BEGIN();
  RUN_TEST(parses_valid_mouse_frame);
  RUN_TEST(rejects_bad_checksum_and_resynchronizes);
  RUN_TEST(ignores_noise_before_magic);
  return UNITY_END();
}

#ifdef ARDUINO
void setup() {
  delay(2000);
  (void)runTests();
}

void loop() {}
#else
int main() { return runTests(); }
#endif
