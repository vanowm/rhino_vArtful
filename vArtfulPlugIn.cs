using Rhino;
using Rhino.PlugIns;
using System.Reflection;

namespace VArtful;

[System.Runtime.InteropServices.Guid("e65c3282-3c23-408e-901e-86d82dd41070")]
public sealed class VArtfulPlugIn : PlugIn
{
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;
    protected override string LocalPlugInName => "vArtful";

    public VArtfulPlugIn()
    {
        Instance = this;
    }

    public static VArtfulPlugIn Instance { get; private set; } = null!;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        var asm     = GetType().Assembly;
        var version = (!string.IsNullOrEmpty(asm.Location)
                         ? System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location).FileVersion
                         : null)
                      ?? asm.GetName().Version?.ToString()
                      ?? "unknown";
        Log.Initialize();
        Log.Write($"startup  rhino={RhinoApp.Version}  version={version}  dll={asm.Location}");
        RhinoApp.WriteLine($"vArtful v{version} loaded.");
        return LoadReturnCode.Success;
    }
}
