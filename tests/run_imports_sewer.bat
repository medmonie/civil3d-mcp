@echo off
rem Import retry test (sewer), docs\AUDIT_TEST_PLAN.md B-lines. See tests\imports_live.py. Output: tests\results\imports_sewer.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0imports_live.py" sewer > "%~dp0results\imports_sewer.txt" 2>&1
