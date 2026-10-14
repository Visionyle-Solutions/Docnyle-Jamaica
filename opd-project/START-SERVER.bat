@echo off
echo ============================================
echo  OPD Consultation Module - Server Launcher
echo  (ASP.NET Web Forms / .NET Framework 4.8)
echo ============================================
echo.

REM The site now runs on IIS Express (installed with Visual Studio 2022),
REM not Python. Build the project once first (Visual Studio: Build > Build
REM Solution) so that bin\OpdProject.dll exists.

if not exist "%~dp0bin\OpdProject.dll" (
    echo  bin\OpdProject.dll not found. Build OpdProject.sln in Visual Studio first.
    echo.
    pause
    exit /b 1
)

set "IISEXPRESS=%ProgramFiles%\IIS Express\iisexpress.exe"
if not exist "%IISEXPRESS%" set "IISEXPRESS=%ProgramFiles(x86)%\IIS Express\iisexpress.exe"
if not exist "%IISEXPRESS%" (
    echo  IIS Express was not found. Install it with Visual Studio 2022
    echo  ^(ASP.NET and web development workload^), or run from Visual Studio with F5.
    echo.
    pause
    exit /b 1
)

REM If the site is already running (e.g. started from Visual Studio with F5),
REM a second IIS Express cannot bind port 8095 - just open the browser instead.
netstat -ano | findstr /R /C:":8095 .*LISTENING" >nul
if not errorlevel 1 (
    echo  Port 8095 is already in use - the site is probably already running
    echo  ^(Visual Studio F5 or another START-SERVER window^). Opening the browser.
    echo  If the page does not load, close the other IIS Express ^(tray icon ^> Exit^)
    echo  and run this script again.
    start "" http://localhost:8095
    echo.
    pause
    exit /b 0
)

echo Starting IIS Express on port 8095...
echo.
echo  Open your browser to: http://localhost:8095
echo  Press Q in this window to stop the server when done.
echo.
echo ============================================

"%IISEXPRESS%" /path:"%~dp0." /port:8095 /clr:v4.0

pause
