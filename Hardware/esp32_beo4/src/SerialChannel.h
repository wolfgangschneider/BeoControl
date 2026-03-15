#pragma once
#include <Arduino.h>
#include "Channel.h"

class SerialChannel : public Channel {
public:
    bool available()          override { return Serial.available(); }
    char read()               override { return (char)Serial.read(); }
    void print(const char* s) override { Serial.print(s); }
    void println(const char* s) override { Serial.println(s); }
    void printf(const char* fmt, ...) override {
        va_list args;
        va_start(args, fmt);
        char buf[128];
        vsnprintf(buf, sizeof(buf), fmt, args);
        va_end(args);
        Serial.print(buf);
    }
    bool connected() override { return true; }
};
