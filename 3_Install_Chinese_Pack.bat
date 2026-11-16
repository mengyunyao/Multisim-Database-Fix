@echo off
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /r:System.dll /out:"%TEMP%\Multisim_InstallChinese.exe" "%~dp0src\InstallChinese.cs"
"%TEMP%\Multisim_InstallChinese.exe"
