# -*- coding: utf-8 -*-
"""月光极速电子教室 —— 图标模块

按钮/菜单图标：Material Design Icons（MDI, Apache-2.0）矢量图标，已在构建期
栅格化为 96px 的 alpha 遮罩（见 icon_data.py），运行时按需染色，任意尺寸
缩放都是抗锯齿的，不再使用 PIL 手绘（小尺寸锯齿明显）。
应用/托盘图标：优先使用项目内置 icon.ico。
"""
import io
import base64

from PIL import Image, ImageDraw

try:
    from icon_data import ICONS as _ICON_DATA
except Exception:            # 数据缺失时退回手绘，保证程序仍可启动
    _ICON_DATA = {}

BG = (19, 26, 43, 255)       # 应用图标底
ACCENT = (232, 182, 75, 255)  # 月亮金
WHITE = (245, 248, 252, 255)
SLATE = (120, 138, 176, 255)  # 中性
MENU_FG = (51, 64, 78, 255)   # 托盘菜单图标（深灰）

_ICON_CACHE = {}
_MASK_CACHE = {}


def _mask(name):
    """取该图标的 alpha 遮罩（L 模式），并裁切到图形外框后补成正方形，
    这样不同图标在同一尺寸下视觉重量一致（如播放三角不会显得偏小）。"""
    if name in _MASK_CACHE:
        return _MASK_CACHE[name]
    b64 = _ICON_DATA.get(name)
    if not b64:
        _MASK_CACHE[name] = None
        return None
    try:
        raw = base64.b64decode("".join(b64) if isinstance(b64, (tuple, list)) else b64)
        im = Image.open(io.BytesIO(raw))
        m = im.convert("LA").split()[1]
        box = m.getbbox()
        if box:
            m = m.crop(box)
        w, h = m.size
        s = max(w, h)
        sq = Image.new("L", (s, s), 0)
        sq.paste(m, ((s - w) // 2, (s - h) // 2))
        m = sq
    except Exception:
        m = None
    _MASK_CACHE[name] = m
    return m


def _tint(mask, size, color):
    """把遮罩缩放到 size 并用 color 着色，返回 RGBA 图。"""
    S = int(max(size, 8))
    inner = max(6, int(round(S * 0.94)))     # 留一点边距，避免贴边
    a = mask.resize((inner, inner), Image.Resampling.LANCZOS)
    canvas = Image.new("L", (S, S), 0)
    off = (S - inner) // 2
    canvas.paste(a, (off, off))
    im = Image.new("RGBA", (S, S), (color[0], color[1], color[2], 0))
    im.putalpha(canvas)
    return im


def _draw_fallback(name, size, color):
    """兜底手绘（仅当 icon_data 缺失时使用）。"""
    S = int(max(size, 8))
    col = color or WHITE
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    if name == "play":
        d.polygon([(S * 0.34, S * 0.24), (S * 0.34, S * 0.76), (S * 0.76, S * 0.50)], fill=col)
    elif name == "stop":
        d.rectangle((S * 0.30, S * 0.30, S * 0.70, S * 0.70), fill=col)
    elif name in ("watch", "refresh"):
        d.ellipse((S * 0.28, S * 0.28, S * 0.72, S * 0.72), outline=col, width=max(1, S // 10))
    else:
        d.ellipse((S * 0.32, S * 0.32, S * 0.68, S * 0.68), fill=col)
    return im


def make_small(name, size=24, color=None):
    """按钮/菜单用的纯色图标：play / stop / watch / refresh / quit。"""
    col = tuple(color) if color is not None else WHITE
    if name == "app":
        return load_app_icon(size)
    m = _mask(name)
    if m is None:
        return _draw_fallback(name, size, col if color is not None else None)
    return _tint(m, size, col)


def make_app_icon(size=64):
    """回退用应用图标：深色圆角底 + 金色新月（icon.ico 缺失时才用）。"""
    S = int(max(size, 8))
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = int(S * 0.24)
    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=r, fill=BG)
    cx, cy, mr = S * 0.5, S * 0.5, S * 0.29
    d.ellipse((cx - mr, cy - mr, cx + mr, cy + mr), fill=ACCENT)
    off = S * 0.135
    d.ellipse((cx - mr + off, cy - mr - off * 0.55,
               cx + mr + off, cy + mr - off * 0.55), fill=BG)
    return im


def load_app_icon(size=64):
    """应用图标：优先使用项目内置 icon.ico（用户指定），缺失时回退到生成图标。
    支持 PyInstaller 冻结模式（sys._MEIPASS）与源码模式。"""
    import os
    import sys
    bases = []
    meip = getattr(sys, "_MEIPASS", "")
    if meip:
        bases.append(meip)
    bases.append(os.path.dirname(os.path.abspath(__file__)))
    bases.append(os.getcwd())
    for b in bases:
        p = os.path.join(b, "icon.ico")
        if os.path.exists(p):
            try:
                im = Image.open(p)
                try:
                    im.size = (size, size)
                    im.load()
                except Exception:
                    pass
                return im.convert("RGBA").resize((size, size), Image.Resampling.LANCZOS)
            except Exception:
                pass
    return make_app_icon(size)


def get(name, size=24, color=None):
    """带缓存的图标获取。"""
    key = (name, int(size), tuple(color) if color else None)
    im = _ICON_CACHE.get(key)
    if im is None:
        im = make_app_icon(size) if name == "app" else make_small(name, size, color)
        _ICON_CACHE[key] = im
    return im
