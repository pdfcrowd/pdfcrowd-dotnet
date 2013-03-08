@echo off

rem * path to csc.exe can be either passed on the command line or the one from the latest
rem * framework is used automatically

set NET_DIR=%windir%\Microsoft.NET\Framework\csc.exe
set CSC_EXE=%1
if not exist "%CSC_EXE%" for /f "delims=" %%a in ('dir "%NET_DIR%" /s/a/b') do (set CSC_EXE=%%a)

mkdir bin 2> nul 
echo Using %CSC_EXE%

%CSC_EXE% /nologo /checked /t:library /out:bin/pdfcrowd.dll src\*.cs && echo created bin/pdfcrowd.dll
if exist tests %CSC_EXE% /nologo /checked /t:exe /out:bin/tests.exe tests\*.cs /r:bin\pdfcrowd.dll && echo created bin/tests.exe
