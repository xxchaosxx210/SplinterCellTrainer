@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
if not exist "%~dp0bin" mkdir "%~dp0bin"
cl /nologo /W4 /WX /EHsc /std:c++17 "%~dp0FreezePolicyTests.cpp" /Fo"%~dp0bin\FreezePolicyTests.obj" /Fe"%~dp0bin\FreezePolicyTests.exe"
if errorlevel 1 exit /b 1
"%~dp0bin\FreezePolicyTests.exe"
exit /b %errorlevel%
