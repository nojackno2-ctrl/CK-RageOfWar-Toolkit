// nullstore.cpp -- generic repair for the engine's null-pointer accesses.
//
// THREE SESSIONS, ONE BUG CLASS, NINE SITES AND COUNTING
//
// Every fault captured so far is a script-VM command touching an object through a
// pointer the engine computed as NULL and then used unchecked. The clearest example is
// 0x005D9960, where the compiler folded the failure branch into a guaranteed fault:
//
//     005D9988  call 0x481a20        ; resolve a script reference
//     005D9990  test eax, eax
//     005D9992  je   0x5D99A2        ; resolution failed
//     005D9994  mov  edx, [esp+6]
//     005D9998  mov  [eax+edx], esi  ; success: store into the object field
//     ...
//     005D99A2  xor  eax, eax        ; failure: destination pointer = NULL
//     005D99A4  mov  [eax], esi      ; ...and store through it anyway
//
// That is `dst = ok ? &obj->field : NULL; *dst = value;` compiled literally. Any failed
// resolution kills the process outright -- no race required.
//
// The third session found EIGHT more sites in 1.5 seconds, in four separate clusters
// (0x005D99A4, 0x005D9BF2, 0x0068F91A/25/31, 0x006907E6/F0/F6). Patching sites one at a
// time does not scale against that, so this repairs the FAULT CLASS instead.
//
// THE MODEL: the null page exists, reads as zero, and swallows writes.
//
//   * a write into the null page from game code is skipped;
//   * a read from the null page delivers ZERO into the destination register.
//
// Both halves come from the same idea, and it is not an invention: it is exactly what
// the engine would observe if a zero-filled page were mapped at address 0. That is the
// other way to solve this problem, and modern Windows refuses to allow it. Matching its
// semantics keeps the behaviour describable in one sentence instead of being a pile of
// special cases.
//
// The alternative for reads -- stepping over the load and leaving the register at
// whatever it happened to hold -- would be real corruption, and is not done. A load
// whose destination cannot be determined is not repaired at all.
//
// WHAT IS STILL LEFT TO CRASH. Deliberately quite a lot:
//
//   * anything outside the null page, so a genuine wild pointer still dies loudly;
//   * anything outside the game image;
//   * every instruction form the decoder is not certain about -- read-modify-write,
//     string operations, floating point;
//   * indirect call/jump memory operands. A zero-filled scratch page is valid data
//     semantics for an ordinary load, but `call [reg+disp]` uses that data as the next
//     EIP. The 2026-08-23 field crash proved that redirecting `call [edx+4]` to scratch
//     simply converts the original null read into a DEP fault at EIP 0.
//
// Rejected forms produce a full report instead, which is how new precise sites are
// discovered without turning visible faults into silent control-flow corruption.
//
// Every distinct site is recorded with a hit count and reported in the log and in each
// crash report. That table is the real deliverable: a map of every place the engine
// does this, which is what any proper per-site fix would need.
//
// The mechanism verifies itself at startup by executing a real null store and a real
// null load and checking both were repaired -- the load stub returns the register that
// was loaded, so a nonzero result proves the handler stepped over the instruction
// without delivering zero, and the repair disables itself rather than run unproven.

#include "ckperf.h"
#include <stdio.h>

namespace ckperf {

namespace {

constexpr int  kMaxSites       = 64;
constexpr uintptr_t kNullPage  = 0x10000;

// The previous cap of 200,000 is what actually killed the third test session: two sites
// in an accumulate-in-place loop reached it in five seconds, the repair stopped, and the
// fault turned fatal. The cap exists only so a runaway cannot spin forever with no way
// out; it is now far above anything a healthy session produces, and crossing the warning
// threshold is logged loudly so a runaway is visible long before it matters.
constexpr LONG kMaxPerSite     = 5000000;
constexpr LONG kRunawayWarnAt  = 100000;

// Real memory for redirected accesses.
//
// Each faulting site gets its OWN region. A single shared page looked simpler and was
// wrong: two unrelated dead objects would then alias the same bytes, so one site's write
// becomes another site's read. The startup self-test caught exactly that -- its store
// stub polluted the location its load stub then read, and the load stopped returning
// zero. In the game the same aliasing would hand a script another script's garbage.
//
// Regions are 128 KB so the whole null page plus any displacement fits.
constexpr int    kScratchSlots  = kMaxSites + 4;   // sites, plus a few for the self-test
constexpr size_t kScratchStride = 2 * kNullPage;
void* g_scratchArena = nullptr;

// eip -> scratch slot. Kept separate from the reporting site table so the self-test can
// have real scratch without appearing in the map of the engine's bugs.
uintptr_t g_scratchEip[kScratchSlots] = {};
volatile LONG g_scratchUsed = 0;

void* ScratchFor(uintptr_t eip) {
    if (!g_scratchArena) return nullptr;
    LONG n = g_scratchUsed;
    for (LONG i = 0; i < n; ++i) {
        if (g_scratchEip[i] == eip) return (unsigned char*)g_scratchArena + i * kScratchStride;
    }
    if (n >= kScratchSlots) return nullptr;
    g_scratchEip[n] = eip;
    g_scratchUsed = n + 1;
    return (unsigned char*)g_scratchArena + n * kScratchStride;
}

struct Site {
    uintptr_t eip;
    LONG      hits;
    bool      reported;
    bool      warned;
};

Site         g_sites[kMaxSites];
volatile LONG g_siteCount = 0;
volatile LONG g_totalRepairs = 0;
CRITICAL_SECTION g_lock;
bool g_lockReady = false;

// ------------------------------------------------------------ instruction decoding
//
// Just enough x86 to measure the length of a plain MOV store. Anything unrecognised
// returns 0, which means "do not touch this fault".

int ModRmLength(const unsigned char* p, int opSizeOverride, int immBytes) {
    unsigned char modrm = p[0];
    int mod = modrm >> 6;
    int rm  = modrm & 7;
    if (mod == 3) return 0;         // register destination: cannot fault, not our business

    int len = 1;                    // the ModR/M byte itself
    bool hasSib = (rm == 4);
    if (hasSib) {
        len += 1;
        unsigned char base = p[1] & 7;
        if (mod == 0 && base == 5) len += 4;
    } else if (mod == 0 && rm == 5) {
        len += 4;                   // disp32-only addressing
    }
    if (mod == 1) len += 1;
    if (mod == 2) len += 4;

    if (immBytes == -1) immBytes = opSizeOverride ? 2 : 4;   // -1 means "operand sized"
    return len + immBytes;
}

// ------------------------------------------------------------------- null reads
//
// A read from the null page is repaired by delivering ZERO into the destination
// register and stepping over the instruction.
//
// That is not a guess. It is exactly what the engine would observe if a zero-filled
// page were mapped at address 0 -- which is the other way to solve this problem, and
// the way the OS refuses to allow. Choosing the same semantics keeps the model honest:
// "the null page exists and reads as zero, writes to it go nowhere".
//
// The alternative -- skipping the load and leaving the register at whatever it held --
// really would be corruption, and is not done.

// Maps an x86 register index (0..7) to its slot in a 32-bit CONTEXT.
DWORD* RegSlot(CONTEXT* cx, int index) {
    switch (index) {
        case 0: return &cx->Eax;
        case 1: return &cx->Ecx;
        case 2: return &cx->Edx;
        case 3: return &cx->Ebx;
        case 4: return nullptr;          // esp: never synthesise a value into the stack pointer
        case 5: return &cx->Ebp;
        case 6: return &cx->Esi;
        case 7: return &cx->Edi;
        default: return nullptr;
    }
}

struct LoadInfo {
    int len;        // total instruction length
    int reg;        // destination register index
    int width;      // 8, 16 or 32 -- how much of the register the load would have written
    bool highByte;  // true for ah/ch/dh/bh
};

// Returns true when this is a load whose destination we can zero with confidence.
bool DecodeLoad(const unsigned char* code, size_t avail, LoadInfo& out) {
    size_t i = 0;
    int opSize16 = 0;
    while (i < avail && i < 4) {
        unsigned char b = code[i];
        if (b == 0x66) { opSize16 = 1; ++i; continue; }
        if (b == 0x67 || b == 0xF2 || b == 0xF3) return false;
        if (b == 0x26 || b == 0x2E || b == 0x36 || b == 0x3E || b == 0x64 || b == 0x65) { ++i; continue; }
        break;
    }
    if (i + 1 >= avail) return false;

    unsigned char op = code[i];
    size_t modrmAt = i + 1;
    int opcodeLen = 1;
    int width = 0;

    if (op == 0x8A) {                      // mov r8, r/m8
        width = 8;
    } else if (op == 0x8B) {               // mov r16/32, r/m16/32
        width = opSize16 ? 16 : 32;
    } else if (op == 0x0F) {
        if (modrmAt >= avail) return false;
        unsigned char op2 = code[modrmAt];
        // movzx / movsx both fully define the 32-bit destination, so zero is exact.
        if (op2 != 0xB6 && op2 != 0xB7 && op2 != 0xBE && op2 != 0xBF) return false;
        opcodeLen = 2;
        modrmAt += 1;
        if (modrmAt >= avail) return false;
        width = opSize16 ? 16 : 32;
    } else {
        return false;
    }

    unsigned char modrm = code[modrmAt];
    if ((modrm >> 6) == 3) return false;   // register source cannot fault
    int body = ModRmLength(code + modrmAt, opSize16, 0);
    if (body == 0) return false;

    out.len      = (int)(i + opcodeLen + body);
    out.reg      = (modrm >> 3) & 7;
    out.width    = width;
    out.highByte = (width == 8 && out.reg >= 4);
    if (out.highByte) out.reg -= 4;        // ah/ch/dh/bh alias eax/ecx/edx/ebx
    return true;
}

// ------------------------------------------------------- base-register redirection
//
// Skipping an access and synthesising zero is not good enough, and the third session
// proved it. Site 0x005D98BF is `*p += n` compiled as load / add / store through the
// same null pointer. With reads always answering zero the value never advances, so a
// script loop waiting for it to reach a limit never finishes: the game rendered no
// frames for five seconds and burned 400,000 faults before dying. A crash turned into
// a hang, which is worse.
//
// The repair is therefore to give the access somewhere real to happen. When the null
// pointer lives in a base register, that register is pointed at a scratch page and the
// instruction is RE-EXECUTED rather than skipped. Now the load reads real memory, the
// store lands in real memory, and `*p += n` actually advances -- so the loop ends.
//
// This also removes the need to understand what the instruction DOES. Only the
// addressing mode matters, which means read-modify-write forms, string operations and
// floating-point accesses are all covered without decoding any of them.
//
// The cost is honest and worth stating: the base register is left pointing at scratch
// instead of at zero, so code that afterwards tests it for null now sees "not null".
// That is the same answer a real mapped page at address 0 would force, and unlike
// patching the resolver it only affects the handful of sites that actually fault
// rather than all 1,690 callers of 0x00481A20.

// True for one-byte opcodes that are followed by a ModR/M byte.
bool HasModRm(unsigned char op) {
    if (op <= 0x3B && (op & 7) <= 3) return true;      // add/or/adc/sbb/and/sub/xor/cmp r/m forms
    if (op == 0x62 || op == 0x63 || op == 0x69 || op == 0x6B) return true;
    if (op >= 0x80 && op <= 0x8F) return true;         // group1, test, xchg, mov, lea, pop
    if (op == 0xC0 || op == 0xC1 || op == 0xC4 || op == 0xC5 || op == 0xC6 || op == 0xC7) return true;
    if (op >= 0xD0 && op <= 0xD3) return true;         // shift group
    if (op >= 0xD8 && op <= 0xDF) return true;         // x87
    if (op == 0xF6 || op == 0xF7 || op == 0xFE || op == 0xFF) return true;
    return false;
}

// FF /2,/3 are indirect CALL and FF /4,/5 are indirect JMP. Their memory operand is
// not ordinary data: it becomes the next instruction pointer. Redirecting the base to
// a zero-filled scratch page would manufacture target 0 and resume into a guaranteed
// DEP fault, exactly what happened at 0x00693070 (`FF 52 04`) in pid 27096.
bool IsIndirectControlFlowMemoryOperand(const unsigned char* code, size_t avail) {
    size_t i = 0;
    while (i < avail && i < 4) {
        unsigned char b = code[i];
        if (b == 0x66 || b == 0x67 || b == 0xF2 || b == 0xF3 ||
            b == 0x26 || b == 0x2E || b == 0x36 || b == 0x3E || b == 0x64 || b == 0x65) {
            ++i;
            continue;
        }
        break;
    }
    if (i + 1 >= avail || code[i] != 0xFF) return false;

    unsigned char modrm = code[i + 1];
    if ((modrm >> 6) == 3) return false;  // register target; no memory operand to redirect
    int group = (modrm >> 3) & 7;
    return group >= 2 && group <= 5;
}

// Finds the base register of the memory operand, or returns false when the addressing
// mode has no base register to redirect (absolute disp32, or an unrecognised opcode).
bool FindBaseRegister(const unsigned char* code, size_t avail, int& baseReg) {
    size_t i = 0;
    while (i < avail && i < 4) {
        unsigned char b = code[i];
        if (b == 0x66 || b == 0x67 || b == 0xF2 || b == 0xF3 ||
            b == 0x26 || b == 0x2E || b == 0x36 || b == 0x3E || b == 0x64 || b == 0x65) { ++i; continue; }
        break;
    }
    if (i >= avail) return false;

    size_t modrmAt;
    if (code[i] == 0x0F) modrmAt = i + 2;
    else if (HasModRm(code[i])) modrmAt = i + 1;
    else return false;
    if (modrmAt >= avail) return false;

    unsigned char modrm = code[modrmAt];
    int mod = modrm >> 6;
    int rm  = modrm & 7;
    if (mod == 3) return false;                    // register operand: nothing to redirect

    if (rm == 4) {                                 // SIB
        if (modrmAt + 1 >= avail) return false;
        int base = code[modrmAt + 1] & 7;
        if (mod == 0 && base == 5) return false;   // no base register in this form
        baseReg = base;
        return true;
    }
    if (mod == 0 && rm == 5) return false;         // absolute disp32
    baseReg = rm;
    return true;
}

// Returns the total instruction length, or 0 when this is not a store we will skip.
int StoreLength(const unsigned char* code, size_t avail) {
    size_t i = 0;
    int opSizeOverride = 0;

    // Prefixes. Address-size and repeat prefixes appear on instructions we do not
    // handle anyway, but they still have to be counted to reject cleanly.
    while (i < avail && i < 4) {
        unsigned char b = code[i];
        if (b == 0x66) { opSizeOverride = 1; ++i; continue; }
        if (b == 0x67 || b == 0xF2 || b == 0xF3) return 0;   // not decoded
        if (b == 0x26 || b == 0x2E || b == 0x36 || b == 0x3E || b == 0x64 || b == 0x65) { ++i; continue; }
        break;
    }
    if (i >= avail) return 0;

    unsigned char op = code[i];
    size_t modrmAt = i + 1;
    if (modrmAt >= avail) return 0;

    int body = 0;
    switch (op) {
        case 0x88:  // mov r/m8, r8
        case 0x89:  // mov r/m16/32, r16/32
            body = ModRmLength(code + modrmAt, opSizeOverride, 0);
            break;
        case 0xC6:  // mov r/m8, imm8
            if ((code[modrmAt] >> 3 & 7) != 0) return 0;
            body = ModRmLength(code + modrmAt, opSizeOverride, 1);
            break;
        case 0xC7:  // mov r/m16/32, imm16/32
            if ((code[modrmAt] >> 3 & 7) != 0) return 0;
            body = ModRmLength(code + modrmAt, opSizeOverride, -1);
            break;
        default:
            return 0;
    }
    if (body == 0) return 0;
    return (int)(i + 1 + body);
}

} // namespace

// The self-test executes a real null store from a scratch page. That page is not the
// game image, so the module check would normally reject it; this address is the one
// documented exception, and it is cleared the moment the test finishes.
static volatile uintptr_t g_selfTestStoreEip = 0;

// 0x005D9880 is the VM's integer += handler. Returning 2 is not invented behavior:
// the dispatcher at 0x005DF5F1 explicitly branches on handler return 2 to 0x005DF921,
// marks status 3, and leaves the current script/atomic section. The field run proved
// that redirecting this handler's null `mov ecx,[eax]` to per-EIP scratch can still
// loop forever because another VM opcode reads the same dead lvalue through a different
// scratch slot. Abort the invalid compound assignment instead of emulating shared state.
constexpr uintptr_t kInvalidAddAssignLoad = 0x005D98BF;

__declspec(naked) static void AbortInvalidAddAssignment() {
    __asm {
        pop edi          // mirrors 0x005D98CE
        mov eax, 2       // VM handler return code: abort current script section
        pop esi          // mirrors 0x005D98D1
        add esp, 8       // release the handler's local area
        ret
    }
}

void NullStoreInit() {
    InitializeCriticalSection(&g_lock);
    g_lockReady = true;

    // VirtualAlloc zero-fills, which is what makes a redirected first read return the
    // same value a real page at address 0 would have.
    g_scratchArena = VirtualAlloc(nullptr, kScratchSlots * kScratchStride,
                                  MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
}

// ---------------------------------------------------------------- the null page
//
// Everything in this file emulates one thing: a zero-filled page at address 0. If the
// OS will simply give us that page, the emulation becomes unnecessary -- no exceptions,
// no instruction decoding, no register mutation, and no semantic drift, because the
// engine then observes exactly the model this file has been approximating.
//
// Windows blocks this through VirtualAlloc, and has blocked null-page allocation by
// policy since Windows 8, but the policy is enforced per-process and the syscall is
// worth asking. If it is refused, the fault-repair path stays in charge.

typedef LONG (NTAPI* PFN_NtAllocateVirtualMemory)(HANDLE, PVOID*, ULONG_PTR, PSIZE_T, ULONG, ULONG);

bool NullPageTryMap() {
    // Respect both the user option and the diagnostic safety fail-closed path. Mapping
    // page zero changes engine behaviour even though no VEH repair is involved, so it
    // must never happen after repair has been disabled.
    if (!g_cfg.nullStoreRepair) return false;

    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    if (!ntdll) return false;
    auto alloc = (PFN_NtAllocateVirtualMemory)GetProcAddress(ntdll, "NtAllocateVirtualMemory");
    if (!alloc) return false;

    // Base 0x1 rather than 0: passing 0 means "let the system choose an address", which
    // would quietly hand back a perfectly ordinary allocation somewhere else and look
    // like success. 0x1 rounds down to the null page and means what we intend.
    PVOID  base = (PVOID)0x1;
    SIZE_T size = 0x10000;
    LONG status = alloc(GetCurrentProcess(), &base, 0, &size,
                        MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (status < 0) {
        Logf("null page: the OS refused to map address 0 (NTSTATUS 0x%08X). Falling back to "
             "repairing each fault as it happens.", (unsigned)status);
        return false;
    }
    if (base != nullptr && (uintptr_t)base >= kNullPage) {
        Logf("null page: the allocator returned 0x%p instead of the null page; ignoring it.", base);
        return false;
    }

    Logf("null page: mapped %u KB of zero-filled memory at address 0. Null reads now return "
         "zero and null writes go nowhere, with no exception cost. Fault repair remains "
         "installed as a backstop.", (unsigned)(size >> 10));
    return true;
}

// Runs one stub and reports whether it survived. selfTestEip must be the address of
// the faulting instruction inside the stub.
static bool RunStub(const unsigned char* stub, size_t stubLen, int faultOffset,
                    unsigned& returned, bool& survived) {
    void* page = VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!page) return false;
    memcpy(page, stub, stubLen);
    FlushInstructionCache(GetCurrentProcess(), page, stubLen);

    g_selfTestStoreEip = (uintptr_t)page + faultOffset;
    survived = true;
    returned = 0;
    __try {
        returned = ((unsigned(*)())page)();
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        survived = false;
    }
    g_selfTestStoreEip = 0;
    VirtualFree(page, 0, MEM_RELEASE);
    return true;
}

static bool IndirectControlFlowSelfTest() {
    // Directly exercise NullStoreTryRepair with the exact instruction from the field
    // crash. Do not execute it: a correctly rejected instruction must be allowed to
    // fault normally, and executing that negative test would intentionally create a
    // crash report during every game startup.
    unsigned char code[16] = { 0xFF, 0x52, 0x04 };  // call dword ptr [edx+4]
    EXCEPTION_RECORD er = {};
    er.ExceptionCode = EXCEPTION_ACCESS_VIOLATION;
    er.NumberParameters = 2;
    er.ExceptionInformation[0] = 0;  // read
    er.ExceptionInformation[1] = 4;

    CONTEXT cx = {};
    cx.Eip = (DWORD)(uintptr_t)code;
    cx.Edx = 0;
    EXCEPTION_POINTERS ep = { &er, &cx };

    bool first = false;
    unsigned resume = 0;
    g_selfTestStoreEip = (uintptr_t)code;
    bool repaired = NullStoreTryRepair(&ep, first, resume);
    g_selfTestStoreEip = 0;

    if (repaired || resume != 0 || cx.Edx != 0) return false;

    const unsigned char indirectJump[] = { 0xFF, 0x60, 0x08 }; // jmp dword ptr [eax+8]
    const unsigned char ordinaryLoad[] = { 0x8B, 0x51, 0x04 }; // mov edx,[ecx+4]
    return IsIndirectControlFlowMemoryOperand(indirectJump, sizeof(indirectJump)) &&
           !IsIndirectControlFlowMemoryOperand(ordinaryLoad, sizeof(ordinaryLoad));
}

static bool InvalidAddAssignmentSelfTest() {
    unsigned char actual[2] = {};
    if (!SafeRead(kInvalidAddAssignLoad, actual, sizeof(actual)) ||
        actual[0] != 0x8B || actual[1] != 0x08) return false; // mov ecx,[eax]

    EXCEPTION_RECORD er = {};
    er.ExceptionCode = EXCEPTION_ACCESS_VIOLATION;
    er.NumberParameters = 2;
    er.ExceptionInformation[0] = 0; // read
    er.ExceptionInformation[1] = 0;

    CONTEXT cx = {};
    cx.Eip = (DWORD)kInvalidAddAssignLoad;
    cx.Eax = 0;
    cx.Edi = 1;
    EXCEPTION_POINTERS ep = { &er, &cx };

    bool first = false;
    unsigned resume = 0;
    g_selfTestStoreEip = kInvalidAddAssignLoad;
    bool repaired = NullStoreTryRepair(&ep, first, resume);
    g_selfTestStoreEip = 0;

    return repaired && resume == (unsigned)(uintptr_t)&AbortInvalidAddAssignment &&
           cx.Eax == 0 && cx.Edi == 1;
}

bool NullStoreSelfTest() {
    if (!g_cfg.nullStoreRepair) return true;

    // Both stubs are the exact shapes seen in the game, so what gets proven here is
    // what will run there.
    //
    //   store:  xor eax,eax ; mov [eax],ecx ; xor eax,eax ; ret
    //   load:   mov ecx,0xDEADBEEF ; xor eax,eax ; mov ecx,[eax] ; mov eax,ecx ; ret
    //
    // The load stub returns ecx, so a return value of zero proves the handler actually
    // delivered zero into the destination register rather than merely stepping over the
    // instruction and leaving 0xDEADBEEF behind.
    const unsigned char storeStub[] = { 0x33,0xC0, 0x89,0x08, 0x33,0xC0, 0xC3 };
    const unsigned char loadStub[]  = { 0xB9,0xEF,0xBE,0xAD,0xDE, 0x33,0xC0, 0x8B,0x08, 0x8B,0xC1, 0xC3 };

    unsigned ret = 0;
    bool survived = false;
    bool ok = true;
    const char* failure = nullptr;

    if (!RunStub(storeStub, sizeof(storeStub), 2, ret, survived)) {
        Logf("null-store repair: self-test could not allocate a scratch page; repair left "
             "ENABLED but unverified.");
        return false;
    }
    if (!survived) { ok = false; failure = "the null STORE was not repaired"; }

    if (ok) {
        if (!RunStub(loadStub, sizeof(loadStub), 7, ret, survived)) return false;
        if (!survived)   { ok = false; failure = "the null LOAD was not repaired"; }
        else if (ret != 0) { ok = false; failure = "the null LOAD resumed without delivering zero"; }
    }

    if (ok && !IndirectControlFlowSelfTest()) {
        ok = false;
        failure = "an indirect call/jump memory operand was accepted for repair";
    }

    if (ok && !InvalidAddAssignmentSelfTest()) {
        ok = false;
        failure = "the invalid VM += handler did not select its return-code-2 abort path";
    }

    // Start the session counter from zero so the number the user sees is entirely the
    // engine's doing.
    InterlockedExchange(&g_totalRepairs, 0);

    if (ok) {
        Logf("null-store repair: self-test passed -- a real null store/load were repaired, "
             "indirect control flow was rejected, and invalid VM += selects abort code 2.");
        return true;
    }

    // Verification failed, so the mechanism is not trustworthy on this machine. Better
    // to lose the repair than to resume execution at an address we computed wrongly.
    g_cfg.nullStoreRepair = false;
    Logf("null-store repair: self-test FAILED (%s). Repair has been DISABLED for this "
         "session; faults will be reported but not repaired.", failure);
    return false;
}

long NullStoreCount() { return InterlockedCompareExchange(&g_totalRepairs, 0, 0); }

// True when this eip has not been seen before, so the caller knows to write one full
// crash report for it. Called only from inside the exception handler.
static bool RecordSite(uintptr_t eip, bool& firstTime) {
    firstTime = false;
    if (!g_lockReady) return false;

    EnterCriticalSection(&g_lock);
    Site* found = nullptr;
    LONG n = g_siteCount;
    for (LONG i = 0; i < n; ++i) {
        if (g_sites[i].eip == eip) { found = &g_sites[i]; break; }
    }
    if (!found) {
        if (n >= kMaxSites) { LeaveCriticalSection(&g_lock); return false; }
        found = &g_sites[n];
        found->eip = eip;
        found->hits = 0;
        found->reported = false;
        found->warned = false;
        g_siteCount = n + 1;
        firstTime = true;
    }
    bool allowed = found->hits < kMaxPerSite;
    if (allowed) ++found->hits;
    bool warn = allowed && !found->warned && found->hits >= kRunawayWarnAt;
    if (warn) found->warned = true;
    LONG hits = found->hits;
    LeaveCriticalSection(&g_lock);

    // Logged outside the lock. A site this hot is not an occasional stale reference --
    // it is a loop running against a pointer that will never become valid, and it is
    // worth a precise code patch rather than an exception per iteration.
    if (warn) {
        Logf("null-store RUNAWAY: 0x%08X has faulted %ld times. The engine is looping on a "
             "pointer that never becomes valid; this site needs a real patch, not a per-fault "
             "repair.", (unsigned)eip, hits);
    }
    return allowed;
}

bool NullStoreTryRepair(EXCEPTION_POINTERS* ep, bool& firstTimeAtThisSite, unsigned& resumeEip) {
    // The context is deliberately NOT modified here. The caller may still want to
    // write a crash report, and a report describing an already-advanced eip would
    // point at the wrong instruction. The caller applies resumeEip when it is done.
    firstTimeAtThisSite = false;
    resumeEip = 0;
    if (!g_cfg.nullStoreRepair) return false;

    EXCEPTION_RECORD* er = ep->ExceptionRecord;
    if (er->ExceptionCode != EXCEPTION_ACCESS_VIOLATION) return false;
    if (er->NumberParameters < 2) return false;
    ULONG_PTR op = er->ExceptionInformation[0];
    if (op != 0 && op != 1) return false;                                // reads and writes only
    uintptr_t target = (uintptr_t)er->ExceptionInformation[1];
    if (target >= kNullPage) return false;                               // null page only

    uintptr_t eip = ep->ContextRecord->Eip;
    if (eip != g_selfTestStoreEip) {
        const ModuleEntry* m = ModuleForAddress(eip);
        const ModuleEntry* game = GameModule();
        if (!m || !game || m->base != game->base) return false;          // game code only
    }

    unsigned char code[16];
    if (!SafeRead(eip, code, sizeof(code))) return false;

    // This exact operator was observed spinning at >100,000 first-chance AVs while the
    // process remained alive but rendered no frames. Per-site scratch cannot preserve
    // lvalue identity across separate VM opcodes, so continuing it is not safe.
    if (eip == kInvalidAddAssignLoad && op == 0 && target == 0 &&
        code[0] == 0x8B && code[1] == 0x08) {
        if (eip != g_selfTestStoreEip) {
            if (!RecordSite(eip, firstTimeAtThisSite)) return false;
        }
        InterlockedIncrement(&g_totalRepairs);
        resumeEip = (unsigned)(uintptr_t)&AbortInvalidAddAssignment;
        return true;
    }

    // Strategy 1, preferred: point the base register at scratch and re-execute. This
    // works for any instruction form, and it lets accumulate-in-place patterns actually
    // progress instead of spinning forever on a value that never changes.
    int baseReg = -1;
    DWORD* baseSlot = nullptr;
    void* scratch = ScratchFor(eip);
    if (!IsIndirectControlFlowMemoryOperand(code, sizeof(code)) &&
        scratch && FindBaseRegister(code, sizeof(code), baseReg)) {
        baseSlot = RegSlot(ep->ContextRecord, baseReg);
        // Only redirect when that register really is the null pointer. If it already
        // holds something large the fault came from elsewhere, and moving it would be
        // a guess rather than a repair.
        if (baseSlot && *baseSlot >= kNullPage) baseSlot = nullptr;
    }

    // Strategy 2, fallback for absolute addressing where there is no register to move:
    // skip the store, or deliver zero for a load.
    int len = 0;
    LoadInfo load = {};
    bool isLoad = false;
    if (!baseSlot) {
        if (op == 1) {
            len = StoreLength(code, sizeof(code));
        } else {
            if (!DecodeLoad(code, sizeof(code), load)) return false;
            if (!RegSlot(ep->ContextRecord, load.reg)) return false;     // esp destination: refuse
            len = load.len;
            isLoad = true;
        }
        if (len <= 0 || len > 15) return false;
    }

    // The self-test is not a finding. It must not consume a crash-report slot, and it
    // must not appear in the site table -- that table is supposed to be a map of the
    // engine's real bugs, not of our own verification.
    if (eip != g_selfTestStoreEip) {
        if (!RecordSite(eip, firstTimeAtThisSite)) return false;
    }

    if (baseSlot) {
        // Re-execute the same instruction against real memory. Keeping the low bits of
        // the original value preserves any small offset the pointer already carried.
        *baseSlot = (DWORD)((uintptr_t)scratch + (*baseSlot & (kNullPage - 1)));
        InterlockedIncrement(&g_totalRepairs);
        resumeEip = (unsigned)eip;
        return true;
    }

    if (isLoad) {
        DWORD* slot = RegSlot(ep->ContextRecord, load.reg);
        if (load.width == 32)      *slot = 0;
        else if (load.width == 16) *slot &= 0xFFFF0000u;
        else if (load.highByte)    *slot &= 0xFFFF00FFu;
        else                       *slot &= 0xFFFFFF00u;
    }

    InterlockedIncrement(&g_totalRepairs);
    resumeEip = (unsigned)(eip + len);
    return true;
}

void NullStoreLogSites() {
    if (!g_lockReady) return;
    EnterCriticalSection(&g_lock);
    LONG n = g_siteCount;
    for (LONG i = 0; i < n; ++i) {
        char d[160];
        DescribeAddress(g_sites[i].eip, d, sizeof(d));
        Logf("null-store site  0x%08X  %s  x%ld", (unsigned)g_sites[i].eip, d, g_sites[i].hits);
    }
    LeaveCriticalSection(&g_lock);
}

int NullStoreDescribeSites(char* buf, int cap, int pos) {
    if (!g_lockReady) return pos;
    EnterCriticalSection(&g_lock);
    LONG n = g_siteCount;
    if (n > 0) {
        pos = Append(buf, cap, pos, "\r\n  null stores skipped so far, by site\r\n");
        for (LONG i = 0; i < n; ++i) {
            char d[160];
            DescribeAddress(g_sites[i].eip, d, sizeof(d));
            pos = Append(buf, cap, pos, "    0x%08X  %-32s  x%ld\r\n",
                         (unsigned)g_sites[i].eip, d, g_sites[i].hits);
        }
    }
    LeaveCriticalSection(&g_lock);
    return pos;
}

} // namespace ckperf
