@echo off
setlocal
dotnet publish DoRing -c Release
if errorlevel 1 exit /b 1
call "%~dp0sign.cmd" "%~dp0DoRing\bin\Release\net10.0-windows\win-x64\publish\DoRing.exe"
endlocal
