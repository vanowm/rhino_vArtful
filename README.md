# vArtful  ·  v26.7.23.016

vArtful is a Rhino 8 plug-in that applies the `Artful.3dm` template to the active document and organizes the document for the Artful layer workflow.

## Features

- Synchronizes document settings, annotation tables, layers, and related template data.
- Moves objects from numeric root layers to the configured Proliner layer.
- Removes the emptied numeric layers and restores the template layer order.
- Sets the configured Traced layer as current.
- Wraps document changes in one Rhino undo record.

## Commands

| Command | Purpose |
| --- | --- |
| `vArtful` | Apply `Artful.3dm` and organize the active document. |
| `vArtfulOptions` | Change or reset the Traced and Proliner layer names. |

## Requirements

- Rhino 8 for Windows
- .NET 7 SDK or newer to build
- `Artful.3dm` in Rhino 8's English template folder under `%APPDATA%`, unless the source template location is customized

## Configuration

Settings are stored in `vArtfulOptions.json` beside the plug-in DLL.

```json
{
  "TracedLayer": "---[ Traced ]---",
  "ProlinerLayer": ". Proliner ."
}
```

Changes made through `vArtfulOptions` are saved immediately.

## Build

From the repository folder:

```powershell
.\build.ps1
```

The default Release build does not require Git and never commits or pushes. Maintainers can use `.\build.ps1 -Publish` to build, create a signed semantic commit when the DLL changes, push `master`, and publish a GitHub release containing the DLL and any generated `.rui` files.

## Installation

The Release plug-in is `bin/Release/net7.0-windows/vArtful.dll`. Load it with Rhino's Plug-in Manager and keep `vArtfulOptions.json` beside the DLL when deploying custom defaults.

## Versioning

Build versions use `yy.m.d.hmm`, derived from the newest C# source file rather than the compile time.

## License

Released under the [MIT License](LICENSE).