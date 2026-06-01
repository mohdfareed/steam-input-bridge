#include <unity.h>

#include "BridgeProtocol.h"

#ifdef ARDUINO
#include <Arduino.h>
#endif

using SteamInputBridge::BridgeMessage;
using SteamInputBridge::BridgeMessageType;
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
  frame[6] = BridgeProtocolReader::MousePayloadSize;

  writeUInt16LittleEndian(frame + 7, report.buttons);
  writeInt16LittleEndian(frame + 9, report.deltaX);
  writeInt16LittleEndian(frame + 11, report.deltaY);
  writeInt16LittleEndian(frame + 13, report.wheel);

  const uint16_t checksum =
      crc16(frame, BridgeProtocolReader::HeaderSize +
                       BridgeProtocolReader::MousePayloadSize);
  writeUInt16LittleEndian(frame + 15, checksum);
}

void writeHandshakeProbe(uint8_t* frame, uint8_t sequence) {
  frame[0] = 'S';
  frame[1] = 'I';
  frame[2] = 'B';
  frame[3] = 1;
  frame[4] = 0;
  frame[5] = sequence;
  frame[6] = 0;

  const uint16_t checksum = crc16(frame, BridgeProtocolReader::HeaderSize);
  writeUInt16LittleEndian(frame + 7, checksum);
}

bool readFrame(BridgeProtocolReader& reader, const uint8_t* frame,
               uint8_t length, BridgeMessage& message) {
  bool parsed = false;
  for (uint8_t i = 0; i < length; i++) {
    parsed = reader.read(frame[i], message);
  }

  return parsed;
}

}  // namespace

void parses_valid_mouse_frame() {
  BridgeProtocolReader reader;
  MouseReport input{0x0005, -42, 256, 120};
  BridgeMessage output;
  uint8_t frame[BridgeProtocolReader::MouseFrameSize] = {};
  writeMouseFrame(frame, 7, input);

  TEST_ASSERT_TRUE(
      readFrame(reader, frame, BridgeProtocolReader::MouseFrameSize, output));
  TEST_ASSERT_EQUAL_UINT8(static_cast<uint8_t>(BridgeMessageType::MouseReport),
                          static_cast<uint8_t>(output.type));
  TEST_ASSERT_EQUAL_UINT8(7, output.sequence);
  TEST_ASSERT_EQUAL_UINT16(input.buttons, output.mouse.buttons);
  TEST_ASSERT_EQUAL_INT16(input.deltaX, output.mouse.deltaX);
  TEST_ASSERT_EQUAL_INT16(input.deltaY, output.mouse.deltaY);
  TEST_ASSERT_EQUAL_INT16(input.wheel, output.mouse.wheel);
  TEST_ASSERT_TRUE(output.mouse.hasInput());
}

void rejects_bad_checksum_and_resynchronizes() {
  BridgeProtocolReader reader;
  BridgeMessage message;
  uint8_t badFrame[BridgeProtocolReader::MouseFrameSize] = {};
  writeMouseFrame(badFrame, 1, MouseReport{0x0001, 2, 3, 4});
  badFrame[15] ^= 0x7F;

  TEST_ASSERT_FALSE(readFrame(reader, badFrame,
                              BridgeProtocolReader::MouseFrameSize, message));

  uint8_t goodFrame[BridgeProtocolReader::MouseFrameSize] = {};
  writeMouseFrame(goodFrame, 2, MouseReport{0, 0, 0, 0});
  TEST_ASSERT_TRUE(readFrame(reader, goodFrame,
                             BridgeProtocolReader::MouseFrameSize, message));
  TEST_ASSERT_FALSE(message.mouse.hasInput());
}

void ignores_noise_before_magic() {
  BridgeProtocolReader reader;
  BridgeMessage message;
  TEST_ASSERT_FALSE(reader.read(0x00, message));
  TEST_ASSERT_FALSE(reader.read('X', message));

  uint8_t frame[BridgeProtocolReader::MouseFrameSize] = {};
  writeMouseFrame(frame, 3, MouseReport{0x0002, 0, 0, 0});
  TEST_ASSERT_TRUE(
      readFrame(reader, frame, BridgeProtocolReader::MouseFrameSize, message));
  TEST_ASSERT_EQUAL_UINT16(0x0002, message.mouse.buttons);
}

void parses_handshake_probe_and_writes_response() {
  BridgeProtocolReader reader;
  BridgeMessage message;
  uint8_t probe[BridgeProtocolReader::HandshakeProbeFrameSize] = {};
  writeHandshakeProbe(probe, 9);

  TEST_ASSERT_TRUE(readFrame(
      reader, probe, BridgeProtocolReader::HandshakeProbeFrameSize, message));
  TEST_ASSERT_EQUAL_UINT8(
      static_cast<uint8_t>(BridgeMessageType::HandshakeProbe),
      static_cast<uint8_t>(message.type));
  TEST_ASSERT_EQUAL_UINT8(9, message.sequence);

  uint8_t response[BridgeProtocolReader::HandshakeResponseFrameSize] = {};
  const uint8_t bytes =
      SteamInputBridge::writeHandshakeResponse(9, response, sizeof(response));
  TEST_ASSERT_EQUAL_UINT8(BridgeProtocolReader::HandshakeResponseFrameSize,
                          bytes);
  TEST_ASSERT_EQUAL_UINT8('S', response[0]);
  TEST_ASSERT_EQUAL_UINT8('I', response[1]);
  TEST_ASSERT_EQUAL_UINT8('B', response[2]);
  TEST_ASSERT_EQUAL_UINT8(1, response[3]);
  TEST_ASSERT_EQUAL_UINT8(0x80, response[4]);
  TEST_ASSERT_EQUAL_UINT8(9, response[5]);
  TEST_ASSERT_EQUAL_UINT8(BridgeProtocolReader::HandshakeResponsePayloadSize,
                          response[6]);
  TEST_ASSERT_EQUAL_UINT8('T', response[7]);
  TEST_ASSERT_EQUAL_UINT8('N', response[8]);
  TEST_ASSERT_EQUAL_UINT8('S', response[9]);
  TEST_ASSERT_EQUAL_UINT8('Y', response[10]);
}

int runTests() {
  UNITY_BEGIN();
  RUN_TEST(parses_valid_mouse_frame);
  RUN_TEST(rejects_bad_checksum_and_resynchronizes);
  RUN_TEST(ignores_noise_before_magic);
  RUN_TEST(parses_handshake_probe_and_writes_response);
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
