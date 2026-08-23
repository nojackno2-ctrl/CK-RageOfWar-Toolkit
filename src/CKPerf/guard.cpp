// guard.cpp — runtime null-guard for the script write-back crash.
//
// WHAT CRASHED (first captured fault, 2026-08-19 20:53:18, pid 35668):
//
//     eip 0x0068FDA6   mov dword ptr [ecx], eax      with ecx = 0
//     ACCESS_VIOLATION writing to 0x00000000
//
// SUBSEQUENT OBSERVATION (2026-08-22 pid 35620):
//
//     Six more access violations were captured at two additional exit sites with the
//     exact same shape:
//       - 0x0068F91A, 0x0068F925, 0x0068F931 (function exit at 0x0068F912)
//       - 0x00690315, 0x00690320, 0x00690328 (function exit at 0x00690309)
//     This proved that guarding only the first two sites was hardcoded to specific
//     observed sites rather than protecting all identical epilogues.
//     Disassembly and branch analysis using Capstone linear scanning (tools in tools/perf/)
//     verified that both 40-byte epilogues can only be entered at their first instruction
//     (0x0068F912 has 8 branches targeting it; 0x00690309 has 1 branch targeting it; no
//     branches land inside either 40-byte range). Replacing each with jmp rel32 + int3
//     padding is therefore completely safe.
//
// WHY. The function at 0x0068F9E0 is a script-VM command implementation. It pops three
// operands off the VM stack and resolves each one to a real object through 0x00481A20.
// Each resolution is null-checked -- and when it FAILS, the engine deliberately records
// a null pointer and carries on:
//
//     0068FA0D  call 0x481a20            ; resolve reference #1
//     0068FA17  cmp  eax, ebp            ; ebp is zero here
//     0068FA19  je   0x68fa27
//     0068FA21  mov  [esp+0x20], eax     ; resolved   -> real pointer
//     0068FA27  mov  [esp+0x20], ebp     ; unresolved -> NULL, and we keep going
//
// The same shape repeats for reference #2 into [esp+0x1C] and #3 into [esp+0x18].
// Then BOTH exit paths dereference all three without checking:
//
//     0068FACB..0068FAE6   early exit: writes 0 through all three pointers
//     0068FD9E..0068FDC5   normal exit: writes the computed results through all three
//
// So any script that writes back into a reference which has since become invalid --
// most obviously a unit that died between the script reading it and writing to it --
// takes the process down. That is a plain missing null check, and it explains why the
// crash gets more likely the more units are alive: more units means more deaths per
// tick, and more chances for a captured reference to go stale mid-script.
//
// WHAT THIS DOES. All four exit sequences are re-implemented in a code cave with the twelve
// stores guarded, and every suppressed store is counted. The counter is the experiment:
// if the game stops crashing and the counter is zero, this patch is not what fixed it.
//
// A skipped store means one script variable does not receive its update. That is a far
// smaller problem than terminating the process, but it is a behaviour change, which is
// why it is logged, counted, and switchable (opts guard=0).
//
// This patch lives entirely in the injected process. No game file is touched, so there
// is nothing to reverse -- not injecting is the "off" state.

#include "ckperf.h"
#include <stdio.h>

namespace ckperf {

// Counter lives in this DLL, and the cave increments it through an absolute address.
volatile LONG g_suppressedNullStores = 0;

namespace {

// The engine has no relocation directory, so these are absolute and stable. The
// original bytes are verified before anything is written; a mismatch means we are not
// looking at the build this analysis was done on, and the patch is refused outright.
constexpr uintptr_t kEarlyExit      = 0x0068FACB;
constexpr uintptr_t kNormalExit     = 0x0068FD9E;
constexpr uintptr_t kWriteBackExitC = 0x0068F912;
constexpr uintptr_t kWriteBackExitD = 0x00690309;

const unsigned char kEarlyOriginal[] = {
    0x8B,0x44,0x24,0x18,              // mov eax, [esp+0x18]
    0x8B,0x4C,0x24,0x1C,              // mov ecx, [esp+0x1c]
    0x8B,0x54,0x24,0x20,              // mov edx, [esp+0x20]
    0x89,0x28,                        // mov [eax], ebp
    0x5F,                             // pop edi
    0x89,0x29,                        // mov [ecx], ebp
    0x5E,                             // pop esi
    0x89,0x2A,                        // mov [edx], ebp
    0x5D,                             // pop ebp
    0x33,0xC0,                        // xor eax, eax
    0x5B,                             // pop ebx
    0x83,0xC4,0x64,                   // add esp, 0x64
    0xC3                              // ret
};

const unsigned char kNormalOriginal[] = {
    0x8B,0x44,0x24,0x38,              // mov eax, [esp+0x38]
    0x8B,0x4C,0x24,0x18,              // mov ecx, [esp+0x18]
    0x89,0x01,                        // mov [ecx], eax        <-- the observed fault
    0x8B,0x54,0x24,0x3C,              // mov edx, [esp+0x3c]
    0x8B,0x44,0x24,0x1C,              // mov eax, [esp+0x1c]
    0x5F,                             // pop edi
    0x89,0x10,                        // mov [eax], edx
    0x8B,0x4C,0x24,0x3C,              // mov ecx, [esp+0x3c]
    0x8B,0x54,0x24,0x1C,              // mov edx, [esp+0x1c]
    0x5E,                             // pop esi
    0x5D,                             // pop ebp
    0x89,0x0A,                        // mov [edx], ecx
    0x33,0xC0,                        // xor eax, eax
    0x5B,                             // pop ebx
    0x83,0xC4,0x64,                   // add esp, 0x64
    0xC3                              // ret
};

const unsigned char kExitCOriginal[] = {
    0x8B,0x4C,0x24,0x30,              // 0068F912  mov ecx, [esp+0x30]
    0x8B,0x54,0x24,0x60,              // 0068F916  mov edx, [esp+0x60]
    0x89,0x0A,                        // 0068F91A  mov [edx], ecx      <-- observed fault
    0x8B,0x44,0x24,0x34,              // 0068F91C  mov eax, [esp+0x34]
    0x8B,0x4C,0x24,0x10,              // 0068F920  mov ecx, [esp+0x10]
    0x5F,                             // 0068F924  pop edi
    0x89,0x01,                        // 0068F925  mov [ecx], eax      <-- observed fault
    0x8B,0x44,0x24,0x10,              // 0068F927  mov eax, [esp+0x10]
    0x8B,0x54,0x24,0x34,              // 0068F92B  mov edx, [esp+0x34]
    0x5E,                             // 0068F92F  pop esi
    0x5D,                             // 0068F930  pop ebp
    0x89,0x10,                        // 0068F931  mov [eax], edx      <-- observed fault
    0x33,0xC0,                        // 0068F933  xor eax, eax
    0x5B,                             // 0068F935  pop ebx
    0x83,0xC4,0x4C,                   // 0068F936  add esp, 0x4c
    0xC3                              // 0068F939  ret
};

const unsigned char kExitDOriginal[] = {
    0x8B,0x54,0x24,0x30,              // 00690309  mov edx, [esp+0x30]
    0x8B,0x44,0x24,0x10,              // 0069030D  mov eax, [esp+0x10]
    0x8B,0x4C,0x24,0x34,              // 00690311  mov ecx, [esp+0x34]
    0x89,0x10,                        // 00690315  mov [eax], edx      <-- observed fault
    0x8B,0x54,0x24,0x14,              // 00690317  mov edx, [esp+0x14]
    0x8B,0x44,0x24,0x38,              // 0069031B  mov eax, [esp+0x38]
    0x5F,                             // 0069031F  pop edi
    0x89,0x0A,                        // 00690320  mov [edx], ecx      <-- observed fault
    0x8B,0x4C,0x24,0x14,              // 00690322  mov ecx, [esp+0x14]
    0x5E,                             // 00690326  pop esi
    0x5D,                             // 00690327  pop ebp
    0x89,0x01,                        // 00690328  mov [ecx], eax      <-- observed fault
    0x33,0xC0,                        // 0069032A  xor eax, eax
    0x5B,                             // 0069032C  pop ebx
    0x83,0xC4,0x6C,                   // 0069032D  add esp, 0x6c
    0xC3                              // 00690330  ret
};

// ------------------------------------------------------------------ cave assembly

struct CaveWriter {
    unsigned char* p;
    unsigned char* begin;

    void Byte(unsigned char b) { *p++ = b; }
    void Bytes(const unsigned char* b, size_t n) { memcpy(p, b, n); p += n; }
    void Dword(unsigned int v) { memcpy(p, &v, 4); p += 4; }

    // test <reg>,<reg> / jnz over / inc [counter] / jmp over the store / <store>
    //
    // Encoded by hand because the whole point is to reproduce the original instruction
    // stream byte for byte apart from the guard: same registers, same order, same esp
    // at every memory access. Anything cleverer risks changing what the function does.
    void GuardedStore(unsigned char testModRm, const unsigned char* store, size_t storeLen) {
        Byte(0x85); Byte(testModRm);          // test reg, reg
        Byte(0x75); Byte(0x08);               // jnz +8  -> straight to the store
        Byte(0xFF); Byte(0x05);               // inc dword ptr [imm32]
        Dword((unsigned int)(uintptr_t)&g_suppressedNullStores);
        Byte(0xEB); Byte((unsigned char)storeLen);  // jmp over the store
        Bytes(store, storeLen);
    }
};

// ModR/M byte for "test r32, r32" with both operands the same register.
constexpr unsigned char kTestEax = 0xC0;
constexpr unsigned char kTestEcx = 0xC9;
constexpr unsigned char kTestEdx = 0xD2;

unsigned char* g_cave = nullptr;

bool VerifyOriginal(uintptr_t addr, const unsigned char* expected, size_t len) {
    unsigned char actual[64];
    if (len > sizeof(actual)) return false;
    if (!SafeRead(addr, actual, len)) return false;
    return memcmp(actual, expected, len) == 0;
}

// Writes "jmp rel32" at addr, padding the rest of the replaced range with int3 so a
// stray jump into the middle of it traps loudly instead of executing garbage.
bool WriteJump(uintptr_t addr, const void* target, size_t rangeLen) {
    unsigned char patch[64];
    if (rangeLen > sizeof(patch) || rangeLen < 5) return false;
    memset(patch, 0xCC, rangeLen);
    patch[0] = 0xE9;
    int rel = (int)((uintptr_t)target - (addr + 5));
    memcpy(patch + 1, &rel, 4);

    DWORD old = 0;
    if (!VirtualProtect((void*)addr, rangeLen, PAGE_EXECUTE_READWRITE, &old)) return false;
    memcpy((void*)addr, patch, rangeLen);
    VirtualProtect((void*)addr, rangeLen, old, &old);
    FlushInstructionCache(GetCurrentProcess(), (void*)addr, rangeLen);
    return true;
}

} // namespace

void GuardInstall() {
    if (!g_cfg.nullGuard) {
        Logf("script write-back guard: disabled by configuration (guard=0).");
        return;
    }

    if (!VerifyOriginal(kEarlyExit, kEarlyOriginal, sizeof(kEarlyOriginal))) {
        Logf("script write-back guard: REFUSED -- bytes at 0x%08X are not the expected "
             "sequence. This is not the build the analysis was done on; nothing patched.",
             (unsigned)kEarlyExit);
        return;
    }
    if (!VerifyOriginal(kNormalExit, kNormalOriginal, sizeof(kNormalOriginal))) {
        Logf("script write-back guard: REFUSED -- bytes at 0x%08X are not the expected "
             "sequence. This is not the build the analysis was done on; nothing patched.",
             (unsigned)kNormalExit);
        return;
    }
    if (!VerifyOriginal(kWriteBackExitC, kExitCOriginal, sizeof(kExitCOriginal))) {
        Logf("script write-back guard: REFUSED -- bytes at 0x%08X are not the expected "
             "sequence. This is not the build the analysis was done on; nothing patched.",
             (unsigned)kWriteBackExitC);
        return;
    }
    if (!VerifyOriginal(kWriteBackExitD, kExitDOriginal, sizeof(kExitDOriginal))) {
        Logf("script write-back guard: REFUSED -- bytes at 0x%08X are not the expected "
             "sequence. This is not the build the analysis was done on; nothing patched.",
             (unsigned)kWriteBackExitD);
        return;
    }

    g_cave = (unsigned char*)VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!g_cave) {
        Logf("script write-back guard: VirtualAlloc for the code cave failed (%u).", GetLastError());
        return;
    }

    // ---- cave for the early exit: three stores of zero (ebp) through the pointers ----
    unsigned char* caveEarly = g_cave;
    unsigned char* caveNormal = nullptr;
    unsigned char* caveC = nullptr;
    unsigned char* caveD = nullptr;
    {
        CaveWriter w{ caveEarly, caveEarly };
        const unsigned char loadAll[] = { 0x8B,0x44,0x24,0x18, 0x8B,0x4C,0x24,0x1C, 0x8B,0x54,0x24,0x20 };
        const unsigned char st1[] = { 0x89,0x28 };   // mov [eax], ebp
        const unsigned char st2[] = { 0x89,0x29 };   // mov [ecx], ebp
        const unsigned char st3[] = { 0x89,0x2A };   // mov [edx], ebp
        const unsigned char popEdi[] = { 0x5F };
        const unsigned char popEsi[] = { 0x5E };
        const unsigned char tail[]   = { 0x5D, 0x33,0xC0, 0x5B, 0x83,0xC4,0x64, 0xC3 };

        w.Bytes(loadAll, sizeof(loadAll));
        w.GuardedStore(kTestEax, st1, sizeof(st1));
        w.Bytes(popEdi, sizeof(popEdi));
        w.GuardedStore(kTestEcx, st2, sizeof(st2));
        w.Bytes(popEsi, sizeof(popEsi));
        w.GuardedStore(kTestEdx, st3, sizeof(st3));
        w.Bytes(tail, sizeof(tail));
        caveNormal = w.p;
    }

    // ---- cave for the normal exit: the three computed write-backs ----
    {
        CaveWriter w{ caveNormal, caveNormal };
        const unsigned char load1[] = { 0x8B,0x44,0x24,0x38, 0x8B,0x4C,0x24,0x18 };
        const unsigned char st1[]   = { 0x89,0x01 };                       // mov [ecx], eax
        const unsigned char load2[] = { 0x8B,0x54,0x24,0x3C, 0x8B,0x44,0x24,0x1C, 0x5F };
        const unsigned char st2[]   = { 0x89,0x10 };                       // mov [eax], edx
        const unsigned char load3[] = { 0x8B,0x4C,0x24,0x3C, 0x8B,0x54,0x24,0x1C, 0x5E, 0x5D };
        const unsigned char st3[]   = { 0x89,0x0A };                       // mov [edx], ecx
        const unsigned char tail[]  = { 0x33,0xC0, 0x5B, 0x83,0xC4,0x64, 0xC3 };

        w.Bytes(load1, sizeof(load1));
        w.GuardedStore(kTestEcx, st1, sizeof(st1));
        w.Bytes(load2, sizeof(load2));
        w.GuardedStore(kTestEax, st2, sizeof(st2));
        w.Bytes(load3, sizeof(load3));
        w.GuardedStore(kTestEdx, st3, sizeof(st3));
        w.Bytes(tail, sizeof(tail));
        caveC = w.p;
    }

    // ---- cave for exit C (0x0068F912): three computed write-backs ----
    {
        CaveWriter w{ caveC, caveC };
        const unsigned char load1[] = { 0x8B,0x4C,0x24,0x30, 0x8B,0x54,0x24,0x60 };
        const unsigned char st1[]   = { 0x89,0x0A };                       // mov [edx], ecx
        const unsigned char load2[] = { 0x8B,0x44,0x24,0x34, 0x8B,0x4C,0x24,0x10, 0x5F };
        const unsigned char st2[]   = { 0x89,0x01 };                       // mov [ecx], eax
        const unsigned char load3[] = { 0x8B,0x44,0x24,0x10, 0x8B,0x54,0x24,0x34, 0x5E, 0x5D };
        const unsigned char st3[]   = { 0x89,0x10 };                       // mov [eax], edx
        const unsigned char tail[]  = { 0x33,0xC0, 0x5B, 0x83,0xC4,0x4C, 0xC3 };

        w.Bytes(load1, sizeof(load1));
        w.GuardedStore(kTestEdx, st1, sizeof(st1));
        w.Bytes(load2, sizeof(load2));
        w.GuardedStore(kTestEcx, st2, sizeof(st2));
        w.Bytes(load3, sizeof(load3));
        w.GuardedStore(kTestEax, st3, sizeof(st3));
        w.Bytes(tail, sizeof(tail));
        caveD = w.p;
    }

    // ---- cave for exit D (0x00690309): three computed write-backs ----
    {
        CaveWriter w{ caveD, caveD };
        const unsigned char load1[] = { 0x8B,0x54,0x24,0x30, 0x8B,0x44,0x24,0x10, 0x8B,0x4C,0x24,0x34 };
        const unsigned char st1[]   = { 0x89,0x10 };                       // mov [eax], edx
        const unsigned char load2[] = { 0x8B,0x54,0x24,0x14, 0x8B,0x44,0x24,0x38, 0x5F };
        const unsigned char st2[]   = { 0x89,0x0A };                       // mov [edx], ecx
        const unsigned char load3[] = { 0x8B,0x4C,0x24,0x14, 0x5E, 0x5D };
        const unsigned char st3[]   = { 0x89,0x01 };                       // mov [ecx], eax
        const unsigned char tail[]  = { 0x33,0xC0, 0x5B, 0x83,0xC4,0x6C, 0xC3 };

        w.Bytes(load1, sizeof(load1));
        w.GuardedStore(kTestEax, st1, sizeof(st1));
        w.Bytes(load2, sizeof(load2));
        w.GuardedStore(kTestEdx, st2, sizeof(st2));
        w.Bytes(load3, sizeof(load3));
        w.GuardedStore(kTestEcx, st3, sizeof(st3));
        w.Bytes(tail, sizeof(tail));
    }

    if (!WriteJump(kEarlyExit, caveEarly, sizeof(kEarlyOriginal))) {
        Logf("script write-back guard: could not patch 0x%08X (%u); nothing changed.",
             (unsigned)kEarlyExit, GetLastError());
        return;
    }
    if (!WriteJump(kNormalExit, caveNormal, sizeof(kNormalOriginal))) {
        Logf("script write-back guard: could not patch 0x%08X (%u). The first patch is "
             "already live and is harmless on its own, but the crashing path is NOT covered.",
             (unsigned)kNormalExit, GetLastError());
        return;
    }
    if (!WriteJump(kWriteBackExitC, caveC, sizeof(kExitCOriginal))) {
        Logf("script write-back guard: could not patch 0x%08X (%u). Prior patches are "
             "already live, but exit C is NOT covered.",
             (unsigned)kWriteBackExitC, GetLastError());
        return;
    }
    if (!WriteJump(kWriteBackExitD, caveD, sizeof(kExitDOriginal))) {
        Logf("script write-back guard: could not patch 0x%08X (%u). Prior patches are "
             "already live, but exit D is NOT covered.",
             (unsigned)kWriteBackExitD, GetLastError());
        return;
    }

    Logf("script write-back guard installed. 0x%08X, 0x%08X, 0x%08X, 0x%08X now redirect to a cave at %p "
         "that null-checks all twelve write-backs. Suppressed stores are counted and reported.",
         (unsigned)kEarlyExit, (unsigned)kNormalExit, (unsigned)kWriteBackExitC, (unsigned)kWriteBackExitD, g_cave);
}

LONG GuardSuppressedCount() {
    return InterlockedCompareExchange(&g_suppressedNullStores, 0, 0);
}

} // namespace ckperf
