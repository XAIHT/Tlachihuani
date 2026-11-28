// Tlachihuani — Angela López Mendoza · XAIHT
// Tlachihuani.HandGI.Checks.exe --layout desktop|96dpi
//   Opens HandGI's windows on the main screen and checks their layout (docs/handgi.md). The visible
//   run tests\desktop\ServicesAndHandGI.ps1 starts it and has Shoter photograph every window.
if (args is ["--layout", var mode]) return HandGILayoutChecks.Run(mode);
if (args is ["--control-pipe"]) return await ControlPipeChecks.Run();
Console.WriteLine("Usage: Tlachihuani.HandGI.Checks.exe --layout desktop|96dpi | --control-pipe");
return 2;
