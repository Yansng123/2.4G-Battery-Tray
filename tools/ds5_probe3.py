# -*- coding: utf-8 -*-
"""第三轮：对同一设备多次取样，diff 出变化字节（时间戳/噪声）与稳定字节（电量）；
   同时兼容蓝牙 PID_0CE6 与有线 PID_0DF2 两种模式，便于插线做充电对照。"""
import ctypes, ctypes.wintypes as wt, time, os
from ds5_probe import (enum_paths, describe, read_reports, hexdump,
                       hid, k32, INVALID_HANDLE, GENERIC_READ,
                       FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING)

OUT = []
def w(s=""):
    OUT.append(s)

def get_input_report(path, rid, length):
    h = k32.CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE or not h:
        return None
    try:
        b = ctypes.create_string_buffer(max(length, 2))
        b[0] = rid
        if hid.HidD_GetInputReport(h, b, max(length, 2)):
            return bytes(b[:max(length, 2)])
        return None
    finally:
        k32.CloseHandle(h)

def main():
    w("=== DS5 第三轮对照探针 %s ===" % time.strftime("%H:%M:%S"))
    devs = []
    for p in enum_paths():
        info, h = describe(p)
        if h not in (INVALID_HANDLE, None):
            try: k32.CloseHandle(h)
            except Exception: pass
        if info and info["vid"] == 0x054C and info["pid"] in (0x0CE6, 0x0DF2):
            devs.append(info)
    if not devs:
        w("未找到任何索尼 DS5 设备（0CE6/0DF2）")
        txt = "\n".join(OUT)
        open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ds5-probe3.txt"), "w", encoding="utf-8").write(txt)
        return
    for info in devs:
        path, length = info["path"], info.get("in", 64)
        mode = "蓝牙" if info["pid"] == 0x0CE6 else "有线USB"
        w("")
        w("######## %s  PID=%04X  in=%d ########" % (mode, info["pid"], length))
        w("path: %s" % path)
        samples = []
        for rid in (0x31, 0x01):
            r = get_input_report(path, rid, length)
            if r:
                samples.append((rid, r))
        if not samples:
            w("  GetInputReport 均失败，回退流式读取")
            reps, st = read_reports(path, length, count=4, per_read_ms=1200)
            w("  流式: %s (%d 帧)" % (st, len(reps)))
            for ms, d in reps[:2]:
                for line in hexdump(d):
                    w(line)
            continue
        for rid, data in samples:
            w("  [GetInputReport id=0x%02X]" % rid)
            for line in hexdump(data):
                w(line)
        # diff 两次取样（间隔 2.5 秒），找出稳定字节与变化字节
        base = samples[0][1]
        time.sleep(2.5)
        again = get_input_report(path, samples[0][0], length)
        if again:
            changed = [i for i in range(min(len(base), len(again))) if base[i] != again[i]]
            stable = [i for i in range(min(len(base), len(again)))
                      if base[i] == again[i] and base[i] != 0]
            w("  2.5 秒后变化字节: %s" % (", ".join(str(i) for i in changed) or "无"))
            w("  非零且稳定字节: %s" % (", ".join("%d=0x%02X" % (i, base[i]) for i in stable)))
            w("  第 50~58 字节: %s" % " ".join("%02X" % b for b in base[50:59]))

    txt = "\n".join(OUT)
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ds5-probe3.txt")
    open(out, "w", encoding="utf-8").write(txt)
    print("WROTE", out, len(txt))

if __name__ == "__main__":
    main()
