# AI_HANDOFF.md

## Project State
- **Status**: Stable and shipped. Performance optimisation + 16-bit compatibility fix + **1920x1080 HD support**, exposed identically through the Win32 GUI, the interactive text menu, and the CLI. Release|Win32 builds with 0 warnings / 0 errors.
- **Last Updated**: 2026-08-18
- **Current Milestone (2026-08-18)**: **English Localization Support (加入英文版)** + HD 1080p + 16-bit Compatibility & Optimization.
  1. **Full bilingual i18n support in CKPatcher Win32 GUI**:
     - Added `i18n.h` and `i18n.cpp` supporting runtime switching between **繁體中文 (Traditional Chinese)** and **English**.
     - Auto-detects system UI locale (non-Chinese defaults to English, Chinese defaults to Traditional Chinese).
     - UI dropdown allows on-the-fly switching with instant UI text re-rendering across all 4 tabs, status readouts, log messages, and modal dialogs.
     - Saved language preference is persisted in `ckpatcher.cfg` (`lang=en` / `lang=zh_tw`).
  2. **Bilingual Documentation**:
     - `README.md` and `CKPatcher/README.md` consolidated into single files containing both complete Traditional Chinese and English documentation with quick anchor navigation.
  3. **Release v1.0.0 Published to GitHub**:
     - GitHub Release `v1.0.0` published with standalone binary assets (`CKPatcher.exe`, `CKPatcher-v1.0.0-win32.zip`, `CKPatcher-v1.0.0-x64.zip`).
  4. **Verified Build**:
     - MSVC C++17 compiles cleanly on Win32 and x64 with 0 warnings / 0 errors.

- **Previous Milestone (2026-08-18)**: The HD track is **frozen at 1920x1080 by the user's decision** — no further resolution bisection. Three things landed on top of the 2026-08-17 work:
  1. All of it was committed (`30330e0`), together with a `.gitignore` that keeps the 597 MB third-party mod archive and the extra backup snapshots out of the repo.
  2. **`ckpatcher.cfg` now records the HD settings** (`hires`, `launcherfix`, `addres`), so a Steam update no longer silently drops them — `--reapply` and the GUI's 「一鍵套回」 restore the exe patch, the launcher patch and the `data.pak` resolution list in that order. Verified idempotent: `--hd` followed by `--reapply` leaves `Celtic kings.exe`, `Celtic kings Launcher.exe` and `data.pak` byte-identical.
  3. **The GUI reached parity with the CLI.** A new 「高解析度 (HD)」 tab (index 1, so the profiler and backup tabs shifted to 2 and 3) carries a status readout, a one-click 1920x1080 preset, and an advanced block that still accepts an arbitrary `WxH` and an arbitrary table capacity — the expandability is deliberately kept even though the shipped configuration is conservative. A new `--hd` CLI flag and an `[8] 高解析度 (HD) 設定` submenu do the same from the terminal.
  4. **Fixed: the 1920x1080 one-click preset was not self-contained.** After the user restored the whole game
     to vanilla, pressing 一鍵套用 1920x1080 left the resolution not taking effect. `doHdPreset()` /
     `IDC_HD_PRESET_BTN` applied only ZoomMap tables + launcher fix + `data.pak` + `vxSettings.ini`; they never
     applied LAA or the `SetVideoMode` fix. On a previously-patched exe that was invisible, because an earlier
     session had already stubbed `SetVideoMode`. On a pristine exe the engine still runs the real 16bpp
     `ChangeDisplaySettingsA` for the selected mode, WDDM rejects it, and the resolution silently never
     applies. Both preset paths now run `laa -> videofix -> hires -> launcherfix -> addres -> writeSetting`
     (the same order as `doReapply`) and record `laa` / `videofix` in `ckpatcher.cfg`. **Any future one-click
     preset must cover the full validated configuration table below, not just the HD-specific patches.**
  5. **Fixed: `vxSettings.ini` `Resolution=` is a 0-based position, not the `Res<N>` number.** This was the
     actual cause of the black main menu (audio playing, no picture) at 1920x1080 — every write and readback
     in the tool passed `Resolution::index` straight through, so selecting the appended `Res5` wrote
     `Resolution=5`, one past the end of a five-entry list. Measured in-game: with `Res1..Res5` listed,
     `Resolution=4` renders 1920x1080 and `Resolution=3` renders `Res4 = 1600x1200`. The stock value
     `Resolution=3` selecting `Res4 = 1600x1200` independently corroborates it — that is exactly the mode the
     unpatched launcher forces the desktop to. `doHdPreset()`, `IDC_HD_PRESET_BTN`, `--status`, `--list-res`
     and the GUI status line all convert now; `--list-res` prints the `Resolution=` value per row and both
     status paths flag an out-of-range value explicitly instead of silently showing nothing selected.
     **This also invalidates the earlier "validated end to end" claim for `Resolution=5`** — that state was
     only ever verified by file inspection, and the last confirmed in-game 1080p render (`screen00.bmp`,
     2026-08-17 16:39) predates trimming `data.pak` back to four stock entries plus `Res5`.

     **Confirmed in-game 2026-08-18 by the user: with `Resolution=4`, both the main menu and actual gameplay
     render correctly at 1920x1080.** The shipped-configuration table below is now backed by a real play test,
     not file inspection. Bisection that got there, for the record: `Resolution=1` and `Resolution=3` both
     rendered a menu (so the failure was resolution-specific, not a missing game file), and the gameplay
     screenshot at `Resolution=3` came out 16:9 rather than the 4:3 that `Res4 = 1600x1200` would imply — that
     mismatch is what exposed the off-by-one.
  6. **Fixed: the engine rewrote `vxSettings.ini`'s `Resolution` to 0 on every exit.** Found while testing the
     HUD artifact: after a session the key was back to `0`, so 1920x1080 never survived a restart and every
     patch above was effectively undone each time the game was played. Initially suspected to be an Alt+F4
     teardown artifact; **measured on a clean in-game Quit and it happens there too**, so it is the normal
     save path, not abnormal termination. The settings writer at `0x00658F90` saves the key from a struct
     member at `[edi+0x34]` that is already 0 by then:

     ```
     0x00658FAB  8B 47 34        mov  eax, [edi+0x34]
     0x00658FAE  50              push eax
     0x00658FAF  68 C8 53 74 00  push 0x7453C8   ; "Resolution"
     0x00658FB4  68 4C 54 74 00  push 0x74544C   ; "Options"
     0x00658FB9  8B CE           mov  ecx, esi
     0x00658FBB  E8 C0 AC DB FF  call 0x00413C80
     ```

     `writeResolutionWriteback()` NOPs those 21 bytes, so that one key is never written back while every other
     `[Options]` key still saves. Registered in `exePristine()`. Folded into `--hd`, the GUI preset and
     `--reapply`; also `--keep-res on|off`. Trade-off: the in-game options menu can no longer change the
     resolution — acceptable, since it was writing the field that gets cleared anyway, and CKPatcher's picker
     is the only thing that gets the 0-based index right. **Confirmed in-game 2026-08-18**: after a play
     session `vxSettings.ini` was rewritten (other keys updated) with `Resolution=4` intact.

     Two config-honesty bugs were fixed alongside: `--optimal` and the GUI's 推薦最佳化 recorded `keepres=on`
     without applying the patch.
- **Deferred by the user**: the unit-count lag/crash investigation. The user's read is that it is simply too many units, and it is explicitly not being fixed this round.
- **Previous Milestone (2026-08-17)**: Analysed the `Imperivm1-HD-4-multi` mod (author: JosueCA) to recover the technique required for 1920x1080, restored the `hmmpak` `.pak` read/patch layer, and landed `--hires` / `--launcherfix` / `--add-res`. A same-session attempt at making the OS resolution switch *automatically* was tried and reverted — see "SetVideoMode Patch Rewrite". **Supported workflow remains: set the Windows desktop resolution yourself before launching, then pick the matching entry in-game.**
- **Previous Milestone (2026-08-16)**: Removed all resolution injection and launcher patching code at the user's request (see `Resolution Support` below for why the first attempt failed and what was actually missing).

## Project Structure
- `CKPatcher/`: C++17 Visual Studio 2022 project (`CKPatcher.sln` / `CKPatcher.vcxproj`), producing standalone `CKPatcher.exe`.
  - `src/gui.cpp` / `gui.h`: Modern Win32 GUI (zero external dependencies, Per-Monitor DPI v2, Common Controls v6, **4 tabs** — 效能與相容 / 高解析度 (HD) / 取樣分析 / 備份管理 — real-time logging, background profiling thread, LAA 4GB, 16-bit Video Mode Crash Fix, One-Click Optimal Preset, One-Click 1920x1080 Preset, bilingual runtime switcher).
  - `src/i18n.cpp` / `i18n.h`: **Bilingual localization layer** (Traditional Chinese & English) for all GUI labels, status formats, logs, and message boxes.
  - `src/config.cpp` / `config.h`: Centralized configuration storage (`ckpatcher.cfg`), parsing and serialization for `laa`, `videofix`, `fast`, `hires`, `resolution`, `keepres`, `launcherfix`, `launchermode`, `addres`, and `lang`. Missing keys fall back to defaults, so older cfg files still load.
  - `src/main.cpp`: Entry point (GUI only since 2026-08-18 — `wWinMain` detects the game folder and hands off to `gui::run`; all CLI flags and the interactive menu were removed).
  - `src/game.cpp` / `game.h`: Steam path detection, backup & restore mechanism for `Celtic kings.exe`, `vxSettings.ini`, etc.
  - `src/patches.cpp` / `patches.h`: LAA 4GB flag toggle, `SetVideoMode` compatibility patch (`0x006BE340`), `vxSettings.ini` optimization flags, and **`readResolutions()` / `addResolutions()`** — the `VXCONST.INI` `[Resolutions]` editor (ported from `tools/add_resolutions.py` on 2026-08-17).
  - `src/profile.cpp` / `profile.h`: Sampler profiler for `Celtic kings.exe` (non-intrusive EIP / thread CPU time tracking).
  - `src/io.cpp` / `io.h`: Win32 wide character path I/O and UTF-8 console output.
  - `src/hmmpak.cpp` / `hmmpak.h`: **HMMSYS PackFile (`.pak`) reader / patcher.** Restored 2026-08-17. Parses the uncompressed container used by `data.pak`, `local.pak`, `assets.pak`, `update.pak`, including the front-coded directory entries. `replace()` appends new content at the end and repoints the directory entry, leaving every other entry's offset untouched. `loadBytes()` is the in-memory entry point used by `patches.cpp`. Does **not** handle the `LZIS`/`LZSS` whole-file-compressed variants (`minimap.pak`, `randommap.pak`).
- `tools/`: Python 3 scripts providing CLI tools:
  - `patch_videomode.py`: Python CLI for patching `SetVideoMode` (`0x006BE340`) in `Celtic kings.exe`.
  - `large_address_aware.py`: Python CLI for flipping LAA bit in PE characteristics.
  - `hmmpak.py`: Python counterpart of the `.pak` reader (restored 2026-08-17).
  - `add_resolutions.py`: appends `Res%d_x/y` pairs to `VXCONST.INI` inside `data.pak` (restored 2026-08-17, verified working). `--list` / `--apply [WxH ...]` / `--restore`. Always re-patches from `backup/data.pak.orig`, so repeated runs are idempotent rather than stacking edits. Requires `py -3` (the repo's venv `python` shim is broken; use `py -3`).
  - *Not restored* (still deleted in the working tree, recoverable via `git checkout HEAD -- <path>`): `enum_display_modes.py`, `patch_launcher.py`.
- `backup/`: Backups of original pristine files (`Celtic kings.exe.orig`, `vxSettings.ini.orig`, etc.).
- `ckpatcher.cfg`: State persistence file recording desired settings for `--reapply`.
- `README.md`: Detailed reverse engineering notes, engine architecture analysis, and tool usage.

## Key Findings & 16-bit Video Mode Crash Root Cause Analysis
1. **Windows 10/11 16-Bit Display Mode Rejection**:
   - Modern Windows WDDM drivers reject setting hardware display modes to 16bpp (`ChangeDisplaySettingsA` with `dmBitsPerPel = 16` returns `-1` / `DISP_CHANGE_FAILED`).
2. **The Fatal Crash Trigger**:
   - When `SetVideoMode` (`0x006BE340`) fails, `0x006BFF90` returns `0xFFFF` (Failure).
   - `0x00657F2E` catches `0xFFFF` and jumps to error handler at `0x00657DB3` -> `0x00657DCC`.
   - At `0x00657DCC`, the error handling routine attempts to read `[0x8c2784] + 0xac8`.
   - Because `0x8c2784` (the Game Settings/Context pointer) has not been allocated yet at this startup phase, dereferencing `0x00000000 + 0xAC8` causes a fatal **ACCESS_VIOLATION (0xC0000005)**, crashing the game instantly.
3. **Why `SetDIBitsToDevice` Doesn't Need 16bpp Display Mode**:
   - The engine's software rasterizer generates RGB565 frames in memory and blits them to the window DC via `SetDIBitsToDevice` (`0x0044F536`).
   - `SetDIBitsToDevice` handles RGB565 bitmaps on 32-bit DCs natively and seamlessly in software.
   - **Current fix (as of 2026-08-17, see "SetVideoMode Patch Rewrite" below for the full story)**: patching
     `SetVideoMode` at file offset `0x002BE340` with `xor eax, eax; ret` (`31 C0 C3 90 90 90`) avoids the
     crash by skipping the whole function. This is deliberate: a surgical alternative that let the function
     actually call `ChangeDisplaySettingsA` was tried and reverted after it corrupted video playback and got
     the game stuck at 1024x768 as soon as real gameplay started. **This whole-function stub is once again
     the shipped patch — this is not a stale note, it is correct and intentional.** To play above 1024x768,
     set the Windows desktop to the target resolution yourself before launching, then pick the matching
     entry in-game; see the rewrite section for why.

## SetVideoMode Patch Rewrite + ensureBackup Fix (2026-08-17)

Testing the resolution list against the real install (`--add-res 1920x1080`) surfaced that the
`0x006BE340` stub, and a pre-existing bug in how `writeLargeAddressAware`/`writeVideoModePatch` manage
backups, were both wrong in ways that only show up once two exe patches are combined. Both are fixed now;
details below because the failure mode is subtle enough to reintroduce if this area gets touched again.

### What the user actually saw
Selecting `Res5` (1920x1080) in-game produced letterboxing (black bars top/bottom), and on exit
`vxSettings.ini`'s `Resolution` reverted from `5` back to `4` on its own. The user's own read — "launching
the game forcibly changes my desktop resolution" — was the right instinct and led directly to the fix,
even though the direction of the bug was the opposite of what it sounded like (see below).

### Root cause: SetVideoMode's own gate, not just the ChangeDisplaySettingsA call
Full disassembly of `0x006BE340` (the earlier notes only covered the first ~10 instructions) shows it
does **two** things, not one:
1. Enumerates the display's supported modes via `EnumDisplaySettingsA`, looking for one whose
   `dmPelsWidth`/`dmPelsHeight`/`dmDisplayFrequency` match the request **and** whose `dmBitsPerPel == 16`
   (hardcoded). If no enumerated mode satisfies all four, it bails out at `0x006BE422` **before ever
   calling `ChangeDisplaySettingsA`** — this is the `0xFFFF` that the previously-documented crash chain
   (`0x00657F2E` -> `0x00657DB3` -> `0x00657DCC` null deref) is reacting to.
2. Only if a match is found does it re-fetch that mode's real `DEVMODE` (`0x006BE431`) and call
   `ChangeDisplaySettingsA` (`0x006BE458`) with it.

Modern WDDM drivers do not enumerate 16bpp modes through `EnumDisplaySettingsA` at all, on **any**
resolution — so step 1 always fails on Windows 10/11, regardless of what the player picked. The first
version of this patch (a 6-byte stub at the function's entry, `31 C0 C3 90 90 90`, returning success
immediately) avoided the crash by skipping the whole function — but that also means the OS desktop
resolution can never actually change, for any selection, ever. This was invisible for resolutions that
happened to already match the current desktop size, and showed up as letterboxing for one that didn't.

### The fix: NOP one comparison, not the whole function
`0x006BE3CA` is the `jne` that rejects a candidate mode for not being 16bpp (bytes `75 2C`). Patching it to
`90 90` removes only that constraint — width/height/frequency matching is untouched, so a genuine match is
still required, and the code re-fetches + applies via `ChangeDisplaySettingsA` exactly as designed, now
succeeding at whatever bpp the driver actually reports (32bpp today). This is implemented in
[patches.cpp](CKPatcher/src/patches.cpp) as `kBppGateOffset` / `kBppGatePatch`, replacing the old
`kSetVideoModeOffset` whole-function stub. `writeVideoModePatch` rebuilds the *entire* span from
`0x006BE340` through the gate (`kVideoModeSize = 0x140`) from the pristine backup on every write, then
conditionally applies the 2-byte NOP — this is what lets `enable` cleanly supersede an old install of the
whole-function stub with no separate migration step.

Verified end-to-end on a throwaway install: pristine detection, on/off round-trip, and a full-file diff
against pristine `Celtic kings.exe` showing **exactly 2 bytes changed** (plus 1 more for LAA when both are
on) — nothing else in the 3.5 MB file moves.

### A second, independent bug this uncovered: `ensureBackup`'s staleness check doesn't understand multiple patches
`game::ensureBackup`'s `isPristine` parameter exists to detect "Steam updated the game since my last
backup" and re-capture the baseline when that happens. Both `writeLargeAddressAware` and
`writeVideoModePatch` were computing `isPristine` from **only their own flag** (e.g. "LAA is currently
off"). That is not the same claim as "the whole file is untouched" once a second, independent patch
(SetVideoMode) can also be live in the same file: toggling LAA while SetVideoMode was still patched made
`ensureBackup` conclude the live/backup mismatch could only be an external change, and it silently
overwrote the pristine backup with the already-patched exe. This happened for real in this session's
`backup-current/` — verified and repaired by copying the known-good `backup/Celtic kings.exe.orig` back
over it (same build, confirmed via matching PE compile timestamp `2004-02-20 01:17:37` and file size).

Fixed with a shared `exePristine()` helper (`patches.cpp`) that requires **both** `readLargeAddressAware`
and `readVideoModePatch` to report off before treating a mismatch as external. Both writers now pass that
instead of their own single flag.

### A third bug this uncovered: `writeLargeAddressAware` rebuilt from the pristine backup, not the live file
Independent of the above, `writeLargeAddressAware` read `game::readPristine()` as the base for the byte
buffer it writes back — meaning every LAA toggle silently discarded whatever else was currently patched in
the exe (i.e. SetVideoMode), by design, not by the staleness bug. LAA is a single reversible bit flip with
no need to rebuild-from-pristine at all. Fixed by reading the *live* file instead. Verified: toggling
`--videofix on` then `--laa on` then `--laa off` now leaves the SetVideoMode patch intact throughout, where
before the LAA step would have silently reverted it.

### Practical implication for this project's real install
Because of the above, applying `--videofix on` and `--laa on` in sequence earlier in this session left the
**old** stub live on the real exe (LAA correctly on, but SetVideoMode still using the whole-function stub —
confirmed by reading the live bytes directly) despite `backup-current`'s corruption. Both bugs are now
fixed, `backup-current/Celtic kings.exe.orig` has been repaired, and the real install has been re-patched
with the new surgical NOP. A whole-file diff against true pristine now shows exactly 3 bytes different: the
LAA characteristic byte (`0x146`) and the two gate bytes (`0x2BE3CA`/`0x2BE3CB`).

**In-game test 1 result (bpp gate only)**: letterboxing is gone — the user confirmed the menu behind the
dialog renders correctly at 16:9 with no black bars. A separate "Cannot switch to 1920 x 1080" dialog still
fired once per click of "確定" in the options menu, non-blocking (dismissing it lets play continue
normally).

**Attempted follow-up, reverted**: the dialog's likely cause was a second, independent gate at
`0x006BE3F4` (`jb`, bytes `72 02`) requiring the caller's target refresh rate to be `>=` the enumerated
mode's `dmDisplayFrequency` — same failure shape as the bpp gate, just for frequency (at least one caller in
the options-apply chain was observed passing a target as low as `1`, which no real mode satisfies). NOPing
it (`kFreqGateOffset = 0x002BE3F4`, `72 02` -> `90 90`) was applied together with the bpp gate and verified
clean the same way as before (throwaway round-trip, order-sensitive toggle test, whole-file diff showing
exactly 5 bytes different).

**In-game test 2 result: regression, reverted the same session.** With both gates NOPed, the user reported
severe colour corruption during video playback (a false-colour, static-like corruption pattern -- screenshot
in this session's transcript) and the game became unable to switch away from 1024x768 at all, with the
dialog still firing. Leading theory: the engine's DirectDraw surfaces do not handle a *live*, successful
resolution switch gracefully -- something that could never have been exercised on any modern Windows
install before now, since no switch had ever actually succeeded (crash, or the original blunt stub,
or the bpp gate alone leaving frequency mismatches to block it). The freq-gate NOP directly reverted on the
live install (`0x006BE3F4` bytes written back to pristine `72 02`) and in `patches.cpp`: the constants stay
declared (with this history in a comment) but are no longer applied by `writeVideoModePatch`. Current
shipped state is bpp-gate-only, matching test 1's confirmed-working result. `readVideoModePatch` was
updated back to checking only the bpp gate.

**In-game test 3 result: the bpp gate alone is ALSO not safe, not just the frequency gate. Reverted
entirely, back to the whole-function stub.** With only the bpp gate NOPed (frequency gate manually reverted
on the live install, `vxSettings.ini`'s `Resolution` fixed from an invalid `0` — see below — back to `5`),
the user tested again: main menu was correct 16:9, but clicking "開始遊戲" (start game) to actually enter
gameplay reproduced the *same* colour corruption and stuck-resolution symptoms as test 2. This rules out the
frequency gate as the cause -- **any** patch that lets `ChangeDisplaySettingsA` genuinely switch the desktop
while the process is running corrupts something once gameplay (not just the main menu) actually starts.
This is a structural problem with the surgical approach itself, not a bug in one specific NOP.

Also found along the way: at some point during test 2's failure, the engine wrote an **invalid**
`Resolution=0` into `vxSettings.ini` (there is no `Res0_x`/`Res0_y` — the list starts at `Res1`), which on
its own was enough to make every subsequent launch fall back to a hardcoded 1024x768 regardless of what the
exe patch was doing. Manually corrected back to `Resolution=5` mid-session; worth remembering that an
invalid index here can *look* exactly like a resolution-switching bug and waste a debugging cycle.

**Final decision this session: `writeVideoModePatch` is reverted to the original whole-function stub**
(`kSetVideoModeOffset` = `0x002BE340`, `31 C0 C3 90 90 90`, exactly as before this whole rewrite started).
`readVideoModePatch` checks the same 3 bytes it always did. The bpp-gate and frequency-gate NOP addresses
are preserved in a comment (not applied) in case a future session wants to retry with the DirectDraw-surface
question answered first — do not re-apply either blind, both were tried and both corrupt gameplay. Applied
to the real install and verified via round-trip test (throwaway install: on -> `31 C0 C3 90 90 90`, off ->
pristine `81 EC 38 01 00 00`) before touching the live exe.

**The supported way to play above 1024x768 with this patch**: SetVideoMode is now a total no-op again, so
it will never try to change the OS resolution — the engine just renders at whatever size the player picked,
into whatever the desktop already is. Set the Windows *desktop* resolution to the target size yourself
(Settings -> System -> Display -> Resolution) *before* launching the game, then pick the matching entry
in-game. With the sizes already matching and no live switch ever attempted, there is nothing left to
corrupt.

**Open question for a future session, if the fully-automatic switch is worth revisiting**: is the
corruption tied to a video actually playing at the moment of the switch (stale DirectDraw surface), or does
it also happen switching straight into a fresh scenario with no video involved (pointing at a
pixel-format/mask mismatch instead)? Does forcing a full process relaunch after changing the resolution
(rather than switching while the same process keeps running) avoid it? Answering either would tell you
whether this is fixable with another small patch or needs the engine's surface-recreation path rewritten,
which is a much bigger undertaking.

## ZoomMap scanline tables are hardcoded for 1600 columns — FIXED (2026-08-17)

**Scope correction, read this first.** An earlier revision of this section claimed these tables were the
root cause of the colour corruption and called them "the software rasterizer". Both were overstated. The
containing function `fcn.00456A30` is reached from a caller holding the string `"ZoomMap.BuildZoomMap"`, so
these are the **ZoomMap** (zoomed-out overview) tables specifically, not the main-view rasterizer. They are
genuinely sized for 1600 and genuinely overrun above that width — that part is verified and now fixed — but
whether that overrun is what produced the observed corruption is **not established**. Against that theory:
the corrupted frames kept their *structure* (terrain, buildings and water were all clearly recognisable) and
only had wildly wrong *colours*, which looks far more like a pixel-format/colour-mask mismatch than like a
buffer overrun, which would normally garble geometry too. Treat the table fix as necessary-but-maybe-not-
sufficient, and keep the pixel-format hypothesis alive.

Verified directly in `Celtic kings.exe` (`.data`, writable, so an overrun corrupts live engine state):

| VA | What | Size |
|---|---|---|
| `0x0076FF78` | `col_table` — 1600 entries x 12 bytes (stride confirmed by `add esi, 0xc` at `0x00456a94`) | `0x4B00` (19200) |
| `0x00774A78` | second table, immediately after col_table (`0x76FF78 + 0x4B00` == `0x774A78` exactly) | — |
| `0x00774A94` | `row_ptr_table` — 1600 entries x 4 bytes | `0x1900` (6400) |
| `0x00776394` | unrelated engine data — `0x774A94 + 0x1900` == `0x776394`, the collision the mod's log names | — |

The column count is a literal in the code: `0x00456a83` is `mov edi, 0x640` — **1600**, i.e. the tables are
sized exactly for the stock maximum resolution of 1600x1200 and nothing more.

What the overrun does explain cleanly is *why width, not height, is the boundary*: `row_table` is indexed by
height and 1080/1440 both fit under 1600, while `col_table` is indexed by width, so 1920 walks 320 entries x
12 bytes past its end into adjacent `.data`. Stock 1280x1024 and 1600x1200 both fit exactly, which is
consistent with those being the resolutions that never misbehaved.

**Corollary worth revisiting:** the surgical `SetVideoMode` patch (NOPing `0x006BE3CA`) may have been fine
all along and been reverted for the wrong reason. Re-evaluate it only *after* the tables are fixed — and
only once, deliberately, given how many cycles the live-switch experiments already cost.

### How the mod fixes it (facts extracted; implementation NOT copied)
`_patch_zoommap` (`hd.dll`) `VirtualAlloc`s replacements and rewrites the **immediate operands** of the
rasterizer instructions that reference the static tables — it does not move the tables in place. Sizes:
`0x5A00` (= 1920 x 12) for col_table, `0x1E00` (= 1920 x 4) for row_ptr_table, i.e. exactly the same
layout re-derived for width 1920.

Base-game sites it rewrites (each is the immediate field of an instruction; verified against our own exe):

| patch target | instruction at | original |
|---|---|---|
| `0x00456A7F` | `0x456A7E` `mov esi, 0x76FF78` | col_table |
| `0x00456A84` | `0x456A83` `mov edi, 0x640` | **column count = 1600** |
| `0x00456B36` | `0x456B35` `mov esi, 0x76FF7C` | col_table + 4 |
| `0x00456B51` | — | col_table + 4 (second field) |
| `0x00456CDB` | `0x456CD8` `lea ecx, [eax*4 + 0x76FF78]` | col_table |
| `0x00456D1C` | `0x456D19` `lea ecx, [eax*4 + 0x76FF78]` | col_table |
| `0x00456DBA` | `0x456DB9` `mov esi, 0x76FF7C` | col_table + 4 |
| `0x00456DFD` | — | col_table + 4 |
| `0x00456DB5` | `0x456DB4` `mov ebx, 0x774A94` | row_ptr_table |
| `0x00456E54` | `0x456E51` `lea edx, [ecx*4 + 0x774A94]` | row_ptr_table |
| `0x00456EF3` | `0x456EF2` `mov esi, 0x774A78` | table after col_table |
| `0x00456EF8` | — | column count |
| `0x00743FC1`, `0x00743FC8` | (in `.data`, not code) | column count |

### IMPLEMENTED: `--hires <N>` / `--hires off` / `--hires-status`
`patches::readZoomTables` / `writeZoomTables`. No runtime DLL and no injection: the whole fix is a static
exe edit, so it stays inside CKPatcher's existing backup / `--reapply` model.

It appends a PE section named `.ckhr` sized `N*12 + 16 + N*4`, rounded up to `SectionAlignment`, with
`SizeOfRawData = 0` / `PointerToRawData = 0` and `CNT_UNINITIALIZED_DATA|MEM_READ|MEM_WRITE` — i.e. a plain
BSS section the loader zero-fills, so **the file does not grow at all**. It then rewrites the 12 immediates
to point into it. The 16 bytes of slack exist because the engine forms a `col_end + 4` pointer, which is
past the last entry.

Layout for max dimension `N`: `col_table` at the section base, `row_table` at `base + N*12 + 16`.

Implementation notes worth keeping:
- `vaToFileOffset` maps through the section table rather than assuming `VA - 0x400000`. That shortcut holds
  for `.text`/`.rdata`/`.data` but **not** for `.rsrc` (`.data`'s virtual size dwarfs its raw size), so the
  naive version would silently corrupt anything resolved in a later section.
- Enabling validates every site against its stock value first (unless `.ckhr` already exists, since
  re-running with a different `N` is legitimate), and aborts before writing a single byte on mismatch — that
  is what makes it safe against a different build of the exe.
- Re-running with a different `N` resizes `.ckhr` in place instead of appending another section.
- `off` restores the stock immediates, zeroes the section header, decrements `NumberOfSections`, and
  recomputes `SizeOfImage`.

Verified on a throwaway install: all 12 immediates land on the computed values; file size unchanged; only 47
bytes differ from pristine; the resize chain 2560 -> 1920 -> 4096 keeps exactly 5 sections; `--hires off`
returns the exe **byte-identical to pristine**; repeated runs are stable; and it coexists with LAA +
videofix with the backup staying pristine throughout.

### ...but it is FUNCTIONALLY INCOMPLETE — do not enable it (2026-08-17)
In-game result: intro video plays, then **black screen**. Reverted on the live install.

Every check above was about *mechanical* correctness (does the patcher write what it intended, reversibly).
None of them could catch the actual problem, which is that **the intended patch set is itself incomplete**.
A byte-scan for 4-byte values landing inside the table ranges found genuine references that neither the mod
nor this patch rewrites — two of them in the very same function:

| VA | instruction | role |
|---|---|---|
| `0x00456AB5` | `0x456AB4` `mov esi, 0x76FF78` | col_table base for a *second* loop |
| `0x00456B2F` | `0x00456B2D` `cmp esi, 0x774A78` | that loop's end bound |

With the table relocated, that loop still walks the original (now permanently zeroed, never-initialised)
`.data` range while the rest of the code uses the new section. That split-brain state is a very plausible
source of the black screen.

**Why the mod gets away with 14 rewrites when more sites clearly reference the tables:** `_patch_zoommap`
does not only rewrite immediates. Later in the same function it `VirtualAlloc`s with `PAGE_EXECUTE_READWRITE`
(`0x40`), assembles a 34-byte payload byte by byte on the stack (`0xBF` = `mov edi, imm32` … `0xC3` = `ret`),
and computes `lea esi, [eax - 0x456D9B]` — a rel32 for a `jmp` placed at `0x00456D96`. In other words the
mod's technique has **two layers: immediate rewrites *and* injected executable code caves**, and only the
first layer was extracted and reimplemented here. Reading the first ~130 instructions of that function and
assuming the `_write_u32` list was the whole story was the mistake.

**Consequences for anyone picking this up:**
- `--hires` currently ships but must be treated as non-functional. Leave it off.
- Completing it means replicating the code-cave layer too, which is a materially bigger job than static
  immediate rewriting and cannot be done from the `_write_u32` list alone.

### The code-cave layer, decoded (2026-08-17)
One cave was fully reconstructed. The mod assembles it byte-by-byte on the stack, `VirtualAlloc`s 0x22 (34)
bytes `PAGE_EXECUTE_READWRITE`, copies it in, and installs a `jmp` reached via `lea esi, [eax - 0x456D9B]`
(so the `jmp` sits at `0x00456D96`, since `0x456D96 + 5 == 0x456D9B`). Reconstructed payload:

```
33 C0                    xor eax, eax
BF 20 86 18 62           mov edi, 0x62188620        ; hd.dll-owned heap buffer
89 B4 24 D8 06 00 00     mov [esp+0x6D8], esi
89 9C 24 E0 06 00 00     mov [esp+0x6E0], ebx
B9 E0 01 00 00           mov ecx, 0x1E0             ; 480 dwords = 1920 bytes
F3 AB                    rep stosd
68 B4 6D 45 00           push 0x00456DB4
C3                       ret
```

The stock code it displaces (`0x00456D96`-`0x00456DB3`) is:

```
0x456D96  mov ecx, 0x190           ; 400 dwords = 1600 bytes
0x456D9B  xor eax, eax
0x456D9D  lea edi, [esp + 0x94]    ; <-- a STACK buffer in this function's own frame
0x456DA4  mov [esp+0x6D8], esi
0x456DAB  mov [esp+0x6E0], ebx
0x456DB2  rep stosd
```

**This is a third, separate 1600-sized object: a 1600-byte scratch buffer living on the stack.** The mod's
cave relocates it off the stack into a heap buffer and enlarges it to 1920 bytes. This is almost certainly
the direct cause of the black screen seen after enabling `--hires` at 2560 wide: the tables were relocated
and enlarged, but this stack buffer stayed 1600 bytes, so the engine overran its own stack frame by 960
bytes.

**Why this defeats a pure static-exe-patch architecture:** the buffer is addressed `esp`-relative, so there
is no immediate to rewrite. Fixing it requires executing replacement code. A static patcher *could* still do
it — put the cave in our own `.ckhr` section, mark that section executable, and point the buffer at the
section instead of the heap — but that is a substantially different design from "rewrite some immediates",
and every other instruction in the function that touches `[esp+0x94]` has to be found and redirected too.

**Measured scope of the full job:** the mod contains **13** `VirtualAlloc(..., PAGE_EXECUTE_READWRITE)`
call sites, i.e. on the order of a dozen code caves, plus the 14 immediate rewrites. Two `jmp` install
points are identified so far (`0x00456B57` and `0x00456D96`, from `lea esi, [eax - 0x456B5C]` and
`lea esi, [eax - 0x456D9B]`). Each cave needs its displaced stock code decoded, an enlarged equivalent
assembled, and a landing address chosen — and any mistake is a silent corruption or a black screen.

### RESOLVED: the caves are avoidable — equal-length in-place rewrites do the same job (2026-08-17)

Decoding the second cave and then properly disassembling `fcn.00456A30` (function-level, control-flow
aware — **not** byte-scanning, which produced a misleading picture earlier) settled every open question:

**1. Why the mod's 14 rewrites looked incomplete.** Within the function there are exactly 11 references to
the tables. Two are absent from the mod's list — `0x00456AB5` (`mov esi, 0x76FF78`) and `0x00456B2F`
(`cmp esi, 0x774A78`). They are the base and end bound of a second loop, and that loop is gated on a global
flag: `0x00456AA7 mov al, byte [0x007763A0]` / `test al, al` / `je 0x456B35`. With the flag clear the loop
never runs, which is why the mod can ignore it. We patch both anyway — an immediate rewrite is free, and
depending on a flag staying clear is not worth it.

**2. What actually caused the black screen.** Not the tables, and not that loop. A **third** 1600-sized
object: a scratch buffer on the function's own stack frame at `[esp+0x94]`, zeroed by
`0x00456D96 mov ecx, 0x190` (400 dwords = 1600 bytes) + `rep stosd`. Enlarging the table count to 2560
without enlarging this buffer made the engine overrun its own stack frame by 960 bytes.

**3. The caves are not actually necessary.** All three instructions that address the scratch buffer can be
replaced by absolute-form instructions of *exactly the same length*, so nothing shifts and no executable
memory is needed:

| VA | stock (7 bytes) | replacement |
|---|---|---|
| `0x00456D9D` | `8D BC 24 94 00 00 00` `lea edi, [esp+0x94]` | `BF <abs>` + 2x `90` |
| `0x00456E65` | `8A 84 0C 94 00 00 00` `mov al, [esp+ecx+0x94]` | `8A 81 <abs>` + `90` |
| `0x00456E91` | `88 84 0C 94 00 00 00` `mov [esp+ecx+0x94], al` | `88 81 <abs>` + `90` |

plus the zero-fill count at `0x00456D97`, which is a same-length immediate (`N/4` dwords).

The mod uses injected `PAGE_EXECUTE_READWRITE` code caves for this; equal-length rewrites are strictly
simpler and keep `.ckhr` a plain zero-filled, non-executable data section with no file growth. **Task 3
("extend .ckhr to hold executable cave code") was therefore dropped as unnecessary.**

Independent cross-check that the sizing model is right: for N=1920 our formula emits `mov ecx, 0x1E0`
(480 dwords) — byte-for-byte the value the mod's own cave uses. Two independent derivations agreeing.

**Implemented.** `kSites` now holds 15 immediates (adding `0x456AB5`, `0x456B2F`, `0x456D97`) and a new
`kRewrites` table holds the 3 instruction rewrites, each validated against its exact stock bytes before
anything is written. Section layout is now col_table, 16 bytes slack, row_table, scratch buffer.

Verified at N=1920 on a throwaway install: all four rewritten instructions **disassemble** to the intended
form (`mov ecx, 0x1E0`; `mov edi, 0x8D2810`; `mov al, byte [ecx + 0x8D2810]`; `mov byte [ecx + 0x8D2810], al`);
70 bytes changed; file size unchanged; `--hires off` restores **byte-identical to pristine**; coexists with
LAA + videofix; backup stays pristine.

**Deferred, not yet shown to be needed:** the second cave at `0x00456B57` clamps a value at `[esp+0x6D8]`
to `>= 0` (`mov eax,[esp+0x6D8]; test eax,eax; jns +9; xor eax,eax; mov [esp+0x6D8],eax; push 0x456B5E; ret`).
This one has no equal-length in-place equivalent and would need a real cave. Also still undecoded: the null
guards at `0x004159C7` / `0x00431BA7` and minimap dot scaling (`0x0045511C` / `0x004551B3`).

### CONFIRMED WORKING at 1920x1080 (2026-08-17)
In-game: correct geometry, correct colours, full 16:9, no black screen, no corruption. The scratch-buffer
relocation was the missing piece. One cosmetic artifact remains: a black rectangle in the top-right of the
upper HUD bar, i.e. the HUD does not span the full width — see "remaining work" below.

### The launcher was forcing the desktop resolution — FIXED
Separate root cause, separate binary. `Celtic kings Launcher.exe` is a 2019-era **x64** executable, so none
of the `Celtic kings.exe` patches touch it. Before starting the game it:

1. Enumerates the display's supported modes via `EnumDisplaySettingsA`.
2. Matches each against a hardcoded 4-entry table at VA `0x1400043B0`:
   `1600x1200`, `1280x1024`, `1152x864`, `1024x768`.
3. Keeps the **lowest matching table index** — the table is sorted descending, so this is the highest of
   those four the monitor supports.
4. Calls `ChangeDisplaySettingsA` to force the desktop to it (`0x1400015CE`).
5. After the game exits, calls `ChangeDisplaySettingsA(NULL, 0)` to restore the registry default
   (`0x1400019F9`).

Any modern monitor reports 1600x1200, so launching always dropped the desktop to 1600x1200 regardless of
what the game was set to render at. Editing the table is not enough — the launcher only ever picks a mode the
display actually enumerates, and the entries also populate the `DEVMODE` it applies. Suppressing the mode
change outright is smaller and matches what we want now that the game does not change modes either:

| launcher VA | stock | patched | effect |
|---|---|---|---|
| `0x14000159B` | `74 37` (`je 0x1400015D4`) | `EB 37` (`jmp`) | always skip the set block |
| `0x1400019F9` | `FF 15 C9 26 00 00` (`call CDS`) | `90` x6 | skip the restore on exit |

Implemented as `patches::readLauncherDisplayPatch` / `writeLauncherDisplayPatch`, CLI `--launcherfix on|off`.
Note the launcher is PE32+ with ImageBase `0x140000000` and `.text` mapping RVA `0x1000` -> file offset
`0x400`; the site table stores **RVAs**, not low-32-bits of the VA (getting that wrong initially produced a
bogus offset far past the 25 KB file, which the bounds check caught before anything was written).
Verified: both sites disassemble to the intended `jmp` / `nop`s, `off` restores byte-identical, idempotent.

### The engine's real ceiling is just above 1920 wide — 1920x1080 is the shipped target (2026-08-17)

Empirically bisected, all at 16:9 to keep aspect ratio out of it:

| resolution | pixels | result |
|---|---|---|
| 1920x1080 | 2,073,600 | **fully playable** |
| 2048x1152 | 2,359,296 | main menu renders, entering a game crashes |
| 2560x1440 | 3,686,400 | main menu renders, entering a game crashes |
| 3840x2160 | 8,294,400 | black screen (also exceeds the DPI-virtualised screen, so confounded) |

Ruled out along the way:
- **Total pixel count.** A `1600x1440` probe (width only 1600, but 2,304,000 px) failed *earlier* than
  `2560x1440` did — it black-screened at the menu. Failure is not monotonic in pixel count. That probe was
  also a badly designed test on my part: its 10:9 aspect ratio introduced a third variable, so its result
  cannot cleanly support either hypothesis. Later probes were held at 16:9.
- **Launcher / startup order.** Switching to 2K from inside an already-running 1920x1080 session crashes
  identically, so it is nothing to do with launch sequencing.
- **Desktop mismatch.** Physical desktop confirmed as 3840x2160 @ 32bpp via WMI.
- **DPI virtualisation being the whole story.** `2560x1440` exactly equals the virtualised screen size
  (3840/1.5) and its menu renders fine, yet entering a game still crashes.

Note on measuring the desktop: `System.Windows.Forms.Screen.PrimaryScreen.Bounds` returns **DPI-virtualised**
pixels. At 150% scaling on a 3840x2160 panel it reports 2560x1440, which I initially misread as "the user
never changed the desktop". Use `Get-CimInstance Win32_VideoController` (or `EnumDisplaySettings`) for
physical pixels. Relatedly, `Celtic kings.exe` has **no `dpiAware` manifest**, so Windows virtualises it —
which is a genuine and separate obstacle to anything above the virtualised screen size. Marking it DPI-aware
(exe Properties -> Compatibility -> Change high DPI settings -> Override -> Application, or an app-compat
`HIGHDPIAWARE` layer) is the fix if that path is revisited.

**Corroborating evidence that ~1920 is the engine's own ceiling, not a missed constant:** the third-party HD
mod is built entirely around 1920 — every size it computes is derived from it (`0x5A00` = 1920x12,
`0x1E0` = 1920). Its author never went above 1920 either. If 2K were a couple more constants away, they
would very likely have shipped it.

**Decision (user's): stop at 1920x1080 for this round, but keep the ability to go higher later.** The
*tooling* is deliberately left fully general — `--add-res <WxH ...>` takes any resolution and `--hires <N>`
takes any capacity up to 16384. Only the current *configuration* is conservative.

Also note: **every crash writes an invalid `Resolution=0` into `vxSettings.ini`**, and on the next launch an
invalid index silently falls back to 1024x768. This happened twice and looks exactly like a resolution bug.
If the game mysteriously starts at 1024x768, check that value first.

### Where to resume if 2K is attempted again
The failure signature is specific and narrow: **the main menu renders correctly, and the crash happens on
entering a game** — i.e. in scenario/gameplay setup, not in the general render path. That is the same phase
`ZoomMap.BuildZoomMap` runs in, so the remaining limit is plausibly another fixed-size buffer in that
neighbourhood that has not been found yet. Untried leads, in order of promise:
1. The two vtable null guards (`0x004159C7`, `0x00431BA7`). Both are `mov eax,[ecx]` followed by
   `jmp dword [eax+0x28]` where the engine has already null-checked `ecx` but **not** the vtable pointer it
   loads. The mod substitutes a dummy vtable when it is null. Weak evidence they matter here (1920 plays
   fully without them), but they are exactly the shape of an "entering a game" crash. **These do need a real
   code cave** — `mov eax,[ecx]` is 2 bytes and the following `jmp` makes 5 total, just enough for a
   `jmp rel32`, so the executable-section work dropped in task 3 would have to be done after all.
2. The clamp cave at `0x00456B57`.
3. The two unidentified `.data` writes at `0x00743FC1` / `0x00743FC8`.
4. Get an actual fault address instead of guessing. The engine swallows its own exceptions, so nothing
   reaches WER — enabling `HKLM\...\Windows Error Reporting\LocalDumps\Celtic kings.exe` (`DumpType=2`) or
   attaching x64dbg is the way to turn this from inference into measurement. Several cycles in this session
   were spent on hypotheses that a single fault address would have settled immediately.

### Current live state and remaining work
**Shipped configuration (validated end to end):**

| component | state |
|---|---|
| `Celtic kings.exe` LAA | on |
| `Celtic kings.exe` videofix | on (whole-function `SetVideoMode` stub) |
| `Celtic kings.exe` ZoomMap tables | `--hires 1920` |
| `Celtic kings Launcher.exe` | `--launcher-res 1920x1080` (模式表第 0 筆改寫，開遊戲切桌面、離開切回；`--launcherfix` off) |
| `data.pak` resolution list | stock 4 entries + `Res5 = 1920x1080` only |
| `vxSettings.ini` | `Resolution=4` (**0-based position in the list**, not the `Res<N>` number — see below) |

`data.pak` was deliberately reset to the pristine baseline and only `1920x1080` re-added, so the list contains
no entry that crashes. The higher resolutions probed during bisection were removed for that reason — picking
one would crash *and* corrupt `Resolution` to `0`.

**Required Windows-side setting:** because `SetVideoMode` is stubbed and the launcher no longer changes modes,
nothing sets the desktop automatically. Set the Windows desktop to **1920x1080** before playing so it matches
what the engine renders. 100% scaling avoids the DPI-virtualisation complication entirely.

**IMPLEMENTED 2026-08-18 as `--launcher-res` / `writeLauncherModeTable()`.** The launcher's hardcoded table at
VA `0x1400043B0` (file offset `0x2BB0`, in `.rdata`, four `(width, height)` int32 pairs) has entry 0 rewritten
from `1600x1200` to the game's resolution, *instead of* suppressing the mode change. The launcher then sets
the desktop to it on start and restores the previous mode on exit, which removes the black border you get when
the game renders smaller than the desktop — no scaling anywhere, native pixels throughout.

Note the history, because it reversed twice in one session: the idea was first deferred over DPI scaling, then
**rejected outright** by the user (「絕對禁止自動切換使用者桌面解析度」), then explicitly requested by the same
user once they saw the black border on a 2560x1440 panel with the game at 1920x1080. The current state is the
last of those. Do not "restore" the prohibition from an older revision of this file.

Mechanics worth keeping in mind:
- **Mutually exclusive with `--launcherfix`.** That patch NOPs the `ChangeDisplaySettingsA` call outright, so
  with it applied the mode table is dead code. Every path that enables one disables the other, in that order.
- **Entries 1-3 are deliberately left stock** (`1280x1024 / 1152x864 / 1024x768`). They are the launcher's
  fallback chain when the display does not enumerate entry 0, and they double as the build fingerprint that
  `modeTableAt()` validates before touching entry 0.
- The launcher only ever selects a mode the display actually enumerates, so an unsupported entry 0 degrades to
  the fallbacks rather than failing.
- `--launcher-res off` restores `1600x1200`; verified byte-for-byte identical to the pristine backup.
- `launcherPristine()` now gates `ensureBackup` for *both* launcher patches, mirroring `exePristine()` — with
  only the old per-flag check, applying one patch while the other was live re-captured a patched launcher as
  the baseline.

The old advice for filling a larger panel (set the desktop yourself plus GPU full-panel scaling) still works
and costs no patch, but it upscales 1080p to 1440p; the mode-table route keeps native pixels instead.

**Confirmed working in-game 2026-08-18** on the user's 2560x1440 panel: the launcher switches the desktop to
1920x1080 on start and restores 2560x1440 on exit, and the black border is gone. This row is a play test, not
byte inspection — the distinction matters here, because the `Resolution=5` entry in this same table was once
marked verified on byte inspection alone and turned out to be wrong.

Still open — but note the user froze the resolution track at 1920x1080 on 2026-08-18, so none of these are
active work. They are recorded so a future session does not have to re-derive them:
1. **HUD does not span the full width** at 1920x1080 — a black rectangle in the top-right of the upper bar,
   exactly `1920 - 1600 = 320` px wide. Investigated 2026-08-18; **the layout INI is not the constraint, the
   artwork is.** Findings, so nobody repeats the dead end:
   - The bar geometry is data, not code. `VXCONST.INI` has `[UIBars]` with
     `UpperDefault = data/interface/Infobar/empty/infobar.ini` and `LowerDefault = .../cmdbar.ini`.
   - `data.pak` holds six `RectWH = 0, 0, 1600, 80` entries: `[InfoBar_Gaul]` and `[Infobar_Rome]`
     × (`[Background]`, `[BackgroundEmpty]`), plus two for the map editor. `gameini/template.ini`'s
     `[UpperDlg]` / `[Background]` are `1024` wide with `MaxSize = 1024, 768`.
   - **Tested in-game: changing all four in-game rects to `1920` changed nothing.** `1600`→`1920` is the same
     byte length so it patches in place without disturbing the pak directory, and the pak still parsed — but
     the drawn bar still stopped at 1600. A wider rect does not create pixels. The edit was reverted.
   - The bar art is not a BMP: scanning `data.pak` and `local.pak` for bitmaps wider than 400 px and 20-120 px
     tall returns nothing, and the referenced asset is `infobar/common/back.rle`. It lives in the 169 MB
     `rle.mmp` as an RLE sprite, a format `hmmpak` explicitly does not handle.

   So the three remaining routes, none cheap: (a) reverse the RLE sprite format and widen the art, repacking
   `rle.mmp`; (b) a code cave that draws the bar twice, at `x=0` and `x=W-1600`, letting the second copy
   cover the gap — viable if the texture is uniform enough to hide the seam; (c) decode what
   `ImageType = AAAAA` means (five letters, one per slice?) in case the engine already supports tiling, which
   would make this data-only after all. (c) is the cheapest to try and the least certain.

   Cosmetic only. The user was advised to leave it until something functional needs attention.
2. Everything under "Where to resume if 2K is attempted again" above. **Do not restart this by bisecting
   resolutions** — that was tried and the failure is not monotonic. Get a fault address first (WER
   `LocalDumps` for `Celtic kings.exe` with `DumpType=2`, or attach x64dbg).
3. Re-evaluate the surgical `SetVideoMode` patch. It was reverted on the theory that live mode switching
   corrupts DirectDraw surfaces, but that corruption is now known to have been the scratch-buffer overrun, so
   the original reasoning no longer holds. If it works, the game could set its own desktop mode and both
   `--launcherfix` and manual desktop changes become unnecessary.
4. **Not a resolution issue, and explicitly deferred by the user (2026-08-18)**: the lag + silent crash as
   unit count grows. Nothing in this file's HD work touches it. If it is ever picked up, the two things it
   needs are a continuous profiler capture spanning few-units → many-units → crash, and an actual crash
   artifact (there is currently no WER entry at all, which is itself a clue — see the session memory notes).

### Options assessment (from before the above was resolved)
1. **Complete the port** — replicate ~12 caves plus the immediate rewrites, with our own section made
   executable. Tractable and now well-understood in approach, but it is a multi-session reverse-engineering
   effort with a high chance of further broken-game cycles along the way.
2. **Cap at 1600x1200** — the widest the stock engine handles with zero patching. Was the shipped state until 2026-08-17; superseded by the 1920x1080 configuration now shipping.
3. **Use the third-party mod for playing at 1920x1080** and keep CKPatcher for what it already does well
   (LAA, the 16bpp SetVideoMode fix, the performance switches, the profiler, the resolution-list editor).
   It is free and non-commercial; its licence forbids redistributing or integrating its work, not using it.

An alternative worth evaluating before committing to option 1: rather than relocating the tables, check
whether the object at `0x00776394` that they collide with can itself be moved (it may have fewer
references), letting the tables grow in place and leaving every existing absolute reference valid. That
would not, however, solve the stack-buffer problem, which needs a cave regardless.

**`exePristine()` had to be extended to include this patch.** It gates `ensureBackup`'s staleness check, and
omitting the new patch made that check mistake our own edit for a Steam update and overwrite the pristine
backup with an already-patched exe — the exact bug class already fixed twice in this file. Any future exe
patch must be added there too; there is a comment on the function saying so.

**Still unhandled** (from the mod's patch map): the `0x4159C7` / `0x431BA7` null guards, minimap dot scaling
(`0x45511C` / `0x4551B3`), the 640x480 movie path (`0x6C3A49`), and the two `.data` word writes at
`0x00743FC1` / `0x00743FC8` (both are `0` in the file, their role is unidentified, and writing to unknown
fields was judged riskier than omitting them — revisit if the tables alone prove insufficient).

## Resolution Support — Findings from the `Imperivm1-HD-4-multi` Mod (2026-08-17)

### Licensing constraint (read before touching this)
The mod ships a `LICENSE` (author: JosueCA) whose clauses 3.1 / 3.3 forbid reverse-engineering `hd.dll`
and integrating the author's work into other projects. **Do not decompile `hd.dll` and do not copy its
code into CKPatcher.** Everything recorded below was obtained either from the *base game engine*
(explicitly excluded from that licence by clauses 1.2 / 5.1, and already our own analysis target) or from
`hd.dll`'s public PE metadata — export table, import table, and its own log format strings.

### The engine takes its resolution list from an INI, not from hardcoded code
`.data` of `Celtic kings.exe` contains the key names the engine formats at runtime:

| VA | String |
|---|---|
| `0x745384` | `Res%d_y` |
| `0x74538C` | `Resolutions` |
| `0x7453A0` | `Res%d_x` |
| `0x7453A4` | `menuini/gameoptions.ini` (menu layout only — *not* the resolution list) |
| `0x7453C0` | `%d x %d` (options-menu display format) |
| `0x7453C8` | `Resolution` (the `vxSettings.ini` key) |

The reader at `0x006582D0` is an open-ended loop — it keeps asking for `Res%d_x` until the key is
missing, so **the list has no built-in size limit**. `vxSettings.ini`'s `Resolution=N` is an *index* into
that list, so entries must only ever be appended, never renumbered.

**The list lives in `VXCONST.INI` inside `data.pak`, section `[Resolutions]`** — verified 2026-08-17 by
extracting both candidates with `tools/hmmpak.py`. Note that `INTERFACE\MENU\GAMEOPTIONS.INI` (the string
at VA `0x7453A4`) is the menu *layout* file and contains **no** `Res*_x/y` keys; do not chase it.

Stock list as shipped (4 entries, indices 1–4):

| Index | Resolution |
|---|---|
| 1 | 1024 x 768 |
| 2 | 1152 x 864 |
| 3 | 1280 x 1024 (this repo's `vxSettings.ini` default) |
| 4 | 1600 x 1200 |

So appended entries start at `Res5`. This is what `tools/add_resolutions.py` does, and that part works.

### The mod modifies the EXE by *zero* bytes of HD logic
Byte-diff of the mod's `Imperivm.exe` against `backup/Celtic kings.exe.orig`:

- Same compile timestamp (`2004-02-20 01:17:37`) and identical `.text/.rdata/.data/.rsrc` VAs — **same build**, so every address below transfers 1:1 to our exe.
- Exactly **three** `.text` differences: `0x6BE0F0`, `0x6C11B8`, `0x6C3AE4`. All three are **CD copy-protection**, not HD. `0x6BE0F0` scans for a CD-ROM via `GetLogicalDriveStringsA` + `GetDriveTypeA == DRIVE_CDROM` + `CreateFileA`. Our Steam build stubs the *function body* to `xor eax,eax; inc eax; ret`; the FX Interactive build instead patches the two *call sites* to `mov eax,1`. Two different no-CD approaches — irrelevant to resolution.
- One added section `.hdimp` @ `0x008CB000`.

**Loader mechanism:** the import directory was copied from RVA `0x31EB18` (size `0xF0`, 11 DLLs) into
`.hdimp` at RVA `0x4CB000` (size `0x104`, 12 DLLs) with one appended descriptor for `hd.dll!HDInit`
(IAT slot `0x8CB120`). Because `.text` is otherwise unchanged, **`HDInit` is never actually called** — the
import exists purely to make the Windows loader map the DLL before the EXE entry point. All work happens
in `DllMain`. No launcher, no injector, no `CreateRemoteThread`.

> **Conclusion: HD support is 100% runtime patching. The hard part was never adding the resolution to the
> list — it was the downstream breakage that follows.**

### Downstream patch map (from `hd.dll`'s own log strings — semantics to be re-derived ourselves)
| Address(es) | What needs handling |
|---|---|
| IAT `0x70631C` (`ChangeDisplaySettingsA`), IAT `0x706024` (`GetDeviceCaps`) | IAT hooks so the engine believes the desktop mode changed |
| scanline `col_table` / `row_ptr_table` | Engine's **static** tables are too small above 1024x768; growing them in place collides with data at `0x776394`. Must be relocated to `VirtualAlloc`'d memory |
| `0x4159C7`, `0x431BA7` | EAX null-guard code caves on two virtual dispatches |
| `0x45511C` | Minimap unit dot size (dots become sub-pixel at high res) |
| `0x4551B3` | Minimap dot colour brighten |
| `0x6C3A49` | Intro/movie playback hardcodes 640x480 — verified in our exe as `push 0x1E0; push 0x280; call 0x6BFF90` |
| `0x006BE340` / `0x006BE3CA` | `SetVideoMode` 16bpp crash — **already fixed by us**, see "SetVideoMode Patch Rewrite" section above |

Unrelated to HD but present in the mod: `0x40ADC0` (sound language redirect), `0x64D88D` (editor unlock).
The DLL also installs a vectored exception handler that swallows writes to `.rdata` — a blunt
compatibility fallback we should not need if our patches are correct.

### Resolution gate in the engine
`0x006BE4A0` guards `SetVideoMode` with a **pixel-count budget**:
```
esi = [0x8C1DD0] * [0x8C1DCC]   ; capacity
eax = width * height            ; request
cmp eax, esi / ja <fallback>
```
Both capacity variables are **never written by any code in our exe** (verified by full-image search — only
this function reads them), so they stay 0 and the gate always falls through. It will not block us.
`SetVideoMode(w, h, refreshRate)` — the third argument is a refresh rate (default `0x3E7` = 999), not bpp.

### 4K feasibility
No hard engine blocker was found: the list is unbounded, the gate above is inert, the blit path uses
signed 16-bit coordinates (`movsx ecx, word [...]`) which comfortably holds 3840, a 4K RGB565 pitch of
7680 fits in `int16`, and 3840x2160x2 = 16.6 MB per surface is covered by the existing LAA patch.

The real limits are practical, not architectural:
1. **Performance.** This is a *software* rasteriser (RGB565 in memory -> `SetDIBitsToDevice`, `0x0044F536`), no GPU acceleration. 4K is 4x the pixels of 1080p, and we are already chasing a lag+crash bug at high unit counts.
2. **UI scale.** HUD/font/button assets are fixed-size bitmaps. The mod already needs minimap dot patches and a custom `hd.png` just for 1080p; at 4K the UI would need art rework, which no address patch can fix.

**Recommendation: land 1080p first, evaluate 1440p as a midpoint, and treat native 4K as out of scope.**
For a 4K display, 1080p with 2:1 integer scaling beats native 4K on both fidelity and framerate.

## Next Steps / Active Tasks

**There is no active engineering task.** The user closed the resolution track at 1920x1080 on 2026-08-18 and
explicitly deferred the unit-count lag/crash bug. What follows is the state a future session inherits.

### Shipped and verified (2026-08-18)
- `CKPatcher.exe` toggles the 16-bit display fix, LAA 4GB, the animation switches, the full HD stack, and
  runs the sampling profiler — from the GUI, the text menu, or the CLI. All three surfaces expose the same
  operations.
- Release|Win32 builds with 0 warnings / 0 errors on MSVC C++17.
- End-to-end check against the real Steam install: `--hd` then `--reapply` left `Celtic kings.exe`,
  `Celtic kings Launcher.exe` and `data.pak` **byte-identical** (md5 unchanged), `vxSettings.ini`
  `Resolution` stayed at 5, and `ckpatcher.cfg` gained `hires=1920` / `launcherfix=on` / `addres=1920x1080`.
- `--status` reports the ZoomMap table capacity, the launcher patch state, and the resolution list, and its
  "settings were reverted" detector now covers the two exe/launcher HD patches.

### Invariants a future session must not break
1. **`exePristine()` gates `ensureBackup`'s staleness check.** Every new `Celtic kings.exe` patch must be
   added there, or our own edit gets mistaken for a Steam update and the pristine backup is overwritten with
   an already-patched exe. This bug class has been hit three times; there is a comment on the function.
2. **`addResolutions` reads and patches the live `data.pak`**, not the pristine backup, and passes
   `isPristine = !hasBackup(kDataPak)`. Both deviations are deliberate — see the 2026-08-17 notes below.
3. **The `[Resolutions]` list is append-only.** `vxSettings.ini` stores an index, so renumbering silently
   changes what an existing install selects.
4. **`Desired` / `ckpatcher.cfg` must gain a field for any new persistent patch**, and `doReapply()` (CLI)
   *and* the GUI's 「一鍵套回」 must both apply it, in exe → launcher → `data.pak` order.

### If the resolution track is ever re-opened
Read "Where to resume if 2K is attempted again" and the "Still open" list above. The one instruction that
matters: **get a real fault address before writing any more patches.** The engine swallows its own
exceptions so nothing reaches WER; enable `HKLM\...\Windows Error Reporting\LocalDumps\Celtic kings.exe`
with `DumpType=2`, or attach x64dbg. Several cycles were spent on hypotheses a single fault address would
have settled immediately.

### Historical notes on the resolution work (kept for reference)
0. **Done 2026-08-17:** the Python path is restored and verified end-to-end — `add_resolutions.py` +
   `hmmpak.py` append `Res5=1920x1080` / `Res6=2560x1440` to `VXCONST.INI`, the patched `data.pak`
   round-trips, all 877 entries stay byte-identical apart from `VXCONST.INI`, and re-running is
   idempotent. **This only makes the modes selectable — it does not make them work.** Expect the
   downstream breakage in the patch map above.
1. **Done 2026-08-17:** ported into CKPatcher as `--list-res` / `--add-res <WxH ...>`
   (`patches::readResolutions` / `patches::addResolutions`). No Python needed. Two behaviours differ
   deliberately from the Python tool, both learned the hard way during testing:
   - It reads and patches the **live** `data.pak`, not `game::readPristine()`. The list is append-only, so
     starting from pristine every time would make a second `--add-res` silently drop the first run's
     entries. Dedupe is against the live list, which is what makes re-running a no-op.
   - It passes `isPristine = !hasBackup(kDataPak)` to `game::ensureBackup`. Passing `true` unconditionally
     makes ensureBackup's staleness check mistake our own append for a Steam update and **overwrite the
     pristine backup with an already-patched pak**, which quietly destroys `--restore`.
   - The `[Resolutions]` splice anchors past the last character of the last `Res<N>_y` line, *not* at the
     `'\n'`. These files are CRLF; anchoring at the newline lands between `\r` and `\n` and corrupts every
     line ending downstream. Verified byte-exact afterwards (403 -> 407 CRLF, 0 bare LF, 0 `\r\r`).
   - Verified: 877/877 entries intact, no non-`VXCONST.INI` entry changed, incremental add, duplicate
     no-op, `--restore` round-trip, and cross-checked against the independent Python reader.
2. `tools/add_resolutions.py` is now redundant with the C++ path but kept as a cross-check oracle.
2. Decide the load mechanism for our own runtime patch DLL. Two options:
   - *Import-table injection* (the mod's approach): clean, no launcher, but modifies the EXE — and Steam updates overwrite it, which adds to the existing `--reapply` maintenance burden.
   - *`CreateProcess(CREATE_SUSPENDED)` + inject from CKPatcher*: leaves the EXE untouched. **Preferred**, given the project already fights Steam file restoration.
3. Re-derive the semantics of each address in the patch map above against our own exe in a debugger before writing any code cave. Do not assume the mod's addresses mean what its log strings imply.
4. Keep the existing `0x006BE340` `SetVideoMode` patch (the whole-function stub, see "SetVideoMode Patch
   Rewrite" above for why the surgical alternative was tried and reverted) — it is a prerequisite on this
   path, not an alternative to it. Any future runtime patch DLL that wants automatic, in-process resolution
   switching will hit the exact same DirectDraw-surface corruption this session found and reverted; that
   problem needs solving (or the switch needs to happen only via a full process relaunch) before this path
   can offer anything better than "set the Windows desktop resolution yourself before launching."


## The high-unit-count crash, root-caused (2026-08-19)

First fault captured by `ckperf.dll`'s vectored exception handler. pid 35668, alive
117 seconds (78 s user + 38 s kernel), died at 20:53:18.

```
eip 0x0068FDA6   mov dword ptr [ecx], eax     ecx = 0
ACCESS_VIOLATION writing 0x00000000
working set 137 MB, largest free block 2046 MB   <- NOT memory exhaustion
```

### The function

`0x0068F9E0` is a script-VM command implementation (cdecl, `sub esp,0x64`, plain `ret`).
It pops three operands off the VM operand stack -- the stack pointer lives at `[esi]`
and each pop is `[esi] -= n; value = *[esi]` -- and resolves each to a real object via
`0x00481A20(dword id, word type)`.

**Each resolution is null-checked, and a failed resolution is deliberately recorded as a
null pointer:**

```
0068FA0D  call 0x481a20          ; resolve reference #1
0068FA17  cmp  eax, ebp          ; ebp == 0 here
0068FA19  je   0x68fa27
0068FA21  mov  [esp+0x20], eax   ; resolved   -> real pointer
0068FA27  mov  [esp+0x20], ebp   ; unresolved -> NULL, execution continues
```

The identical shape repeats for reference #2 into `[esp+0x1C]` (`0068FA4E` / `0068FA56` /
`0068FA5C`) and #3 into `[esp+0x18]` (`0068FA83` / `0068FA8B` / `0068FA91`).

### The bug

Both exit paths then dereference all three pointers **without checking**:

| Range | Length | What it does |
|---|---|---|
| `0068FACB`–`0068FAE6` | 28 bytes | early exit: writes 0 through all three |
| `0068FD9E`–`0068FDC5` | 40 bytes | normal exit: writes the computed results through all three |

The captured fault is the first store of the normal exit, on the `[esp+0x18]` slot,
i.e. **reference #3 failed to resolve**.

So: a script that writes back into a reference which has become invalid since it was
read takes the whole process down. The most obvious way for that to happen is a unit
dying between the script reading it and the script writing to it. That is exactly why
the crash gets likelier the more units are alive -- more units means more deaths per
tick, so more chances for a captured reference to go stale mid-script.

### Corroborating stack

The raw stack scan puts `0x005DF460` on the stack **three times**
(`0x005DF5EE` at +0x0074, +0x0140, +0x0258). That is the function that references
`"WARNING: Atomic section instruction limit exceeded!"` at `.data:0x0073FC4C` -- the
script VM's execution loop, re-entered recursively. `0x00690640` also appears twice
(return addresses `0x00690A89` and `0x00690D1F`, its two call sites into `0x0068F9E0`).
`0x00690A50` is visibly a VM opcode handler: it pops four operands off `[esi]` and
forwards them as arguments.

### What this rules out

- **Not** memory or address-space exhaustion: 137 MB working set, 2046 MB largest free
  block, LAA active.
- **Not** the `FSPtrPool` fixed-capacity pool (`.data:0x00725C80`). That remains a
  plausible cause for *other* crashes but it is not this one.
- **Not** caused by the toolkit's tweaks directly. `hero_max_army = 2000` and
  `pop_growth_rate = 100` raise the unit count and therefore the *probability* of
  hitting this race, but the missing null check is the engine's.

### The fix (runtime, `src/CKPerf/guard.cpp`)

Both exit sequences are re-implemented in a `VirtualAlloc`'d code cave with each store
guarded by `test reg,reg / jnz`, and every suppressed store increments a counter that is
reported in the log and in every subsequent crash report. Original bytes are verified
before patching; a mismatch refuses the patch outright.

Skipping a store means one script variable does not receive its update. That is a much
smaller problem than terminating the process, but it *is* a behaviour change, hence the
counter and the `guard=0` switch.

**Nothing is written to disk.** The patch exists only in the injected process, so "off"
is simply "do not inject" -- there is no reversal path to maintain and no interaction
with the `AGENTS.md` §2 file-patch discipline.

### Second crash: same bug class, different site (2026-08-19 21:11:07)

pid 31380, alive 114 seconds. `guard: 0` -- the site-specific cave from the first crash
never fired, so that hypothesis is still unconfirmed.

```
eip 0x005D99A4   mov dword ptr [eax], esi     eax = 0
```

`0x005D9960` is a two-operand script store command. It pops a value and a reference off
the VM stack, resolves the reference, and writes the value into the object's field:

```
005D9960  sub  esp, 8
005D9963  mov  eax, [esp+0xC]      ; the VM stack-pointer holder
005D9967  mov  edx, [eax]
005D9969  add  edx, -4
005D996C  mov  [eax], edx          ; pop the value
005D9971  mov  esi, [ecx]          ; esi = value to store
005D9973  add  ecx, -6
005D9976  mov  [eax], ecx          ; pop the 6-byte reference (dword id + word type)
005D9988  call 0x481a20            ; resolve it
005D9990  test eax, eax
005D9992  je   0x5D99A2            ; resolution FAILED
005D9994  mov  edx, [esp+6]        ; field offset
005D9998  mov  [eax+edx], esi      ; success: store into the field
005D999B  xor  eax, eax
005D999D  pop  esi
005D999E  add  esp, 8
005D99A1  ret
005D99A2  xor  eax, eax            ; failure: destination pointer = NULL
005D99A4  mov  [eax], esi          ; ...and store through it anyway   <-- always faults
005D99A6  pop  esi
005D99A7  add  esp, 8
005D99AA  ret
```

This is `dst = ok ? &obj->field : NULL; *dst = value;` compiled literally, with the
compiler constant-folding the null case into `xor eax,eax` + a store through it. Unlike
the first site there is no race: **every failed resolution here kills the process.**

Stack top is `0x005DF5EE` again -- the script VM loop. Memory was flat at 127 MB.

### Consequence: stop patching sites, repair the fault class

Two crashes, two different addresses, one shape: the engine computes a write-back
destination that can be NULL and stores through it unchecked. Site-by-site code caves do
not scale against that, so `src/CKPerf/nullstore.cpp` repairs the *fault* instead --
a store into the null page from game code is decoded, skipped, and execution resumes at
the next instruction.

Deliberately narrow: writes only (a skipped read would leave a register holding garbage,
which corrupts quietly instead of crashing loudly), null page only, game code only, and
only plain MOV forms the length decoder is certain about. Everything else still crashes
and still gets a full report.

The mechanism is verified at startup by executing a real null store from a scratch page
and checking that it was skipped; if that cannot be proven the repair disables itself
rather than resuming execution at an address it may have computed wrongly.

Every distinct site is recorded with a hit count and reported in the log and in every
crash report. **That table is the actual deliverable** -- it maps every place the engine
does this, which is what a proper per-site fix would need.

### Performance observation from the same session

Framerate in a real battle was 37-61 fps (frame 17-28 ms), so the average is fine -- but
**every single second contained 3-5 frames over 50 ms, with the worst between 200 ms and
500 ms**. The blit stayed at 0.1-0.4 ms, i.e. under 1% of the frame. Whatever causes the
stutter is neither the framerate nor the presentation path; it is a periodic spike in the
simulation. That is where the performance track should start.

### Third session: the repair works, and the bug is much wider than two sites (2026-08-19 21:26)

pid 40200, alive 97 seconds. The null-store repair fired on **eight distinct sites,
40 stores in 1.5 seconds**, and the process kept running through all of them:

| Site | Hits | Shape |
|---|---|---|
| `0x005D99A4` | 7 | `mov [eax], esi` |
| `0x005D9BF2` | 15 | store |
| `0x0068F91A` | 5 | `mov [edx], ecx` |
| `0x0068F925` | 5 | `mov [ecx], eax` |
| `0x0068F931` | 5 | store |
| `0x006907E6` | 1 | `mov dword [edx], 0xFFFFFFFF` |
| `0x006907F0` | 1 | `mov dword [ecx], 0xFFFFFFFF` |
| `0x006907F6` | 1 | `mov dword [edx], 0xFFFFFFFF` |

Four separate clusters, all reached from the script VM, all within one and a half
seconds. This is not two unlucky functions; the engine does this everywhere it writes a
script result back.

Then it died anyway, at a **ninth** site the repair refused:

```
006908DB  mov  eax, [esp+0x14]
006908DF  mov  ecx, [eax]        ; eax = 0  -- a READ, not a write
006908E1  cmp  ecx, -1
006908E4  je   0x6908EA
006908E6  cmp  ebx, ecx
006908E8  jge  0x6908EC
006908EA  mov  [eax], ebx        ; and then a write through the same null pointer
```

`if (*p != -1 && value < *p) *p = value;` -- a clamp, through a pointer that is null.

The repair handled writes only, on the reasoning that stepping over a load would leave
the destination register holding garbage. That reasoning was right about *stepping over*
and wrong about the conclusion: the correct repair for a null read is to **deliver zero**
into the destination, which is precisely what a zero-filled page mapped at address 0
would produce. Reads are now repaired that way, and the startup self-test proves it by
loading through a null pointer into a register preloaded with `0xDEADBEEF` and checking
the result is zero rather than the sentinel.

Also learned: writing a half-megabyte minidump per repaired site cost more than the
faults did. Nine of them in 1.5 seconds took the game to 9 fps. Repaired faults now get
a text report only.

### The handle table, and why every one of these crashes has the same shape

`0x00481A20` is four instructions:

```
00481A20  mov eax, [esp+4]              ; handle
00481A24  and eax, 0xFFFF               ; low 16 bits are the slot index
00481A29  mov eax, [eax*4 + 0x798CB8]   ; return table[index]
00481A30  ret
```

A flat **65,536-entry object pointer table at `0x00798CB8`** (256 KB, in `.data`).
`0x00481A40` is its counterpart: it writes 0 into a slot when the object dies, and has
56 callers. `0x00481A20` has **1,690**.

So "resolution failed" simply means the slot is empty -- the object was destroyed. Every
crash so far is a script command dereferencing a handle whose object died first.

That also makes the live object count free: **count the non-empty slots**. The telemetry
thread now does exactly that once a second, along with births and deaths computed by
diffing against the previous sample. Deaths are the number that actually drives this bug
class, since each death is a chance for some script to still be holding that handle.

### Fourth session: the repair works, and then it hangs (2026-08-19 21:38)

Sixteen sites repaired, the game survived all of them -- and then died anyway, this time
because of the tool rather than the engine.

Sites `0x005D98BF` and `0x005D98C3` are one statement, `*p += n`:

```
005D98BD  xor  eax, eax
005D98BF  mov  ecx, [eax]     ; load  *p     -> repaired: ecx = 0
005D98C1  add  ecx, edi       ; ecx = 0 + n
005D98C3  mov  [eax], ecx     ; store *p     -> repaired: skipped
```

With reads answering zero and writes going nowhere, the value never advances. The script
loop waiting for it never finishes: **no frames were rendered for five seconds** while
those two sites took 200,000 faults each, hit the per-site cap, and became fatal.

A crash had been converted into a hang and then back into a crash. Two lessons:

1. **"Reads return zero" is not a safe universal answer.** It is safe for a value nobody
   depends on, and unsafe the moment a loop's exit condition depends on it.
2. The 200,000 cap was the proximate cause of death. A cap is still needed so a runaway
   cannot spin forever, but it must be far above anything healthy, and crossing a warning
   threshold has to be loud.

**The fix: redirect, do not skip.** When the null pointer is in a base register, that
register is pointed at a per-site scratch region and the instruction is *re-executed*.
The load then reads real memory, the store lands in real memory, `*p += n` advances, and
the loop terminates. It also removes the need to know what the instruction does -- only
the addressing mode matters -- so read-modify-write, string and floating-point forms are
covered without decoding any of them.

Scratch is **per site**, not shared. A single shared page aliases unrelated dead objects
so that one site's write becomes another site's read; the startup self-test caught this
immediately when its store stub polluted what its load stub read.

Also tried and rejected: asking the OS for a real zero page at address 0, which would
make all of this unnecessary. `NtAllocateVirtualMemory` refuses with
`STATUS_CONFLICTING_ADDRESSES` (0xC0000018). The attempt is kept in the code because it
costs nothing and would be strictly better if a future Windows ever allowed it.

### Fifth session: watcher worked, and it caught a SECOND, unrelated bug (2026-08-20)

The persistent watcher attached automatically to a Steam-launched process at
`06:35:01`, no user action beyond leaving the watch running. Trainer was **disabled**
this session (`ckrun-config.txt`: `啟用 否`) -- no tweaks, vanilla cheat/tweak values --
so whatever happened here happens in unmodified gameplay parameters, given enough units.

**Object count climbed monotonically for the whole session and never turned around:**

| Time | Live objects | Frame time |
|---|---|---|
| 06:35:12 | 5,352 | ~30 ms |
| 06:35:42 | 12,767 | ~35 ms |
| 06:36:12 | 24,054 | ~45 ms |
| 06:36:32 | 29,279 | ~50 ms |
| 06:36:37 | 30,645 | 105 ms |
| 06:36:38 | 31,030 | 155 ms |
| 06:36:40 | 31,341 (peak) | 216 ms, **2 fps** |

Frame time is flat until roughly 25,000 objects, then rises sharply -- this is the first
hard evidence for the "LAG" complaint independent of any crash, and it points at
something whose cost scales worse than linearly with live object count (O(n log n) at
best, plausibly O(n^2)) rather than at rendering, which stayed at 1.2-1.5 ms throughout.

The null-handle repair fired 11 times across 9 sites and the game kept running through
all of them -- the mechanism from the fourth session held up under real load, with no
hangs and no repeat crashes at the same site (kMaxPerSite change confirmed working).

**Fault #11 killed the process, and it is a different bug:**

```
faulting eip  : 0x0069305D   mov edx, [ecx+4]
fault address : 0x61FA0004   (read from)
region        : base 0x61FA0000  size 0x5C0000  state FREE
```

Not the null page -- a real address, correctly outside the repair's scope (it only
touches `target < 0x10000`), so it produced a full report and killed the process exactly
as it would have with no diagnostic layer installed. The surrounding code:

```
00693044  xor  eax, eax
00693046  mov  ax, [esi]        ; a handle from a list node
0069304A  call 0x481a20         ; resolve it
00693052  test eax, eax
00693054  jne  0x69305A         ; resolution SUCCEEDED here -- eax is a real object
0069305A  mov  ecx, [eax+4]     ; ecx = some pointer field of the resolved object
0069305D  mov  edx, [ecx+4]     ; FAULT: dereferencing that field
```

The handle resolved fine. The crash is in a **field of the resolved object** --
`[eax+4]` -- holding `0x61FA0000`, a base address that was a real allocation (0x5C0000 =
~5.75 MB, 64 KB-aligned like a `VirtualAlloc` block) and is now `FREE`. The registers at
the fault (`ebx=080C0C0C`, `edx=0E0D0F0F`, `esi=0A0A0CB0`, `edi=05040808`) are full of
small, uniform nibbles -- not the pattern of an uninitialised pointer, more consistent
with the memory having been freed and its bytes since overwritten by something unrelated
(tile data, a small-integer array) that got allocated into the same address range.

This reads as a **use-after-free**, not a null-handle bug: something walks a list off a
resolved object, and one node's secondary pointer refers to a block that has since been
freed and reused. It is a plausible second, independent cause of "crashes when the map
is busy" -- distinct from the null-handle class, and NOT something the current repair
can or should touch, since the address is real and the block genuinely no longer exists.

### What this session settles and what it reopens

- The crash is **not** caused by extreme trainer tweaks. This session had none active.
- The live-object census is doing its job: the crash-report line
  `live objects : 31134 (peak this session 31134)` is the first hard number tying a
  fault to battle scale, and the growth curve above is the first hard number tying
  frame-time collapse to battle scale independently of any crash.
- There are now confirmed to be (at least) two independent crash causes: the null-handle
  class this file already repairs, and a use-after-free class this session found for the
  first time. The next fault report needs to be read for WHICH class it is before
  assuming the existing repair is expected to cover it.
