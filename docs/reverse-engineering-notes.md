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

## 「拉遠視角看更大範圍地圖」的可行性 — 已結案 (2026-08-21)

問題：能不能改攝影機高度／縮放主視角，讓一般遊戲畫面看到更大範圍的地圖。

### 主視角無法縮放，這是引擎架構層級的限制（已確證，不是推測）

三項獨立證據互相印證：

1. **整支執行檔只匯入一個 GDI 輸出函式：`SetDIBitsToDevice`**（IAT `0x00706038`，
   呼叫點 `0x0044F536`）。它的簽章沒有「目的地寬高」參數，本質是 1:1 像素搬移。
   沒有 `StretchDIBits`、沒有 `StretchBlt`、沒有任何 DirectDraw／D3D 匯入。
2. **字串表裡找不到任何 `Scale`**（`izz~Scale` 零命中）。
3. **引擎預先存了五套降採樣美術**：`minimap.pak` / `outlines.pak` 內的
   `zoom2` / `zoom4` / `zoom8` / `zoom16` / `zoom32`。產生這些路徑的程式碼在
   `0x0045B308`：

       0x0045B308  mov ecx, [eax]      ; ecx = 縮放階數
       0x0045B30A  mov edx, 1
       0x0045B30F  shl edx, cl         ; 1 << 階數
       0x0045B315  shl edx, 1          ; edx = 2 << 階數 -> 2,4,8,16,32
       0x0045B318  push str."assets/minimap/zoom%d/terrain/%s"

   會預先烘焙五套降採樣圖，正是因為執行期沒有縮放器。這是反證裡最強的一項。

結論：主視角要縮放＝要重寫整條 sprite 光柵化路徑，不是補丁等級的工作。
**一般遊戲畫面想看到更多地圖，唯一的手段就是提高解析度**（見本檔 HiRes 各節；
目前 2048x1152 已驗證乾淨，2560x1440 仍有 x≈2048 起的捲動塗抹）。

### 引擎的「全圖俯瞰」模式：ZoomMap（原版就能用，不需要修改）

- 官方腳本文件 `data/subai/scdoc.xml` 對 `ToggleZoomMap` 的說明就是
  **"Zooms the main map."**
- 註冊點 `0x00456FE0`，與 `BuildMiniMap` / `ShowZoomMap` / `HideZoomMap` 同一批，
  走的是跟 `SetSpeed` 完全相同的腳本註冊路徑（`call 0x005DD770`），
  所以 scdebug.xml 綁得到。
- 實作 `0x00456420`：透過全域 ZoomMap 物件指標 `0x00776394` 的 vtable 派送
  （`+0x10` Show／`+0x14` Hide／`+0x18` Toggle）。
  註：`0x00776394` 就是 ZoomTables 那一節裡被稱為「row_table 後面的無關引擎資料」
  的那個位址——現在知道它是什麼了。
- 切換門檻是 `vxConst.ini` `[zoommap] ToggleTreshold`（原版 1200），
  讀取點 `0x00452C86`；同一節的 `MinimapEmptyColor` 讀取點 `0x00452E64`。
  兩者都是「必要參數」，缺了會噴 `Can't load required param : ToggleTresshold`。
- 版面設定在 `data/interface/common/zoommap.ini`（`MinSize = 200, 220`）。
  ZoomMap 的掃描線表 `col_table` 是**依螢幕寬度索引**（1600 欄對應 1600x1200），
  所以它是鋪滿整個畫面寬度的模式，不是那個 200x220 的最小尺寸。
- 縮放倍率（2/4/8/16/32）由引擎自行決定，**沒有 ZoomIn／ZoomOut 函式**，
  字串表裡只有 Show／Hide／Toggle，所以這是二元切換，不是連續縮放。

### 不需要、也不應該做成作弊項目

一度在 `Cheats.cs` 加過一項 `toggle_zoomout`（一行 `ToggleZoomMap();`）想把這個模式
綁到熱鍵上，**已移除**：使用者實測確認原版本來就能叫出這個俯瞰視角，不需要任何修改。
上面那串位址是為了回答「主視角能不能縮放」而查的，結論（不能，只能提高解析度）
仍然成立，位址本身留著當參考；但本工具不該重複提供遊戲已經給的功能。

## 2560x1440 捲動塗抹 — 根因已定位 (2026-08-21)

**結論先講：CVXVisible 的 dirty-rect 網格，每一列只有 16 bytes = 128 bits，
一個 bit 代表 16 像素，128 x 16 = 2048。視埠內 x >= 2048 的區域沒有對應的 bit，
永遠無法被標記為「需要重畫」，所以永遠不會被重畫。這就是塗抹。**

方法：以 capstone 對磁碟上的 `Celtic kings.exe` 做線性反組譯（1,112,279 條指令），
從 `SetDIBitsToDevice` 反推、再正向讀完 `0x00479400` / `0x0047A020` / `0x0047ABF0`
三個函式的完整控制流。所有數字都是從指令讀出來的，不是推測。

### 結構：CVXVisible+0x10 是一張 75 列 x 128 行的位元網格

| 欄位 | 內容 | 證據 |
|---|---|---|
| `+0x10 .. +0x4BF` | 75 個 16-byte 槽位，每個槽位 = **一列的 128-bit dirty 遮罩** | 建構子 `0x00478AD0`：`mov edx,0x4B` + 每圈寫 4 個 dword、`add ecx,0x10` |
| `+0x4C0` | dirty 旗標 | `0x0047A026` 讀、`0x004796FE` 寫 |
| `+0x4C8 .. +0x4D7` | **目前視埠矩形（世界座標 l, t, r, b）** | `0x00479400` 拿參數逐一比對、`0x004796D3` 寫回 |
| `+0x4D8 .. +0x4E7` | 地圖邊界（視埠的夾限值） | `0x004794EE` 起四段 clamp |
| `+0x4E8 / +0x4F4 / +0x500` | 三個真正的 STL 容器 | 建構子 `0x00478B00`/`0x00478B1D`/`0x00478B39` 各呼叫 `0x006873E0` 初始化 |

這同時修正了先前兩份文件的說法：`+0x4C8` **既不是矩形容器、也不是「普通座標數值」，
它就是視埠矩形本身**。傍晚那次除錯器讀到的 `1692 / 17151 / 4251 / 18456`
正是 (left, top, right, bottom)：寬 = 4251-1692 = **2559**，就是 2560 視埠；
高 = 18456-17151 = **1305**，就是 1440 減去約 135px 的介面列。

### 像素 <-> 格子的換算全部寫死為 16 像素

生產端 `0x0047ABF0`（15 個呼叫點全部走這裡，是唯一的寫入者）：

    0047AC53  mov edi, [ebp+0x4C8]      ; viewLeft
    0047AC62  sub eax, edi              ; rect.left - viewLeft
    0047AC64  sar eax, 4                ; >>4  -> startCol      <-- 格寬 16px
    0047AC6F  mov esi, 0x7F             ; endCol 飽和值 = 127    <-- 上限
    0047AC76  sub ecx, edi
    0047AC78  sar ecx, 4                ; >>4  -> endCol
    ...
    0047AEE4  sub edx, eax              ; rect.top - viewTop
    0047AEE6  sar edx, 4                ; >>4  -> firstRow      <-- 格高 16px
    0047AF00  sub edx, [ebp+0x4CC]
    0047AF07  sar edx, 4                ; >>4  -> lastRow

消費端 `0x0047A020` 把 bit run 還原回像素（`0x0047A7E9` 起）：

    left   = viewLeft + startCol*16
    right  = viewLeft + endCol  *16 + 15
    top    = viewTop  + startRow*16
    bottom = viewTop  + (endRow+1)*16 - 1

兩端一致，**格子就是 16x16 像素**。掃描端 `0x0047A0A8` 對每個槽位以
`cmp eax,4` 掃 4 個 dword（= 128 bits），`0x0047A122 cmp esi,0x7F` 是行號上限；
合併函式 `0x0047CA80` 是純粹的 4-dword OR。四處獨立互證：一列就是 128 bits。

### 為什麼剛好是 2048、為什麼剛好是那些解析度

- 行數需求 = 視埠寬 / 16，上限 **128**（4 個 dword）-> 最大寬度 **2048 px**
- 列數需求 = 視埠高 / 16，上限 **75**（內嵌陣列格數）-> 最大視埠高 **1200 px**

| 解析度 | 需要行數 | 需要列數 | 行 <=128 | 列 <=75 | 實測 |
|---|---|---|---|---|---|
| 1600x1200（原版上限） | 100 | 67 | 是 | 是 | 正常 |
| 1920x1080 | 120 | 60 | 是 | 是 | 正常 |
| 2048x1152 | **128（剛好用滿）** | 64 | 是 | 是 | 正常 |
| 2560x1440 | 160 | 82 | **否** | **否** | 閃退（列）+ x>=2048 塗抹（行） |
| 3840x2160 | 240 | 127 | **否** | **否** | 閃退 + 右半整片壞掉 |

「2048x1152 是目前唯一驗證乾淨的高解析度」不是巧合，**它正好是這個結構的極限**。

三條完全獨立的觀察在此收斂：
1. 塗抹起點 x≈2048 == 128 x 16。
2. sidecar 擴充的是**槽位數（列）**，所以修好了垂直方向的溢位（閃退），
   而水平方向的塗抹原封不動 —— 症狀只剩右側，正好對應「只有一個軸被修好」。
3. `kLimitSiteF`（把 `cmp esi,0x7F` 提高到 `g_columnCap`）確實有觸發卻毫無效果：
   **16-byte 的槽位裡根本沒有第 128 個 bit**，把上限調高沒有東西可掃。

### 溢位是「靜默丟棄」，不是記憶體損毀

行號 >= 128 時，`0x0047AD69` 起那串展開四層的 cascade 會在第四層
（`0x0047AE7A`）把四個 dword 全部寫 0 後結束，不會越過槽位邊界。
所以水平溢位只會**讓那塊區域從此不再被重畫**，不會像先前垂直溢位那樣寫爆物件。
這也解釋了為什麼 sidecar 上線後「不再閃退但畫面照壞」。

方向相依的原因：`0x00479400` 的架構是「捲動時搬移已畫好的像素，再把新露出的
L 形長條標記為 dirty」（搬移在 `0x00469A90`，標記在 `0x004798CC` 等 5 處
`call 0x47ABF0`）。往右捲時新露出的長條落在 x≈2560 -> 行號 160 -> 被丟棄；
往左捲時落在 x≈0 -> 行號 0 -> 正常。

### 已排除的、不要再回頭查的東西

- `SetDIBitsToDevice` 輸出路徑（幾何正確，本來就沒問題）
- `0x00798C5C` 那塊 2048x2048 的 16x16 block 遮罩表面（`0x0047D9E0` 建構，
  大小 = w*h/2048 bytes，即每個 16x16 block 一個 bit）。它**是**參數化的、
  已被 `hires.cpp` 放大到 2560，也確實生效 —— 它只是不是瓶頸。
  附帶記錄：`0x0047DD9C` 還有第三個同型別 2048x2048 建構點（inline 版，
  在 `0x0047DD20` 內），`hires.cpp` 沒有處理，但與主視埠塗抹無關。
- `+0x4C8` 是矩形容器的模型（錯的，它是視埠矩形）
- sidecar 容量、`g_columnCap`、`g_surfaceWidth` 三輪修補（無害但無效）

### 修法評估

**方案 A（已實作於 CKPerf，見本節末）— 把格子從 16px 改成 32px，只動 9 個 byte。**

格數上限（128 行 x 75 列）不變，只把換算的位移量從 4 改成 5：
128 x 32 = **4096 px 寬**、75 x 32 = **2400 px 高**，連 3840x2160 都涵蓋，
而且 4K 只需要 240/2 = 120 行、2025/32 = 64 列，**連 CVXVisible sidecar 都不再需要**。

| 位址 | 原始 byte | 改成 | 意義 |
|---|---|---|---|
| `0x0047AC64` | `C1 F8 04` | `C1 F8 05` | startCol = dx>>5 |
| `0x0047AC78` | `C1 F9 04` | `C1 F9 05` | endCol |
| `0x0047AEE6` | `C1 FA 04` | `C1 FA 05` | firstRow |
| `0x0047AF07` | `C1 FA 04` | `C1 FA 05` | lastRow |
| `0x0047A7F1` | `C1 E3 04` | `C1 E3 05` | left = viewLeft + col*32 |
| `0x0047A802` | `C1 E3 04` | `C1 E3 05` | right |
| `0x0047A805` | `8D 5C 2B 0F` | `8D 5C 2B 1F` | right 的 +15 改 +31 |
| `0x0047A814` | `C1 E3 04` | `C1 E3 05` | top |
| `0x0047A822` | `C1 E1 04` | `C1 E1 05` | bottom |

`0x0047A825` 的 `-1`、`0x0047A122` 的 `cmp esi,0x7F`、`0x0047AC6F` 的
`mov esi,0x7F`、以及所有 `add reg,0x10`（那些是 16-byte 的槽位/矩形步進，
不是像素）**都不要動**。上述 9 處以外，`0x00478000..0x0047C600` 範圍內
所有 `shl/sar ,4` 已逐一分類確認：其餘全部是槽位定址（要保持 x16 bytes），
且該範圍內沒有其他 `lea [reg*16]` / `imul ,16` / `and ,0xF` 形式的換算。

代價：dirty 區域的粒度變粗（32px 對齊），每幀重畫面積略增；
下游只把這些矩形當一般矩形用（`0x0047A8C0` 逐個畫，步進 `add ebp,0x10`），
沒有 16px 對齊的假設，32px 對齊是 16px 對齊的子集，安全。

**方案 B — 把每列遮罩加寬（16 -> 20 或 32 bytes）。** 需要重寫
`0x0047ACC1` / `0x0047AD69` / `0x0047A17A` 三段完全展開的四層 cascade、
`0x0047A0A8` 的 `cmp eax,4` 掃描迴圈、`0x0047A724..0x0047A7A4` 的
`[ebx-8]/[ebx-4]/[ebx]/[ebx+4]` 合併迴圈，以及所有槽位步進。
等於重寫這三個函式，不值得。

**方案 C — 用 `byte [0x008AF118]` 強制每幀全畫面重繪：無效。**
`0x0047A041` 那條路徑吐出的是**舊視埠矩形**，接著一樣要經過 `0x0047ABF0`
重新投影進新網格，一樣在第 127 行被截斷。（該旗標唯一的寫入點是
`0x005E7D5A`，看起來是主控台開關。）

### 可以拿來否證這份結論的預測

1. **2560x1152** 應該仍然塗抹（寬度才是問題），而 **2048x1440** 應該不塗抹
   但會踩到列上限（沒有 sidecar 就閃退）。
2. 塗抹的左邊界應該精準落在視埠左緣 +2048 px，不隨解析度變化 ——
   3840x2160 下也應該是 x=2048，而不是按比例往右移。
3. 往左捲不塗抹、往右捲塗抹。

### 方案 A 的實作（`src/CKPerf/hires.cpp`，2026-08-21）

`kCellSites` 九個站點的 runtime 重寫，與既有的 sidecar 並存但**完全獨立**：
sidecar 修的是列軸（閃退），這個修的是行軸（塗抹），兩者門檻不同、
驗證與拒絕互不影響、log 也分開報告。

- 啟用條件 `g_capacity > 2048`（也就是 128 x 16 = 2048 蓋不住的寬度才動），
  上限 `128 * 32 = 4096`，超過就 `RefusedCapacity`。
  1920x1080 與 2048x1152 一個 byte 都不會被改，已驗證乾淨的基線不受影響。
- 特意獨立於 sidecar 的 2400 門檻：2240 這種寬度會塗抹卻在 2400 以下，
  舊架構下 `HighResolutionInstallDeferred()` 會整個提早 return，
  所以把它改成「兩個修補各自 prepared / 各自安裝」，共用同一個
  suspend + `VirtualProtect` 視窗；原本的 sidecar 主體抽成
  `InstallSidecarPatches()`，內容一行未改。
- 九個站點的原始位元組逐一比對，任一不符就只停用這個修補（不影響 sidecar），
  不寫入任何 byte。
- 連帶更正了 `g_columnCap` 與 site F 兩段當時寫錯的註解：
  提高 `cmp esi,0x7F` 不可能修好行軸，因為 16-byte 槽位裡沒有第 128 個 bit。

交叉驗證工具 `tools/perf/verify_cell_sites.py`（零寫入）：對執行檔套用同一份
重寫到記憶體副本，確認九處原始位元組正確、每處反組譯成預期指令且長度不變、
只有 9 個 byte 改動、兩個函式（707 + 279 條指令）的指令邊界完全沒有位移。
目前對 Steam 版執行檔全部通過。

**已於 2026-08-21 經使用者實機遊玩驗證通過。** 2560x1440（2K）與 3840x2160（4K）下進關卡零閃退、向右向下捲動鏡頭無任何塗抹殘留與破圖，畫面 100% 渲染正確（幀率維持 75~98 FPS）！方案 A（9-byte 32px cell 換算）徹底解決了原版 2048px 寬度上限的捲動塗抹問題，全線高解析度正式驗收通過。

## 大軍團閃退：第一份實機故障報告 (2026-08-22)

呼應本檔前面「Where to resume」清單第 4 項——「隨單位數增加而出現的延遲與靜默閃退，
目前沒有任何 WER 記錄，這本身就是線索」——**現在有了那份一直缺的故障報告。**

### 重現操作

使用者原話：「我最後一個動作是呼叫一個英雄編組去攻擊，然後那個英雄帶了一千多個單位」。
即：一個由英雄統率、成員數量遠超一般規模（1000+ 單位）的編組，下達攻擊指令後閃退。

### 故障報告（`ckperf.dll` 注入式診斷層擷取，`GameRunner.LaunchWithDiagnostics` 路徑）

```
CKPerf fault report #1
2026-08-22 15:22:14.115   thread 20948

  game module   : Celtic kings.exe base 0x00400000 size 0x4D6000
  live objects  : 37894  (peak this session 37900)
  guard         : 0 null write-backs suppressed before this fault
  exception     : 0xC0000005  ACCESS_VIOLATION
  faulting eip  : 0x004AA5C9   Celtic kings.exe+0xAA5C9
  fault address : 0x0094B600   (read from)
  region        : base 0x0094B000  size 0x57000  state RESERVE  protect 0x0  type 0x20000

  registers
    eax 0094B600  ebx 00001722  ecx 00000000  edx 0094B600
    esi 00806568  edi 00006BCF  ebp 00000012  esp 001AFB84
    eip 004AA5C9  eflags 00210246

  code at eip-8 (fault is at +8)
    8D 44 31 18 33 C9 8B D0 83 3A 00 7C 13 41 83 C2
    08 83 F9 04 7C F2 5F 5E 5D 5B 83 C4 18 C2 08 00
```

遊戲在這次 fault 後約 0.9 秒就 `process exiting`——**這次不是被引擎吞掉繼續跑的
非致命例外，是真正的閃退**（telemetry log 裡只有 `FAULT #1`，沒有更高編號，
符合本檔前面故障報告固定寫的那句「最高編號的才是造成結束的那份」）。

### 跟已知的「stale reference 寫回」bug（本檔前面「第二／三／四／五次故障」章節）是不同的東西

- 那一類是**寫入**（write-back）到失效的引用，位址在 `0x0068FACB`/`0x0068FD9E` 一帶，
  guard.cpp 的 null-guard 專門防這個，而這次故障報告明寫 `guard: 0 null write-backs
  suppressed before this fault`——guard 沒有動作，因為這次根本不是它防的那種寫入。
- 這次是**讀取**（read from），EIP `0x004AA5C9` 從未出現在本檔任何一次故障記錄，
  也不在 `Profiler.cs` 的 `KnownRegions` 熱區表或 hires.cpp 的 redirect 站點表裡——
  是全新的位址，需要另外反組譯確認。

### 正式反組譯（capstone，AGY CLI 執行，2026-08-22——粗讀 100% 核對成功）

委託 AGY CLI 寫了 `tools/perf/analyze_crash_004aa5c9.py`（唯讀，手法照抄
`verify_cell_sites.py` 的 section table / VA-to-offset），對 `0x004AA400`..
`0x004AA700` 做 capstone 線性反組譯。**下面粗讀的每一條指令、運算元、位元組長度
全部核對吻合，EIP 落點也完全對上**，可以當結論用了：

```
0x004AA5C1:  8D443118          lea      eax, [ecx + esi + 0x18]
0x004AA5C5:  33C9              xor      ecx, ecx
0x004AA5C7:  8BD0              mov      edx, eax
0x004AA5C9:  833A00            cmp      dword ptr [edx], 0      <- FAULT (讀取 [0x0094B600])
0x004AA5CC:  7C13              jl       0x4aa5e1
0x004AA5CE:  41                inc      ecx
0x004AA5CF:  83C208            add      edx, 8
0x004AA5D2:  83F904            cmp      ecx, 4
0x004AA5D5:  7CF2              jl       0x4aa5c9
0x004AA5D7:  5F                pop      edi
0x004AA5D8:  5E                pop      esi
0x004AA5D9:  5D                pop      ebp
0x004AA5DA:  5B                pop      ebx
0x004AA5DB:  83C418            add      esp, 0x18
0x004AA5DE:  C20800            ret      8
```

#### 函式邊界與呼叫慣例

- 函式範圍 `0x004AA4F0`–`0x004AA69B`（430 bytes，83 條指令），前後都是
  `nop` 對齊到下一個函式，邊界乾淨。
- `__thiscall`：`ecx` = this，堆疊帶兩個 dword 參數（X、Y 座標，
  `ret 8` 由被呼叫端清堆疊）。進入函式先 `mov esi, ecx` 把 this 存進 esi。

#### 完整控制流（confirmed，不是猜測）

1. `0x004AA4F0`–`0x004AA530`：邊界檢查。讀全域指標 `[0x895E40]`，
   把傳入的 (X, Y) 跟 `[eax+0xA8..0xB4]` 這個矩形比對，超出範圍就直接跳到
   共用出口 `0x004AA694`（什麼都不做，安全返回）。
2. `0x004AA536`–`0x004AA561`：位元遮罩檢查。`ecx = (Y>>4)*[eax+0x64] + (X>>9)`，
   查 `[eax+0x54+ecx*4]` 的第 `(X>>4)&0x1F` 個 bit 是不是 1，是的話一樣跳出口。
   （這兩步都是 this=`[0x895E40]` 那個「外部管理員」物件的資料，不是本函式自己的。）
3. `0x004AA567`–`0x004AA5C1`：**這裡開始才是本函式自己 (esi) 的資料**。
   `eax = X>>4`, `ecx = Y>>4`，跟 `[esi]`/`[esi+4]`/`[esi+8]`/`[esi+0xC]`
   （esi 自己的一個矩形邊界）比對；呼叫子函式 `0x004AA730` 算出
   `delta_x = (X>>4) - origin_x`、`delta_y = (Y>>4) - origin_y`
   （origin 存在 esi 自己的欄位裡）；接著算網格位移：
   `edx = delta_x*33`，`ecx = (delta_y + edx*4) * 32 = (delta_y + delta_x*132) * 32`，
   目標陣列元素位址 = `ecx + esi + 0x18`。
4. `0x004AA5C5`–`0x004AA5D5`（**故障處**）：在算出來的那個位址上，
   固定掃 4 格（每格 8 bytes），找第一格 `dword < 0`（空格）的位置；
   4 格都不是空的就直接 `ret 8` 放棄（沒有任何錯誤處理，靜默失敗）。
5. `0x004AA5E1`–`0x004AA693`：找到空格就把 (X, Y) 寫進去，順便更新
   `esi` 身上好幾個計數器／索引（`+0x8D764`、`+0x8D798`、`+0x88218`..`+0x88224`、
   `+0x88228` 那塊做 `add dword ptr [esi], 0x01010101`——四個 packed byte
   計數器一次全部 +1）。

#### 是誰在呼叫這個函式（xrefs，全 `.text` 線性掃描找到 5 處）

四處直接硬編 `mov ecx, 0x806568`（同一個全域物件，`.data`/BSS 段的固定實例）：
`0x0049EEC6`、`0x004A13A5`、`0x004A23D5`、`0x005F130C`；第五處
`0x004AA715` 在緊鄰的下一個函式 `0x004AA6A0` 裡，this 是從呼叫端參數
（`[ebp+8]`）轉傳進來的，X/Y 座標還先各呼叫一次 `0x006DB6E0`
（浮點轉整數）——`0x004AA6A0` 看起來是「接收 float 座標」的外層包裝，
轉成整數後呼叫同一個核心函式。

#### 故障當下的位移量（confirmed）

`esi=0x00806568`，故障位址 `0x0094B600`，扣掉固定的 `+0x18` 之後，
偏移量是 `0x145080`（約 1.33 MB）——落在 `region: base 0x0094B000
size 0x57000 state RESERVE`，也就是這塊只保留、沒提交的記憶體。
1.33 MB 的偏移量對照公式 `(delta_y + delta_x*132) * 32`，反推
`delta_x`（往格線那個方向的座標差）大約落在 **300 以上**的等級——
也就是說，觸發故障的 (X, Y) 跟 esi 自己記的 origin，在其中一軸上差了
至少幾百格（每格 16 單位），是「座標差距很大」造成偏移量爆掉，
不是「陣列真的有第 1000 個成員被讀到」（迴圈本身固定只跑 4 次）。

### 附件

- `ckperf-20260822-150024-pid23712.log`（完整 telemetry，含這次故障前後的
  frame/memory/物件數趨勢）與 `ckcrash-20260822-152214-01.dmp`（minidump）
  留在 `%LOCALAPPDATA%\CKToolkit\diag`，還沒歸檔進本檔案，下次要深入分析時
  先去那裡找同一個時間戳的檔案。

### 排除實驗：純粹的超大編組不會炸（使用者實測，2026-08-22）

使用者接著測了「下一步」建議的排除實驗：組一個同樣超大（1000+ 單位）的編組，
**不下攻擊指令**——結果**不會閃退，只是有點 lag**。

這排除了「單純編組人數/物件數本身」是觸發條件：光是把 1000+ 單位塞進同一個
編組、讓它們站在那裡（甚至移動），引擎撐得住，只是效能變差。**閃退的必要條件
是對這個超大編組下攻擊指令**，故障點很可能就在「攻擊指令」專屬會走到的那段
程式碼裡（例如建立攻擊目標清單、逐一驗證編組成員、或指派攻擊任務給每個成員），
而不是每一幀都會執行的一般編組維護/繪製邏輯——這跟前面粗讀出來「函式看起來像
在檢查某個陣列前 4 格是否非零」的猜測方向是吻合的（如果那是「攻擊指令建立目標
清單」相關的一次性檢查，而不是每幀都跑的東西，就能同時解釋「為何只有下攻擊指令
才會觸發」跟「為何 fault 發生在 ecx=0 也就是第一輪」）。

### 這個函式本身看起來跟「編組/英雄」沒有直接關係——要小心不要走錯方向

反組譯結果有一點跟原本的直覺不一樣：`0x004AA4F0` 本身只是一個很通用的
「在 (X, Y) 座標登記一筆資料到某個以 esi 為 this 的網格物件，4 格滿了就默默放棄，
不報錯」的工具函式，5 個呼叫點也沒有一個名字或參數看起來專屬於編組／英雄／
攻擊指令——4 處直接寫死呼叫同一個全域單例（`0x00806568`），像是某種
全域的座標事件/標記登記系統（例如視覺特效觸發點、聲音播放點、或某種
全域格狀索引），跟「英雄帶超大編組攻擊」不是同一層概念。

所以合理的解讀是：**這個函式大概率不是 bug 的根本原因，只是最後被炸到的那一棒。**
真正該問的問題變成——「英雄編組攻擊指令」為什麼會產生一個座標，讓
delta_x/delta_y 算出來的偏移量差了幾百格（見上面「故障當下的位移量」小節），
遠遠超出這個全域物件（esi）原本記錄的 origin 附近？可能性：
(a) 攻擊指令本身把某個座標算錯／算爆了（例如 1000+ 單位在算「編組平均位置」
或「攻擊目標中心點」時整數溢位或用了錯的單位換算）再傳進來給這個登記函式；
(b) 這個全域物件的 origin 本身就沒有針對「攻擊」場景正確設定/更新過。

### 下一步

1. **往上追、不要往下追。** 已知呼叫點 `0x0049EEC6` / `0x004A13A5` /
   `0x004A23D5` / `0x005F130C` 各自把 (X, Y) 從哪個結構的哪個欄位讀出來——
   對這四個呼叫點各自往回反組譯它們自己的函式，找出 X/Y 的真正來源，
   看有沒有一條路徑會被「編組成員數」或「攻擊指令」影響到座標計算。
2. **確認 `0x00895E40` 全域指標與 esi=`0x00806568` 這兩個物件是什麼。**
   前者的 `+0xA8`..`+0xB4` 矩形、`+0x54`/`+0x64` 的位元遮罩，看起來像某種
   「全地圖分區管理員」；後者的 `+0x18` 起始、8-byte/格、4 格上限的小陣列，
   加上 `+0x88218`..`+0x88228` 那一串計數器，是否對得上遊戲資料裡任何
   已知的類別（`docs/HMMSYS_PackFile格式.md` 或已解開的 `.sc.xml` 類別定義
   可能有線索）。
3. **實機重現實驗**：小編組（10~20 人）一樣下攻擊指令，看是否也會炸——
   如果不會，代表確實跟「1000+ 這種遠超一般規模的人數」有關（見前面
   「排除實驗」小節已排除「純編組不下攻擊指令」這個變因，這是下一個要排除的）。
   如果分析器（`Profiler.cs`）這次能提前掛上（先在分析器分頁按「開始分析」
   再操作），偵錯器模式會直接給 minidump + JSON，不必再靠 ckperf.dll 那邊
   湊資料。

### 執行期修復：`arrayguard.cpp`（2026-08-22，使用者要求「完全修好」）

上面「往上追」還沒做完——4 個呼叫點各自的座標來源還沒追出來，所以真正
「為什麼攻擊指令會算出離譜座標」的根因**仍然未知**。但使用者要的是先讓遊戲
不要閃退，不是非得先找到根因才能動手；而這個 bug 剛好符合本檔 `guard.cpp`
（`0x0068FACB`/`0x0068FD9E` 那次）已經驗證過的修法哲學：**在唯一真的會炸的
那個讀取點做防護，而不是等追完整條因果鏈**——只要防護點本身的行為
「跟這個函式自己既有的失敗語意完全一致」，就不算是亂猜。

這個函式自己就有「失敗語意」可以借：4 格全部非空時，它本來就什麼都不做、
直接 `ret 8`，呼叫端拿不到任何回饋。所以防護的做法是：**每次讀格子前先確認
那塊記憶體真的有 COMMIT、讀得到**（用 `common.cpp` 既有的 `SafeRead`，
VirtualQuery-based，設計上就是「不管地址多離譜都不會出例外」），讀不到就當
「這格不是空的」處理——這正是函式在真正遇到 4 格都滿時本來就會做的事，
行為上完全沒有新增分支，只是把「無法判斷」也歸進「當作滿格」這個既有選項。

新增檔案 `src/CKPerf/arrayguard.cpp`：
- 驗證 `0x004AA5C5`（迴圈起點 `xor ecx,ecx`）的 18 個原始位元組完全吻合才會
  patch，不吻合就拒絕（同 `guard.cpp` 的驗證優先原則）。
- 用 `__declspec(naked)` + MASM inline asm 蓋一個 5-byte `jmp` 過去，
  把整個 4 格迴圈換成呼叫一個 C++ 函式 `FindFreeSlot(base)`（逐格
  `SafeRead`，讀不到就回傳 -1 並計數，讀到負數就回傳格子編號）——
  eax（陣列基底位址）全程不變、ecx 在找到空格時等於格子編號，跟原本
  的呼叫慣例完全對齊，找到空格跳 `0x004AA5E1`（寫入路徑），沒找到跳
  `0x004AA5D7`（原本的共用結尾）。
- 可疑讀取次數計入 `g_suppressedArrayReads`，跟 `guard.cpp` 的
  `g_suppressedNullStores` 一樣寫進每份故障報告與 telemetry（只在數字變動
  時才印，同樣的「只在移動時才報」紀律）、可用 `arrayguard=0` 關掉。

**已做的驗證**（實機遊戲測試前）：
1. `FindFreeSlot` 的獨立邏輯測試（`tools/perf` 之外，純測試用途，未簽入）：
   4 格全滿、格 0/2/3 各自為空、以及**完全複製故障當下的記憶體狀態**
   （`VirtualAlloc(..., MEM_RESERVE, PAGE_NOACCESS)`，只保留不提交，
   跟故障報告的 `region: ... state RESERVE` 一模一樣）——全部通過，
   RESERVE 情境下正確回傳 -1、不閃退、計數器有增加。
2. Cave 的暫存器保留與分支邏輯測試（技巧相同，但指向測試用的假出口位址，
   因為真正的 `0x004AA5E1`/`0x004AA5D7` 只存在於真的遊戲行程裡）：
   確認 `eax` 全程等於原始基底位址、`ecx` 正確等於命中的格子編號、
   在 RESERVE-not-COMMIT 的情境下正確跳到「沒找到」出口而不閃退。
   全部通過。

**還沒做、也做不到的驗證**：實機重現「英雄帶 1000+ 單位下攻擊指令」再確認
真的不閃退了——這需要使用者實際玩。建置已完成並內嵌進
`assets/ckperf/ckperf.dll`（`tools/perf/build-ckperf.ps1` 產出，SHA256
`81EA680E423D61478C0E688EF8525A12B1D6EF5AB068A5507AB096F3BC1CB852`），
下次用「帶診斷啟動遊戲」或修改器頁的「啟動遊戲」跑遊戲時就會生效；
故障報告（若還有别的原因閃退）會多印一行 `arrayguard : N 次不可讀的
格子讀取已被攔截`，那個數字從 0 變成非 0 就是這次防護實際生效過的證據。

### 第二次實機測試：防護生效了，但防錯了東西（2026-08-22 18:39）

使用者帶 1300 個士兵下攻擊指令，**又閃退了**。這次是從分析器分頁啟動的，
所以輸出跑到 `Profiler` 的預設資料夾（桌面）而不是 `%LOCALAPPDATA%\CKToolkit\diag`——
下次找不到檔案時先想到這件事。

telemetry log（`ckperf-20260822-183033-pid20772.log`）把整件事講得很清楚：

```
[18:30:33.679] grid-slot read guard installed. 0x004AA5C5 now redirects ...
[18:39:11.539] FAULT #1  code=0xC0000005 (ACCESS_VIOLATION)  eip=0x004AA5E1
[18:39:11.545] arrayguard: suppressed 1 unreadable grid-slot reads so far (+1 ...)
```

防護**有裝上、有攔到、計數器有動**，遊戲還是死了，而且死在
`0x004AA5E1`——也就是 `kFoundExit`，「找到空格、要寫進去」那一條：

```
0x004AA5E1:  891CC8   mov dword ptr [eax + ecx*8], ebx
```

#### 為什麼 SafeRead 是錯的判準

`arrayguard.cpp` 第一版問的問題是「這一格**讀得到**嗎」。但一個離陣列尾端幾百格的
位址，完全可能落在「已提交、可讀、不可寫」的頁面上：於是防護放行 → 掃描在一塊
根本不屬於陣列的記憶體裡找到「空格」→ 崩潰點從 `0x004AA5C9` 的讀往下移四條指令，
變成 `0x004AA5E1` 的寫。

更糟的是，可讀性不只是「不夠」，是**危險**：萬一那頁剛好可寫，這個防護就會把一次
看得見的閃退，換成一次寫進別人記憶體的靜默破壞。頁面保護不是該問的問題，
**位址在不在陣列裡**才是。

#### 陣列真正的邊界（這是這次新拿到的關鍵事實）

物件的初始化函式 `0x004AA010` 一次把整個陣列清掉：

```
0x004AA02A:  push 0x88200            ; count
0x004AA02F:  lea  ecx, [esi + 0x18]  ; dst
0x004AA032:  push 0xff               ; fill
0x004AA043:  push ecx
0x004AA04A:  call 0x41E880           ; memset(esi + 0x18, 0xFF, 0x88200)
```

所以網格陣列**精確地**是 `[esi+0x18, esi+0x18+0x88200)`，而且預填 `0xFF`——
這同時解釋了掃描為什麼用 `dword < 0` 判斷空格：`0xFFFFFFFF` 就是「空」的寫法。
三件事互相對得上：

- 物件下一個已知欄位在 `+0x88218`，而 `0x18 + 0x88200 = 0x88218`，剛好接上；
- `0x88200 / 32` bytes per cell `= 17424 = 132 x 132`；
- `132` 正是引擎自己的位移公式 `(delta_y + delta_x * 132) * 32` 裡的列距。

回頭看 15:22 那次故障：`esi = 0x00806568`、base `0x0094B600`，位移 `0x145080`，
換算成 cell 41604，也就是 `delta_x = 315`——在一個只有 132 格寬的網格裡。
不是差一點點，是差了約 2.4 倍。它碰到的每一個 byte 都是別人的。

#### 修法：把引擎漏掉的邊界檢查補上

`arrayguard.cpp` 改成純組合語言的範圍檢查，不再呼叫 C++ 函式、熱路徑上也不再用
`SafeRead`（一旦證明 base 在物件自己的陣列裡，那塊記憶體就是初始化函式 memset 過的
同一塊配置，普通讀取就是對的，每格做一次 VirtualQuery 只是白付成本）：

```
offset = eax - esi - 0x18
拒絕，除非 (unsigned)offset <= 0x881E0     ; 0x88200 - 0x20，四格 32 bytes 要全部放得下
拒絕，除非 (offset & 0x1F) == 0            ; 引擎只會算出 cell*32
拒絕時：計數 +1，跳 0x004AA5D7（函式自己既有的靜默放棄）
放行時：跑原本的 4 格掃描，找到 → ecx=格號、eax=base，跳 0x004AA5E1
                              沒找到 → 跳 0x004AA5D7
```

用**無號**比較是刻意的：base 落在陣列之前會 wrap 成一個巨大的無號數，同一條
比較就一起擋掉了。這順帶清掉一個引擎自己的老問題——`(X>>4, Y>>4)` 沒通過
`0x004AA585` 的矩形檢查時，引擎做的是 `xor eax, eax; jmp 0x004AA5C5`，
**帶著 base = 0 走進同一個迴圈**，也就是原版程式碼自己會去解參考位址 0。
現在那條路徑一樣走到靜默放棄出口，不必再靠 `nullstore.cpp` 事後修補一次 null 讀取。

Cave 的暫存器紀律（建置後反組譯核對過，見下）：不 push 任何東西，所以兩個出口的
堆疊跟進入時 byte-for-byte 相同（`kNotFoundExit` 是 `pop/pop/pop/pop/add esp/ret 8`，
這件事非做對不可）；`ebx`/`esi`/`edi` 全程不碰；`eax` 只讀不寫；`ecx`/`edx` 是原本
就死掉的暫存器。**不借用 `ebp` 當 scratch**——沒有必要為了省一個暫存器，去賭一個
「ebp 在這裡是死的」的論證。

出口位址一律寫成字面立即數並加 `static_assert` 綁住具名常數。原因值得記下來：
MSVC 的 inline asm 會把 C++ 變數名當成**記憶體運算元**，所以 `mov edx, kFoundExit`
會從那個常數的位址**載入**，而不是把它的值當立即數——第一版就是這個寫法。

建置後從 `assets/ckperf/ckperf.dll` 反組譯出來的 cave（確認立即數是立即數）：

```
8BC8             mov     ecx, eax
2BCE             sub     ecx, esi
83E918           sub     ecx, 0x18
81F9E0810800     cmp     ecx, 0x881e0
7725             ja      Reject
F6C11F           test    cl, 0x1f
7520             jne     Reject
33C9             xor     ecx, ecx
8BD0             mov     edx, eax
833A00      Scan:cmp     dword ptr [edx], 0
7C10             jl      Found
41               inc     ecx
83C208           add     edx, 8
83F904           cmp     ecx, 4
7CF2             jl      Scan
BAD7A54A00       mov     edx, 0x4aa5d7
FFE2             jmp     edx
BAE1A54A00  Found:mov    edx, 0x4aa5e1
FFE2             jmp     edx
F0FF05707F0210 Reject:lock inc dword ptr [g_suppressedArrayReads]
BAD7A54A00       mov     edx, 0x4aa5d7
FFE2             jmp     edx
```

計數器語意跟著改了：不再是「攔下幾次讀不到的格子」，而是
「拒絕幾次落在網格外的登記」，`crash.cpp` 與 `telemetry.cpp` 的字串同步更新。

**根因仍然未解**：為什麼攻擊指令會產生一個離網格 315 格遠的座標，還通過了
`0x004AA567..0x004AA594` 那個矩形檢查？前面「下一步」第 1、2 項還是要做——
特別值得懷疑的是矩形（`[esi]`/`[esi+4]`/`[esi+8]`/`[esi+0xC]`）跟原點
（`[esi+0x10]`/`[esi+0x14]`）是不是會跟這個**固定 132x132** 的陣列失去同步，
例如矩形按實際地圖尺寸設定、陣列卻永遠是 132x132。這次補的是引擎漏掉的邊界檢查，
不是那條因果鏈。

### 順帶修掉：非 ASCII 輸出資料夾會把整份故障報告毀掉

18:39 那份故障報告 `ckcrash-20260822-183911-01.txt` 在磁碟上是 65535 bytes，
第三行之後**全部是 NUL**。活下來的只有：

```
CKPerf fault report #1
2026-08-22 18:39:11.223   thread 7292

  telemetry log : C:\Users\nojac\Desktop\（然後就沒有了）
```

一份真實崩潰的故障報告就這樣整份沒了，而且是靜默地沒。機制：

- `crash.cpp` 用 `Append(..., "  telemetry log : %S\r\n\r\n", LogFilePath())`。
  窄字元 printf 的 `%S` 走 C locale 轉換，而 locale 是 `"C"`，只認 ASCII。
  這次的路徑是 `C:\Users\nojac\Desktop\紀錄\...`，第一個中文字就轉不過去，
  `_vsnprintf_s` 失敗回傳 -1。
- `common.cpp` 的 `Append()` 把 `n < 0` 一律對映成 `return cap - 1`。那個寫法
  對「截斷」是對的，對「格式化失敗」卻是災難：`pos` 變成 65535，之後每一次
  `Append` 都被 `if (pos >= cap - 1) return pos` 擋掉什麼都不寫，最後
  `WriteFile(h, buf, (DWORD)pos, ...)` 把 64 KB 幾乎全是零的靜態緩衝區倒進檔案。

兩邊都修：

1. `Append()` 在 `n < 0` 時不再把 `pos` 推到 `cap - 1`。`_vsnprintf_s` 搭
   `_TRUNCATE` 在「截斷」和「失敗」兩種情況都會補 NUL（失敗時留下空字串），
   所以改成量實際寫出去多少：`buf[cap-1] = 0; return pos + strlen(buf + pos);`。
   截斷照樣把 `pos` 推到塞得下的結尾，失敗則讓 `pos` 原封不動，
   **報告剩下的部分照常寫出來**。
2. 不再把寬字串餵給窄 printf。`common.cpp` 新增 `WideToUtf8()`
   （`WideCharToMultiByte(CP_UTF8, ...)`，一定補 NUL，轉不過去時退回
   `"(path could not be converted)"` 這種看得見的字串，不會再變成空白行），
   `ckperf.h` 宣告。`crash.cpp` 與 `dllmain.cpp` 這兩個 `%S` 站點都改用 `%s` +
   `WideToUtf8`。UTF-8 是對的目標：報告其餘內容都是 ASCII，整份檔案仍是合法 UTF-8。

`dllmain.cpp` 那一站也是真的壞掉的——18:30 那份 log 第 5 行
（`[18:30:33.669] `）就是被同一個原因洗成空白的 `log file:` 行。

## 2026-08-23: Null indirect call repair and reporter EIP underflow

The 08:54 session (pid 27096) established a three-stage chain at the end of a 35,764-object
battle:

```text
0069305D  mov edx, [ecx+4]       ecx=0 -> read AV at 0x00000004
00693070  call dword ptr [edx+4] edx=0 -> read AV at 0x00000004
00000000  DEP execute AV         return address on stack = 0x00693073
```

The generic repair redirected both base registers to zero-filled per-site scratch. That is
valid data semantics for the first ordinary load, but invalid for the second instruction:
`FF /2` consumes the loaded dword as the next EIP. Scratch supplied zero, so the repair
manufactured the DEP fault. `nullstore.cpp` now rejects memory forms of `FF /2,/3` (CALL) and
`FF /4,/5` (JMP); rejected control flow is reported and returned to the engine unchanged.
The exact field bytes `FF 52 04` are part of the startup regression test.

While reporting EIP 0, `WriteReport` computed `eip-8 = 0xFFFFFFF8`. `SafeRead` then computed
`end = addr+32 = 0x18`; because the unsigned validation loop saw `p >= end`, it skipped every
`VirtualQuery` check and entered `memcpy`, faulting at shipped `ckperf.dll+0x23FE`. `SafeRead`
now rejects wrapping ranges and non-progressing/wrapping regions, while `ReadCodeWindow`
independently rejects EIP 0..7 before subtraction. Both have DLL-startup self-tests; failure
disables crash reporting and null repair for the session rather than risk a recursive VEH fault.

This does not resolve why the object returned by `0x00481A20` has a null or freed `[eax+4]`
field. That lifetime/initialisation problem remains ISSUE-006. The change only prevents the
diagnostic repair from inventing a new control-flow target and guarantees the original fault can
be captured faithfully.

## 2026-08-23: second corrupt VM lvalue proves high-half contamination

The next field run (pid 3736) validated the diagnostic fixes and then died at the previously
open ISSUE-017 success-path store:

```text
005D9BE2  mov edx, [esp+6]
005D9BE6  mov byte ptr [eax+edx], bl

eax          = 0x15FEE2C0
edx          = 0x428800F6
fault target = 0x5886E3B6 (FREE)
VM bytes     = DA 00 F6 00 88 42
```

At the fault, the reconstructed six-byte lvalue starts at current `esp+4`. Decoding the exact
VM representation gives `objectId=0x00DA` and `byteOffset=0x428800F6`; the handle resolves to a
live object, then the high half of the offset drives the store 1.1 GB away. The earlier dump at
the same instruction contained `0x4A8800E4`. In both cases the low half (`0x00E4/0x00F6`) is a
plausible field offset while the high half (`0x4A88/0x4288`) is stale data. This is not a
35,000-object capacity boundary; it is repeatable partial corruption of the packed lvalue.

### AV-time repair instead of a guessed offset ceiling

`vmlvalue.cpp` registers eight exact stores: the dword and byte success/null pairs plus three
multi-field assignment families. Each site records expected instruction bytes, length, target
equation, and repair mode. Nothing runs on valid assignments. After a real write AV, the handler
requires all of the following before changing context:

1. EIP is one of the eight sites and still belongs to the Steam image at base `0x00400000`.
2. The live bytes exactly match the disassembled instruction.
3. The exception is a write AV and its reported target equals EAX or 32-bit `EAX+EDX`, as declared.

Single-store handlers skip only the faulting MOV and resume their original epilogue. Multi-store
handlers redirect EAX to a per-site 4 KB scratch page and re-execute, so their interleaved stores,
reads, and register pops retain the original stack discipline. Startup self-tests exercise all
eight real game instructions and disable the subsystem on any mismatch.

The source of the contaminated high word remains unknown. This is a narrow repair of a store that
Windows has already proved invalid, not a claim that the producer-side lifetime bug is solved.

## 2026-08-23: per-EIP scratch still hangs across VM opcodes

The first field run with `vmlvalue.cpp` prevented all previously fatal assignment stores, then
froze at the old `0x005D98BF` integer `+=` handler. The telemetry is conclusive: 35,883 live
objects, zero births/deaths, and zero frames for six minutes while the same null load accumulated
exactly 5,000,000 repairs. At the configured cap the handler stopped repairing and the game exited
on that AV.

The previous explanation was incomplete. Within one invocation, `0x005D98BF` redirects EAX and
`0x005D98C3` stores through that same redirected EAX, so its private scratch value does advance.
But the script loop checks the same dead logical lvalue through a different VM opcode/EIP, which
maps to a different scratch slot and still reads zero. Per-EIP scratch preserves instruction-local
progress, not cross-opcode lvalue identity.

The VM dispatcher already defines the correct control path for an operator that cannot continue:
after the indirect handler call at `0x005DF5EB`, return value 2 branches at `0x005DF5FA` to
`0x005DF921`, sets status 3, and leaves the current script/atomic section. The repair for exact site
`0x005D98BF` now selects a naked epilogue equivalent to the handler's own cleanup but returns 2:

```asm
pop edi
mov eax, 2
pop esi
add esp, 8
ret
```

This does not guess a loop bound and does not attempt to make all dead objects share memory. It
uses an error/abort path already designed into the interpreter. Startup self-test checks the live
`8B 08` instruction and the selected resume stub; live validation must still prove that frames and
simulation resume after the bad script is aborted.
