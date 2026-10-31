@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /W4 /EHsc /O2 /MT /DUNICODE /D_UNICODE /DBRIDGE_DLL /LD TravelBridge.cpp /Foout\bridge.obj /Feout\SCTravelBridge.dll /link user32.lib
if errorlevel 1 exit /b 1
cl /nologo /W4 /EHsc /O2 /MT /DUNICODE /D_UNICODE TravelBridge.cpp /Foout\host.obj /Feout\SCTravelHost.exe /link user32.lib
