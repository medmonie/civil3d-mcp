@echo off
rem Import retry test (water), docs\AUDIT_TEST_PLAN.md B-lines. See tests\imports_live.py. Output: tests\results\imports_water_b3.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0imports_live.py" water_b3 > "%~dp0results\imports_water_b3.txt" 2>&1
