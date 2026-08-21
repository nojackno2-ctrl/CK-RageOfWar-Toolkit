import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ZH_TW_PATH = os.path.join(ROOT, "assets", "langpacks", "zh-TW", "campaign-celtic-kings-adventure.json")
ES_PATH = os.path.join(ROOT, "assets", "langpacks", "es-ES", "campaign-celtic-kings-adventure.json")
RU_PATH = os.path.join(ROOT, "assets", "langpacks", "ru-RU", "campaign-celtic-kings-adventure.json")

with open(ZH_TW_PATH, "r", encoding="utf-8") as f:
    source_tw = json.load(f)

print(f"Total source keys: {len(source_tw)}")
