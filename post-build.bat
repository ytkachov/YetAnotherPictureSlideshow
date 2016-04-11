@ECHO OFF

cd %1
DEL %2.scr
COPY /Y %2.exe %2.scr

pause