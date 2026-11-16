@echo off
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /r:System.dll /out:"%TEMP%\Multisim_FixInstaller.exe" "%~dp0src\FixInstaller.cs"
"%TEMP%\Multisim_FixInstaller.exe"
