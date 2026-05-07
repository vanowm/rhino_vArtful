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
        var version = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                         ?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "unknown";
        RhinoApp.WriteLine($"vArtful v{version} loaded.");
        return LoadReturnCode.Success;
    }
}
