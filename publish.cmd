@echo off
rem Pravi jedan self-contained exe: dist\UsbCopyStation.exe
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET SDK nije instaliran.
  echo Preuzmi .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

dotnet publish UsbCopyStation.csproj -c Release -o "%~dp0dist"
if errorlevel 1 (
  echo.
  echo BUILD NIJE USPEO - pogledaj greske iznad.
  pause
  exit /b 1
)

echo.
echo Gotovo: %~dp0dist\UsbCopyStation.exe
pause
