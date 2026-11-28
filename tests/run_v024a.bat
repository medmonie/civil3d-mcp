@echo off
rem 0.24 part A (#69 network edits, #70 data, #71 property_set, #72 source tags, #73 pressure runs), live, in a NEW unsaved drawing (DrawingN after MCPALLOW). Output: tests\results\v024a.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0v024a_live.py" > "%~dp0results\v024a.txt" 2>&1
