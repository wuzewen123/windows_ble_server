// BlePacketManager.h
#pragma once
#include <vector>
#include <string>
#include <map>
#include <mutex>

class BlePacketManager {
public:
    BlePacketManager();
    std::vector<std::vector<uint8_t>> buildPackets(const std::string& json);
    void reset(const std::string& uuid);
    void savePacket(const std::string& uuid, uint8_t total, uint8_t index, uint16_t dataTotalLen, const std::vector<uint8_t>& dataPart);
    bool isComplete(const std::string& uuid, uint8_t total);
    std::string assemble(const std::string& uuid, uint8_t total, uint16_t dataTotalLen);
private:
    std::mutex mtx;
    std::map<std::string, std::map<uint8_t, std::vector<uint8_t>>> blePacketCache;
    std::map<std::string, uint8_t> blePacketTotal;
    std::map<std::string, uint16_t> blePacketDataLen;
};