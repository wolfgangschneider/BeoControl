#include <Arduino.h>
#include "IrBeo4.h"
#include "SerialChannel.h"
#include "CommandProcessor.h"

// IR TX pin per board
#ifdef BOARD_M5ATOMS3
  #include "BtNimbleMultiChannel.h"
  using ActiveBtChannel = BtNimbleMultiChannel;
  constexpr uint8_t IR_TX_PIN = 38;   // M5 Atom S3
#elif defined(BOARD_M5STAMPS3)
  #include "BtNimbleMultiChannel.h"
  using ActiveBtChannel = BtNimbleMultiChannel;
  constexpr uint8_t IR_TX_PIN = 9;    // M5 Stamp S3 — G0
#else
  //#include "BtClassicChannel.h"
  //using ActiveBtChannel = BtClassicChannel;
  #include "BtNimbleSingleChannel.h"
  using ActiveBtChannel = BtNimbleSingleChannel;
  constexpr uint8_t IR_TX_PIN = 32;   // esp32dev
#endif

// ── Beo4 IR ─────────────────────────────────────────────────────────
static IrBeo4 beo4(-1, IR_TX_PIN);
QueueHandle_t beo4_tx_queue;           // referenced by CommandProcessor.h

// ── Channels ─────────────────────────────────────────────────────────
static SerialChannel serialCh;
static ActiveBtChannel btCh;

// ── Input buffers ─────────────────────────────────────────────────────
static constexpr size_t CMD_BUF_SIZE = 64;
static char    serialCmdBuf[CMD_BUF_SIZE];
static uint8_t serialCmdLen = 0;
static char    btCmdBuf[CMD_BUF_SIZE];
static uint8_t btCmdLen = 0;

static void printStartup(Channel& channel, bool irReady) {
    channel.print("===> start beo4... ");
    channel.println(irReady ? "OK" : "failed");
    channel.println("\n\n=== ESP32 Beo4 Emulator ===");
    printHelp(channel);
    channel.print("beo4> ");
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
                channel.print("beo4> ");
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
#if defined(BOARD_M5ATOMS3) || defined(BOARD_M5STAMPS3)
    uint32_t t0 = millis();
    while (!Serial && millis() - t0 < 3000) delay(10);
#endif

 if (Serial) {
     // USB/UART connected — use serial channel
     
 } else {
     // No serial host — fall back to BLE
     btCh.begin(DEVICE_NAME);
    
 }

    //btCh.begin(DEVICE_NAME);

    // Beo4 IR TX
    pinMode(IR_TX_PIN, OUTPUT);
    beo4_tx_queue = xQueueCreate(50, sizeof(uint32_t));
    bool irReady = beo4.Begin(NULL, beo4_tx_queue) == 0;
    printStartup(serialCh, irReady);
    printStartup(btCh, irReady);
}

void loop() {
    pollChannel(serialCh, serialCmdBuf, serialCmdLen);
    pollChannel(btCh, btCmdBuf, btCmdLen);
}
