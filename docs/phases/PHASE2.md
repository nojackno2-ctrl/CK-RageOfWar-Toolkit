You are implementing Phase 2 of the CK-RageOfWar-Toolkit project: the Perf module.

FIRST read, in this order:
  - AGENTS.md            collaboration rules and patching discipline, binding
  - docs/SPEC.md         sections 3, 4, 5 and 8 are the ones this phase implements
  - AI_HANDOFF.md        current state and decision log
  - docs/reverse-engineering-notes.md   the 67KB of accumulated findings behind every
                         address below. Consult it whenever an address needs context.

The authoritative behaviour source is the predecessor C++ implementation (CK_RageOfWar 性能最佳化, now deleted),
specifically patches.cpp, patches.h, profile.cpp, profile.h, game.cpp, config.cpp.
Read them. Port their logic AND their comments. Every hex address and every paragraph of
reverse-engineering rationale must survive the port - those notes cost real work to obtain
and cannot be regenerated. Where the C++ explains WHY a patch is shaped a certain way,
carry that explanation into the C# verbatim in meaning, translated only as needed.

Cross-check oracles, for behaviour questions the C++ leaves ambiguous:
  tools/perf/patch_videomode.py, tools/perf/large_address_aware.py,
  tools/perf/add_resolutions.py, tools/perf/hmmpak.py

EXECUTION ENVIRONMENT:
   You have NO permission to run terminal commands. Do not run git, dotnet, msbuild or
   anything else - the attempt is denied and aborts your whole run. Ignore the "inspect
   git status first" rule in AGENTS.md for this session. Read and write files only.

   The calling agent compiles and tests after you finish and sends you the errors. Because
   you cannot compile, be conservative: plain obviously-correct C# over clever constructs,
   check every using directive, namespace, type name and signature against the file that
   declares it, and leave no stub that other code already calls.

   Phase 1 is committed and green: the solution builds warning-free and nine SelfTest
   groups pass. Do not regress any of it.

---

SCOPE - src/CKToolkit/Core/Perf/

Implement all nine capabilities from SPEC.md section 5. Suggested file split; adjust if
you have a better structure, but keep one concern per file:

  PerfModule.cs          IPatchModule implementation; registers this module's steps into
                         PatchPipeline and its signatures into BackupManager
  LargeAddressAware.cs   Exe. The PE characteristics bit. 2GB -> 4GB user address space.
  VideoModePatch.cs      Exe. SetVideoMode at 0x006BE340 becomes xor eax,eax; ret. Without
                         it the obsolete 16bpp mode switch fails on modern Windows and the
                         engine null-derefs at 0x00657DCC. With it GDI translates the
                         engine's RGB565 framebuffer to the 32bpp desktop DC.
  ResolutionWriteback.cs Exe. 0x00658FAB. Stops the engine writing Resolution=0 into
                         vxSettings.ini on shutdown.
  ZoomTables.cs          Exe. The HD patch, and the most delicate one. The ZoomMap scanline
                         tables at 0x0076FF78 and 0x00774A94 are hardcoded for 1600 columns.
                         Relocate them into an appended .ckhr section sized for the target
                         width and rewrite every immediate that references them. PeFile
                         already provides AddSection and VA/file-offset conversion.
  LauncherDisplay.cs     Launcher. NOP the ChangeDisplaySettingsA calls at 0x14000159B and
                         0x1400019F9 so nothing touches display settings.
  LauncherModeTable.cs   Launcher. Rewrite entry 0 of the hardcoded mode table at
                         0x1400043B0 so the launcher switches the desktop to the game
                         resolution on start and restores it on exit.
  Resolutions.cs         data.pak. Read, append to, and select from the [Resolutions] list
                         in VXCONST.INI inside the pak.
  VxSettingsPatch.cs     vxSettings.ini. NoObjectAnimations, NoWaterAnimation, Resolution.
  Profiler.cs            The sampling profiler. See below.

MUTUAL EXCLUSION - the launcher has two incompatible ways to handle the desktop resolution.
LauncherDisplay NOPs the ChangeDisplaySettingsA call; LauncherModeTable rewrites the table
the suppressed call would have read. With suppression applied that table is dead code.
Enabling either MUST disable the other, enforced in the module itself and not merely in the
UI, because Phase 5 and Phase 6 both drive this. Shipped default is the auto-switch, which
was a user decision on 2026-08-18 reversing an earlier prohibition.

RESOLUTION IS STORED AS WxH, NEVER AS AN INDEX. vxSettings.ini stores the player choice as
an index into the [Resolutions] list, and that list changes whenever data.pak is rebuilt.
So the pipeline must select the resolution AFTER data.pak has been written, by looking the
WxH up in the list that actually ended up in the file.

HD CEILING - the shipped configuration is frozen at 1920x1080. That is the empirically
verified ceiling: 2048x1152 and above reach the main menu but crash on entering gameplay,
and each crash writes Resolution=0, so the next launch silently falls back to 1024x768.
Keep the machinery general - arbitrary table capacity and arbitrary WxH must still work,
because the ceiling may be raised later - but ship the conservative default and surface the
warning text the predecessor GUI showed.

---

SIGNATURE REGISTRATION - this is the part that makes status honest again.

Phase 1 established that BackupManager tracks per-file signature coverage and reports
Unknown until every expected signature is registered. Register this module's detectors:

  Exe:          laa, video_fix, hires_zoom, res_writeback     (key_map arrives in Phase 4)
  Launcher:     launcher_display, launcher_mode_table         (completes Launcher coverage)
  DataPak:      resolutions_append                            (trainer_marker in Phase 4)
  VxSettings:   vxsettings_custom                             (completes VxSettings coverage)

After this phase, Launcher and VxSettings coverage become complete and status must report
real pristine/patched verdicts for them instead of unknown. Exe and DataPak stay incomplete
until Phase 4, and must keep reporting unknown. Do not fake completeness.

Each signature probes the file bytes for its own patch and must be exact: it has to
distinguish "we applied this" from "vanilla" from "some other tool applied something".
A signature that returns a false negative causes the stale-backup guard to treat a patched
file as a game update, which is the failure mode that destroys a vanilla backup.

---

PIPELINE INTEGRATION

PerfModule plugs into PatchPipeline per SPEC.md section 4. Its steps layer onto the
pristine bytes in this order within each target file:

  Exe:      LAA -> VideoMode -> HiRes ZoomMap -> ResolutionWriteback   (KeyMap follows in Phase 4)
  Launcher: DisplaySuppress XOR ModeTable
  data.pak: [Resolutions] append   (runs AFTER the Trainer steps once Phase 4 lands)
  vxSettings.ini: animation switches, then Resolution selected from the final list

Every step takes bytes and returns bytes. No step writes to disk; the pipeline owns writes,
does them once per file, and writes through .cktmp then atomic replace.

Every patch must be idempotent and must have a clean off path. With everything disabled the
rebuilt file must be byte-for-byte identical to the pristine backup.

---

PROFILER

Port profile.cpp. The game has no ASLR - the PE has no relocation directory and DYNAMIC_BASE
is clear - so it always loads at 0x00400000 and runtime EIPs map 1:1 onto addresses in a
static disassembly. Read-only with respect to the game: OpenProcess for query plus VM read,
then suspend, read EIP, resume, per sample. Nothing is injected and nothing is written into
the game's memory. Preserve that guarantee explicitly in the code comments.

This toolkit builds x64 and the game is 32-bit, so use Wow64SuspendThread and
Wow64GetThreadContext reading WOW64_CONTEXT.Eip. Not GetThreadContext, which cannot read a
WOW64 target's 32-bit context from an x64 process.

Options to preserve: seconds (0 means run until the game exits), hz (default 250),
segmentSeconds (default 60), waitForProcess, outFile, processName (default
"Celtic kings.exe"). The report is split into segments so early game can be compared against
late game, and it is rewritten to disk at every segment boundary so that if the game crashes
the segment leading up to the crash survives. Port the known-hot-region annotation table
from profile.cpp as well; docs/profiler-sample-output.txt shows the expected report shape.

---

I18N

Every user-visible string this phase adds goes into both strings.zh-TW.json and
strings.en.json. That includes the HD ceiling warning and every error message. The SelfTest
already asserts the two tables have identical key sets, so a one-sided addition fails the
build. Do not hardcode display text anywhere.

---

SELFTEST ADDITIONS

Use synthetic PE and pak fixtures built in the test, not real game files - the repository
cannot ship game binaries and the tests must be deterministic. Cover at minimum:

  - each Exe patch applies, is detected by its signature, and reverses to the exact
    pristine bytes
  - all Exe patches applied together, then all disabled, reproduces pristine byte for byte
  - applying twice equals applying once, for every patch
  - ZoomTables: the .ckhr section is appended with correct size and alignment, SizeOfImage
    grows, every rewritten immediate points into the new section, and the result re-parses
  - launcher mutual exclusion: enabling either mode disables the other, from both directions
  - Resolutions: append is idempotent, and selecting by WxH resolves to the index that the
    final list actually contains
  - coverage: Launcher and VxSettings report complete after this phase; Exe and DataPak
    still report incomplete and therefore Unknown
  - a signature must not fire on vanilla bytes, and must fire on its own patched bytes

---

SMALL CLEANUP while you are here: the status command emits the warning
"已自修改器專案 settings.json 遷移設定" even though nothing is written - migration is
in-memory only during a read-only query. Reword it so it reports a detected, not a
performed, migration, in both string tables.

---

FINALLY: append a "Phase 2 完成" section to AI_HANDOFF.md - what you created, any deviation
from the spec and why, which signatures are now registered, and what Phase 3 needs to know.
