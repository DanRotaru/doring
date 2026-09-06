@echo off
rem Authenticode-sign a file with the local "DanRotaru" code-signing certificate.
rem Usage: sign.cmd <file>
rem Create the certificate once with:
rem   powershell -Command "New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=DanRotaru' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(10)"
setlocal

if "%~1"=="" (
  echo Usage: sign.cmd ^<file^>
  exit /b 1
)
if not exist "%~1" (
  echo sign: "%~1" not found.
  exit /b 1
)

set KITBIN=%ProgramFiles(x86)%\Windows Kits\10\bin
set SIGNTOOL=
for /f "usebackq tokens=*" %%i in (`dir /b /o-n "%KITBIN%\10.*" 2^>nul`) do (
  if not defined SIGNTOOL if exist "%KITBIN%\%%i\x64\signtool.exe" set "SIGNTOOL=%KITBIN%\%%i\x64\signtool.exe"
)
if not defined SIGNTOOL (
  echo Note: signtool.exe not found, skipping signing.
  exit /b 0
)

"%SIGNTOOL%" sign /n DanRotaru /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d "%~n1" "%~1" >nul 2>&1
if errorlevel 1 (
  echo Note: not signed ^(no DanRotaru certificate, or the file is in use^).
  exit /b 0
)
echo Signed as DanRotaru: %~nx1
exit /b 0
