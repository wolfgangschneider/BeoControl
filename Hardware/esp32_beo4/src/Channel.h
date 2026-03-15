#pragma once
#include <stdarg.h>

// Abstract communication channel — implemented by SerialChannel and BtChannel.
struct Channel {
    virtual bool available()                  = 0;
    virtual char read()                       = 0;
    virtual void print(const char* s)         = 0;
    virtual void println(const char* s)       = 0;
    virtual void printf(const char* fmt, ...) = 0;
    virtual bool connected()                  = 0;
    virtual ~Channel() {}
};
