#pragma once

#include "BridgeProtocol.h"

namespace SteamInputBridge {

class MouseEmulator {
 public:
  void begin();
  void apply(const MouseReport& report);
};

}  // namespace SteamInputBridge
