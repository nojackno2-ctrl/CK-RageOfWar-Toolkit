"""處理 *.LOC.XML / *.CONV.XML 這類 <translationtable> 翻譯表。

作法是拿原有語系（預設德文）的檔案當模板，只換掉 result="..." 的內容，
其他屬性與排版一個位元組都不動，確保結構與引擎預期完全一致。
"""

from __future__ import annotations

import re

ENTRY_RE = re.compile(rb'<translationtableentry\b[^>]*?/>', re.S)
ATTR_RE = re.compile(r'([\w:]+)="(.*?)"', re.S)
RESULT_RE = re.compile(r'(\bresult=")(.*?)(")', re.S)


def unescape(s: str) -> str:
    return (s.replace("&lt;", "<").replace("&gt;", ">")
             .replace("&quot;", '"').replace("&apos;", "'")
             .replace("&amp;", "&"))


def escape(s: str) -> str:
    return (s.replace("&", "&amp;").replace("<", "&lt;")
             .replace(">", "&gt;").replace('"', "&quot;"))


def entries(data: bytes):
    """逐筆產生 (原始 tag bytes, 已解碼的屬性 dict)。"""
    for m in ENTRY_RE.finditer(data):
        tag = m.group(0).decode("utf-8")
        attrs = {k: unescape(v) for k, v in ATTR_RE.findall(tag)}
        yield m.group(0), attrs


def source_text(attrs: dict) -> str:
    """取出這筆的英文原文。"""
    if "justtext" in attrs:
        return attrs["justtext"]
    text = attrs.get("text", "")
    return text


def rebuild(data: bytes, translate) -> tuple[bytes, int, int]:
    """用 translate(attrs) -> str|None 重寫每筆的 result。

    回傳 (新內容, 已翻譯筆數, 總筆數)。translate 回傳 None 時退回英文原文。
    """
    done = 0
    total = 0

    def repl(m: re.Match) -> bytes:
        nonlocal done, total
        total += 1
        tag = m.group(0).decode("utf-8")
        attrs = {k: unescape(v) for k, v in ATTR_RE.findall(tag)}
        zh = translate(attrs)
        if zh:
            done += 1
        else:
            zh = source_text(attrs)
        new = RESULT_RE.sub(lambda r: r.group(1) + escape(zh) + r.group(3), tag, count=1)
        return new.encode("utf-8")

    return ENTRY_RE.sub(repl, data), done, total
