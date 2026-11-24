@echo off
rem 0.18 corridor, profile, network and extraction additions, live, in a NEW unsaved drawing (Drawing1 after MCPALLOW). Output: tests\results\extras.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0extras_live.py" > "%~dp0results\extras.txt" 2>&1
