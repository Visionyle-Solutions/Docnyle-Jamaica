#!/bin/bash
echo "============================================"
echo " OPD Consultation Module - Server Launcher"
echo " (ASP.NET Web Forms / .NET Framework 4.8)"
echo "============================================"
echo ""

# The project is now an ASP.NET Web Forms app. The supported way to run it
# is Visual Studio 2022 / IIS Express on Windows (see START-SERVER.bat).
# On Linux/macOS it can be run for preview with Mono's XSP server
# (package: mono-xsp4), after building with msbuild/xbuild.
cd "$(dirname "$0")"

if ! command -v xsp4 >/dev/null 2>&1; then
  echo "xsp4 (Mono) not found. Run the project from Visual Studio 2022 on Windows,"
  echo "or install mono-complete + mono-xsp4 and build OpdProject.csproj first."
  exit 1
fi

# Stop anything already listening on port 8095
lsof -ti:8095 | xargs kill -9 2>/dev/null
sleep 1

echo "Starting server on port 8095..."
echo ""
echo "  Open your browser to: http://localhost:8095"
echo "  Press Enter to stop the server."
echo ""
echo "============================================"

xsp4 --port 8095 --root .
