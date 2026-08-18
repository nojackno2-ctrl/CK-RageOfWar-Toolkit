"""把「翻譯來源/*.txt」（索引<TAB>中文）編成「翻譯/*.json」（英文原文 -> 中文）。

翻譯來源用索引只是為了人工作業方便；實際發布與 install 讀的是英文原文為鍵的
JSON，不會因為重新 extract 而錯位。
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "翻譯來源"
TPL = ROOT / "翻譯範本"
OUT = ROOT / "翻譯"


def compile_group(prefix: str, template: str, outname: str) -> None:
    keys = list(json.loads((TPL / template).read_text(encoding="utf-8")))
    table: dict[str, str] = {}
    dupes: list[str] = []
    for path in sorted(SRC.glob(prefix + "*.txt")):
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            line = line.rstrip("\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            if "\t" not in line:
                raise SystemExit(f"{path.name}:{lineno} 少了 TAB 分隔：{line!r}")
            idx, zh = line.split("\t", 1)
            idx = int(idx.strip())
            if idx >= len(keys):
                raise SystemExit(f"{path.name}:{lineno} 索引 {idx} 超出範本（共 {len(keys)} 條）")
            key = keys[idx]
            if key in table and table[key] != zh:
                dupes.append(f"{key!r}: {table[key]!r} / {zh!r}")
            table[key] = zh
    OUT.mkdir(exist_ok=True)
    (OUT / outname).write_text(
        json.dumps(table, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"{outname}: {len(table)} / {len(keys)} 條"
          f"（{len(table) * 100 // max(len(keys), 1)}%）")
    for d in dupes[:5]:
        print("  ! 同一原文有兩種譯法：", d)


GROUPS = [
    ("ui-", "ui.json", "ui.json"),
    ("help-", "help.json", "help.json"),
    ("tutorial-", "campaign-tutorial.json", "campaign-tutorial.json"),
    ("adventure-", "campaign-celtic-kings-adventure.json",
     "campaign-celtic-kings-adventure.json"),
]

if __name__ == "__main__":
    only = sys.argv[1] if len(sys.argv) > 1 else None
    for prefix, template, outname in GROUPS:
        if only and not prefix.startswith(only):
            continue
        if not (TPL / template).exists():
            continue
        if not any(SRC.glob(prefix + "*.txt")):
            continue
        compile_group(prefix, template, outname)
