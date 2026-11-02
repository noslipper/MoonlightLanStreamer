# -*- coding: utf-8 -*-
# 生成应用图标 icon.ico（与界面/托盘一致的小清新浅色图标）
import sys
from PIL import Image
sys.path.insert(0, __file__.rsplit("\\", 1)[0])
import icons

img = icons.make_app_icon(64)
img.save("icon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64)], format="ICO")
print("icon.ico created (fresh light)")
