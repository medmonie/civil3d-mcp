@echo off
rem 0.22 (bearings, H5 vertical curves, inspect_file budget, block / other-DWG reads, style_copy, band-set import), live, in a NEW unsaved metric drawing (DrawingN after MCPALLOW). Output: tests\results\v022.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0v022_live.py" > "%~dp0results\v022.txt" 2>&1
