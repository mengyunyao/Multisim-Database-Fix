@echo off
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] Microsoft .NET Framework v4.0.30319 compiler not found.
    pause
    exit /b 1
)
"%CSC%" /nologo /r:System.dll /r:System.Xml.dll /out:"%TEMP%\FixMultisimDatabase.exe" "%~dp0FixDatabase.cs"
if errorlevel 1 (
    echo [ERROR] Failed to compile FixDatabase.cs.
    pause
    exit /b 1
)
"%TEMP%\FixMultisimDatabase.exe"
