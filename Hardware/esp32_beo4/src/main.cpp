#include <Arduino.h>
#include "IrBeo4.h"
#include "SerialChannel.h"
#include "BtChannel.h"
#include "CommandProcessor.h"

// IR TX pin per board
#ifdef BOARD_M5ATOMS3
  constexpr uint8_t IR_TX_PIN = 38;   // M5 Atom S3
#elif defined(BOARD_M5STAMPS3)
  constexpr uint8_t IR_TX_PIN = 0;    // M5 Stamp S3 — G0
#else
  constexpr uint8_t IR_TX_PIN = 32;   // esp32dev
#endif

// ── Beo4 IR ─────────────────────────────────────────────────────────
static IrBeo4 beo4(-1, IR_TX_PIN);
QueueHandle_t beo4_tx_queue;           // referenced by CommandProcessor.h

// ── Channels ─────────────────────────────────────────────────────────
static SerialChannel serialCh;
static BtChannel     btCh;
static Channel*      active = nullptr;

// ── Input buffer (single, for the active channel) ───────────────────
static char    cmdBuf[64];
static uint8_t cmdLen = 0;

// ── poll active channel ──────────────────────────────────────────────
static void pollChannel() {
    while (active->available()) {
        char c = active->read();
        if (c == '\n' || c == '\r') {
            if (cmdLen > 0) {
                cmdBuf[cmdLen] = '\0';
                processCommand(cmdBuf, *active);
                cmdLen = 0;
                active->print("beo4> ");
            }
        } else if (cmdLen < sizeof(cmdBuf) - 1) {
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
        active = &serialCh;
    } else {
        // No serial host — fall back to BLE
        btCh.begin(DEVICE_NAME);
        active = &btCh;
    }

    // Beo4 IR TX
    pinMode(IR_TX_PIN, OUTPUT);
    beo4_tx_queue = xQueueCreate(50, sizeof(uint32_t));
    active->print("===> start beo4... ");
    int ok = beo4.Begin(NULL, beo4_tx_queue);
    active->println(ok == 0 ? "OK" : "failed");

    active->println("\n\n=== ESP32 Beo4 Emulator ===");
    printHelp(*active);
    active->print("beo4> ");
}

void loop() {
    pollChannel();
}
