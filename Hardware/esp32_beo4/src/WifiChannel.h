#pragma once
#include <Arduino.h>
#include <WiFi.h>
#include <ESPmDNS.h>
#include <Preferences.h>
#include "Channel.h"

// WiFi STA + single-client TCP server (raw line protocol, port 23).
// Credentials live in NVS (namespace "wifi"); set via the "wifi <ssid> <pass>" command.
// Without stored credentials the radio stays off.
class WifiChannel : public Channel {
public:
    static constexpr uint16_t PORT = 23;

    void begin(const char* deviceName) {
        _name = deviceName;
        // mDNS/DHCP hostname: lowercase, '_' -> '-'  (Beo4Remote_Atom -> beo4remote-atom)
        size_t i = 0;
        for (; deviceName[i] && i < sizeof(_host) - 1; i++)
            _host[i] = deviceName[i] == '_' ? '-' : (char)tolower((unsigned char)deviceName[i]);
        _host[i] = '\0';

        Preferences prefs;
        prefs.begin("wifi", true);
        String ssid = prefs.getString("ssid", "");
        String pass = prefs.getString("pass", "");
        prefs.end();
        if (ssid.length() > 0) start(ssid.c_str(), pass.c_str());
    }

    void handle() {
        if (!_started) return;

        bool up = WiFi.status() == WL_CONNECTED;
        if (up && !_wasUp) {
            Serial.printf("WiFi: connected, IP %s, host %s.local\n",
                          WiFi.localIP().toString().c_str(), _host);
            if (!_mdns && MDNS.begin(_host)) {
                MDNS.setInstanceName(_name);              // app lists this name
                MDNS.addService("beo4", "tcp", PORT);     // app discovers _beo4._tcp.local
                _mdns = true;
            }
        } else if (!up && _wasUp) {
            Serial.println("WiFi: disconnected");
        }
        _wasUp = up;

        // newest client wins
        if (_server.hasClient()) {
            if (_client) _client.stop();
            _client = _server.accept();
            Serial.printf("WiFi: client %s\n", _client.remoteIP().toString().c_str());
        }
    }

    void setCredentials(const char* ssid, const char* pass) {
        Preferences prefs;
        prefs.begin("wifi", false);
        prefs.putString("ssid", ssid);
        prefs.putString("pass", pass);
        prefs.end();
        if (_started) {
            // begin() fails with ESP_ERR_WIFI_CONN while a previous connect is still
            // retrying -> restart the STA from scratch
            if (_client) _client.stop();
            if (_mdns) MDNS.end();
            WiFi.disconnect(true);
            WiFi.mode(WIFI_OFF);
            _mdns = _wasUp = false;
        }
        start(ssid, pass);
    }

    void clearCredentials() {
        Preferences prefs;
        prefs.begin("wifi", false);
        prefs.clear();
        prefs.end();
        if (!_started) return;
        if (_client) _client.stop();
        _server.end();
        if (_mdns) MDNS.end();
        WiFi.disconnect(true);
        WiFi.mode(WIFI_OFF);
        _started = _mdns = _wasUp = false;
    }

    void printStatus(Channel& reply) {
        if (!_started) { reply.println("WiFi: off (no credentials)"); return; }
        if (WiFi.status() != WL_CONNECTED) {
            reply.printf("WiFi: connecting to %s\n", WiFi.SSID().c_str());
            return;
        }
        reply.printf("WiFi: %s  IP %s  RSSI %d  host %s.local:%u\n",
                     WiFi.SSID().c_str(), WiFi.localIP().toString().c_str(),
                     WiFi.RSSI(), _host, PORT);
    }

    // ── Channel ──────────────────────────────────────────────────────
    bool available()            override { return _client && _client.available(); }
    char read()                 override { return (char)_client.read(); }
    void print(const char* s)   override { if (_client) _client.print(s); }
    void println(const char* s) override { if (_client) _client.println(s); }
    void printf(const char* fmt, ...) override {
        va_list args;
        va_start(args, fmt);
        char buf[128];
        vsnprintf(buf, sizeof(buf), fmt, args);
        va_end(args);
        print(buf);
    }
    bool connected() override { return _client && _client.connected(); }

private:
    void start(const char* ssid, const char* pass) {
        WiFi.setHostname(_host);   // must precede mode() to reach DHCP
        WiFi.mode(WIFI_STA);
        WiFi.begin(ssid, pass);
        _server.begin();
        _server.setNoDelay(true);
        _started = true;
        Serial.printf("WiFi: connecting to %s\n", ssid);
    }

    const char* _name   = "";
    char       _host[32] = "";
    WiFiServer _server{PORT};
    WiFiClient _client;
    bool       _started = false;
    bool       _mdns    = false;
    bool       _wasUp   = false;
};
