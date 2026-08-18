Phase 2B - remove the backup layer, replace it with exact reversal.

FIRST read AGENTS.md sections 2.1, 2.2 and 2.3. They were rewritten for this change and are
binding. Also re-read docs/SPEC.md sections 3 and 4 - you are updating those in this phase.

EXECUTION ENVIRONMENT: NO terminal-command permission. Do not run git, dotnet or anything
else - the attempt is denied and aborts your run. Read and write files only. The calling
agent builds and tests afterwards and sends you any errors.

CONTEXT - WHY THIS CHANGES

The user has decided the toolkit must not keep copies of game files. This is a Steam-only
tool; "verify integrity of game files" is always available and is a sufficient safety net.
The HD patch has now been confirmed working in the real game, so Phase 2 is behaviourally
validated - this is a design change, not a bug fix.

Deleting the backups removes the pristine source that the whole pipeline was built on, so
idempotency has to come from somewhere else. It comes from NORMALIZATION: before applying
anything, reverse every patch of ours that is currently present, which returns the file to
vanilla; then layer on whatever the config asks for; then write once.

This is strictly better than the append-only behaviour the old tools had. Changing the
resolution from 1920x1080 to 1600x1200 must REPLACE the entry, not append a second one.

WHAT TO REMOVE

  - src/CKToolkit/Core/Common/BackupManager.cs entirely, along with BackupProvenance, the
    provenance sidecar files, legacy backup candidate scanning and migration, the
    .superseded stale-backup mechanism, and every string table entry that only served them.
  - every call site: PatchPipeline, CliHost, the GUI placeholder, ToolkitConfig, SelfTest.
  - the GameFile enum stays - it names the five target files and is still useful.

WHAT REPLACES IT

Create src/CKToolkit/Core/Common/PatchState.cs (name it as you see fit) providing:

    // What our patches have done to this file, derived from its bytes alone.
    FileState Inspect(GameFile file, byte[] liveBytes);
        -> Vanilla | PatchedByUs(list of patch ids) | Unrecognised

    // Reverse every one of our patches found in the bytes. Returns vanilla bytes.
    Result<byte[]> Normalise(GameFile file, byte[] liveBytes);

Unrecognised means the bytes are neither vanilla nor any combination our signatures explain -
a third-party tool has been there. In that case REFUSE the operation and tell the user to run
Steam verify. Never guess, never write partially.

Every patch class gains a reverse operation alongside its apply. The vanilla constants needed
to reverse live in the code, next to the constants used to apply:

  LargeAddressAware   clear the characteristics bit
  VideoModePatch      restore the original prologue bytes at 0x006BE340
  ResolutionWriteback restore the original instruction bytes at 0x00658FAB
  ZoomTables          restore the original immediates (4 pointing at 0x0076FF78, 2 at
                      0x00774A94) and remove the appended .ckhr section, returning the
                      section count, SizeOfImage and headers to their original values
  LauncherDisplay     restore the original ChangeDisplaySettingsA call bytes
  LauncherModeTable   restore entry 0 to the stock 1600x1200
  Resolutions         rewrite [Resolutions] to exactly the stock four entries:
                      1024x768, 1152x864, 1280x1024, 1600x1200
  VxSettingsPatch     remove the keys we added; restore Resolution to its stock value

For anything a later phase adds, the same rule applies - see AGENTS.md 2.3: a patch that
cannot be exactly reversed does not belong in this project.

PIPELINE

ApplyAll becomes, per target file:

    live = read(file)
    state = Inspect(file, live)
    if state is Unrecognised: fail with the Steam-verify message, write NOTHING anywhere
    bytes = Normalise(file, live)
    bytes = apply each enabled patch, in the documented order
    if bytes != live: write once via .cktmp then atomic replace
    else: skip the write entirely

That last line matters: local.pak is currently rewritten - 4.8MB - even when nothing was
layered onto it. Never write a file whose contents did not change.

RestoreAll becomes: normalise every target file and write back whatever changed. It must
report per file whether anything was reversed, and must NOT claim success for files it could
not recognise - those get the Steam-verify message.

Verify becomes: report each file as vanilla, patched with this list, or unrecognised.
Read-only, zero writes, same guarantee as status.

CLI AND CONFIG

  - drop any backup-related command, flag, field and output key
  - status and verify report the new FileState, not pristine/unknown/hasBackup
  - the incomplete-coverage warnings disappear; coverage now only affects whether
    normalisation is complete, and an unregistered patch would show up as Unrecognised
  - keep every string table in sync across zh-TW and en; delete orphaned keys

SELFTEST - rewrite the affected groups. The reversal tests are now the safety net that the
backups used to be, so they must be thorough:

  - for EVERY patch individually: vanilla -> apply -> reverse -> byte-for-byte vanilla
  - all patches applied together -> normalise -> byte-for-byte vanilla
  - apply twice -> identical to applying once
  - change a setting and re-apply: 1920x1080 then 1600x1200 leaves exactly ONE non-stock
    entry in [Resolutions], not two
  - Inspect on vanilla bytes returns Vanilla; on our patched bytes returns the exact patch
    id list; on bytes with an unknown modification returns Unrecognised
  - an Unrecognised file causes apply to fail and write nothing
  - a file whose content would not change is not written at all

DOCS - update docs/SPEC.md sections 3 and 4 to describe PatchState and normalisation instead
of BackupManager, and update section 10 for the CLI changes. Update AI_HANDOFF.md.

Report what you changed.
