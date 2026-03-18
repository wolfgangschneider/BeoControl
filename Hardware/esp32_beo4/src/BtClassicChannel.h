#pragma once

#include <Arduino.h>
#include <BluetoothSerial.h>
#include "Channel.h"

class BtClassicChannel : public Channel {
public:
    void begin(const char* deviceName) { _bt.begin(deviceName); }

    bool available() override { return _bt.available(); }
    char read() override { return (char)_bt.read(); }
    bool connected() override { return _bt.connected(); }

    void print(const char* s) override { _bt.print(s); }
    void println(const char* s) override { _bt.println(s); }

    void printf(const char* fmt, ...) override {
        va_list args;
        va_start(args, fmt);
        char buf[128];
        vsnprintf(buf, sizeof(buf), fmt, args);
        va_end(args);
        _bt.print(buf);
    }

private:
    BluetoothSerial _bt;
};
