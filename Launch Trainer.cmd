@echo off
setlocal
set "TRAINER_ROOT=%~dp0"
set "TRAINER_BUILD_LOG=%TRAINER_ROOT%bin\launcher-build.log"
:choose_output
set "TRAINER_OUTPUT=%TRAINER_ROOT%bin\Runs\v0.14.0-%RANDOM%-%RANDOM%"
if exist "%TRAINER_OUTPUT%" goto choose_output
if not exist "%TRAINER_ROOT%bin" mkdir "%TRAINER_ROOT%bin"
echo Building Splinter Assault Trainer...
call "%TRAINER_ROOT%Native\build.cmd" >"%TRAINER_BUILD_LOG%" 2>&1
if errorlevel 1 goto failed
cd /d "%TRAINER_ROOT%"
dotnet build "%TRAINER_ROOT%SCTrainer.csproj" --nologo -o "%TRAINER_OUTPUT%" >>"%TRAINER_BUILD_LOG%" 2>&1
if errorlevel 1 goto failed
if /i "%~1"=="--build-only" goto built
start "" "%TRAINER_OUTPUT%\SCTrainer.exe"
exit /b 0
:built
echo Build ready: %TRAINER_OUTPUT%\SCTrainer.exe
exit /b 0
:failed
type "%TRAINER_BUILD_LOG%"
echo Trainer build failed. No older trainer was launched.
if /i not "%~1"=="--build-only" pause
exit /b 1
