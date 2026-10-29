@echo off
rem run-all.cmd: the suite in stock\ and turbo\ taking turns, three times each (see BENCHMARKS.md). Run from C:\bench.
cd /d "%~dp0"
for %%i in (1 2 3) do (
  call stock\bench-suite.cmd stock %%i
  call turbo\bench-suite.cmd turbo %%i
)
echo == all done: stock\results\replay and turbo\results\replay
