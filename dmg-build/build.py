#!/usr/bin/env python3
"""
Build the AI接线员 DMG (660x440 window, sharp background, native full-path
bookmark so the background renders on macOS 26).

Usage:  python3 build.py [output.dmg]
Default output: <project>/终版分发/AI接线员.dmg

All persistent assets (bg images, install guide, genbm) live next to this
script so the build survives /tmp being cleaned.
"""
import os, subprocess, time, shutil, sys
import ds_store.store as S
if b"pBBk" in S.codecs:
    del S.codecs[b"pBBk"]          # store native bookmark bytes verbatim
from ds_store import DSStore
from mac_alias import Alias

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(HERE)

APP_SRC  = os.path.join(PROJ, "AI接线员.app")
TXT_SRC  = os.path.join(HERE, "install-guide.txt")
BG_SRC   = os.path.join(HERE, "bg.png")
BG2X_SRC = os.path.join(HERE, "bg@2x.png")
GENBM    = os.path.join(HERE, "genbm")

VOL   = "AI接线员"
STAGE = "/tmp/Commander-DMG"
RW    = "/tmp/AI接线员-rw.dmg"
MOUNT = "/Volumes/" + VOL
OUT   = sys.argv[1] if len(sys.argv) > 1 else os.path.join(PROJ, "终版分发", "AI接线员.dmg")

TXT_NAME = "⚠️ 打不开看这里.txt"
WIN = (400, 150, 1060, 590)   # 660 x 440
ICONS = {
    "AI接线员.app": (165, 200),
    "Applications": (495, 200),
    TXT_NAME:        (330, 340),
}

def run(*a):   subprocess.run(a, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
def quiet(*a): subprocess.run(a, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

for p in (APP_SRC, TXT_SRC, BG_SRC, BG2X_SRC, GENBM):
    if not os.path.exists(p):
        print("MISSING:", p); sys.exit(1)

# ---- staging (fresh) ----
quiet("hdiutil", "detach", MOUNT, "-force"); time.sleep(1)
shutil.rmtree(STAGE, ignore_errors=True)
os.makedirs(os.path.join(STAGE, ".background"))
run("ditto", APP_SRC, os.path.join(STAGE, "AI接线员.app"))
os.symlink("/Applications", os.path.join(STAGE, "Applications"))
shutil.copy(TXT_SRC, os.path.join(STAGE, TXT_NAME))
shutil.copy(BG_SRC,   os.path.join(STAGE, ".background", "bg.png"))
shutil.copy(BG2X_SRC, os.path.join(STAGE, ".background", "bg@2x.png"))

# ---- read-write dmg ----
try: os.remove(RW)
except FileNotFoundError: pass
du = subprocess.check_output(["du", "-sk", STAGE]).split()[0]
size_mb = int(du)//1024 + 30
print(f"==> RW dmg {size_mb}MB")
run("hdiutil","create","-srcfolder",STAGE,"-volname",VOL,"-fs","HFS+",
    "-fsargs","-c c=64,a=16,e=16","-format","UDRW","-size",f"{size_mb}m",RW)
run("hdiutil","attach",RW,"-readwrite","-noverify","-noautoopen","-mountpoint",MOUNT)
time.sleep(2)

bg = os.path.join(MOUNT, ".background", "bg.png")
subprocess.run([GENBM, bg, "/tmp/bg.bookmark"], check=True)
native = open("/tmp/bg.bookmark", "rb").read()
alias  = Alias.for_file(bg).to_bytes()
print("native bookmark:", len(native), "bytes")

bwsp = {
    "ShowStatusBar": False, "ShowToolbar": False, "ShowTabView": False,
    "ShowSidebar": False, "ShowPathbar": False, "ContainerShowSidebar": False,
    "WindowBounds": "{{%d, %d}, {%d, %d}}" % (WIN[0], WIN[1], WIN[2]-WIN[0], WIN[3]-WIN[1]),
}
icvp = {
    "viewOptionsVersion": 1, "backgroundType": 2,
    "backgroundColorRed": 1.0, "backgroundColorGreen": 1.0, "backgroundColorBlue": 1.0,
    "backgroundImageAlias": alias,
    "gridOffsetX": 0.0, "gridOffsetY": 0.0, "gridSpacing": 100.0,
    "arrangeBy": "none", "showIconPreview": True, "showItemInfo": False,
    "labelOnBottom": True, "textSize": 13.0, "iconSize": 96.0,
}
with DSStore.open(os.path.join(MOUNT, ".DS_Store"), "w+") as d:
    d["."]["vSrn"] = ("long", 1)
    d["."]["bwsp"] = bwsp
    d["."]["icvp"] = icvp
    d["."]["pBBk"] = ("blob", native)
    d["."]["icvl"] = ("type", "icnv")
    for k, v in ICONS.items():
        d[k]["Iloc"] = v
quiet("sync"); time.sleep(1)
run("hdiutil", "detach", MOUNT, "-force")

# ---- compress ----
os.makedirs(os.path.dirname(OUT), exist_ok=True)
try: os.remove(OUT)
except FileNotFoundError: pass
run("hdiutil", "convert", RW, "-format", "UDZO", "-imagekey", "zlib-level=9", "-o", OUT)
os.remove(RW)
print("DONE:", OUT)
