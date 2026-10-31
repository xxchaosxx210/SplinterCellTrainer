@echo off
cd /d "%~dp0"
dotnet run --project "%~dp0SCTrainer.csproj"
if errorlevel 1 pause
