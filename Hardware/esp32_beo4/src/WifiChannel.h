#pragma once
#include <Arduino.h>
#include <WiFi.h>
#include <ESPmDNS.h>
#include <Preferences.h>
#include <ArduinoOTA.h>
#include <WebServer.h>
#include "Channel.h"

// Collects the reply of one command for the HTTP response.
struct ReplyBuffer : Channel {
    String text;
    bool available()            override { return false; }
    char read()                 override { return 0; }
    void print(const char* s)   override { text += s; }
    void println(const char* s) override { text += s; text += '\n'; }
    void printf(const char* fmt, ...) override {
        va_list args;
        va_start(args, fmt);
        char buf[128];
        vsnprintf(buf, sizeof(buf), fmt, args);
        va_end(args);
        text += buf;
    }
    bool connected() override { return true; }
};

// WiFi STA + HTTP command endpoint (GET /cmd?c=<command>, port 80, any number of clients)
// + single-client TCP server (raw line protocol, port 23, kept as fallback) + ArduinoOTA (espota, UDP 3232).
// Credentials live in NVS (namespace "wifi"); set via the "wifi <ssid> <pass>" command.
// Without stored credentials the radio stays off.
class WifiChannel : public Channel {
public:
    static constexpr uint16_t PORT = 23;
    static constexpr uint16_t HTTP_PORT = 80;
    using CommandHandler = void (*)(char* line, Channel& reply);

    void begin(const char* deviceName, CommandHandler onCommand) {
        _name = deviceName;
        _onCommand = onCommand;
        _http.on("/cmd", [this] { handleCmd(); });
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
            if (!_ota) {
                ArduinoOTA.setHostname(_host);
                ArduinoOTA.setMdnsEnabled(false);         // mDNS already runs (see above)
                ArduinoOTA.onStart([] { Serial.println("OTA: start"); });
                ArduinoOTA.onEnd([] { Serial.println("OTA: done, rebooting"); });
                ArduinoOTA.onError([](ota_error_t e) { Serial.printf("OTA: error %u\n", e); });
                ArduinoOTA.begin();
                _ota = true;
            }
        } else if (!up && _wasUp) {
            Serial.println("WiFi: disconnected");
        }
        _wasUp = up;
        if (_ota) ArduinoOTA.handle();
        _http.handleClient();

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
            _http.stop();
            if (_ota) ArduinoOTA.end();
            if (_mdns) MDNS.end();
            WiFi.disconnect(true);
            WiFi.mode(WIFI_OFF);
            _ota = _mdns = _wasUp = false;
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
        _http.stop();
        if (_ota) ArduinoOTA.end();
        if (_mdns) MDNS.end();
        WiFi.disconnect(true);
        WiFi.mode(WIFI_OFF);
        _started = _ota = _mdns = _wasUp = false;
    }

    bool configured() const { return _started; }                                   // credentials stored
    bool online()     const { return _started && WiFi.status() == WL_CONNECTED; }

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
        _http.begin();
        _started = true;
        Serial.printf("WiFi: connecting to %s\n", ssid);
    }

    // GET /cmd?c=<command>: same commands as Serial/BLE, the reply lines come back as text/plain
    void handleCmd() {
        String cmd = _http.arg("c");
        if (cmd.length() == 0 || cmd.length() >= 128) {
            _http.send(400, "text/plain", "usage: /cmd?c=<command>\n");
            return;
        }
        char line[128];
        cmd.toCharArray(line, sizeof(line));
        ReplyBuffer reply;
        _onCommand(line, reply);   // "name <new>" restarts inside: no response then
        _http.send(200, "text/plain", reply.text);
    }

    const char* _name   = "";
    char       _host[32] = "";
    WiFiServer _server{PORT};
    WiFiClient _client;
    WebServer  _http{HTTP_PORT};
    CommandHandler _onCommand = nullptr;
    bool       _started = false;
    bool       _mdns    = false;
    bool       _ota     = false;
    bool       _wasUp   = false;
};
