# -*- coding: utf-8 -*-
"""只读探针：枚举 HID 设备，定位索尼 DS5 手柄，抓取原始输入报文并 dump。"""
import ctypes, ctypes.wintypes as wt, struct, time, os, uuid

OUT = []
def w(s=""):
    OUT.append(s)

setupapi = ctypes.WinDLL("setupapi.dll", use_last_error=True)
hid = ctypes.WinDLL("hid.dll", use_last_error=True)
k32 = ctypes.WinDLL("kernel32.dll", use_last_error=True)

HID_GUID = uuid.UUID("4d1e55b2-f16f-11cf-88cb-001111000030")
DIGCF_PRESENT, DIGCF_DEVICEINTERFACE = 0x02, 0x10
GENERIC_READ, GENERIC_WRITE = 0x80000000, 0x40000000
FILE_SHARE_READ, FILE_SHARE_WRITE = 0x01, 0x02
OPEN_EXISTING, FILE_FLAG_OVERLAPPED = 3, 0x40000000
ERROR_IO_PENDING, WAIT_OBJECT_0 = 997, 0
INVALID_HANDLE = ctypes.c_void_p(-1).value

class GUID(ctypes.Structure):
    _fields_ = [("b", ctypes.c_ubyte * 16)]

HID_GUID_S = GUID()
HID_GUID_S.b = (ctypes.c_ubyte * 16)(*HID_GUID.bytes_le)

class SP_DEVINFO_DATA(ctypes.Structure):
    _fields_ = [("cbSize", wt.DWORD), ("InterfaceClassGuid", GUID),
                ("Flags", wt.DWORD), ("Reserved", ctypes.c_void_p)]

class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", ctypes.c_ulong), ("VendorID", wt.USHORT),
                ("ProductID", wt.USHORT), ("VersionNumber", wt.USHORT)]

class HIDP_CAPS(ctypes.Structure):
    _fields_ = [("Usage", wt.USHORT), ("UsagePage", wt.USHORT),
                ("InputReportByteLength", wt.USHORT), ("OutputReportByteLength", wt.USHORT),
                ("FeatureReportByteLength", wt.USHORT),
                ("Reserved", wt.USHORT * 17),
                ("NumberLinkCollectionNodes", wt.USHORT),
                ("NumberInputButtonCaps", wt.USHORT), ("NumberInputValueCaps", wt.USHORT),
                ("NumberInputDataIndices", wt.USHORT), ("NumberOutputButtonCaps", wt.USHORT),
                ("NumberOutputValueCaps", wt.USHORT), ("NumberOutputDataIndices", wt.USHORT),
                ("NumberFeatureButtonCaps", wt.USHORT), ("NumberFeatureValueCaps", wt.USHORT),
                ("NumberFeatureDataIndices", wt.USHORT)]

class OVERLAPPED(ctypes.Structure):
    _fields_ = [("Internal", ctypes.c_void_p), ("InternalHigh", ctypes.c_void_p),
                ("Offset", wt.DWORD), ("OffsetHigh", wt.DWORD), ("hEvent", wt.HANDLE)]

setupapi.SetupDiGetClassDevsW.restype = wt.HANDLE
setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), wt.LPCWSTR, wt.HWND, wt.DWORD]
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.POINTER(GUID), wt.DWORD, ctypes.POINTER(SP_DEVINFO_DATA)]
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [wt.HANDLE, ctypes.POINTER(SP_DEVINFO_DATA), ctypes.c_void_p, wt.DWORD, ctypes.POINTER(wt.DWORD), ctypes.c_void_p]
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [wt.HANDLE]

hid.HidD_GetAttributes.argtypes = [wt.HANDLE, ctypes.POINTER(HIDD_ATTRIBUTES)]
hid.HidD_GetPreparsedData.argtypes = [wt.HANDLE, ctypes.POINTER(ctypes.c_void_p)]
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)]
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidD_GetProductString.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_ulong]
hid.HidD_GetManufacturerString.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_ulong]
hid.HidD_GetInputReport.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_ulong]

k32.CreateFileW.restype = wt.HANDLE
k32.CreateFileW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, ctypes.c_void_p, wt.DWORD, wt.DWORD, wt.HANDLE]
k32.CreateEventW.restype = wt.HANDLE
k32.CreateEventW.argtypes = [ctypes.c_void_p, wt.BOOL, wt.BOOL, wt.LPCWSTR]
k32.ResetEvent.argtypes = [wt.HANDLE]
k32.ReadFile.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.DWORD, ctypes.POINTER(wt.DWORD), ctypes.POINTER(OVERLAPPED)]
k32.WriteFile.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.DWORD, ctypes.POINTER(wt.DWORD), ctypes.POINTER(OVERLAPPED)]
k32.GetOverlappedResult.argtypes = [wt.HANDLE, ctypes.POINTER(OVERLAPPED), ctypes.POINTER(wt.DWORD), wt.BOOL]
k32.CancelIoEx.argtypes = [wt.HANDLE, ctypes.POINTER(OVERLAPPED)]
k32.WaitForSingleObject.argtypes = [wt.HANDLE, wt.DWORD]


def enum_paths():
    paths = []
    h = setupapi.SetupDiGetClassDevsW(ctypes.byref(HID_GUID_S), None, None, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    if h == INVALID_HANDLE or not h:
        return paths
    try:
        i = 0
        while True:
            did = SP_DEVINFO_DATA()
            did.cbSize = ctypes.sizeof(SP_DEVINFO_DATA)
            if not setupapi.SetupDiEnumDeviceInterfaces(h, None, ctypes.byref(HID_GUID_S), i, ctypes.byref(did)):
                break
            i += 1
            need = wt.DWORD(0)
            setupapi.SetupDiGetDeviceInterfaceDetailW(h, ctypes.byref(did), None, 0, ctypes.byref(need), None)
            if need.value == 0:
                continue
            buf = ctypes.create_string_buffer(need.value)
            ctypes.cast(buf, ctypes.POINTER(wt.DWORD))[0] = 8
            if not setupapi.SetupDiGetDeviceInterfaceDetailW(h, ctypes.byref(did), buf, need, ctypes.byref(need), None):
                continue
            path = ctypes.wstring_at(ctypes.addressof(buf) + 4)
            paths.append(path)
    finally:
        setupapi.SetupDiDestroyDeviceInfoList(h)
    return paths


def get_string(h, func):
    buf = ctypes.create_unicode_buffer(256)
    try:
        if func(h, buf, 512):
            return buf.value
    except Exception:
        pass
    return ""


def describe(path, for_read=False):
    access = GENERIC_READ | (GENERIC_WRITE if not for_read else 0)
    flags = FILE_FLAG_OVERLAPPED if for_read else 0
    h = k32.CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING, flags, None)
    if h == INVALID_HANDLE or not h:
        return None, None
    try:
        a = HIDD_ATTRIBUTES()
        a.Size = ctypes.sizeof(HIDD_ATTRIBUTES)
        if not hid.HidD_GetAttributes(h, ctypes.byref(a)):
            return None, None
        prep = ctypes.c_void_p()
        caps = None
        if hid.HidD_GetPreparsedData(h, ctypes.byref(prep)):
            try:
                c = HIDP_CAPS()
                if hid.HidP_GetCaps(prep, ctypes.byref(c)) == 0x00110000:
                    caps = c
            finally:
                hid.HidD_FreePreparsedData(prep)
        info = {
            "path": path, "vid": a.VendorID, "pid": a.ProductID, "ver": a.VersionNumber,
            "product": get_string(h, hid.HidD_GetProductString),
            "manufacturer": get_string(h, hid.HidD_GetManufacturerString),
        }
        if caps:
            info.update({"up": caps.UsagePage, "usage": caps.Usage,
                         "in": caps.InputReportByteLength, "out": caps.OutputReportByteLength,
                         "feat": caps.FeatureReportByteLength})
        return info, h
    except Exception:
        return None, h


def read_reports(path, length, count=8, per_read_ms=1500):
    """只读方式打开并抓取输入报文，返回 [(elapsed_ms, bytes)]"""
    h = k32.CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, None,
                        OPEN_EXISTING, FILE_FLAG_OVERLAPPED, None)
    if h == INVALID_HANDLE or not h:
        return None, "open failed (err=%d)" % ctypes.get_last_error()
    ev = k32.CreateEventW(None, True, False, None)
    buf = ctypes.create_string_buffer(max(length, 2))
    got = []
    t0 = time.time()
    try:
        for _ in range(count):
            ov = OVERLAPPED()
            ov.hEvent = ev
            k32.ResetEvent(ev)
            read = wt.DWORD(0)
            ok = k32.ReadFile(h, buf, max(length, 2), ctypes.byref(read), ctypes.byref(ov))
            if not ok:
                if ctypes.get_last_error() != ERROR_IO_PENDING:
                    k32.CloseHandle(h); k32.CloseHandle(ev)
                    return got, "ReadFile err=%d" % ctypes.get_last_error()
                rc = k32.WaitForSingleObject(ev, per_read_ms)
                if rc != WAIT_OBJECT_0:
                    k32.CancelIoEx(h, ctypes.byref(ov))
                    k32.WaitForSingleObject(ev, 100)
                    break
                if not k32.GetOverlappedResult(h, ctypes.byref(ov), ctypes.byref(read), False):
                    break
            if read.value:
                got.append((int((time.time() - t0) * 1000), bytes(buf[:read.value])))
    finally:
        k32.CloseHandle(h)
        k32.CloseHandle(ev)
    return got, ("ok" if got else "no report received")


def hexdump(data, base=0):
    lines = []
    for off in range(0, len(data), 16):
        chunk = data[off:off + 16]
        hexs = " ".join("%02X" % b for b in chunk)
        idx = " ".join("%2d" % (base + off + k) for k in range(len(chunk)))
        lines.append("  %3d | %-47s | idx: %s" % (off, hexs, idx))
    return lines


def main():
    w("=== DS5 只读探针 %s ===" % time.strftime("%Y-%m-%d %H:%M:%S"))
    paths = enum_paths()
    w("HID 接口总数: %d" % len(paths))
    w("")
    w("--- 所有 HID 设备摘要 ---")
    sony = []
    for p in paths:
        info, h = describe(p)
        if h is not None and h != INVALID_HANDLE:
            try: k32.CloseHandle(h)
            except Exception: pass
        if not info:
            continue
        mark = ""
        if info["vid"] == 0x054C:
            mark = "  <<< SONY"
            sony.append(info)
        elif "controller" in info["product"].lower() or "dualsense" in info["product"].lower():
            mark = "  <<< 疑似手柄"
            sony.append(info)
        if mark:
            w("VID=%04X PID=%04X up=%04X usage=%04X in=%s out=%s feat=%s | %s | %s%s" % (
                info["vid"], info["pid"], info.get("up", 0), info.get("usage", 0),
                info.get("in", 0), info.get("out", 0), info.get("feat", 0),
                info["manufacturer"], info["product"], mark))
            w("    path: %s" % info["path"])
    w("")
    w("--- 索尼/手柄候选: %d 个 ---" % len(sony))

    for info in sony:
        w("")
        w("######## 设备 VID=%04X PID=%04X ########" % (info["vid"], info["pid"]))
        w("product   : %s" % info["product"])
        w("manufactur: %s" % info["manufacturer"])
        w("usage     : page=%04X usage=%04X  in=%s out=%s feat=%s" % (
            info.get("up", 0), info.get("usage", 0), info.get("in", 0), info.get("out", 0), info.get("feat", 0)))
        w("path      : %s" % info["path"])
        # 标准 HID Battery Strength 检查（Usage Page 0x0006 / Usage 0x0020）
        if info.get("up") == 0x0006:
            w("!! 该集合是 Generic Device Controls 页，可能承载 Battery Strength(0x20)")
        length = info.get("in", 0) or 64
        reports, status = read_reports(info["path"], length)
        w("读取结果  : %s（%d 帧）" % (status, len(reports) if reports else 0))
        for idx, (ms, data) in enumerate(reports or []):
            w("  帧#%d  t+%dms  len=%d  report_id=0x%02X" % (idx + 1, ms, len(data), data[0]))
            for line in hexdump(data):
                w(line)
            if idx >= 2 and len(data) > 40:
                w("  (后续帧省略部分，仅保留前 3 帧)")
                break
        if not reports:
            # 退路：HidD_GetInputReport 取系统缓存的最后一帧
            h = k32.CreateFileW(info["path"], GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
            if h not in (INVALID_HANDLE, None):
                for rid in (0x01, 0x31):
                    b = ctypes.create_string_buffer(max(length, 2))
                    b[0] = rid
                    if hid.HidD_GetInputReport(h, b, max(length, 2)):
                        w("  [HidD_GetInputReport id=0x%02X] " % rid + " ".join("%02X" % c for c in b[:min(length, 96)]))
                k32.CloseHandle(h)

    txt = "\n".join(OUT)
    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ds5-probe.txt")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(txt)
    print("WROTE", out_path, "chars", len(txt))


if __name__ == "__main__":
    main()
