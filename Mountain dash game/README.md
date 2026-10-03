Mountain Dash Game
===================

A simple WinForms driving game written in C# targeting .NET 10 (net10.0-windows).

Controls
- Right / D: Gas
- Left / A: Brake / Reverse
- Space / Up / W: Jump (when on ground)
- R / Enter: Start / Restart
- Escape: Quit

Run locally
1. Open the solution in Visual Studio and run (F5), or
2. From PowerShell in the repo root:
   cd "Mountain dash game"
   dotnet run --project "Mountain dash game.csproj"

Publish (produce distributable)
- Framework-dependent (requires .NET installed on target):
  dotnet publish -c Release -r win-x64 --self-contained false -o publish/fd "Mountain dash game/Mountain dash game.csproj"

- Self-contained single EXE (no .NET install required):
  dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish/sc "Mountain dash game/Mountain dash game.csproj"

Quick publish script
- Windows PowerShell: run publish.ps1 at repo root. Example:
  .\publish.ps1 -SelfContained

CI / Automated builds
- A GitHub Actions workflow is included (.github/workflows/publish.yml) to build both self-contained and framework-dependent artifacts on push to main.

Notes
- The project targets net10.0-windows and uses Windows Forms; published builds are Windows-only.
- Test the published output on a clean Windows machine before distributing.

License
Add a LICENSE file if you plan to publish this repository publicly.
