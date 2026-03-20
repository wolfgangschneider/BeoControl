#pragma once
#include <Arduino.h>
#include "IrBeo4.h"
#include "Channel.h"

//#define DEVICE_NAME "Beo4Remote"

// ── Device identity ──────────────────────────────────────────────────
/*
#ifdef BOARD_M5ATOMS3
  #define DEVICE_NAME "Beo4Remote_AtomS3"
#elif defined(BOARD_M5STAMPS3)
  #define DEVICE_NAME "Beo4Remote_StampS3"
#else
  #define DEVICE_NAME "Beo4Remote_ESP32"
#endif
*/
// ── Command table ────────────────────────────────────────────────────
struct CmdEntry {
    const char* name;
    uint8_t     src;
    uint8_t     cmd;
    bool        setsSource;
};

static const CmdEntry cmdTable[] = {
    // source-selecting commands
    { "tv",      BEO_SRC_VIDEO,  BEO_CMD_TV,        true  },
    { "radio",   BEO_SRC_AUDIO,  BEO_CMD_RADIO,     true  },
    { "cd",      BEO_SRC_AUDIO,  BEO_CMD_CD,        true  },
    { "phono",   BEO_SRC_AUDIO,  BEO_CMD_PHONO,     true  },
    { "dvd",     BEO_SRC_VIDEO,  BEO_CMD_DVD,       true  },
    { "sat",     BEO_SRC_VIDEO,  BEO_CMD_SAT,       true  },
    { "vtape",   BEO_SRC_VTAPE,  BEO_CMD_VTAPE,     true  },
    { "pc",      BEO_SRC_VIDEO,  BEO_CMD_PC,        true  },
    { "light",   BEO_SRC_LIGHT,  0xFF,              true  },
    { "a.aux",   BEO_SRC_AUDIO,  BEO_CMD_AUDIO_AUX, true  },
    { "v.aux",   BEO_SRC_VIDEO,  BEO_CMD_VIDEO_AUX, true  },
    { "atape",   BEO_SRC_AUDIO,  BEO_CMD_ATAPE,     true  },
    { "atape2",  BEO_SRC_AUDIO,  BEO_CMD_ATAPE2,    true  },
    { "doorcam", BEO_SRC_VIDEO,  BEO_CMD_DOOR_CAM,  true  },
    { "cd2",     BEO_SRC_AUDIO,  BEO_CMD_CD2,        true  },
    { "text",    BEO_SRC_VIDEO,  BEO_CMD_TEXT,      true  },
    { "spdemo",  BEO_SRC_SPDEMO, BEO_CMD_SP_DEMO,   true  },
    // control commands
    { "vol+",    0, BEO_CMD_VOL_UP,    false },
    { "vol-",    0, BEO_CMD_VOL_DOWN,  false },
    { "mute",    0, BEO_CMD_MUTE,      false },
    { "standby",    0,            BEO_CMD_STANDBY, false },
    { "off",        0,            BEO_CMD_STANDBY, false },
    { "allstandby", BEO_SRC_ALL, BEO_CMD_STANDBY, false },
    { "alloff",     BEO_SRC_ALL, BEO_CMD_STANDBY, false },
    { "up",      0, BEO_CMD_UP,        false },
    { "down",    0, BEO_CMD_DOWN,      false },
    { "left",    0, BEO_CMD_LEFT,      false },
    { "right",   0, BEO_CMD_RIGHT,     false },
    { "go",      0, BEO_CMD_GO,        false },
    { "play",    0, BEO_CMD_GO,        false },
    { "stop",    0, BEO_CMD_STOP,      false },
    { "return",  0, BEO_CMD_RETURN,    false },
    { "record",  0, BEO_CMD_RECORD,    false },
    { "menu",    0, BEO_CMD_MENU,      false },
    { "exit",    0, BEO_CMD_EXIT,      false },
    { "select",  0, BEO_CMD_SELECT,    false },
    { "list",    0, BEO_CMD_LIST,      false },
    { "index",   0, BEO_CMD_INDEX,     false },
    { "store",   0, BEO_CMD_STORE,     false },
    { "clear",   0, BEO_CMD_CLEAR,     false },
    { "tune",    0, BEO_CMD_TUNE,      false },
    { "clock",   0, BEO_CMD_CLOCK,     false },
    { "format",  0, BEO_CMD_FORMAT,    false },
    { "speaker", 0, BEO_CMD_SPEAKER,   false },
    { "picture", 0, BEO_CMD_PICTURE,   false },
    { "turn",    0, BEO_CMD_TURN,      false },
    { "loudness",0, BEO_CMD_LOUDNESS,  false },
    { "bass",    0, BEO_CMD_BASS,      false },
    { "treble",  0, BEO_CMD_TREBLE,    false },
    { "balance", 0, BEO_CMD_BALANCE,   false },
    { "red",     0, BEO_CMD_RED,       false },
    { "green",   0, BEO_CMD_GREEN,     false },
    { "blue",    0, BEO_CMD_BLUE,      false },
    { "yellow",  0, BEO_CMD_YELLOW,    false },
    { "av",      0, BEO_CMD_AV,        false },
};
static constexpr size_t CMD_TABLE_SIZE = sizeof(cmdTable) / sizeof(cmdTable[0]);

static const uint8_t numCmds[10] = {
    BEO_CMD_NUM_0, BEO_CMD_NUM_1, BEO_CMD_NUM_2, BEO_CMD_NUM_3, BEO_CMD_NUM_4,
    BEO_CMD_NUM_5, BEO_CMD_NUM_6, BEO_CMD_NUM_7, BEO_CMD_NUM_8, BEO_CMD_NUM_9,
};

// Shared state
static uint8_t      currentSrc     = BEO_SRC_AUDIO;
static const char*  currentSrcName = "radio";

// ── Forward declaration ──────────────────────────────────────────────
extern QueueHandle_t beo4_tx_queue;

// ── sendBeo ─────────────────────────────────────────────────────────
static void sendBeo(uint8_t src, uint8_t cmd, Channel& reply) {
    uint32_t code = ((uint32_t)src << 8) | (uint32_t)cmd;
    xQueueSend(beo4_tx_queue, &code, 0);
    reply.printf("  -> TX src=0x%02X cmd=0x%02X\n", src, cmd);
}

// ── printHelp ───────────────────────────────────────────────────────
static void printHelp(Channel& reply) {
    reply.println("\n=== Beo4 Command Interface ===");
    reply.println("Source commands (also set context):");
    reply.println("  tv, radio, cd, cd2, phono, dvd, sat, vtape,");
    reply.println("  pc, doorcam, light, a.aux, v.aux, atape, atape2, text, spdemo");
    reply.println("Control commands (use current source):");
    reply.println("  vol+, vol-, mute, standby/off, allstandby/alloff");
    reply.println("  up, down, left, right, go/play, stop");
    reply.println("  menu, exit, return, select, list, record");
    reply.println("  index, store, clear, tune, clock, format");
    reply.println("  speaker, picture, turn, loudness");
    reply.println("  bass, treble, balance, av");
    reply.println("  red, green, blue, yellow");
    reply.println("Numbers: 0-9");
    reply.println("Combined: cd 4  (source + number)");
    reply.println("vol+/vol- n  (repeat n times, max 20)");
    reply.println("Raw hex:  0x0192 (send raw beoCode)");
    reply.println("  help   - show this message");
    reply.println("  status - show current source");
    reply.println("");
}

// ── processCommand ──────────────────────────────────────────────────
static void processCommand(char* line, Channel& reply) {
    while (*line == ' ') line++;
    size_t len = strlen(line);
    while (len > 0 && (line[len-1] == ' ' || line[len-1] == '\r' || line[len-1] == '\n'))
        line[--len] = '\0';
    if (len == 0) return;

    char lower[64];
    for (size_t i = 0; i <= len && i < sizeof(lower)-1; i++)
        lower[i] = tolower((unsigned char)line[i]);
    lower[sizeof(lower)-1] = '\0';

    if (strcmp(lower, "name") == 0) {
        reply.println("Name: " DEVICE_NAME);
        return;
    }
    if (strcmp(lower, "help") == 0) {
        printHelp(reply);
        return;
    }
    if (strcmp(lower, "status") == 0) {
        reply.printf("SRC:%s\n", currentSrcName);
        return;
    }
    if (lower[0] == '0' && lower[1] == 'x') {
        uint32_t code = strtoul(lower, NULL, 16);
        xQueueSend(beo4_tx_queue, &code, 0);
        reply.printf("  -> TX raw 0x%04X\n", (unsigned)code);
        return;
    }

    char* space = strchr(lower, ' ');
    char* arg = NULL;
    if (space) { *space = '\0'; arg = space + 1; while (*arg == ' ') arg++; }

    if (strlen(lower) == 1 && lower[0] >= '0' && lower[0] <= '9') {
        sendBeo(currentSrc, numCmds[lower[0] - '0'], reply);
        return;
    }

    const CmdEntry* found = NULL;
    for (size_t i = 0; i < CMD_TABLE_SIZE; i++) {
        if (strcmp(lower, cmdTable[i].name) == 0) { found = &cmdTable[i]; break; }
    }
    if (!found) {
        reply.printf("Unknown command: %s  (type 'help')\n", line);
        return;
    }

    // allstandby/alloff: broadcast to ALL sources, don't change currentSrc
    if (found->src == BEO_SRC_ALL) {
        sendBeo(BEO_SRC_ALL, found->cmd, reply);
        return;
    }

    uint8_t src = found->setsSource ? found->src : currentSrc;
    if (found->setsSource) { currentSrc = found->src; currentSrcName = found->name; }
    if (found->cmd != 0xFF) sendBeo(src, found->cmd, reply);

    if (arg && strlen(arg) > 0) {
        if (found->cmd != 0xFF) vTaskDelay(pdMS_TO_TICKS(300));

        // vol+/vol- repeat count
        if ((strcmp(lower, "vol+") == 0 || strcmp(lower, "vol-") == 0) && found->cmd != 0xFF) {
            char* end;
            long n = strtol(arg, &end, 10);
            if (end != arg && *end == '\0' && n > 1 && n <= 20) {
                for (long r = 1; r < n; r++) {
                    vTaskDelay(pdMS_TO_TICKS(300));
                    sendBeo(src, found->cmd, reply);
                }
                return;
            }
        }

        // named arg (e.g. "light stop")
        bool argHandled = false;
        for (size_t i = 0; i < CMD_TABLE_SIZE; i++) {
            if (strcmp(arg, cmdTable[i].name) == 0) {
                uint8_t argSrc = cmdTable[i].setsSource ? cmdTable[i].src : currentSrc;
                if (cmdTable[i].setsSource) { currentSrc = cmdTable[i].src; currentSrcName = cmdTable[i].name; }
                if (cmdTable[i].cmd != 0xFF) sendBeo(argSrc, cmdTable[i].cmd, reply);
                argHandled = true;
                break;
            }
        }

        // digit sequence (e.g. "cd 42")
        if (!argHandled) {
            for (size_t i = 0; arg[i]; i++) {
                if (arg[i] >= '0' && arg[i] <= '9') {
                    sendBeo(currentSrc, numCmds[arg[i] - '0'], reply);
                    if (arg[i+1]) vTaskDelay(pdMS_TO_TICKS(300));
                }
            }
        }
    }
}
