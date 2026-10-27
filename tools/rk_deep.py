# -*- coding: utf-8 -*-
"""最后一遍深挖：把 Compx 接收器上还能读的通道全试一遍，看有没有活的电数字节。

1) FF02/0002 的 Feature Report 从 0x01 扫到 0xFF（之前只扫到 0x20）
2) 先 SetFeature 再 GetFeature，看有没有"先请求再应答"的协议
3) FF01/0000 的 8 字节输入报告：流式读 + HidD_GetInputReport
4) 接收器上那些键盘集合（含 in=9 那个多出来的）也试着索取输入报告
"""
import ctypes, os, time, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("rk_sweep", os.path.join(HERE, "rk_sweep.py"))
rk = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rk)

hid, k32, ctypes_ = rk.hid, rk.k32, rk.ctypes
wt = rk.wt
GENERIC_READ, FILE_FLAG_OVERLAPPED = 0x80000000, 0x40000000
ERROR_IO_PENDING, WAIT_OBJECT_0 = 997, 0

class OVERLAPPED(ctypes_.Structure):
    _fields_ = [("Internal", ctypes_.c_void_p), ("InternalHigh", ctypes_.c_void_p),
                ("Offset", wt.DWORD), ("OffsetHigh", wt.DWORD), ("hEvent", wt.HANDLE)]


hid.HidD_GetInputReport.argtypes = [wt.HANDLE, ctypes_.c_void_p, ctypes_.c_ulong]
hid.HidD_SetFeature.argtypes = [wt.HANDLE, ctypes_.c_void_p, ctypes_.c_ulong]
k32.CreateEventW.restype = wt.HANDLE
k32.CreateEventW.argtypes = [ctypes_.c_void_p, wt.BOOL, wt.BOOL, wt.LPCWSTR]
k32.ReadFile.argtypes = [wt.HANDLE, ctypes_.c_void_p, wt.DWORD, ctypes_.POINTER(wt.DWORD), ctypes_.POINTER(OVERLAPPED)]
k32.WaitForSingleObject.argtypes = [wt.HANDLE, wt.DWORD]
k32.GetOverlappedResult.argtypes = [wt.HANDLE, ctypes_.POINTER(OVERLAPPED), ctypes_.POINTER(wt.DWORD), wt.BOOL]
k32.CancelIoEx.argtypes = [wt.HANDLE, ctypes_.POINTER(OVERLAPPED)]


def scan_all():
    """返回 [(path, vid, pid, up, usage, in_len, out_len, feat_len)]，只留 25A7/258A。"""
    out = []
    for p in rk.enum_paths():
        h = k32.CreateFileW(p, 0, rk.FILE_SHARE_READ | rk.FILE_SHARE_WRITE, None, rk.OPEN_EXISTING, 0, None)
        if h == rk.INVALID_HANDLE or not h:
            continue
        try:
            a = rk.HIDD_ATTRIBUTES()
            a.Size = ctypes_.sizeof(rk.HIDD_ATTRIBUTES)
            if not hid.HidD_GetAttributes(h, ctypes_.byref(a)):
                continue
            if a.VendorID not in (0x25A7, 0x258A):
                continue
            caps = None
            prep = ctypes_.c_void_p()
            if hid.HidD_GetPreparsedData(h, ctypes_.byref(prep)):
                try:
                    c = rk.HIDP_CAPS()
                    if hid.HidP_GetCaps(prep, ctypes_.byref(c)) == 0x00110000:
                        caps = c
                finally:
                    hid.HidD_FreePreparsedData(prep)
            out.append((p, a.VendorID, a.ProductID,
                        caps.UsagePage if caps else 0, caps.Usage if caps else 0,
                        caps.InputReportByteLength if caps else 0,
                        caps.OutputReportByteLength if caps else 0,
                        caps.FeatureReportByteLength if caps else 0))
        finally:
            k32.CloseHandle(h)
    return out


def open_any(path, read=False):
    access = GENERIC_READ if read else 0
    flags = FILE_FLAG_OVERLAPPED if read else 0
    return k32.CreateFileW(path, access, rk.FILE_SHARE_READ | rk.FILE_SHARE_WRITE,
                           None, rk.OPEN_EXISTING, flags, None)


def stream_read(path, length, count=5, per_ms=1200):
    """流式读输入报告（需要 GENERIC_READ）。"""
    h = open_any(path, True)
    if h == rk.INVALID_HANDLE or not h:
        return None, "CreateFile 失败 (%d)" % ctypes_.get_last_error()
    try:
        ev = k32.CreateEventW(None, True, False, None)
        got = []
        for _ in range(count):
            buf = (ctypes_.c_ubyte * length)()
            ov = OVERLAPPED()
            ov.hEvent = ev
            k32.ResetEvent(ev)
            read = wt.DWORD(0)
            ok = k32.ReadFile(h, ctypes_.byref(buf), length, ctypes_.byref(read), ctypes_.byref(ov))
            if not ok and ctypes_.get_last_error() != ERROR_IO_PENDING:
                return got, "ReadFile 失败 (%d)" % ctypes_.get_last_error()
            if k32.WaitForSingleObject(ev, per_ms) == WAIT_OBJECT_0:
                k32.GetOverlappedResult(h, ctypes_.byref(ov), ctypes_.byref(read), False)
                if read.value:
                    got.append(bytes(bytearray(buf[:read.value])))
            else:
                k32.CancelIoEx(h, ctypes_.byref(ov))
        return got, ""
    finally:
        k32.CloseHandle(h)


def main():
    ifaces = scan_all()
    print("=== 接口 ===")
    for p, vid, pid, up, us, il, ol, fl in ifaces:
        print("vid=%04X pid=%04X up=%04X usage=%04X in=%d out=%d feat=%d" % (vid, pid, up, us, il, ol, fl))

    # 1) Feature 报告扫到底
    print()
    print("=== Feature Report 0x01~0xFF（FF02/0002） ===")
    for p, vid, pid, up, us, il, ol, fl in ifaces:
        if not (up == 0xFF02 and us == 0x0002 and fl > 0):
            continue
        h = open_any(p)
        if h == rk.INVALID_HANDLE or not h:
            continue
        try:
            n = max(fl, 64)
            hits = []
            for rid in range(0x01, 0x100):
                buf = (ctypes_.c_ubyte * n)()
                buf[0] = rid
                if hid.HidD_GetFeature(h, ctypes_.byref(buf), n):
                    hits.append((rid, bytes(bytearray(buf[:min(n, 12)]))))
            print("响应了 %d 个报告号：%s" % (len(hits), [hex(r) for r, _ in hits]))
            for rid, b in hits:
                print("  0x%02X  %s" % (rid, rk.hexs(b)))
        finally:
            k32.CloseHandle(h)

    # 2) 先写后读
    print()
    print("=== SetFeature 0x06 后 GetFeature ===")
    for p, vid, pid, up, us, il, ol, fl in ifaces:
        if not (up == 0xFF02 and us == 0x0002 and fl > 0):
            continue
        h = open_any(p)
        if h == rk.INVALID_HANDLE or not h:
            continue
        try:
            n = max(fl, 64)
            for payload in ((0x06, 0x01), (0x06, 0x02), (0x06, 0x03)):
                w = (ctypes_.c_ubyte * n)()
                for i, v in enumerate(payload):
                    w[i] = v
                sok = hid.HidD_SetFeature(h, ctypes_.byref(w), n)
                r = (ctypes_.c_ubyte * n)()
                r[0] = 0x06
                gok = hid.HidD_GetFeature(h, ctypes_.byref(r), n)
                print("  写 %s ok=%s -> 读 ok=%s %s" % (
                    rk.hexs(bytes(payload)), sok, gok,
                    rk.hexs(bytes(bytearray(r[:8]))) if gok else "-"))
        finally:
            k32.CloseHandle(h)

    # 3) 厂商输入接口 FF01
    print()
    print("=== FF01/0000 输入报告（流式读） ===")
    for p, vid, pid, up, us, il, ol, fl in ifaces:
        if up != 0xFF01 or il == 0:
            continue
        got, err = stream_read(p, il, count=6)
        print("in=%d err=%s" % (il, err))
        if got:
            for b in got:
                print("  %s" % rk.hexs(b))
            if len(got) > 1 and len(set(got)) > 1:
                diff = [i for i in range(min(len(got[0]), il)) if len(set(g[i] for g in got)) > 1]
                print("  变化字节下标:", diff)
        else:
            print("  没读到任何输入报告（键盘可能没在主动上报）")
        # HidD_GetInputReport 兜底
        h = open_any(p)
        if h != rk.INVALID_HANDLE and h:
            try:
                for rid in (0x00, 0x01, 0x02, 0x06):
                    buf = (ctypes_.c_ubyte * max(il, 8))()
                    buf[0] = rid
                    if hid.HidD_GetInputReport(h, ctypes_.byref(buf), max(il, 8)):
                        print("  GetInputReport 0x%02X -> %s" % (rid, rk.hexs(bytes(bytearray(buf[:max(il, 8)])))))
            finally:
                k32.CloseHandle(h)

    # 4) 键盘集合（含 in=9 那个）
    print()
    print("=== 键盘集合 GetInputReport（access=0，规避系统反键录保护） ===")
    for p, vid, pid, up, us, il, ol, fl in ifaces:
        if not (up == 0x0001 and us == 0x0006):
            continue
        h = open_any(p)
        if h == rk.INVALID_HANDLE or not h:
            print("  in=%d 打开失败 (%d)" % (il, ctypes_.get_last_error()))
            continue
        try:
            for rid in (0x00, 0x01, 0x06):
                buf = (ctypes_.c_ubyte * max(il, 9))()
                buf[0] = rid
                ok = hid.HidD_GetInputReport(h, ctypes_.byref(buf), max(il, 9))
                print("  in=%d 0x%02X -> %s" % (il, rid,
                      rk.hexs(bytes(bytearray(buf[:max(il, 9)]))) if ok else "失败"))
        finally:
            k32.CloseHandle(h)


if __name__ == "__main__":
    main()
