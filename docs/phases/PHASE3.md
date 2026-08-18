You are implementing Phase 3 of the CK-RageOfWar-Toolkit: the Lang module.

FIRST read, in this order:
  - AGENTS.md            sections 2.1 to 2.3 are binding and were rewritten in Phase 2B
  - docs/SPEC.md         sections 3, 4, 6 and 10
  - AI_HANDOFF.md        current state

Authoritative behaviour sources, both read-only references:
  （前身 Lang 專案，已刪除）   (C# .NET FW 4.8)
      ApfFont.cs  GdiFont.cs  FontBuilder.cs  LocXml.cs  Translations.cs  PakFile.cs  Patcher.cs
  （前身 ckpatch.py，已遷入 tools/）  plus tools\apf.py, gdifont.py,
      locxml.py, makefont.py, hmmpak.py                                  (Python oracle)

Port their logic and their comments. Never modify anything under the predecessor directories.

EXECUTION ENVIRONMENT: NO terminal-command permission. Do not run git, dotnet or anything
else - the attempt is denied and aborts your run. Read and write files only. The calling agent
builds, tests and verifies against real game files afterwards, and sends you any errors.

Phases 1, 2 and 2B are committed and green, and the HD patch is confirmed working in the real
game at 1920x1080. Do not regress any of it.

---

THE HARD CONSTRAINT - EXACT REVERSIBILITY

There are no backups any more. Reversal IS the safety net. Read AGENTS.md 2.3: a patch that
cannot be exactly reversed does not belong in this project.

For this module that means:

  - installing a language pack into local.pak must be reversible to a byte-for-byte identical
    local.pak
  - the font work is the risky part. The predecessor rasterises CJK glyphs with a system font
    and APPENDS them to the existing .apf bitmap fonts, leaving the Latin and Cyrillic glyphs
    untouched. Appending is reversible only if you can identify and strip exactly what was
    appended and restore every header field, table offset and padding byte.
  - if you find that the append cannot be reversed byte-exactly, do NOT ship a lossy reversal.
    Change the design instead - for example carry the language pack fonts as NEW entries in
    the pak rather than modifying the existing ones, if the engine can be pointed at them -
    and record the decision and its rationale in AI_HANDOFF.md.
  - decide this EARLY, before writing the font pipeline, because it determines the design.

---

SCOPE - src/CKToolkit/Core/Lang/

  LangModule.cs        IPatchModule implementation; registers steps and the langpack_installed
                       signature, which completes LocalPak coverage
  LanguagePack.cs      pack.json model, discovery, validation
  PackLoader.cs        loads the built-in embedded pack and external packs from disk
  ApfFont.cs           APF bitmap font read/write - port
  GdiFont.cs           system font rasterisation via GDI - port
  FontBuilder.cs       builds the glyph set a pack asks for - port, generalised
  LocXml.cs            the game localisation XML - port
  Translations.cs      translation table model - port
  LangInstaller.cs     install / uninstall / status / export-template

MECHANISM, unchanged from the predecessor:
  1. local.pak holds one folder per language (GERMAN\, FRENCH\ ...). Installing a pack adds a
     new language folder whose structure is cloned from an existing template language, with
     the result strings replaced by the pack translations.
  2. local/fonts/*.apf are Unicode bitmap fonts. The pack declares the character ranges it
     needs and those glyphs are rasterised from a system font and added. Existing glyphs are
     never touched.
  3. vxSettings.ini [Language] Default becomes the pack language key. This file already has a
     single writer in the pipeline - go through it, do not write the file yourself.

---

LANGUAGE PACK FORMAT - the extensibility requirement

docs/SPEC.md section 6.2 defines pack.json. The rule that matters: the character ranges to
rasterise come from pack.json, never hardcoded. Nothing in Core/Lang may contain a CJK range
literal. A new language must be addable by dropping a folder in, with zero code changes.

  - the built-in zh-TW pack ships as an embedded resource, built from assets/langpacks/zh-TW/
    which already holds the four translation JSON files and the glossary
  - external packs load from <exe directory>/langpacks/<id>/ and are discovered at startup
  - a pack.json missing required fields is rejected with a message naming the field, not
    silently ignored
  - export-template produces a skeleton pack from an existing in-game language: an untranslated
    ui.json / help.json / campaign files plus a pack.json stub, so a translator can start a new
    language immediately

PORTING NOTES:
  - replace MiniJson with System.Text.Json
  - keep the LocXml self-closing tag fix. The correct pattern is
    (<entry\b(?![^>]*?/>)[^>]*>)(.*?)(</entry>) - the naive (<entry\b[^>]*>) treats <entry/> as
    an opening tag and the attribute overflow corrupts the key. Both predecessor implementations
    carry this fix; do not lose it.
  - preserve the translation content rules: full-width punctuation, placeholders such as %s1
    and %d kept intact, internal parameters kept intact (NameSet, ReqSet, the NO_ prefix)

---

I18N: every user-visible string this phase adds goes into both strings.zh-TW.json and
strings.en.json. Note the distinction and keep it clear in the UI wording: the toolkit UI
language and the game language pack are two different things.

CLI, per docs/SPEC.md section 10:
    lang list | install | uninstall | export-template
        --pack <id> --font <face> --out <dir>
Same envelope, exit codes and never-interactive rules as the existing commands.

---

SELFTEST - the reversal tests are the safety net, so they must be thorough:

  - install a pack into a synthetic local.pak, then uninstall: byte-for-byte identical
  - install twice equals installing once
  - switching from one pack to another leaves no trace of the first
  - the font glyph set is driven by pack.json ranges: feed a synthetic pack with a small
    unusual range and assert exactly those glyphs were produced, proving nothing is hardcoded
  - a pack.json missing a required field is rejected with the field named
  - LocXml does not corrupt keys when the XML contains self-closing entry tags
  - vxSettings [Language] Default is set on install and restored to its vanilla value on
    uninstall, in place inside its section, with the file byte-identical afterwards

---

SMALL ITEM carried over from Phase 2B: when apply removes [Resolutions] entries that exceed
the current ZoomMap capacity, and when it re-points vxSettings Resolution as a result, it
currently does so silently. Emit a warning naming what was removed or re-pointed and why.

FINALLY: append a "Phase 3 完成" section to AI_HANDOFF.md covering what you created, the
reversibility decision you reached for the fonts and why, and what Phase 4 needs to know.
