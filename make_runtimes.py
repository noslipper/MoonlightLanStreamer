# -*- coding: utf-8 -*-
"""打包前把 Sunshine + Moonlight 运行时打成 runtimes.zip（sunshine/、client/ 两个目录）。

只保留串流真正需要的文件，剔除两类"从不调用"的大件：
- ``sunshine/tools/``：dxgi-info.exe（57MB，显卡诊断工具）、audio-info.exe、sunshinesvc.exe。
  本项目通过 sc.exe 停服务 + 直接启动 sunshine.exe 工作，运行期不会调用它们，约占 20MB 压缩体积。
- ``client/translations/``：Moonlight 的 33 种 Qt 翻译（约 7.8MB，压缩后约 2.5MB），只留英文。
"""
import os
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "runtimes.zip")

SOURCES = [
    (r"C:\Program Files\Sunshine", "sunshine"),
    (r"C:\Program Files\Moonlight Game Streaming", "client"),
]

KEEP_TRANSLATIONS = {"qt_en.qm"}          # client/translations 里只保留英文


def add_dir(zf, root, arcprefix):
    if not os.path.isdir(root):
        return 0
    n = 0
    for base, dirs, files in os.walk(root):
        rel = os.path.relpath(base, root).replace("\\", "/")
        rel_l = "" if rel == "." else rel.lower()

        keep = [d for d in dirs if d.lower() != "config"]
        if arcprefix == "sunshine" and rel_l == "":
            keep = [d for d in keep if d.lower() != "tools"]     # 运行期不需要的诊断工具
        dirs[:] = keep

        in_trans = rel_l == "translations"
        for f in files:
            if f.lower().endswith((".log", ".log.1", ".log.2", ".log.3", ".log.4", ".log.5", ".lock")):
                continue
            if in_trans and f.lower() not in KEEP_TRANSLATIONS:
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

    # VC++ 运行库（app-local 部署）：Moonlight 是 MSVC/Qt 编译，缺了
    # VCRUNTIME140/MSVCP140 在干净机器上会直接 loader 失败（弹框卡住、无日志）
    vc_names = ["msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll",
                "msvcp140_atomic_wait.dll", "msvcp140_codecvt_ids.dll",
                "vcruntime140.dll", "vcruntime140_1.dll", "concrt140.dll"]
    for n in vc_names:
        src = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"), "System32", n)
        if os.path.exists(src):
            zf.write(src, os.path.join("client", n))
            total += 1
            print("VC runtime -> client/%s" % n)
size = os.path.getsize(OUT) / 1024 / 1024
print("runtimes.zip = %.1f MB, %d files" % (size, total))
