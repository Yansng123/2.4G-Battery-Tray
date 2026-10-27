# -*- coding: utf-8 -*-
"""第二轮只读探针：确认是否有完整报文（含电量），并尝试系统缓存报告。"""
import ctypes, ctypes.wintypes as wt, time, os
from ds5_probe import (enum_paths, describe, read_reports, hexdump,
                       hid, k32, wt as w2, INVALID_HANDLE, GENERIC_READ,
                       GENERIC_WRITE, FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING)

OUT = []
def w(s=""):
    OUT.append(s)

def get_input_report(path, rid, length):
    """HidD_GetInputReport：向系统/设备索取指定 ID 的一份报告（只读语义）"""
    h = k32.CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE or not h:
        return "open failed err=%d" % ctypes.get_last_error()
    try:
        b = ctypes.create_string_buffer(max(length, 2))
        b[0] = rid
        if hid.HidD_GetInputReport(h, b, max(length, 2)):
            return " ".join("%02X" % c for c in b[:max(length, 2)])
        return "failed err=%d" % ctypes.get_last_error()
    finally:
        k32.CloseHandle(h)

def get_feature(path, rid, length):
    """HidD_GetFeature：读取特性报告（只读）"""
    h = k32.CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE or not h:
        return "open failed err=%d" % ctypes.get_last_error()
    try:
        b = ctypes.create_string_buffer(max(length, 2))
        b[0] = rid
        if hid.HidD_GetFeature(h, b, max(length, 2)):
            return " ".join("%02X" % c for c in b[:max(length, 2)])
        return "failed err=%d" % ctypes.get_last_error()
    finally:
        k32.CloseHandle(h)

def main():
    w("=== DS5 第二轮只读探针 %s ===" % time.strftime("%H:%M:%S"))
    target = None
    for p in enum_paths():
        info, h = describe(p)
        if h not in (INVALID_HANDLE, None):
            try: k32.CloseHandle(h)
            except Exception: pass
        if info and info["vid"] == 0x054C and info["pid"] == 0x0CE6:
            target = info
            break
    if not target:
        w("未找到 DS5 (054C:0CE6)")
        return
    path, length = target["path"], target.get("in", 64)
    w("设备: %s  in=%d" % (target["product"], length))

    # 1) 抓 12 帧，统计哪些字节曾出现过非零值
    reports, status = read_reports(path, length, count=12, per_read_ms=1200)
    w("")
    w("--- 流式读取: %s, %d 帧 ---" % (status, len(reports)))
    nonzero = {}
    for ms, data in reports:
        for i, b in enumerate(data):
            if b:
                nonzero.setdefault(i, set()).add(b)
    w("出现过非零值的字节下标: %s" % ", ".join(
        "%d" % k for k in sorted(nonzero)))
    w("其余字节恒为 0（说明手柄未上报这些数据）")
    for idx, (ms, data) in enumerate(reports[:2]):
        w("  帧#%d 全量:" % (idx + 1))
        for line in hexdump(data):
            w(line)

    # 2) 系统缓存报告 / 索取报告
    w("")
    w("--- HidD_GetInputReport（索取一份指定 ID 的报告，只读） ---")
    for rid in (0x01, 0x31, 0x11):
        w("  id=0x%02X : %s" % (rid, get_input_report(path, rid, length)))

    w("")
    w("--- HidD_GetFeature（特性报告，只读） ---")
    for rid in (0x05, 0x09, 0x20):
        w("  id=0x%02X : %s" % (rid, get_feature(path, rid, max(target.get("feat", 64), 64))))

    txt = "\n".join(OUT)
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ds5-probe2.txt")
    open(out, "w", encoding="utf-8").write(txt)
    print("WROTE", out, len(txt))

if __name__ == "__main__":
    main()
