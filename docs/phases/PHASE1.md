You are implementing Phase 1 of the CK-RageOfWar-Toolkit project.

FIRST, read these files in the working directory — they are the authoritative spec and rules:
  - AGENTS.md          (collaboration rules and patching discipline — MUST be obeyed)
  - docs/SPEC.md       (full integration spec; Phase 1 covers sections 1, 2, 3, 4, 8)
  - AI_HANDOFF.md      (project state)

PHASE 1 SCOPE — solution skeleton plus Core/Common. Do NOT implement Perf, Lang, Trainer,
GUI pages, or CLI commands yet; those are later phases. Leave clearly-marked TODO seams.

Deliverables:

1. `CKToolkit.sln` at repo root, containing both projects below.

2. `src/CKToolkit/CKToolkit.csproj` — exactly as specified in SPEC.md section 1
   (net10.0-windows, WinExe, UseWindowsForms, x64, nullable enable, LangVersion latest,
   TreatWarningsAsErrors, AssemblyName CKToolkit, ApplicationManifest).
   Plus `src/CKToolkit/app.manifest` (asInvoker, Windows 10/11 supportedOS, PerMonitorV2 DPI).

3. `src/CKToolkit.SelfTest/CKToolkit.SelfTest.csproj` — net10.0-windows console
   (`OutputType Exe`), project-references CKToolkit. A `Program.cs` with a tiny assertion
   harness in the style of the predecessor trainer's SelfTest: prints each check, counts
   failures, exits non-zero when any fail. Register only the Phase 1 checks for now.

4. `src/CKToolkit/Program.cs` — the entry point split:
   - `[STAThread] static int Main(string[] args)`
   - `args.Length == 0` -> run the WinForms GUI (for now: a minimal placeholder MainForm
     showing the toolkit name and version — the real GUI is Phase 5).
   - otherwise -> hand off to `Cli.CliHost.Run(args)`. For Phase 1, CliHost only needs to
     implement `status`, `--help`, and `--version`, plus the JSON envelope, exit codes,
     and `AttachConsole(ATTACH_PARENT_PROCESS)` handling per SPEC.md section 10.
     Unknown commands must exit 2 with a proper JSON error when `--json` is passed.

5. `src/CKToolkit/Core/Common/` — the real work of this phase:

   - `Result.cs` — a shared Ok/Error result type used by both GUI and CLI. No exceptions
     for expected failure paths (game not found, backup missing, file locked); carry an
     error code that maps to the CLI exit codes in SPEC.md section 10.

   - `GamePaths.cs` — locate the game directory. Order: explicit override, then the path
     remembered in cktoolkit.json, then the Steam library guesses. A directory only counts
     as the game if it contains BOTH `local.pak` and `Celtic kings.exe`. Port the Steam
     path hints from the predecessor `ckpatch.py` and additionally parse Steam's
     `libraryfolders.vdf` when present so non-default libraries are found.
     Expose the five target file paths: `Celtic kings.exe`, `Celtic kings Launcher.exe`,
     `data.pak`, `local.pak`, `vxSettings.ini`.

   - `IniFile.cs` — read/write INI preserving original ordering, comments, and line
     endings. Needed for both `vxSettings.ini` and `VXCONST.INI` inside data.pak. Must
     support section-scoped keys (`[Language] Default`) and appending to a list section.

   - `PeFile.cs` — 32-bit and 64-bit PE parsing: DOS/NT headers, section table,
     RVA<->file-offset conversion, reading/writing the characteristics flags, and
     appending a new section with given name/size/characteristics (needed by the HD
     ZoomMap patch in Phase 2, which appends a `.ckhr` section). Port the logic from the
     predecessor C++ `patches.cpp`; keep its comments.

   - `HmmPak.cs` — HMMSYS PackFile reader/writer. START FROM the predecessor trainer's
     `Core/HmmPak.cs` (it is byte-for-byte round-trip verified against all six game paks
     including the 136 MB assets.pak) and reconcile it against the other two
     implementations, keeping whichever behaviour is more complete. Preserve the original
     entry timestamp handling (vanilla data.pak entries are 2004-01-23 12:46:32).

   - `BackupManager.cs` — THE UNIFIED BACKUP LAYER. Implement exactly SPEC.md section 3.
     Critical: `IsPristine(GameFile)` must consult every module's patch signature, not just
     one module's. For Phase 1, structure this as a registry — `IPatchSignature` with an
     `AppliesTo` game file and a `IsApplied(byte[] fileBytes)` probe — that later phases
     register their detectors into. Ship Phase 1 with the registry empty but wired, and
     make it impossible to add a patch in a later phase without registering a signature
     (e.g. the pipeline asks the registry for detectors and the SelfTest asserts every
     known patch id has one).

   - `PatchPipeline.cs` — the single apply pipeline of SPEC.md section 4. Define the
     ordering and the per-file "rebuild from pristine, layer every enabled change, write
     once" contract, with module hooks (`IPatchModule` with an ordered set of steps per
     target file) that Phases 2-4 plug into. Implement `ApplyAll`, `RestoreAll`, `Verify`.
     Write via `.cktmp` then atomic replace; on IO failure report the file-locked error
     code rather than throwing.

   - `ToolkitConfig.cs` — `cktoolkit.json` load/save exactly as SPEC.md section 8, using
     System.Text.Json with source-generation-friendly plain DTOs. Resolution stored as
     "WxH" strings, never an index. Include migration stubs that detect the predecessor
     config formats (`ckpatcher.cfg`, the localizer's `備份/遊戲路徑.txt`, the trainer's
     settings json) and note migrations in a list the UI/CLI can display.

6. Phase 1 SelfTest checks:
   - ToolkitConfig round-trips (save then load yields an equal object).
   - IniFile preserves comments, ordering, and CRLF on round-trip.
   - PeFile can parse a PE, append a section, and the result still parses with the new
     section present and correct RVA/offset mapping.
   - HmmPak round-trips a synthetic pak byte-for-byte.
   - BackupManager registry: every registered patch id has exactly one signature, and
     `IsPristine` returns false when ANY registered signature reports applied.
   - CLI: `status --json` emits a valid envelope; an unknown command exits 2.

CONSTRAINTS:
   - `TreatWarningsAsErrors` is on. The build must be warning-free.
   - Nullable reference types are enabled and must be honoured, not suppressed.
   - All user-visible strings must go through the I18n layer. For Phase 1, create
     `src/CKToolkit/I18n/Strings.cs` plus `strings.zh-TW.json` and `strings.en.json`
     (embedded resources) with the keys this phase needs, and a resolver that picks the
     language from the OS locale when config says "auto". Do not hardcode display text.
   - Preserve every memory address and reverse-engineering comment you carry over from the
     predecessor sources. They are non-regenerable.
   - Do NOT modify anything under the three predecessor project directories. They are
     read-only reference.
   - Do NOT git commit. Leave changes in the working tree.

EXECUTION ENVIRONMENT - READ THIS CAREFULLY:
   You have NO permission to run terminal commands in this session. Do not attempt to run
   git, dotnet, msbuild, or any other command; every such attempt will be denied and will
   abort your entire run. In particular, ignore the "inspect git status before modifying
   code" rule in AGENTS.md for this session - it does not apply to you here.

   You CAN read and write files, and that is all you need.

   The calling agent will run `dotnet build CKToolkit.sln -c Release` and
   `dotnet run --project src/CKToolkit.SelfTest` after you finish, and will send you the
   errors to fix in a follow-up round. Because you cannot compile, be conservative: prefer
   plain obviously-correct C# over clever constructs, check every using directive,
   namespace, type name and method signature against the file that declares it, and make
   sure every file is complete - no unfinished partial classes, no bare TODO stubs that
   other code already calls.

FINALLY: append a short "Phase 1 完成" section to AI_HANDOFF.md listing what you created,
any deviations from the spec and why, and anything Phase 2 needs to know.
