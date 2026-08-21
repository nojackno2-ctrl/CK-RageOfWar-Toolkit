import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ZH_TW_PATH = os.path.join(ROOT, "assets", "langpacks", "zh-TW", "campaign-celtic-kings-adventure.json")
IT_PATH = os.path.join(ROOT, "assets", "langpacks", "it-IT", "campaign-celtic-kings-adventure.json")
ES_PATH = os.path.join(ROOT, "assets", "langpacks", "es-ES", "campaign-celtic-kings-adventure.json")
RU_PATH = os.path.join(ROOT, "assets", "langpacks", "ru-RU", "campaign-celtic-kings-adventure.json")

with open(ZH_TW_PATH, "r", encoding="utf-8") as f:
    source_tw = json.load(f)

with open(IT_PATH, "r", encoding="utf-8") as f:
    source_it = json.load(f)

# Load existing Russian mapped
sys.path.append(os.path.join(os.path.dirname(__file__), '..', 'scratch'))
ru_mapped = {}
try:
    import translate_adv_624_complete as ru_mod
    ru_mapped.update(ru_mod.ADV)
except Exception as e:
    print("RU mod import note:", e)

print(f"Total source keys: {len(source_tw)}")
print(f"RU mapped keys: {len(ru_mapped)}")
