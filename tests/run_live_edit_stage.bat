@echo off
rem Runs chosen stages of the edit tests. Change the stage names on the python line below
rem (stages: see STAGES in live_edit.py). Output: tests\results\live_edit_stage.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0live_edit.py" civil3 > "%~dp0results\live_edit_stage.txt" 2>&1
