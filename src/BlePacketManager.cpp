#include "BlePacketManager.h"
#include <cstring>

BlePacketManager::BlePacketManager() {}

std::vector<std::vector<uint8_t>> BlePacketManager::buildPackets(const std::string& json) {
    std::lock_guard<std::mutex> lock(mtx);
    std::vector<std::vector<uint8_t>> packets;
    size_t totalLen = json.size();
    uint8_t maxDataLen = 16; // 20 - 4 bytes header
    uint8_t total = (totalLen + maxDataLen - 1) / maxDataLen;
    for (uint8_t i = 0; i < total; ++i) {
        std::vector<uint8_t> packet(20, 0);
        packet[0] = 0xAA;
        packet[1] = total;
        packet[2] = i + 1;
        packet[3] = (uint8_t)totalLen;
        packet[4] = (uint8_t)(totalLen >> 8);
        size_t start = i * maxDataLen;
        size_t len = std::min(maxDataLen, totalLen - start);
        memcpy(&packet[5], json.data() + start, len);
        packets.push_back(packet);
    }
    return packets;
}

void BlePacketManager::reset(const std::string& uuid) {
    std::lock_guard<std::mutex> lock(mtx);
    blePacketCache.erase(uuid);
    blePacketTotal.erase(uuid);
    blePacketDataLen.erase(uuid);
}

void BlePacketManager::savePacket(const std::string& uuid, uint8_t total, uint8_t index, uint16_t dataTotalLen, const std::vector<uint8_t>& dataPart) {
    std::lock_guard<std::mutex> lock(mtx);
    blePacketCache[uuid][index] = dataPart;
    blePacketTotal[uuid] = total;
    blePacketDataLen[uuid] = dataTotalLen;
}

bool BlePacketManager::isComplete(const std::string& uuid, uint8_t total) {
    std::lock_guard<std::mutex> lock(mtx);
    if (blePacketCache.count(uuid) == 0) return false;
    return blePacketCache[uuid].size() == total;
}

std::string BlePacketManager::assemble(const std::string& uuid, uint8_t total, uint16_t dataTotalLen) {
    std::lock_guard<std::mutex> lock(mtx);
    std::string result;
    if (blePacketCache.count(uuid) == 0) return result;
    for (uint8_t i = 1; i <= total; ++i) {
        if (blePacketCache[uuid].count(i) == 0) return "";
        result.append((char*)blePacketCache[uuid][i].data(), blePacketCache[uuid][i].size());
    }
    if (result.size() > dataTotalLen) result.resize(dataTotalLen);
    return result;
}