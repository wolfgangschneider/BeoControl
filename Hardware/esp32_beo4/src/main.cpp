#include <Arduino.h>
#include <esp_ota_ops.h>
#include <Preferences.h>
#include <optional>
#include "IrBeo4.h"
#include "SerialChannel.h"

// IR TX pin + soft-GND pin per board (defaults; "txpin" stores overrides in NVS)
#ifdef BOARD_M5ATOMS3
    #include "BtNimbleMultiChannel.h"
    using ActiveBtChannel = BtNimbleMultiChannel;
    constexpr uint8_t IR_TX_PIN = 1;   // M5 Atom S3
    constexpr int8_t  IR_GND_PIN = 2;  // Grove G2 next to G1
    #undef DEVICE_NAME
    #define DEVICE_NAME "Beo4Remote_AtomS3"
#elif defined(BOARD_M5STAMPS3)
    #include "BtNimbleMultiChannel.h"
    using ActiveBtChannel = BtNimbleMultiChannel;
    constexpr uint8_t IR_TX_PIN = 9;    // M5 Stamp S3 — G0
    constexpr int8_t  IR_GND_PIN = -1;
    #undef DEVICE_NAME
    #define DEVICE_NAME "Beo4Remote_StampS3"
#elif defined(BOARD_M5STAMPC3)
    #include "BtNimbleSingleChannel.h"
    using ActiveBtChannel = BtNimbleSingleChannel;
    constexpr uint8_t IR_TX_PIN = 4;    
    constexpr int8_t  IR_GND_PIN = -1;
    #undef DEVICE_NAME
    #define DEVICE_NAME "Beo4Remote_StampC3"

#elif defined(BOARD_M5STACK_ATOM)
    #include "BtNimbleSingleChannel.h"
    using ActiveBtChannel = BtNimbleSingleChannel;
    constexpr uint8_t IR_TX_PIN = 25;    // M5 Stack Atom — BCB board
   // constexpr uint8_t IR_TX_PIN = 32;    // M5 Stack Atom Grove
    constexpr int8_t  IR_GND_PIN = 26;
    #undef DEVICE_NAME
    #define DEVICE_NAME "Beo4Remote_Atom"

#elif defined(BOARD_S3_MINI)
    #include "BtNimbleSingleChannel.h"
    using ActiveBtChannel = BtNimbleSingleChannel;
    constexpr uint8_t IR_TX_PIN = 13;    // ESP32-S3-mini-1-N4R2 — G0
    constexpr int8_t  IR_GND_PIN = -1;
    #undef DEVICE_NAME
    #define DEVICE_NAME "Beo4Remote_S3Mini"

#else

    //#include "BtClassicChannel.h"
    //using ActiveBtChannel = BtClassicChannel;
    #include "BtNimbleSingleChannel.h"
    using ActiveBtChannel = BtNimbleSingleChannel;
    constexpr uint8_t IR_TX_PIN = 32;   // esp32dev
    constexpr int8_t  IR_GND_PIN = -1;
    #undef DEVICE_NAME
    #define DEVICE_NAME "Beo4Remote_ESP32"
#endif

#include "CommandProcessor.h"

// ── Beo4 IR ─────────────────────────────────────────────────────────
uint8_t irTxPin  = IR_TX_PIN;          // referenced by CommandProcessor.h
int8_t  irGndPin = IR_GND_PIN;         // soft GND return for the IR LED, -1 = none
char    deviceName[33] = DEVICE_NAME; // NVS "dev"/"name" overrides the board name (BLE, mDNS, "name"); referenced by CommandProcessor.h
static std::optional<IrBeo4> beo4;     // static storage is zeroed: IrBeo4 relies on that for fields its ctor leaves unset
QueueHandle_t beo4_tx_queue;           // referenced by CommandProcessor.h

// ── Channels ─────────────────────────────────────────────────────────
static SerialChannel serialCh;
static ActiveBtChannel btCh;
WifiChannel wifiCh;                    // referenced by CommandProcessor.h

// ── Input buffers ─────────────────────────────────────────────────────
static constexpr size_t CMD_BUF_SIZE = 128;   // "wifi <ssid 32> <pass 63>" must fit
static char    serialCmdBuf[CMD_BUF_SIZE];
static uint8_t serialCmdLen = 0;
static char    btCmdBuf[CMD_BUF_SIZE];
static uint8_t btCmdLen = 0;
static char    wifiCmdBuf[CMD_BUF_SIZE];
static uint8_t wifiCmdLen = 0;
//-- BL E heartbeat timestamp --
static unsigned long lastHeartbeat = 0;

// ── OTA rollback ──────────────────────────────────────────────────────
// A freshly OTA-flashed firmware stays "pending" until loop() marks it valid.
// If it crashes/reboots before that, the bootloader falls back to the previous one.
extern "C" bool verifyRollbackLater() { return true; }   // overrides Arduino's auto-confirm
static constexpr unsigned long APP_VALID_AFTER_MS = 30000;
static bool appValid = false;

static void printStartup(Channel& channel, bool irReady) {
    channel.print("===> start beo4... ");
    channel.print(deviceName);
    channel.print("  IR TX pin ");
   channel.printf("%d GND pin %d ", irTxPin, irGndPin);
    channel.println(irReady ? "OK" : "failed");
    channel.println("\n\n=== ESP32 Beo4 Emulator ===");
    printHelp(channel);
}

// ── poll a channel with its own command buffer ───────────────────────
static void pollChannel(Channel& channel, char* cmdBuf, uint8_t& cmdLen) {
    while (channel.available()) {
        char c = channel.read();
        if (c == '\n' || c == '\r') {
            if (cmdLen > 0) {
                cmdBuf[cmdLen] = '\0';
                processCommand(cmdBuf, channel);
                cmdLen = 0;
            }
        } else if (cmdLen < CMD_BUF_SIZE - 1) {
            cmdBuf[cmdLen++] = c;
        }
    }
}

// ════════════════════════════════════════════════════════════════════
void setup() {
    Serial.begin(115200);

    // On ESP32-S3 (USB-CDC), wait up to 3 s for a host to open the port.
    // On esp32dev (hardware UART), Serial is always ready immediately.
#if defined(BOARD_M5ATOMS3) || defined(BOARD_M5STAMPS3) || defined(BOARD_S3_MINI)
    uint32_t t0 = millis();
    while (!Serial && millis() - t0 < 3000) delay(10);
    delay(300);   // let the host's monitor finish reconnecting to the freshly re-enumerated USB-CDC port
#endif

    Preferences prefs;
    prefs.begin("ir", true);
    irTxPin  = prefs.getUChar("txpin", IR_TX_PIN);
    irGndPin = prefs.getChar("gndpin", IR_GND_PIN);
    prefs.end();
    prefs.begin("dev", true);
    prefs.getString("name", DEVICE_NAME).toCharArray(deviceName, sizeof(deviceName));
    prefs.end();
    if (irGndPin >= 0) {
        pinMode(irGndPin, OUTPUT);
        digitalWrite(irGndPin, LOW);  // soft GND return for the IR LED on irTxPin
    }

// not working esp32 & Atom
/*
if (Serial) {
  
} 
 else {
     // No serial host — fall back to BLE
     btCh.begin(DEVICE_NAME);
    
 }
*/
    btCh.begin(deviceName);
    wifiCh.begin(deviceName, processCommand);

    // Beo4 IR TX
    beo4.emplace(-1, irTxPin);
    pinMode(irTxPin, OUTPUT);
    beo4_tx_queue = xQueueCreate(50, sizeof(uint32_t));
    bool irReady = beo4->Begin(NULL, beo4_tx_queue) == 0;
    printStartup(serialCh, irReady);
    printStartup(btCh, irReady);
}

void loop() {
    pollChannel(serialCh, serialCmdBuf, serialCmdLen);
    pollChannel(btCh, btCmdBuf, btCmdLen);
    wifiCh.handle();
    pollChannel(wifiCh, wifiCmdBuf, wifiCmdLen);

    // self-test passed: 30 s up, and WiFi online if configured (OTA needs it)
    if (!appValid && millis() > APP_VALID_AFTER_MS && (wifiCh.online() || !wifiCh.configured())) {
        esp_ota_mark_app_valid_cancel_rollback();
        appValid = true;
    }

     if (btCh.connected() && millis() - lastHeartbeat >= 5000) {
         lastHeartbeat = millis();
         btCh.println("HB");
     }

}
