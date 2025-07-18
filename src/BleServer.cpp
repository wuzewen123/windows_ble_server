// BleServer.cpp
// 基于BlueZ的Linux C++ BLE服务端示例（核心逻辑，需结合DBus/BlueZ库完善）
// 仅实现多特征值注册、分包读写、无UI
#include <iostream>
#include <string>
#include <vector>
#include <map>
#include <cstring>
#include <thread>
#include <chrono>
#include "BlePacketManager.h"

// 伪代码：实际需用BlueZ的DBus接口实现GATT服务与特征值注册
// 这里只给出核心分包、数据管理与回调逻辑

struct PacketState {
    std::vector<std::vector<uint8_t>> packets;
    size_t index;
};

std::map<std::string, PacketState> packetStates;

std::vector<std::string> characteristicUuids = {
    "12345678-1234-5678-1234-56789abcdef1",
    "6f8d0b2c-4e1a-3c5d-7b9e-0f2a4c6e8b1d",
    "8b7f1a2c-3e4d-4b8a-9c1e-2f6d7a8b9c0d",
    "1c2d3e4f-5a6b-7c8d-9e0f-1a2b3c4d5e6f",
    "9a8b7c6d-5e4f-3a2b-1c0d-9e8f7a6b5c4d",
    "2f4e6d8c-1b3a-5c7e-9d0f-2a4c6e8b1d3f",
    "7e6d5c4b-3a2f-1e0d-9c8b-7a6f5e4d3c2b",
    "0a1b2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d",
    "3c5e7a9b-1d2f-4b6d-8e0f-3a5c7e9b1d2f"
};

std::string getJsonByUuid(const std::string& uuid) {
    if (uuid == "12345678-1234-5678-1234-56789abcdef1") {
        return "{\"errorcode\":0,\"msg\":\"success\",\"data\":{\"device_status\":\"wait\",\"server_status\":\"connect\",\"characteristic_id\":\"" + uuid + "\"}}";
    } else if (
        uuid == "6f8d0b2c-4e1a-3c5d-7b9e-0f2a4c6e8b1d" ||
        uuid == "8b7f1a2c-3e4d-4b8a-9c1e-2f6d7a8b9c0d" ||
        uuid == "1c2d3e4f-5a6b-7c8d-9e0f-1a2b3c4d5e6f" ||
        uuid == "9a8b7c6d-5e4f-3a2b-1c0d-9e8f7a6b5c4d" ||
        uuid == "2f4e6d8c-1b3a-5c7e-9d0f-2a4c6e8b1d3f" ||
        uuid == "7e6d5c4b-3a2f-1e0d-9c8b-7a6f5e4d3c2b" ||
        uuid == "3c5e7a9b-1d2f-4b6d-8e0f-3a5c7e9b1d2f") {
        return "{\"errorcode\":0,\"msg\":\"success\",\"data\":{\"characteristic_id\":\"" + uuid + "\"}}";
    } else if (uuid == "0a1b2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d") {
        return "{\"errorcode\":0,\"msg\":\"success\",\"data\":{\"uploadfailed_videoes\":[{\"id\":\"1\",\"push_auth_uri\":\"xxxx\",\"title\":\"录制标题\",\"myteam\":\"我的队伍名称\",\"opponentteam\":\"队伍名称\",\"address\":\"比赛场所\",\"type\":\"11V11\",\"created_at\":\"2025-05-26 13:56:06\",\"thumb_image\":\"xxx\",\"second\":\"120\",\"finished_time\":\"2025-05-26 13:56:06\"}],\"characteristic_id\":\"" + uuid + "\"}}";
    }
    return "{\"errorcode\":1,\"msg\":\"uuid not supported\"}";
}

std::vector<std::vector<uint8_t>> buildPackets(const std::string& json) {
    std::vector<std::vector<uint8_t>> packets;
    std::vector<uint8_t> bytes(json.begin(), json.end());
    size_t totalLen = bytes.size();
    size_t payloadLen = 15;
    size_t totalPackets = (totalLen + payloadLen - 1) / payloadLen;
    for (size_t i = 0; i < totalPackets; ++i) {
        std::vector<uint8_t> packet(20, 0);
        packet[0] = 0xAA;
        packet[1] = static_cast<uint8_t>(totalPackets);
        packet[2] = static_cast<uint8_t>(i + 1);
        packet[3] = (totalLen >> 8) & 0xFF;
        packet[4] = totalLen & 0xFF;
        size_t chunkSize = std::min(payloadLen, totalLen - i * payloadLen);
        memcpy(&packet[5], &bytes[i * payloadLen], chunkSize);
        packets.push_back(packet);
    }
    return packets;
}

// 线程安全：为分包状态和缓存加锁
// 替换为 BlePacketManager 实例
static BlePacketManager packetManager;
#include <mutex>
std::mutex packetStatesMutex;
std::mutex blePacketCacheMutex;

// 读请求回调（伪代码，实际需注册到BlueZ的GATT characteristic read handler）
void onReadRequest(const std::string& uuid) {
    std::lock_guard<std::mutex> lock(packetStatesMutex);
    std::string json = getJsonByUuid(uuid);
    if (packetStates.find(uuid) == packetStates.end() ||
        packetStates[uuid].packets.empty() ||
        packetStates[uuid].index >= packetStates[uuid].packets.size()) {
        packetStates[uuid] = {buildPackets(json), 0};
    }
    auto& state = packetStates[uuid];
    const auto& packet = state.packets[state.index];
    // TODO: 通过BlueZ发送packet给客户端，错误处理
    printf("[READ] uuid=%s, send packet %zu/%zu: ", uuid.c_str(), state.index+1, state.packets.size());
    for (auto b : packet) printf("%02X-", b); printf("\n");
    state.index++;
    if (state.index >= state.packets.size()) {
        packetStates.erase(uuid);
    }
}

// 写请求回调（伪代码，实际需注册到BlueZ的GATT characteristic write handler）
std::map<std::string, std::map<uint8_t, std::vector<uint8_t>>> blePacketCache;
std::map<std::string, uint8_t> blePacketTotal;
std::map<std::string, uint16_t> blePacketDataLen;

void onWriteRequest(const std::string& uuid, const std::vector<uint8_t>& packet) {
    std::lock_guard<std::mutex> lock(blePacketCacheMutex);
    if (packet.size() == 20 && packet[0] == 0xAA) {
        uint8_t total = packet[1];
        uint8_t index = packet[2];
        uint16_t dataTotalLen = (packet[3] << 8) | packet[4];
        std::vector<uint8_t> dataPart(packet.begin() + 5, packet.begin() + 20);
        std::string msgKey = uuid + std::to_string(total) + std::to_string(dataTotalLen);
        blePacketCache[msgKey][index] = dataPart;
        blePacketTotal[msgKey] = total;
        blePacketDataLen[msgKey] = dataTotalLen;
        printf("[WRITE] uuid=%s, recv packet idx=%d/%d, HEX:", uuid.c_str(), index, total);
        for (auto b : dataPart) printf("%02X-", b); printf("\n");
        if (blePacketCache[msgKey].size() == total) {
            std::vector<uint8_t> allData;
            for (uint8_t i = 1; i <= total; ++i) {
                auto& part = blePacketCache[msgKey][i];
                allData.insert(allData.end(), part.begin(), part.end());
            }
            allData.resize(dataTotalLen);
            std::string dataStr(allData.begin(), allData.end());
            printf("[WRITE] uuid=%s, 组包完成: %s\n", uuid.c_str(), dataStr.c_str());
            // 清理缓存
            blePacketCache.erase(msgKey);
            blePacketTotal.erase(msgKey);
            blePacketDataLen.erase(msgKey);
        }
    } else {
        printf("[WARN] 非协议包或长度错误，已忽略\n");
    }
}

// main函数仅作结构展示，实际需结合BlueZ注册GATT服务、特征值及回调
int main() {
    printf("BLE服务端启动（伪代码，需结合BlueZ实现GATT注册与事件回调）\n");
    // 伪循环，模拟客户端读写
    std::string testUuid = "12345678-1234-5678-1234-56789abcdef1";
    for (int i = 0; i < 3; ++i) onReadRequest(testUuid);
    // 模拟写入
    auto packets = buildPackets("hello from client");
    for (auto& pkt : packets) onWriteRequest(testUuid, pkt);
    return 0;
}

// TODO: 集成BlueZ的GATT服务注册、特征值注册、回调绑定
// TODO: 增加异常处理和内存释放，避免内存泄漏
// TODO: 可将分包、组包逻辑封装为类，提升可维护性
// TODO: 在此处对接 BlueZ 的 GATT 服务注册、特征值读写回调等接口