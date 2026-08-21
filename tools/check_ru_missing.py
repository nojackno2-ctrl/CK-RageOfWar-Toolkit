import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ZH_TW_PATH = os.path.join(ROOT, "assets", "langpacks", "zh-TW", "campaign-celtic-kings-adventure.json")
RU_PATH = os.path.join(ROOT, "assets", "langpacks", "ru-RU", "campaign-celtic-kings-adventure.json")

with open(ZH_TW_PATH, "r", encoding="utf-8") as f:
    src_tw = json.load(f)

# Load existing Russian mapped
sys.path.append(os.path.join(os.path.dirname(__file__), '..', 'scratch'))
import translate_adv_final_504_complete as ru_mod

ADV = dict(ru_mod.ADV)

print(f"Loaded {len(ADV)} existing RU mappings.")

# Find missing
missing = [k for k in src_tw if k not in ADV and not k.startswith("NO_")]
print(f"Missing count: {len(missing)}")
