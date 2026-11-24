@echo off
rem 0.19 corridor feature lines, targets, slope patterns, solids, surface definitions and sources, catchments, live, in a NEW unsaved drawing (Drawing1 after MCPALLOW). Output: tests\results\model.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0model_live.py" > "%~dp0results\model.txt" 2>&1
