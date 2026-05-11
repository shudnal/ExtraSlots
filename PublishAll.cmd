@echo off
setlocal

pushd "%~dp0"

echo Publishing to Thunderstore...
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0PublishThunderstore.ps1"
set THUNDERSTORE_EXIT=%ERRORLEVEL%

if not "%THUNDERSTORE_EXIT%"=="0" (
    echo.
    echo Thunderstore publish failed. Exit code: %THUNDERSTORE_EXIT%
    set /p CONTINUE_NEXUS=Continue to Nexus anyway [Y,N]?
    if /I not "%CONTINUE_NEXUS%"=="Y" (
        echo.
        echo Publish stopped.
        pause
        exit /b %THUNDERSTORE_EXIT%
    )
)

echo.
echo Publishing to Nexus...
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0PublishNexus.ps1"
set NEXUS_EXIT=%ERRORLEVEL%

if not "%NEXUS_EXIT%"=="0" (
    echo.
    echo Nexus publish failed. Exit code: %NEXUS_EXIT%
    pause
    exit /b %NEXUS_EXIT%
)

echo.
echo Publish finished.
pause
exit /b 0