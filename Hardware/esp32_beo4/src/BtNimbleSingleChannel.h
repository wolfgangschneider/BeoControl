#pragma once

#include <Arduino.h>
#include <NimBLEDevice.h>
#include "Channel.h"

#define NUS_SERVICE_UUID  "6E400001-B5A3-F393-E0A9-E50E24DCCA9E"
#define NUS_RX_UUID       "6E400002-B5A3-F393-E0A9-E50E24DCCA9E"
#define NUS_TX_UUID       "6E400003-B5A3-F393-E0A9-E50E24DCCA9E"

static constexpr size_t BT_SINGLE_RX_BUF = 128;

class BtNimbleSingleChannel : public Channel, NimBLEServerCallbacks, NimBLECharacteristicCallbacks {
public:
    void begin(const char* deviceName) {
        NimBLEDevice::init(deviceName);
        _server = NimBLEDevice::createServer();
        _server->setCallbacks(this);

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
        _rxTail = (_rxTail + 1) % BT_SINGLE_RX_BUF;
        return c;
    }

    bool connected() override { return _connected; }

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
    NimBLEServer* _server = nullptr;
    NimBLECharacteristic* _txChar = nullptr;
    bool _connected = false;
    volatile char _rxBuf[BT_SINGLE_RX_BUF];
    volatile size_t _rxHead = 0;
    volatile size_t _rxTail = 0;

    void sendStr(const char* s) {
        if (!_connected || !_txChar) return;
        _txChar->setValue((const uint8_t*)s, strlen(s));
        _txChar->notify();
    }

    void onConnect(NimBLEServer*) override { _connected = true; }

    void onDisconnect(NimBLEServer*) override {
        _connected = false;
        NimBLEDevice::getAdvertising()->start();
    }

    void onWrite(NimBLECharacteristic* ch) override {
        std::string val = ch->getValue();
        for (char c : val) {
            size_t next = (_rxHead + 1) % BT_SINGLE_RX_BUF;
            if (next != _rxTail) {
                _rxBuf[_rxHead] = c;
                _rxHead = next;
            }
        }
    }
};
