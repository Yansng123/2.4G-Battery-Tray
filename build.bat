@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo csc.exe not found. .NET Framework 4.x is required.
    exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /out:"%~dp02.4G电量显示.exe" ^
    "%~dp0src\Native.cs" ^
    "%~dp0src\HidEnum.cs" ^
    "%~dp0src\Model.cs" ^
    "%~dp0src\BatteryCurve.cs" ^
    "%~dp0src\LogitechHidpp.cs" ^
    "%~dp0src\RkCompx.cs" ^
    "%~dp0src\DualSenseHid.cs" ^
    "%~dp0src\DeviceMonitor.cs" ^
    "%~dp0src\Settings.cs" ^
    "%~dp0src\Autostart.cs" ^
    "%~dp0src\BatteryIcon.cs" ^
    "%~dp0src\PopupCard.cs" ^
    "%~dp0src\DeviceWatcher.cs" ^
    "%~dp0src\TrayController.cs" ^
    "%~dp0src\Probe.cs" ^
    "%~dp0src\Program.cs"

if errorlevel 1 (
    echo.
    echo Build FAILED.
    exit /b 1
)
echo.
echo Build OK: %~dp02.4G电量显示.exe
endlocal
