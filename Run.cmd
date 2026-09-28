@echo off
pushd "%~dp0"
dotnet build "Elka SEL Mini Control.sln" -c Release --nologo
if errorlevel 1 (
    echo Build failed. Install the .NET 8 SDK and check the error above.
    pause
    popd
    exit /b 1
)
start "" "%~dp0src\Elka.SEL.Mini.Control\bin\Release\net8.0-windows\Elka.SEL.Mini.Control.exe"
popd
