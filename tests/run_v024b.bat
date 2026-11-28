@echo off
rem 0.24 part B (#74 labels, #75 TIN editing, #77 alignment extras, #78 plan production reads, #79 AutoCAD extras), live, in a NEW unsaved drawing (DrawingN after MCPALLOW). Output: tests\results\v024b.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0v024b_live.py" > "%~dp0results\v024b.txt" 2>&1
