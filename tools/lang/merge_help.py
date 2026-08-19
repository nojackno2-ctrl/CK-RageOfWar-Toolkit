# -*- coding: utf-8 -*-
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

from trans_help_1 import DATA_1
from trans_help_2 import DATA_2
from trans_help_3 import DATA_3
from trans_help_fix import DATA_FIX

TPL = ROOT / "翻譯範本" / "help.json"
OUT = ROOT / "翻譯" / "help.json"

def main():
    combined = {}
    combined.update(DATA_1)
    combined.update(DATA_2)
    combined.update(DATA_3)
    combined.update(DATA_FIX)

    tpl = json.loads(TPL.read_text(encoding="utf-8"))
    missing = [k for k in tpl if k not in combined]
    
    print(f"Template items: {len(tpl)}")
    print(f"Combined translated items: {len(combined)}")
    print(f"Missing items count: {len(missing)}")

    if missing:
        for idx, m in enumerate(missing):
            print(f"Missing [{idx}]: {repr(m)}")
    else:
        out_table = {k: combined[k] for k in tpl}
        OUT.parent.mkdir(exist_ok=True)
        OUT.write_text(json.dumps(out_table, ensure_ascii=False, indent=1), encoding="utf-8")
        print(f"Successfully generated {OUT} ({len(out_table)} entries)")

if __name__ == "__main__":
    main()
