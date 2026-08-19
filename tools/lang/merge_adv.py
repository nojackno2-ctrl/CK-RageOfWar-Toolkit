# -*- coding: utf-8 -*-
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

from trans_adv_1 import ADV_DATA_1
from trans_adv_2 import ADV_DATA_2
from trans_adv_3 import ADV_DATA_3
from trans_adv_4 import ADV_DATA_4
from trans_adv_5 import ADV_DATA_5
from trans_adv_fix import ADV_DATA_FIX

TPL = ROOT / "翻譯範本" / "campaign-celtic-kings-adventure.json"
OUT = ROOT / "翻譯" / "campaign-celtic-kings-adventure.json"
UI = ROOT / "翻譯" / "ui.json"
TUT = ROOT / "翻譯" / "campaign-tutorial.json"

def main():
    combined = {}
    
    # Existing translations
    if UI.exists():
        combined.update(json.loads(UI.read_text(encoding="utf-8")))
    if TUT.exists():
        combined.update(json.loads(TUT.read_text(encoding="utf-8")))
        
    combined.update(ADV_DATA_1)
    combined.update(ADV_DATA_2)
    combined.update(ADV_DATA_3)
    combined.update(ADV_DATA_4)
    combined.update(ADV_DATA_5)
    combined.update(ADV_DATA_FIX)

    tpl = json.loads(TPL.read_text(encoding="utf-8"))
    
    missing = []
    out_table = {}
    
    for k in tpl:
        if k in combined and combined[k]:
            out_table[k] = combined[k]
        elif k.startswith("NO_"):
            out_table[k] = k  # Internal character/object identifiers
        else:
            missing.append(k)

    print(f"Template total items: {len(tpl)}")
    print(f"Translated total items: {len(out_table)}")
    print(f"Missing items count: {len(missing)}")

    if missing:
        for idx, m in enumerate(missing):
            print(f"Missing [{idx}]: {repr(m)}")
    else:
        OUT.parent.mkdir(exist_ok=True)
        OUT.write_text(json.dumps(out_table, ensure_ascii=False, indent=1), encoding="utf-8")
        print(f"Successfully generated {OUT} ({len(out_table)} entries)")

if __name__ == "__main__":
    main()
