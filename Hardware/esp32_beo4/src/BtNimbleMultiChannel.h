#pragma once

#include <Arduino.h>
#include <NimBLEDevice.h>
#include <array>
#include "Channel.h"

#ifndef NUS_SERVICE_UUID
#define NUS_SERVICE_UUID  "6E400001-B5A3-F393-E0A9-E50E24DCCA9E"
#endif
#ifndef NUS_RX_UUID
#define NUS_RX_UUID       "6E400002-B5A3-F393-E0A9-E50E24DCCA9E"
#endif
#ifndef NUS_TX_UUID
#define NUS_TX_UUID       "6E400003-B5A3-F393-E0A9-E50E24DCCA9E"
#endif

static constexpr size_t BT_MULTI_RX_BUF = 256;
static constexpr size_t BT_MULTI_LINE_BUF = 64;

class BtNimbleMultiChannel : public Channel, NimBLEServerCallbacks, NimBLECharacteristicCallbacks {
public:
    void begin(const char* deviceName) {
        NimBLEDevice::init(deviceName);
        _server = NimBLEDevice::createServer();
        _server->setCallbacks(this);
        _server->advertiseOnDisconnect(true);

        NimBLEService* svc = _server->createService(NUS_SERVICE_UUID);

        _txChar = svc->createCharacteristic(NUS_TX_UUID, NIMBLE_PROPERTY::NOTIFY);

        NimBLECharacteristic* rxChar = svc->createCharacteristic(
            NUS_RX_UUID, NIMBLE_PROPERTY::WRITE | NIMBLE_PROPERTY::WRITE_NR);
        rxChar->setCallbacks(this);

        svc->start();

        NimBLEAdvertising* adv = NimBLEDevice::getAdvertising();
        adv->addServiceUUID(NUS_SERVICE_UUID);
        adv->start();
    }

    bool available() override { return _rxHead != _rxTail; }

    char read() override {
        char c = _rxBuf[_rxTail];
        _rxTail = (_rxTail + 1) % BT_MULTI_RX_BUF;
        return c;
    }

    bool connected() override { return _server && _server->getConnectedCount() > 0; }

    void print(const char* s) override { sendStr(s); }

    void println(const char* s) override {
        sendStr(s);
        sendStr("\r\n");
    }

    void printf(const char* fmt, ...) override {
        va_list args;
        va_start(args, fmt);
        char buf[128];
        vsnprintf(buf, sizeof(buf), fmt, args);
        va_end(args);
        sendStr(buf);
    }

private:
    struct PeerState {
        uint16_t connHandle = BLE_HS_CONN_HANDLE_NONE;
        char lineBuf[BT_MULTI_LINE_BUF] = {};
        size_t lineLen = 0;

        bool inUse() const { return connHandle != BLE_HS_CONN_HANDLE_NONE; }

        void reset() {
            connHandle = BLE_HS_CONN_HANDLE_NONE;
            lineLen = 0;
            lineBuf[0] = '\0';
        }
    };

    NimBLEServer* _server = nullptr;
    NimBLECharacteristic* _txChar = nullptr;
    volatile char _rxBuf[BT_MULTI_RX_BUF];
    volatile size_t _rxHead = 0;
    volatile size_t _rxTail = 0;
    std::array<PeerState, CONFIG_BT_NIMBLE_MAX_CONNECTIONS> _peers{};

    void sendStr(const char* s) {
        if (!_txChar || !connected()) return;
        _txChar->setValue((const uint8_t*)s, strlen(s));
        _txChar->notify();
    }

    void enqueueChar(char c) {
        size_t next = (_rxHead + 1) % BT_MULTI_RX_BUF;
        if (next != _rxTail) {
            _rxBuf[_rxHead] = c;
            _rxHead = next;
        }
    }

    void flushPeerLine(PeerState& peer) {
        for (size_t i = 0; i < peer.lineLen; ++i)
            enqueueChar(peer.lineBuf[i]);
        enqueueChar('\n');
        peer.lineLen = 0;
        peer.lineBuf[0] = '\0';
    }

    PeerState* findPeer(uint16_t connHandle) {
        for (auto& peer : _peers) {
            if (peer.connHandle == connHandle)
                return &peer;
        }
        return nullptr;
    }

    PeerState* ensurePeer(uint16_t connHandle) {
        if (auto* peer = findPeer(connHandle))
            return peer;

        for (auto& peer : _peers) {
            if (!peer.inUse()) {
                peer.connHandle = connHandle;
                peer.lineLen = 0;
                peer.lineBuf[0] = '\0';
                return &peer;
            }
        }

        return nullptr;
    }

    void releasePeer(uint16_t connHandle) {
        if (auto* peer = findPeer(connHandle))
            peer->reset();
    }

    void onConnect(NimBLEServer*) override {}

    void onConnect(NimBLEServer* server, ble_gap_conn_desc* desc) override {
        if (desc)
            ensurePeer(desc->conn_handle);

        if (server && server->getConnectedCount() < CONFIG_BT_NIMBLE_MAX_CONNECTIONS)
            server->startAdvertising();
    }

    void onDisconnect(NimBLEServer*) override {}

    void onDisconnect(NimBLEServer* server, ble_gap_conn_desc* desc) override {
        if (desc)
            releasePeer(desc->conn_handle);

        if (server && server->getConnectedCount() < CONFIG_BT_NIMBLE_MAX_CONNECTIONS)
            server->startAdvertising();
    }

    void onWrite(NimBLECharacteristic*) override {}

    void onWrite(NimBLECharacteristic* ch, ble_gap_conn_desc* desc) override {
        if (!desc) return;

        auto* peer = ensurePeer(desc->conn_handle);
        if (!peer) return;

        std::string val = ch->getValue();
        for (char c : val) {
            if (c == '\r' || c == '\n') {
                if (peer->lineLen > 0)
                    flushPeerLine(*peer);
                continue;
            }

            if (peer->lineLen < BT_MULTI_LINE_BUF - 1) {
                peer->lineBuf[peer->lineLen++] = c;
                peer->lineBuf[peer->lineLen] = '\0';
            }
        }
    }
};
