# -*- coding: utf-8 -*-
"""只读探针：把 Compx(25A7) 接收器的所有 Feature Report ID 全扫一遍，
并连续采样 3 次做差分，找出哪些字节是活的（连接状态/电量），哪些是死的缓存。
同时检查 258A:00A6（有线充电时才会出现的键盘本体）是否存在。

用法: python rk_sweep.py
"""
import ctypes, ctypes.wintypes as wt, time, uuid, sys

setupapi = ctypes.WinDLL("setupapi.dll", use_last_error=True)
hid = ctypes.WinDLL("hid.dll", use_last_error=True)
k32 = ctypes.WinDLL("kernel32.dll", use_last_error=True)

HID_GUID = uuid.UUID("4d1e55b2-f16f-11cf-88cb-001111000030")
DIGCF_PRESENT, DIGCF_DEVICEINTERFACE = 0x02, 0x10
FILE_SHARE_READ, FILE_SHARE_WRITE = 0x01, 0x02
OPEN_EXISTING = 3
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
hid.HidD_GetFeature.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_ulong]
k32.CreateFileW.restype = wt.HANDLE
k32.CreateFileW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, ctypes.c_void_p, wt.DWORD, wt.DWORD, wt.HANDLE]


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
            paths.append(ctypes.wstring_at(ctypes.addressof(buf) + 4))
    finally:
        setupapi.SetupDiDestroyDeviceInfoList(h)
    return paths


def hexs(b):
    return " ".join("%02X" % x for x in b)


def main():
    print("=== HID 接口清单（只看 25A7 / 258A） ===")
    targets = []
    charge_present = False
    for p in enum_paths():
        # 和 C# 一致：access=0，避免键盘集合被系统拒绝（错误码 5）
        h = k32.CreateFileW(p, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
        if h == INVALID_HANDLE or not h:
            continue
        try:
            a = HIDD_ATTRIBUTES()
            a.Size = ctypes.sizeof(HIDD_ATTRIBUTES)
            if not hid.HidD_GetAttributes(h, ctypes.byref(a)):
                continue
            if a.VendorID not in (0x25A7, 0x258A):
                continue
            caps = None
            prep = ctypes.c_void_p()
            if hid.HidD_GetPreparsedData(h, ctypes.byref(prep)):
                try:
                    c = HIDP_CAPS()
                    if hid.HidP_GetCaps(prep, ctypes.byref(c)) == 0x00110000:
                        caps = c
                finally:
                    hid.HidD_FreePreparsedData(prep)
            pbuf = ctypes.create_unicode_buffer(256)
            name = ""
            if hid.HidD_GetProductString(h, pbuf, 512):
                name = pbuf.value
            up = caps.UsagePage if caps else 0
            us = caps.Usage if caps else 0
            feat = caps.FeatureReportByteLength if caps else 0
            print("vid=%04X pid=%04X up=%04X usage=%04X feat=%d in=%d name=%s" % (
                a.VendorID, a.ProductID, up, us, feat,
                caps.InputReportByteLength if caps else 0, name))
            if a.VendorID == 0x258A and a.ProductID == 0x00A6:
                charge_present = True
            if a.VendorID == 0x25A7 and feat > 0:
                targets.append((p, feat, up, us))
        finally:
            k32.CloseHandle(h)

    print()
    print("258A:00A6（有线本体）存在 =", charge_present, " -> 可作为『正在充电』判据")

    print()
    print("=== Feature Report 全扫描（report id 0x01~0x20） ===")
    for p, feat, up, us in targets:
        print()
        print("接口 up=%04X usage=%04X featLen=%d" % (up, us, feat))
        print("  " + p)
        h = k32.CreateFileW(p, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
        if h == INVALID_HANDLE or not h:
            print("  打开失败")
            continue
        try:
            n = max(feat, 64)
            for rid in range(0x01, 0x21):
                buf = (ctypes.c_ubyte * n)()
                buf[0] = rid
                if hid.HidD_GetFeature(h, ctypes.byref(buf), n):
                    print("  id=0x%02X  %s" % (rid, hexs(bytes(bytearray(buf[:min(n, 16)])))))
        finally:
            k32.CloseHandle(h)

    print()
    print("=== 0x06 报告连续 3 次采样（间隔 2 秒，差分看哪些字节在动） ===")
    for p, feat, up, us in targets:
        h = k32.CreateFileW(p, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
        if h == INVALID_HANDLE or not h:
            continue
        try:
            n = max(feat, 64)
            snaps = []
            for _ in range(3):
                buf = (ctypes.c_ubyte * n)()
                buf[0] = 0x06
                if hid.HidD_GetFeature(h, ctypes.byref(buf), n):
                    snaps.append(bytes(bytearray(buf[:n])))
                else:
                    snaps.append(None)
                time.sleep(2)
            print("接口 up=%04X usage=%04X" % (up, us))
            for i, s in enumerate(snaps):
                print("  #%d %s" % (i + 1, hexs(s[:16]) if s else "读取失败"))
            if all(snaps) and snaps[0] == snaps[1] == snaps[2]:
                print("  -> 三次完全一致：这是接收器缓存值，不是实时读数")
            elif all(snaps):
                diff = [i for i in range(min(len(snaps[0]), 16)) if len(set(s[i] for s in snaps)) > 1]
                print("  -> 变化字节下标:", diff)
        finally:
            k32.CloseHandle(h)


if __name__ == "__main__":
    main()
