using Microsoft.NET.HostModel.AppHost;

if (args.Length != 4) throw new ArgumentException("Expected template, output EXE, relative DLL path, source assembly.");
HostWriter.CreateAppHost(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), args[2].Replace('\\', '/'),
    windowsGraphicalUserInterface: true, assemblyToCopyResourcesFrom: Path.GetFullPath(args[3]));
