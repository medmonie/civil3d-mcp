@echo off
rem Imports "..\water network\Output from water model.xlsx" into a copy of the open water drawing and checks the result.
rem Output: tests\results\watergems.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" -m pip install -q -r "%~dp0..\server\requirements.txt" > "%~dp0results\watergems.txt" 2>&1
"%~dp0..\server\.venv\Scripts\python" "%~dp0watergems_live.py" >> "%~dp0results\watergems.txt" 2>&1
