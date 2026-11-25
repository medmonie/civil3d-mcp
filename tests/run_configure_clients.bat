@echo off
rem Offline test of package\configure_clients.ps1 (sample files in %TEMP%, never the real configs). Output: tests\results\configure_clients.txt
if not exist "%~dp0results" mkdir "%~dp0results"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0configure_clients_test.ps1" > "%~dp0results\configure_clients.txt" 2>&1
