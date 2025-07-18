#include "BlePacketManager.h"
#include <iostream>
#include <cassert>

void test_build_and_assemble() {
    BlePacketManager mgr;
    std::string json = "{\"key\":\"value\"}";
    auto packets = mgr.buildPackets(json);
    assert(!packets.empty());
    // 模拟分包写入
    for (size_t i = 0; i < packets.size(); ++i) {
        uint8_t total = packets[i][1];
        uint8_t index = packets[i][2];
        uint16_t dataTotalLen = packets[i][3] | (packets[i][4] << 8);
        std::vector<uint8_t> dataPart(packets[i].begin() + 5, packets[i].end());
        mgr.savePacket("test-uuid", total, index, dataTotalLen, dataPart);
    }
    assert(mgr.isComplete("test-uuid", packets.size()));
    std::string result = mgr.assemble("test-uuid", packets.size(), json.size());
    assert(result == json);
    std::cout << "test_build_and_assemble passed!\n";
}

int main() {
    test_build_and_assemble();
    return 0;
}