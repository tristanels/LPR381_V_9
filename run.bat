@echo off
REM Double-click this to build and run solve.exe, without opening Visual Studio.
cd /d "%~dp0"

echo Building...
dotnet build "LPR381\LPR381.slnx" --nologo --verbosity quiet
if errorlevel 1 (
    echo.
    echo The build failed. See the messages above.
    pause
    exit /b 1
)

echo.
"LPR381\LPR381\bin\Debug\net10.0\solve.exe"

REM If the program closed straight away, the message above says why.
if errorlevel 1 (
    echo.
    echo solve.exe stopped with an error.
    echo If it mentions "Application Control policy", just run this file again -
    echo each rebuild makes a new file and clears the block.
    pause
)
