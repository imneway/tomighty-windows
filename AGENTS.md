# Agent Notes

- Avoid long inline `powershell.exe -Command` scripts that use reflection, `Add-Type`, dynamic assembly loading, or UI event simulation. Windows Defender may flag that command shape as a severe ClickFix-style threat even when it is only local debugging.
- For UI verification, prefer normal builds, committed scripts, small compiled demo hosts, smoke screenshots, or manual app interaction over ad hoc inline PowerShell reflection.
- If a Defender alert appears during debugging, inspect the affected command line first and explain whether it came from the agent's own tooling before continuing.
- This workspace is often used with two monitors: a large primary display and a smaller secondary display. During iterative UI debugging, open or move preview/demo windows to the smaller display when possible; after the UI is ready, move or verify the final check on the large display for the user's review.
