// arrayguard.cpp — bounds check for the out-of-range grid-cell registration at 0x004AA5C9.
//
// WHAT CRASHES (two captured faults, both "hero-led formation of 1000+ units ordered to
// attack", both in the same function):
//
//   2026-08-22 15:22:14, pid 23712, unguarded:
//     eip 0x004AA5C9   cmp dword ptr [edx], 0     edx = 0x0094B600, region RESERVE
//   2026-08-22 18:39:11, pid 20772, with the first version of this guard installed:
//     eip 0x004AA5E1   mov dword ptr [eax+ecx*8], ebx
//
// The second fault is the important one, and it is what this file now exists to fix.
// The first version of this guard checked whether each slot was READABLE (SafeRead, the
// VirtualQuery-based probe) and treated an unreadable slot as "not free". That is the
// wrong predicate. A base pointer hundreds of cells past the end of the array can land
// on memory that is committed and perfectly readable but not writable -- so the guard
// approved the base, the scan found a "free" slot in memory that was never part of the
// array, and the crash simply moved four instructions down the road from the read at
// 0x004AA5C9 to the write at 0x004AA5E1. The telemetry proves the guard ran and let it
// through anyway: "arrayguard: suppressed 1 unreadable grid-slot reads so far".
//
// Worse, readability is not merely insufficient -- it is dangerous. Had that page been
// writable, the guard would have converted a visible crash into a silent write into
// somebody else's memory. Page protection is not the question. Whether the address is
// inside the array is the question.
//
// WHERE THE ARRAY ACTUALLY ENDS (this is what changed; it is now known, not guessed):
//
//   The object's initializer at 0x004AA010 clears the array in one call --
//     0x004AA02A: push 0x88200            ; count
//     0x004AA02F: lea  ecx, [esi + 0x18]  ; dst
//     0x004AA032: push 0xff               ; fill
//     0x004AA043: push ecx
//     0x004AA04A: call 0x41E880           ; memset(esi + 0x18, 0xFF, 0x88200)
//   -- so the grid is exactly [esi+0x18, esi+0x18+0x88200), pre-filled with 0xFF. That
//   fill is also why the scan below tests `dword < 0`: 0xFFFFFFFF is how a free slot is
//   spelled. Three independent things agree with that extent: the next field on the
//   object sits at +0x88218 and 0x18 + 0x88200 = 0x88218 exactly; 0x88200 / 32 bytes per
//   cell = 17424 = 132 x 132; and 132 is the row stride in the engine's own offset
//   formula, (delta_y + delta_x * 132) * 32.
//
//   The 15:22 fault had esi = 0x00806568 and base 0x0094B600 -- offset 0x145080, which
//   is cell 41604, i.e. delta_x = 315 in a grid that is 132 wide. Not a marginal
//   overrun; roughly 2.4x past the end. Every byte it touched belonged to something else.
//
// WHAT THIS DOES: rejects any base that is not inside [esi+0x18, esi+0x18+0x88200)
// with all four of its 8-byte slots fitting, and takes the function's own silent
// give-up exit instead. That exit is not invented for this patch -- it is what the
// function already does whenever all four slots are occupied: `ret 8`, nothing written,
// nothing reported to the caller. So a rejected registration is indistinguishable, from
// the caller's point of view, from a cell that happened to be full, which is a case the
// engine already handles on every ordinary frame.
//
// A welcome side effect: when (X>>4, Y>>4) fails the rectangle test at 0x004AA585 the
// engine does `xor eax, eax; jmp 0x004AA5C5` and walks into this same loop with a base
// of 0, i.e. the shipped code dereferences address 0 on its own out-of-bounds path.
// An unsigned range test rejects that too, so that path now reaches the give-up exit
// instead of relying on nullstore.cpp to repair a null read after the fact.
//
// This does NOT explain why an attack order produces a coordinate 315 cells outside the
// grid in the first place. That is still open -- see "下一步" in
// docs/reverse-engineering-notes.md. What it does is make the engine's missing bounds
// check present, at the one instruction that needs it.
//
// Every rejection is counted and reported, same discipline as guard.cpp's null
// write-back guard: if the game stops crashing and the counter stays at zero, this
// patch is not what fixed it.
//
// This patch lives entirely in the injected process. No game file is touched, so there
// is nothing to reverse -- not injecting is the "off" state.

#include "ckperf.h"
#include <stdint.h>

namespace ckperf {

volatile LONG g_suppressedArrayReads = 0;

namespace {

// The engine has no relocation directory, so these are absolute and stable. Original
// bytes are verified before anything is written; a mismatch means this is not the
// build the analysis was done on, and the patch is refused outright.
constexpr uintptr_t kLoopStart    = 0x004AA5C5; // xor ecx, ecx
constexpr uintptr_t kFoundExit    = 0x004AA5E1; // "found a free slot" -- expects ecx = slot index, eax = base
constexpr uintptr_t kNotFoundExit = 0x004AA5D7; // shared epilogue -- "no free slot", identical to the original

// Array extent, from the initializer's own memset (see the header comment).
constexpr uint32_t kArrayFirst = 0x18;      // first byte of the grid, relative to this
constexpr uint32_t kArrayBytes = 0x88200;   // 17424 cells of 32 bytes
constexpr uint32_t kCellBytes  = 0x20;      // 4 slots x 8 bytes, all read by the scan
constexpr uint32_t kMaxOffset  = kArrayBytes - kCellBytes;  // 0x881E0

const unsigned char kOriginal[] = {
    0x33,0xC9,                   // xor ecx, ecx
    0x8B,0xD0,                   // mov edx, eax
    0x83,0x3A,0x00,               // cmp dword ptr [edx], 0     <-- the 15:22 fault
    0x7C,0x13,                   // jl  0x4aa5e1
    0x41,                         // inc ecx
    0x83,0xC2,0x08,               // add edx, 8
    0x83,0xF9,0x04,               // cmp ecx, 4
    0x7C,0xF2                    // jl  0x4aa5c9
};
constexpr size_t kOriginalLen = sizeof(kOriginal);   // 18 bytes

// MSVC's inline assembler treats a C++ name as a MEMORY operand, so `mov edx, kFoundExit`
// would load *from* the constant rather than load its value -- which is precisely the
// class of mistake this file has already paid for once. The exit addresses below are
// therefore written as literal immediates, and these assertions are what keep the
// literals and the named constants from ever drifting apart.
static_assert(kFoundExit    == 0x004AA5E1, "cave jumps to a literal; keep it in step");
static_assert(kNotFoundExit == 0x004AA5D7, "cave jumps to a literal; keep it in step");
static_assert(kMaxOffset    == 0x881E0,    "cave compares against a literal; keep it in step");
static_assert(kArrayFirst   == 0x18,       "cave subtracts a literal; keep it in step");

// The cave. Register discipline, all of it dictated by what the two exits expect:
//
//   ebx (X), edi (Y), esi (this) are live past both exits and are never touched.
//   eax must still be the cell base at kFoundExit, and ecx must be the slot index there.
//   ecx and edx are scratch in the original code at this point -- the original walks the
//   array in edx and reloads it at 0x004AA5E8 before any use -- so they are free here.
//   ebp is left alone rather than borrowed as scratch; nothing here needs a third
//   register badly enough to depend on an argument about whether ebp is dead.
//   Nothing is pushed, so the stack at either exit is byte-identical to the stack on
//   entry -- which matters, because kNotFoundExit is a pop/pop/pop/pop/add esp/ret 8.
__declspec(naked) void Cave() {
    __asm {
        // offset = eax - this - 0x18. Unsigned throughout: a base BELOW the array wraps
        // to a huge value and is rejected by the same single comparison, and so is the
        // engine's own eax = 0 out-of-rectangle path.
        mov     ecx, eax
        sub     ecx, esi
        sub     ecx, 018h
        cmp     ecx, 0881E0h            // last offset where all four slots still fit
        ja      Reject
        // The engine only ever computes cell * 32, so a base that is not 32-byte aligned
        // within the array means the address did not come from that formula at all.
        test    cl, 01Fh
        jnz     Reject

        // In range: the original scan, unchanged. This memory is inside the same
        // allocation the initializer memset, so an ordinary read is correct here and a
        // page probe per slot would be pure cost.
        xor     ecx, ecx
        mov     edx, eax
    Scan:
        cmp     dword ptr [edx], 0
        jl      Found
        inc     ecx
        add     edx, 8
        cmp     ecx, 4
        jl      Scan

        mov     edx, 004AA5D7h          // all four slots occupied -- the original give-up
        jmp     edx
    Found:
        mov     edx, 004AA5E1h          // eax = base, ecx = slot index, as the writer expects
        jmp     edx
    Reject:
        lock inc dword ptr [g_suppressedArrayReads]
        mov     edx, 004AA5D7h          // out of range -- same give-up the engine already uses
        jmp     edx
    }
}

bool VerifyOriginal(uintptr_t addr, const unsigned char* expected, size_t len) {
    unsigned char actual[32];
    if (len > sizeof(actual)) return false;
    if (!SafeRead(addr, actual, len)) return false;
    return memcmp(actual, expected, len) == 0;
}

// Writes "jmp rel32" at addr, padding the rest of the replaced range with int3 so a
// stray jump into the middle of it traps loudly instead of executing garbage. Same
// technique as guard.cpp's WriteJump; duplicated rather than shared so each crash-fix
// module stays independently reviewable, matching how nullstore.cpp does not import
// guard.cpp's helpers either.
bool WriteJump(uintptr_t addr, const void* target, size_t rangeLen) {
    unsigned char patch[32];
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

void ArrayGuardInstall() {
    if (!g_cfg.arrayGuard) {
        Logf("grid-cell bounds check: disabled by configuration (arrayguard=0).");
        return;
    }

    if (!VerifyOriginal(kLoopStart, kOriginal, kOriginalLen)) {
        Logf("grid-cell bounds check: REFUSED -- bytes at 0x%08X are not the expected "
             "sequence. This is not the build the analysis was done on; nothing patched.",
             (unsigned)kLoopStart);
        return;
    }

    if (!WriteJump(kLoopStart, (void*)&Cave, kOriginalLen)) {
        Logf("grid-cell bounds check: could not patch 0x%08X (%u); nothing changed.",
             (unsigned)kLoopStart, GetLastError());
        return;
    }

    Logf("grid-cell bounds check installed. 0x%08X now rejects any cell base outside "
         "[this+0x%X, this+0x%X) -- the extent the engine's own initializer memsets -- "
         "and takes the function's existing \"no free slot\" exit instead of writing "
         "outside the grid.",
         (unsigned)kLoopStart, (unsigned)kArrayFirst,
         (unsigned)(kArrayFirst + kArrayBytes));
}

long ArrayGuardSuppressedCount() {
    return InterlockedCompareExchange(&g_suppressedArrayReads, 0, 0);
}

} // namespace ckperf
