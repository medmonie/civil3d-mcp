@echo off
rem Steps through diagnose_save to find a stage that crashes Civil 3D. Output: tests\results\save_probe.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0save_probe.py" %* > "%~dp0results\save_probe_console.txt" 2>&1
