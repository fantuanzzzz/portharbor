@echo off
setlocal
set "RUNTIME_FOUND="
set "DOTNET_X64=%ProgramW6432%\dotnet\dotnet.exe"
if not exist "%DOTNET_X64%" set "DOTNET_X64=%ProgramFiles%\dotnet\dotnet.exe"

if exist "%DOTNET_X64%" (
    for /f "tokens=1,2" %%A in ('"%DOTNET_X64%" --list-runtimes 2^>nul') do (
        if /I "%%A"=="Microsoft.WindowsDesktop.App" (
            echo %%B | findstr /R /B "8\." >nul && set "RUNTIME_FOUND=1"
        )
    )
)

if defined RUNTIME_FOUND goto run

echo PortHarbor needs the .NET 8 Desktop Runtime (x64).
echo The download page will open so you can install it.
start "" "https://dotnet.microsoft.com/download/dotnet/8.0"
pause
exit /b 1

:run
"%~dp0PortHarbor.exe" %*
exit /b %ERRORLEVEL%
