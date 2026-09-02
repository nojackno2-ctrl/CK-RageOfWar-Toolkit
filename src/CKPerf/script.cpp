// script.cpp — the runtime script channel (ISSUE-068).
//
// WHY THIS EXISTS
//
// The trainer's cheats are VS (Celtic Kings Script) programs. Until now the only way to
// run one was data/scdebug.xml, which binds a script to one of exactly 20 hard-coded key
// ids. Nine of those are taken by the game (F1 help, F2 save, F3 load, F5 diplomacy,
// F6 quicksave, F7 select team, F8 notes, F9 quickload, F10 main menu) and five more by
// the stock scdebug bindings (Add/Sub/Mul/Pause/Tab), which leaves four free keys -- and
// in numpad mode the twelve F-key slots are redirected onto a numeric keypad that a
// laptop does not physically have. What the user actually saw was "there is no key to
// press and the in-game panel has no buttons".
//
// The key is not the mechanism, though. It is only one way of handing a string to the
// engine's script compiler. Reproducing the tail of the scdebug dispatcher inside the
// game process removes the key from the picture entirely.
//
// THE ENGINE PATH BEING REPRODUCED  (Celtic kings.exe, Steam build, base 0x00400000)
//
//   0x0047D560   key entry: tests [0x0074C3CC] ("[system] DebugKeys", default 1) and
//                bails out when Shift(0x10) / Alt(0x12) / Ctrl(0x11) is held, then calls
//                the dispatcher. This is the ONLY caller of 0x005E7650.
//   0x005E7650   void __cdecl ScDebugDispatch(uint16_t vk)
//                  looks vk up in the map at 0x008AF108
//                  node + 0x0E = key (short), node + 0x10 = script source (char*)
//
//   ... and its tail, which is what this file re-creates:
//
//     005E773B  mov  edi,[eax+0x10]      ; script source text
//     005E773E  lea  edx,[esp+0x10]      ; out/context slot ...
//     005E7749  mov  dword [esp+0x1c],0  ; ... which the caller zeroes first
//     005E7751  call 0x005E0340          ; compile(src, "void", &ctx) -> compiled or 0
//     005E775D  jne  ...                 ; 0 => print "error in key-bound script: '%s'"
//     005E777F  mov  eax,[esi+0x0e]      ; latent? then schedule on the VM scheduler
//     005E7796  call 0x005E1D70          ; ([0x00895E40], 8, compiled, 1, 0, 100)
//     005E77A5  call 0x005E0430          ; otherwise run synchronously ...
//     005E77AD  call 0x0041B480          ; ... and release through the owner singleton
//     005E77B7  call [edx+0x0c]          ;     __thiscall Release(compiled)
//
// DISCIPLINE (AGENTS.md 2.9)
//
//   * Verify first, refuse on mismatch. Every entry point below carries the original
//     prologue bytes. One mismatch disables the whole channel permanently; nothing is
//     ever partially enabled and nothing is ever guessed.
//   * Prove it before offering it. ScriptChannelSelfTest() compiles a harmless script
//     and releases it. If that does not work, the channel stays off.
//   * Refuse unless a session is live. All the engine globals involved must dereference
//     to plausible user-space pointers, or the request is rejected fail-closed.
//   * Main thread only. Scripts run from the SetDIBitsToDevice hook in frames.cpp, which
//     is the same thread the engine would have run them on.
//   * Zero disk. The channel lives and dies with the process.
//   * Authenticated. The pipe carries a per-injection random token supplied by
//     CKToolkit; a request without it is dropped and logged.

#include "ckperf.h"
#include <stdio.h>

namespace ckperf {

// ------------------------------------------------------------------ engine binding

// Preferred image base of the 2004 build. It has no relocation directory, so this never
// moves in practice -- but every address below is still resolved against the live module
// base and never used as an absolute.
static const uintptr_t kImageBase = 0x00400000;

static const uintptr_t kVaCompile        = 0x005E0340; // compile(src, signature, &ctx)
static const uintptr_t kVaRunCompiled    = 0x005E0430; // run(compiled)
static const uintptr_t kVaSchedule       = 0x005E1D70; // schedule(sched, 8, compiled, 1, 0, 100)
static const uintptr_t kVaOwnerSingleton = 0x0041B480; // owner(), vtable + 0x0C releases
static const uintptr_t kVaConsolePrintf  = 0x00470FB0; // printf(console, fmt, ...)

static const uintptr_t kVaSchedulerPtr   = 0x00895E40; // -> VM scheduler
static const uintptr_t kVaConsoleRoot    = 0x008AA6C8; // -> object whose +0x3214 is the console
static const uintptr_t kConsoleOffset    = 0x3214;
static const uintptr_t kVaGameObject     = 0x008AAB80; // -> main game object (also used by MousePtm)
static const uintptr_t kVaDebugKeysFlag  = 0x0074C3CC; // "[system] DebugKeys", default 1
static const uintptr_t kVaSignatureVoid  = 0x007290B0; // the literal "void"
static const uintptr_t kVaErrorFormat    = 0x0073FF6C; // "error in key-bound script: '%s'"

// The compiled-script header field the dispatcher tests to decide synchronous vs
// scheduled execution (005E777F: mov eax,[esi+0x0e]).
static const uintptr_t kCompiledLatentField = 0x0E;

// Release lives at vtable + 0x0C of the singleton returned by 0x0041B480
// (005E77B7: call dword ptr [edx+0x0c]).
static const int kOwnerReleaseVtableIndex = 0x0C / 4;

struct SiteSignature {
    uintptr_t            va;
    const char*          what;
    const unsigned char* bytes;
    int                  length;
};

// Original bytes, read off the retail executable. Kept as named arrays so a future
// engine build that differs is rejected loudly instead of being written into.
static const unsigned char kSigCompile[] = {
    0x81, 0xEC, 0xA0, 0x02, 0x00, 0x00,     // sub esp, 0x2a0
    0x8D, 0x44, 0x24, 0x40                  // lea eax, [esp+0x40]
};
static const unsigned char kSigRunCompiled[] = {
    0x56,                                   // push esi
    0x8B, 0x74, 0x24, 0x08,                 // mov esi, [esp+8]
    0x57,                                   // push edi
    0x6A, 0x52                              // push 0x52
};
static const unsigned char kSigSchedule[] = {
    0x8B, 0x4C, 0x24, 0x18,                 // mov ecx, [esp+0x18]
    0x8B, 0x54, 0x24, 0x14,                 // mov edx, [esp+0x14]
    0x6A, 0x00                              // push 0
};
static const unsigned char kSigOwnerSingleton[] = {
    0xA1, 0x04, 0xA5, 0x76, 0x00,           // mov eax, [0x76a504]
    0x33, 0xC9,                             // xor ecx, ecx
    0x3B, 0xC1                              // cmp eax, ecx
};
static const unsigned char kSigConsolePrintf[] = {
    0x8B, 0x4C, 0x24, 0x08,                 // mov ecx, [esp+8]
    0x83, 0xEC, 0x14,                       // sub esp, 0x14
    0x56                                    // push esi
};

// The dispatcher's own call site. Verifying it pins the calling convention this file
// depends on: the "void" literal really is argument 2 of the compiler, and the zeroed
// context slot really is argument 3.
static const uintptr_t kVaDispatchCall = 0x005E773E;
static const unsigned char kSigDispatchCall[] = {
    0x8D, 0x54, 0x24, 0x10,                 // lea edx, [esp+0x10]
    0x52,                                   // push edx
    0x68, 0xB0, 0x90, 0x72, 0x00,           // push offset "void"
    0x57                                    // push edi
};

// The literal the dispatcher passes as the script signature.
static const unsigned char kSigVoidLiteral[] = { 'v', 'o', 'i', 'd', 0x00 };

static const SiteSignature kSites[] = {
    { kVaCompile,        "script compiler",           kSigCompile,        (int)sizeof(kSigCompile)        },
    { kVaRunCompiled,    "script run",                kSigRunCompiled,    (int)sizeof(kSigRunCompiled)    },
    { kVaSchedule,       "script schedule",           kSigSchedule,       (int)sizeof(kSigSchedule)       },
    { kVaOwnerSingleton, "script owner singleton",    kSigOwnerSingleton, (int)sizeof(kSigOwnerSingleton) },
    { kVaConsolePrintf,  "console printf",            kSigConsolePrintf,  (int)sizeof(kSigConsolePrintf)  },
    { kVaDispatchCall,   "scdebug dispatch tail",     kSigDispatchCall,   (int)sizeof(kSigDispatchCall)   },
    { kVaSignatureVoid,  "void signature literal",    kSigVoidLiteral,    (int)sizeof(kSigVoidLiteral)    },
};

typedef void* (__cdecl* PFN_Compile)(const char* src, const char* signature, void* ctx);
typedef void  (__cdecl* PFN_RunCompiled)(void* compiled);
typedef void  (__cdecl* PFN_Schedule)(void* sched, int kind, void* compiled, int a, int b, int c);
typedef void* (__cdecl* PFN_OwnerSingleton)(void);
typedef void  (__cdecl* PFN_ConsolePrintf)(void* console, const char* fmt, ...);
typedef void  (__thiscall* PFN_OwnerRelease)(void* self, void* compiled);

static bool      g_enabled = false;   // signatures verified AND self-test passed
static bool      g_refused = false;   // something did not match; never retry
static uintptr_t g_base    = 0;

static PFN_Compile        g_compile        = nullptr;
static PFN_RunCompiled    g_runCompiled    = nullptr;
static PFN_Schedule       g_schedule       = nullptr;
static PFN_OwnerSingleton g_ownerSingleton = nullptr;
static PFN_ConsolePrintf  g_consolePrintf  = nullptr;
static const char*        g_signatureVoid  = nullptr;
static const char*        g_errorFormat    = nullptr;

static uintptr_t Resolve(uintptr_t va) { return g_base + (va - kImageBase); }

// Same sanity rule the managed GameMemory path uses: a real object lives in the user
// half of a 32-bit address space and never in the first 64 KB.
static bool LooksLikePointer(uint32_t v) {
    return v >= 0x00010000 && v < 0x80000000;
}

static bool DerefPointer(uintptr_t va, uintptr_t& out) {
    uint32_t v = 0;
    if (!SafeRead(Resolve(va), &v, sizeof(v))) return false;
    if (!LooksLikePointer(v)) return false;
    out = v;
    return true;
}

// ------------------------------------------------------------------- request slot
//
// One script at a time. The channel exists to serve a single operator clicking buttons
// on a panel, so a single-slot mailbox is both sufficient and the easiest thing to
// reason about: the pipe thread fills it and waits, the render thread drains it.

enum SlotState { kSlotEmpty = 0, kSlotPending = 1, kSlotDone = 2 };

static const int   kMaxScriptBytes  = 16 * 1024;
static const int   kMaxMessageBytes = 512;
static const DWORD kExecuteTimeoutMs = 5000;

static CRITICAL_SECTION g_slotLock;
static bool             g_slotLockReady = false;
static HANDLE           g_slotDone      = nullptr;
static volatile LONG    g_slotState     = kSlotEmpty;
static char             g_slotScript[kMaxScriptBytes + 1];
static int              g_slotStatus    = kScriptChannelDisabled;
static char             g_slotMessage[kMaxMessageBytes];

static volatile LONG    g_executed = 0;
static volatile LONG    g_rejected = 0;

// --------------------------------------------------------------------- execution

// Every engine global involved must dereference. This is the state the engine's own key
// path takes for granted, because a key can only reach it while a session is up.
static bool LiveSessionOk() {
    uintptr_t game = 0, root = 0, sched = 0;
    if (!DerefPointer(kVaGameObject, game))    return false;
    if (!DerefPointer(kVaConsoleRoot, root))   return false;
    if (!DerefPointer(kVaSchedulerPtr, sched)) return false;

    uint32_t console = 0;
    if (!SafeRead(root + kConsoleOffset, &console, sizeof(console))) return false;
    return LooksLikePointer(console);
}

static void* ResolveConsole() {
    uintptr_t root = 0;
    if (!DerefPointer(kVaConsoleRoot, root)) return nullptr;
    uint32_t console = 0;
    if (!SafeRead(root + kConsoleOffset, &console, sizeof(console))) return nullptr;
    if (!LooksLikePointer(console)) return nullptr;
    return (void*)(uintptr_t)console;
}

static void ReleaseCompiled(void* compiled) {
    void* owner = g_ownerSingleton();
    if (!owner) return;
    void** vtable = *(void***)owner;
    if (!vtable) return;
    PFN_OwnerRelease release = (PFN_OwnerRelease)vtable[kOwnerReleaseVtableIndex];
    if (release) release(owner, compiled);
}

// The engine is 2004 C++ with no exception safety of its own. A structured handler here
// does not make a faulting script safe, but it turns "the game vanishes" into "the panel
// says what happened", which is strictly better than the status quo -- and it matches the
// posture the rest of this DLL already takes with its vectored handler.
static int RunCompiledGuarded(void* compiled, bool latent, int& status, char* message, int cap) {
    __try {
        if (latent) {
            uintptr_t sched = 0;
            if (!DerefPointer(kVaSchedulerPtr, sched)) {
                // Refuse rather than run a latent script synchronously, which is not
                // something the engine ever does.
                status = kScriptNotInGame;
                Append(message, cap, 0, "the script VM scheduler is not running");
                return 0;
            }
            g_schedule((void*)sched, 8, compiled, 1, 0, 100);
            status = kScriptScheduled;
            Append(message, cap, 0, "scheduled on the script VM");
        } else {
            g_runCompiled(compiled);
            ReleaseCompiled(compiled);
            status = kScriptOk;
            Append(message, cap, 0, "executed");
        }
        return 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        status = kScriptFaulted;
        Append(message, cap, 0, "exception 0x%08X while running the script",
               (unsigned)GetExceptionCode());
        return -1;
    }
}

// Returns the compiled script, or null. `faulted` distinguishes the two ways null can
// happen: the compiler rejected the source (ordinary, report it like the engine does),
// or the call itself raised (never expected, must not be reported as a script error).
static void* CompileGuarded(const char* script, bool& faulted, char* message, int cap) {
    // The dispatcher hands the compiler a zeroed slot it never reads back. 64 bytes is
    // generous for a field the retail code sizes at four, and costs nothing.
    unsigned char ctx[64];
    memset(ctx, 0, sizeof(ctx));

    faulted = false;
    __try {
        return g_compile(script, g_signatureVoid, ctx);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        faulted = true;
        Append(message, cap, 0, "exception 0x%08X while compiling the script",
               (unsigned)GetExceptionCode());
        return nullptr;
    }
}

static void PrintCompileErrorGuarded(const char* script) {
    void* console = ResolveConsole();
    if (!console) return;
    __try {
        // The same console line the engine prints for a bad key-bound script, so a
        // script error looks identical no matter which path delivered it.
        g_consolePrintf(console, g_errorFormat, script);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        // Reporting must never be the thing that kills the process.
    }
}

// Mirrors 005E7751..005E77BF. Runs on the engine's own thread, from the frame hook, and
// only after LiveSessionOk() has proved the globals are populated.
static void ExecuteOnMainThread(const char* script, int& status, char* message, int cap) {
    message[0] = 0;

    bool faulted = false;
    void* compiled = CompileGuarded(script, faulted, message, cap);
    if (!compiled) {
        if (faulted) {
            status = kScriptFaulted;
        } else {
            status = kScriptCompileError;
            PrintCompileErrorGuarded(script);
            Append(message, cap, 0, "the engine rejected the script");
        }
        InterlockedIncrement(&g_rejected);
        return;
    }

    uint32_t latentField = 0;
    bool latent = SafeRead((uintptr_t)compiled + kCompiledLatentField,
                           &latentField, sizeof(latentField)) && latentField != 0;

    if (RunCompiledGuarded(compiled, latent, status, message, cap) != 0 ||
        status == kScriptNotInGame) {
        InterlockedIncrement(&g_rejected);
        return;
    }

    InterlockedIncrement(&g_executed);
}

void ScriptChannelPump() {
    if (!g_enabled) return;
    if (InterlockedCompareExchange(&g_slotState, kSlotPending, kSlotPending) != kSlotPending) return;

    int  status = kScriptNotInGame;
    char message[kMaxMessageBytes];
    message[0] = 0;

    if (!LiveSessionOk()) {
        Append(message, (int)sizeof(message), 0, "no game session is running");
        InterlockedIncrement(&g_rejected);
    } else {
        ExecuteOnMainThread(g_slotScript, status, message, (int)sizeof(message));
    }

    g_slotStatus = status;
    strncpy_s(g_slotMessage, sizeof(g_slotMessage), message, _TRUNCATE);
    InterlockedExchange(&g_slotState, kSlotDone);
    if (g_slotDone) SetEvent(g_slotDone);
}

// Called by the pipe thread. Blocks until the render thread has run the script or the
// wait expires -- an expired wait means the engine is not drawing, so nothing ran.
static int Submit(const char* script, int length, char* message, int cap, DWORD timeoutMs) {
    if (!g_enabled) {
        Append(message, cap, 0, "the script channel is disabled");
        return kScriptChannelDisabled;
    }
    if (length <= 0 || length > kMaxScriptBytes) {
        Append(message, cap, 0, "script length %d is out of range", length);
        return kScriptRejected;
    }

    EnterCriticalSection(&g_slotLock);
    if (InterlockedCompareExchange(&g_slotState, kSlotEmpty, kSlotEmpty) != kSlotEmpty) {
        LeaveCriticalSection(&g_slotLock);
        Append(message, cap, 0, "another script is still running");
        return kScriptBusy;
    }

    memcpy(g_slotScript, script, (size_t)length);
    g_slotScript[length] = 0;
    g_slotStatus = kScriptNotInGame;
    g_slotMessage[0] = 0;
    if (g_slotDone) ResetEvent(g_slotDone);
    InterlockedExchange(&g_slotState, kSlotPending);

    DWORD wait = g_slotDone ? WaitForSingleObject(g_slotDone, timeoutMs) : WAIT_FAILED;

    int status;
    if (wait == WAIT_OBJECT_0 &&
        InterlockedCompareExchange(&g_slotState, kSlotDone, kSlotDone) == kSlotDone) {
        status = g_slotStatus;
        strncpy_s(message, (size_t)cap, g_slotMessage, _TRUNCATE);
    } else {
        status = kScriptTimedOut;
        Append(message, cap, 0, "the engine did not draw a frame within %u ms",
               (unsigned)timeoutMs);
    }

    InterlockedExchange(&g_slotState, kSlotEmpty);
    LeaveCriticalSection(&g_slotLock);
    return status;
}

// ------------------------------------------------------------------------ the pipe
//
// Byte-mode named pipe, one instance, default DACL (creator + SYSTEM + administrators),
// remote clients rejected. The token check is the part that matters: it proves the
// request came from the CKToolkit instance that injected this DLL rather than from any
// other local process that happened to guess the pipe name.

static const uint32_t kMagic   = 0x43534B43; // 'CKSC'
static const uint32_t kVersion = 1;

#pragma pack(push, 1)
struct RequestHeader {
    uint32_t magic;
    uint32_t version;
    char     token[kScriptTokenChars];
    uint32_t flags;
    uint32_t scriptLength;
};
struct ResponseHeader {
    uint32_t magic;
    uint32_t version;
    uint32_t status;
    uint32_t messageLength;
};
#pragma pack(pop)

static HANDLE        g_pipeThread = nullptr;
static volatile LONG g_stopping   = 0;

static bool ReadExact(HANDLE pipe, void* dst, DWORD length) {
    DWORD done = 0;
    while (done < length) {
        DWORD n = 0;
        if (!ReadFile(pipe, (char*)dst + done, length - done, &n, nullptr) || n == 0) return false;
        done += n;
    }
    return true;
}

static void Respond(HANDLE pipe, int status, const char* message) {
    ResponseHeader header;
    header.magic   = kMagic;
    header.version = kVersion;
    header.status  = (uint32_t)status;

    char body[kMaxMessageBytes];
    strncpy_s(body, sizeof(body), message ? message : "", _TRUNCATE);
    header.messageLength = (uint32_t)strlen(body);

    DWORD written = 0;
    WriteFile(pipe, &header, sizeof(header), &written, nullptr);
    if (header.messageLength) WriteFile(pipe, body, header.messageLength, &written, nullptr);
    FlushFileBuffers(pipe);
}

// The token is not a secret worth defending against timing attacks, but there is no
// reason to leak its prefix either.
static bool TokenMatches(const char* candidate) {
    unsigned diff = 0;
    for (int i = 0; i < kScriptTokenChars; ++i) {
        diff |= (unsigned char)(candidate[i] ^ g_cfg.scriptToken[i]);
    }
    return diff == 0;
}

static void ServeOne(HANDLE pipe) {
    RequestHeader header;
    if (!ReadExact(pipe, &header, sizeof(header))) return;

    if (header.magic != kMagic || header.version != kVersion) {
        Respond(pipe, kScriptRejected, "unrecognised request");
        InterlockedIncrement(&g_rejected);
        return;
    }
    if (!TokenMatches(header.token)) {
        Logf("script channel: a request with a bad token was dropped.");
        Respond(pipe, kScriptRejected, "authentication failed");
        InterlockedIncrement(&g_rejected);
        return;
    }
    if (header.scriptLength == 0 || header.scriptLength > (uint32_t)kMaxScriptBytes) {
        Respond(pipe, kScriptRejected, "script length is out of range");
        InterlockedIncrement(&g_rejected);
        return;
    }

    static char script[kMaxScriptBytes + 1];
    if (!ReadExact(pipe, script, header.scriptLength)) return;
    script[header.scriptLength] = 0;

    char message[kMaxMessageBytes];
    message[0] = 0;
    int status = Submit(script, (int)header.scriptLength, message,
                        (int)sizeof(message), kExecuteTimeoutMs);
    Respond(pipe, status, message);
}

static DWORD WINAPI PipeThread(LPVOID) {
    wchar_t name[64];
    swprintf_s(name, L"\\\\.\\pipe\\ckperf-script-%u", (unsigned)GetCurrentProcessId());
    Logf("script channel: listening on %S", name);

    while (!g_stopping) {
        HANDLE pipe = CreateNamedPipeW(
            name,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1,                                   // one instance: one panel, one channel
            8192, 8192,
            0,
            nullptr);                            // default DACL: this user only
        if (pipe == INVALID_HANDLE_VALUE) {
            Logf("script channel: CreateNamedPipe failed (%u); the channel is unreachable.",
                 GetLastError());
            return 0;
        }

        BOOL connected = ConnectNamedPipe(pipe, nullptr) ||
                         GetLastError() == ERROR_PIPE_CONNECTED;
        if (connected && !g_stopping) {
            ServeOne(pipe);
            FlushFileBuffers(pipe);
        }
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }

    Logf("script channel: listener stopped after %ld executed / %ld rejected.",
         g_executed, g_rejected);
    return 0;
}

// ------------------------------------------------------------------------ install

static bool VerifySites() {
    for (int i = 0; i < (int)(sizeof(kSites) / sizeof(kSites[0])); ++i) {
        const SiteSignature& site = kSites[i];
        unsigned char actual[32];
        if (site.length > (int)sizeof(actual)) return false;
        if (!SafeRead(Resolve(site.va), actual, (size_t)site.length)) {
            Logf("script channel: cannot read %s at VA 0x%08X -- channel DISABLED.",
                 site.what, (unsigned)site.va);
            return false;
        }
        if (memcmp(actual, site.bytes, (size_t)site.length) != 0) {
            Logf("script channel: %s at VA 0x%08X does not match the expected bytes -- "
                 "channel DISABLED (this build of the game is not the one this code was "
                 "reverse engineered against).", site.what, (unsigned)site.va);
            return false;
        }
    }
    return true;
}

bool ScriptChannelSelfTest() {
    if (!g_compile || !g_ownerSingleton) return false;

    // A complete, side-effect-free VS program: declare an int, assign it, done. If the
    // compiler will not take this, nothing else in this file is trustworthy.
    static const char kProbe[] = "int i; i = 1;";

    bool faulted = false;
    char message[kMaxMessageBytes];
    message[0] = 0;

    void* compiled = CompileGuarded(kProbe, faulted, message, (int)sizeof(message));
    if (!compiled) {
        Logf("script channel: self-test probe did not compile (%s) -- channel DISABLED.",
             message[0] ? message : "the compiler returned null");
        return false;
    }

    // Release without running it. Running even a trivial script before the engine has a
    // session would be exactly the thing this file promises never to do.
    __try {
        ReleaseCompiled(compiled);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        Logf("script channel: self-test raised 0x%08X while releasing -- channel DISABLED.",
             (unsigned)GetExceptionCode());
        return false;
    }

    return true;
}

void ScriptChannelInstall() {
    if (!g_cfg.scriptChannel) {
        Logf("script channel: not requested (scriptchannel=0).");
        return;
    }
    if (g_enabled || g_refused) return;

    const ModuleEntry* game = GameModule();
    if (!game) {
        Logf("script channel: the game module was not identified -- channel DISABLED.");
        g_refused = true;
        return;
    }
    g_base = game->base;

    if (!VerifySites()) {
        g_refused = true;
        return;
    }

    g_compile        = (PFN_Compile)Resolve(kVaCompile);
    g_runCompiled    = (PFN_RunCompiled)Resolve(kVaRunCompiled);
    g_schedule       = (PFN_Schedule)Resolve(kVaSchedule);
    g_ownerSingleton = (PFN_OwnerSingleton)Resolve(kVaOwnerSingleton);
    g_consolePrintf  = (PFN_ConsolePrintf)Resolve(kVaConsolePrintf);
    g_signatureVoid  = (const char*)Resolve(kVaSignatureVoid);
    g_errorFormat    = (const char*)Resolve(kVaErrorFormat);

    if (!ScriptChannelSelfTest()) {
        g_refused = true;
        return;
    }

    InitializeCriticalSection(&g_slotLock);
    g_slotLockReady = true;
    g_slotDone = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!g_slotDone) {
        Logf("script channel: CreateEvent failed (%u) -- channel DISABLED.", GetLastError());
        g_refused = true;
        return;
    }

    g_enabled = true;

    uint32_t debugKeys = 0;
    SafeRead(Resolve(kVaDebugKeysFlag), &debugKeys, sizeof(debugKeys));
    Logf("script channel: entry points verified and self-test passed; DebugKeys=%u "
         "(informational -- this channel does not use the key path).", (unsigned)debugKeys);

    g_pipeThread = CreateThread(nullptr, 0, PipeThread, nullptr, 0, nullptr);
    if (!g_pipeThread) {
        Logf("script channel: could not start the pipe thread (%u); the channel is "
             "verified but unreachable.", GetLastError());
    }
}

void ScriptChannelUninstall() {
    InterlockedExchange(&g_stopping, 1);
    if (g_pipeThread) {
        // Unblock a ConnectNamedPipe that is waiting for a client that will never come.
        wchar_t name[64];
        swprintf_s(name, L"\\\\.\\pipe\\ckperf-script-%u", (unsigned)GetCurrentProcessId());
        HANDLE poke = CreateFileW(name, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                                  OPEN_EXISTING, 0, nullptr);
        if (poke != INVALID_HANDLE_VALUE) CloseHandle(poke);

        WaitForSingleObject(g_pipeThread, 2000);
        CloseHandle(g_pipeThread);
        g_pipeThread = nullptr;
    }
    g_enabled = false;
    if (g_slotDone) { CloseHandle(g_slotDone); g_slotDone = nullptr; }
    if (g_slotLockReady) { DeleteCriticalSection(&g_slotLock); g_slotLockReady = false; }
}

bool ScriptChannelEnabled()      { return g_enabled; }
long ScriptChannelExecutedCount() { return g_executed; }
long ScriptChannelRejectedCount() { return g_rejected; }

} // namespace ckperf
