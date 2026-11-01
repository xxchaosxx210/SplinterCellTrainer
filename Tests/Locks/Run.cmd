@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
if not exist "%~dp0bin" mkdir "%~dp0bin"
cl /nologo /W4 /WX /EHsc /std:c++17 "%~dp0LockPolicyTests.cpp" /Fo"%~dp0bin\LockPolicyTests.obj" /Fe"%~dp0bin\LockPolicyTests.exe"
if errorlevel 1 exit /b 1
"%~dp0bin\LockPolicyTests.exe"
exit /b %errorlevel%
