# -*- coding: utf-8 -*-
"""打包前把 Sunshine + Moonlight 运行时打成 runtimes.zip（sunshine/、client/ 两个目录）。"""
import os
import zipfile

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "runtimes.zip")

SOURCES = [
    (r"C:\Program Files\Sunshine", "sunshine"),
    (r"C:\Program Files\Moonlight Game Streaming", "client"),
]

def add_dir(zf, root, arcprefix):
    if not os.path.isdir(root):
        return 0
    n = 0
    for base, dirs, files in os.walk(root):
        # 只打包运行二进制，排除 config/、日志与临时文件（由内置 runtime_config 提供配置）
        dirs[:] = [d for d in dirs if d.lower() not in ("config",)]
        for f in files:
            if f.lower().endswith((".log", ".log.1", ".log.2", ".log.3", ".log.4", ".log.5", ".lock")):
                continue
            fp = os.path.join(base, f)
            arc = os.path.join(arcprefix, os.path.relpath(fp, root))
            zf.write(fp, arc)
            n += 1
    return n

if os.path.exists(OUT):
    os.remove(OUT)
total = 0
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
    for root, prefix in SOURCES:
        c = add_dir(zf, root, prefix)
        print("%s -> %s/ (%d files)" % (root, prefix, c))
        total += c
size = os.path.getsize(OUT) / 1024 / 1024
print("runtimes.zip = %.1f MB, %d files" % (size, total))
