// Modified: 2026-06-18 12:07:19 EDT
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SysEnv = System.Environment;

namespace VArtful.Commands;

/// <summary>
/// Artful layer-cleanup helper. Ported from Artful.py.
///
/// Applies the full Artful.3dm template to the current document (same logic as
/// vFileTypeTemplate: document settings, notes, location, document strings, layers,
/// linetypes, hatch patterns, dim styles, materials, named views, named cplanes,
/// runtime settings via headless doc), then performs the Artful-specific cleanup:
///   1) Set current layer to the configured traced layer (default "---[ Traced ]---").
///   2) Move all objects from root-level numeric layers ("0", "1", "2", …) into the configured proliner layer (default ". Proliner .").
///   3) Delete those numeric layers.
///   4) Sort remaining layers to match the template order.
///
/// Everything runs inside a single undo record; doc properties are also undoable
/// via a custom undo event.
/// </summary>
internal static class VArtfulSettings
{
    public const string DefaultTracedLayer   = "---[ Traced ]---";
    public const string DefaultProlinerLayer = ". Proliner .";

    private static readonly string SettingsPath = GetSettingsPath();

    private static bool _loaded;
    private static string _tracedLayer   = DefaultTracedLayer;
    private static string _prolinerLayer = DefaultProlinerLayer;

    public static string TracedLayer
    {
        get { Load(); return NormalizeLayerName(_tracedLayer, DefaultTracedLayer); }
    }

    public static string ProlinerLayer
    {
        get { Load(); return NormalizeLayerName(_prolinerLayer, DefaultProlinerLayer); }
    }

    public static string SettingsFilePath => SettingsPath;

    public static void SetLayerNames(string tracedLayer, string prolinerLayer)
    {
        Load();
        _tracedLayer   = NormalizeLayerName(tracedLayer,   DefaultTracedLayer);
        _prolinerLayer = NormalizeLayerName(prolinerLayer, DefaultProlinerLayer);
        Save();
    }

    public static void Reset()
    {
        _tracedLayer   = DefaultTracedLayer;
        _prolinerLayer = DefaultProlinerLayer;
        _loaded        = true;
        Save();
    }

    private static string GetSettingsPath()
    {
        try
        {
            var dllPath = typeof(VArtfulSettings).Assembly.Location;
            var dllDir  = Path.GetDirectoryName(dllPath);
            if (!string.IsNullOrEmpty(dllDir))
                return Path.Combine(dllDir, "vArtfulOptions.json");
        }
        catch { }

        return Path.Combine(SysEnv.CurrentDirectory, "vArtfulOptions.json");
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (!File.Exists(SettingsPath)) return;

            var json = File.ReadAllText(SettingsPath);
            var traced = ReadJsonString(json, "TracedLayer");
            var proliner = ReadJsonString(json, "ProlinerLayer");

            if (traced != null)
                _tracedLayer = NormalizeLayerName(traced, DefaultTracedLayer);
            if (proliner != null)
                _prolinerLayer = NormalizeLayerName(proliner, DefaultProlinerLayer);
        }
        catch
        {
            _tracedLayer   = DefaultTracedLayer;
            _prolinerLayer = DefaultProlinerLayer;
        }
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine($"  \"TracedLayer\": \"{EscapeJson(_tracedLayer)}\",");
            json.AppendLine($"  \"ProlinerLayer\": \"{EscapeJson(_prolinerLayer)}\"");
            json.AppendLine("}");

            File.WriteAllText(SettingsPath, json.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"vArtfulOptions: Could not save settings: {ex.Message}");
        }
    }

    private static string NormalizeLayerName(string? name, string fallback)
    {
        var value = (name ?? string.Empty).Trim();
        return value.Length == 0 ? fallback : value;
    }

    private static string? ReadJsonString(string json, string key)
    {
        int keyPos = FindJsonProperty(json, key);
        if (keyPos < 0) return null;

        int colon = json.IndexOf(':', keyPos);
        if (colon < 0) return null;

        int start = colon + 1;
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
        if (start >= json.Length || json[start] != '\"') return null;

        start++;
        var sb = new StringBuilder();
        for (int i = start; i < json.Length; i++)
        {
            char ch = json[i];
            if (ch == '\"')
                return sb.ToString();

            if (ch != '\\')
            {
                sb.Append(ch);
                continue;
            }

            if (++i >= json.Length) break;
            char esc = json[i];
            switch (esc)
            {
                case '\"': sb.Append('\"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 < json.Length)
                    {
                        var hex = json.Substring(i + 1, 4);
                        if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                    }
                    break;
                default:
                    sb.Append(esc);
                    break;
            }
        }

        return null;
    }

    private static int FindJsonProperty(string json, string key)
    {
        string quotedKey = "\"" + key + "\"";
        return json.IndexOf(quotedKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeJson(string value)
    {
        var sb = new StringBuilder();
        foreach (char ch in value ?? string.Empty)
        {
            switch (ch)
            {
                case '\"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32)
                        sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else
                        sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }
}

public sealed class vArtfulOptions : Command
{
    public override string EnglishName => "vArtfulOptions";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        string tracedLayer   = VArtfulSettings.TracedLayer;
        string prolinerLayer = VArtfulSettings.ProlinerLayer;

        while (true)
        {
            var go = new GetOption();
            go.SetCommandPrompt("vArtful layer options. Press Enter to save and exit");
            go.AcceptNothing(true);

            int tracedOptionIndex   = go.AddOption("TracedLayer", tracedLayer);
            int prolinerOptionIndex = go.AddOption("ProlinerLayer", prolinerLayer);
            int resetOptionIndex    = go.AddOption("Reset");

            var result = go.Get();

            if (result == GetResult.Cancel)
                return Result.Cancel;

            if (result == GetResult.Nothing)
            {
                VArtfulSettings.SetLayerNames(tracedLayer, prolinerLayer);
                RhinoApp.WriteLine(
                    $"vArtfulOptions saved: TracedLayer='{VArtfulSettings.TracedLayer}', " +
                    $"ProlinerLayer='{VArtfulSettings.ProlinerLayer}'.");
                RhinoApp.WriteLine($"vArtfulOptions file: {VArtfulSettings.SettingsFilePath}");
                return Result.Success;
            }

            if (result != GetResult.Option)
                return go.CommandResult();

            var option = go.Option();
            if (option == null)
                continue;


            if (option.Index == tracedOptionIndex)
            {
                var promptResult = PromptLayerName("Traced layer name", tracedLayer,
                    VArtfulSettings.DefaultTracedLayer, out var value);
                if (promptResult != Result.Success)
                    return promptResult;

                tracedLayer = value;
                VArtfulSettings.SetLayerNames(tracedLayer, prolinerLayer);
                continue;
            }


            if (option.Index == prolinerOptionIndex)
            {
                var promptResult = PromptLayerName("Proliner layer name", prolinerLayer,
                    VArtfulSettings.DefaultProlinerLayer, out var value);
                if (promptResult != Result.Success)
                    return promptResult;

                prolinerLayer = value;
                VArtfulSettings.SetLayerNames(tracedLayer, prolinerLayer);
                continue;
            }


            if (option.Index == resetOptionIndex)
            {
                VArtfulSettings.Reset();
                tracedLayer   = VArtfulSettings.TracedLayer;
                prolinerLayer = VArtfulSettings.ProlinerLayer;
                RhinoApp.WriteLine(
                    $"vArtfulOptions reset: TracedLayer='{tracedLayer}', " +
                    $"ProlinerLayer='{prolinerLayer}'.");
                continue;
            }
        }
    }

    private static Result PromptLayerName(string prompt, string currentValue, string fallback, out string value)
    {
        value = currentValue;

        var gs = new GetString();
        gs.SetCommandPrompt(prompt);
        gs.SetDefaultString(currentValue);
        gs.AcceptNothing(true);

        var result = gs.Get();
        if (result == GetResult.Cancel)
            return Result.Cancel;

        if (result == GetResult.Nothing)
            return Result.Success;

        if (result == GetResult.String)
        {
            value = CleanCommandValue(gs.StringResult(), fallback);
            return Result.Success;
        }

        return gs.CommandResult();
    }

    private static string CleanCommandValue(string? value, string fallback)
    {
        value = (value ?? string.Empty).Trim();
        return value.Length == 0 ? fallback : value;
    }
}

public sealed class vArtful : Command
{
    private static readonly string TemplatePath = Path.Combine(
        SysEnv.GetFolderPath(SysEnv.SpecialFolder.ApplicationData),
        @"McNeel\Rhinoceros\8.0\Localization\en-US\Template Files\Artful.3dm");

    public override string EnglishName => "vArtful";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (!File.Exists(TemplatePath))
        {
            RhinoApp.WriteLine($"vArtful: Template file not found:\n  {TemplatePath}");
            return Result.Failure;
        }

        var beforeSnapshot = TakeDocSnapshot(doc);

        uint undoSn = doc.BeginUndoRecord("vArtful");
        File3dm? templateFile = null;
        RhinoDoc? headlessDoc = null;
        try
        {
            // ── Phase 1: Apply static settings from File3dm ───────────────────
            templateFile = File3dm.Read(TemplatePath);
            if (templateFile == null)
            {
                RhinoApp.WriteLine($"vArtful: Could not read template file:\n  {TemplatePath}");
                return Result.Failure;
            }

            ApplyDocumentSettings(doc, templateFile);
            ApplyNotes(doc, templateFile);
            ApplyLocation(doc, templateFile);
            ApplyDocumentStrings(doc, templateFile);
            ApplyLayers(doc, templateFile);
            ApplyLinetypes(doc, templateFile);
            ApplyHatchPatterns(doc, templateFile);
            ApplyDimStyles(doc, templateFile);
            ApplyMaterials(doc, templateFile);
            ApplyNamedViews(doc, templateFile);
            ApplyNamedCPlanes(doc, templateFile);

            // ── Phase 2: Runtime settings via headless doc ────────────────────
            headlessDoc = RhinoDoc.CreateHeadless(TemplatePath);
            if (headlessDoc != null)
                ApplyRuntimeSettings(doc, headlessDoc);

            // ── Phase 3: Artful-specific layer cleanup ────────────────────────
            string tracedLayer   = VArtfulSettings.TracedLayer;
            string prolinerLayer = VArtfulSettings.ProlinerLayer;

            int prolinerIdx = EnsureLayer(doc, prolinerLayer);
            int tracedIdx   = EnsureLayer(doc, tracedLayer);

            doc.Layers.SetCurrentLayerIndex(tracedIdx, true);

            var numericLayers = CollectNumericLayers(doc);
            RhinoApp.WriteLine($"vArtful: Found {numericLayers.Count} root numeric layer(s).");

            int totalMoved = 0;
            foreach (var layer in numericLayers)
            {
                if (layer.IsLocked || !layer.IsVisible)
                {
                    layer.IsLocked  = false;
                    layer.IsVisible = true;
                    doc.Layers.Modify(layer, layer.Index, true);
                }
                int moved = MoveObjectsToLayer(doc, layer.Index, prolinerIdx);
                totalMoved += moved;
                RhinoApp.WriteLine($"  Moved {moved} obj(s) from '{layer.Name}' → '{prolinerLayer}'.");
            }

            int deletedCount = 0;
            foreach (var layer in numericLayers.OrderByDescending(l => l.FullPath?.Count(c => c == ':') ?? 0))
            {
                PrepareLayerForDelete(doc, layer.Index, tracedIdx);
                if (doc.Layers.Delete(layer.Index, true))
                    deletedCount++;
            }

            int sortedCount = SortLayersLikeTemplate(doc);

            if (undoSn > 0)
            {
                var afterSnapshot = TakeDocSnapshot(doc);
                doc.AddCustomUndoEvent("vArtful", OnDocSettingsUndoRedo,
                    new[] { beforeSnapshot, afterSnapshot });
            }

            doc.Views.Redraw();
            RhinoApp.WriteLine(
                $"vArtful complete: current layer '{tracedLayer}', " +
                $"moved {totalMoved} obj(s) to '{prolinerLayer}', " +
                $"deleted {deletedCount} numeric layer(s), " +
                $"sorted {sortedCount} layer(s).");

            return Result.Success;
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"vArtful error: {ex.Message}");
            return Result.Failure;
        }
        finally
        {
            templateFile?.Dispose();
            headlessDoc?.Dispose();
            if (undoSn > 0)
                doc.EndUndoRecord(undoSn);
        }
    }

    // ── Phase 1: Document settings ────────────────────────────────────────────

    private static void ApplyDocumentSettings(RhinoDoc doc, File3dm f)
    {
        try
        {
            var s = f.Settings;
            doc.ModelUnitSystem            = s.ModelUnitSystem;
            doc.ModelAbsoluteTolerance     = s.ModelAbsoluteTolerance;
            doc.ModelRelativeTolerance     = s.ModelRelativeTolerance;
            doc.ModelAngleToleranceDegrees = s.ModelAngleToleranceDegrees;
            doc.PageUnitSystem             = s.PageUnitSystem;
            doc.PageAbsoluteTolerance      = s.PageAbsoluteTolerance;
            doc.PageRelativeTolerance      = s.PageRelativeTolerance;
            doc.PageAngleToleranceDegrees  = s.PageAngleToleranceDegrees;
        }
        catch { }
    }

    private static void ApplyNotes(RhinoDoc doc, File3dm f)
    {
        try
        {
            var notes = f.Notes?.Notes;
            if (!string.IsNullOrEmpty(notes) && string.IsNullOrEmpty(doc.Notes))
                doc.Notes = notes;
        }
        catch { }
    }

    private static void ApplyLocation(RhinoDoc doc, File3dm f)
    {
        try
        {
            var eap = f.EarthAnchorPoint;
            if (eap != null && eap.EarthLocationIsSet())
                doc.EarthAnchorPoint = eap;
        }
        catch { }
        try { doc.ModelBasepoint = f.Settings.ModelBasepoint; } catch { }
    }

    private static void ApplyDocumentStrings(RhinoDoc doc, File3dm f)
    {
        try
        {
            var strings = f.Strings;
            if (strings == null || strings.Count == 0) return;
            for (int i = 0; i < strings.Count; i++)
            {
                var key   = strings.GetKey(i);
                var value = strings.GetValue(i);
                if (string.IsNullOrEmpty(key)) continue;
                if (doc.Strings.GetValue(key) != null) continue;
                doc.Strings.SetString(key, value);
            }
        }
        catch { }
    }

    private static void ApplyLayers(RhinoDoc doc, File3dm f)
    {
        try
        {
            foreach (var tLayer in f.AllLayers)
            {
                if (tLayer == null || tLayer.IsDeleted) continue;
                var fp = tLayer.FullPath;
                if (string.IsNullOrEmpty(fp)) continue;

                int idx = doc.Layers.FindByFullPath(fp, -1);
                if (idx >= 0)
                {
                    // Update color on existing layers (Artful workflow needs correct colors).
                    try
                    {
                        var lyr = doc.Layers[idx];
                        lyr.Color = tLayer.Color;
                        doc.Layers.Modify(lyr, idx, true);
                    }
                    catch { }
                    continue;
                }

                try
                {
                    var newLayer = new Layer
                    {
                        Name          = tLayer.Name,
                        Color         = tLayer.Color,
                        PlotColor     = tLayer.PlotColor,
                        PlotWeight    = tLayer.PlotWeight,
                        IsVisible     = tLayer.IsVisible,
                        IsLocked      = tLayer.IsLocked,
                        LinetypeIndex = -1,
                    };
                    if (tLayer.ParentLayerId != Guid.Empty)
                    {
                        var parent = doc.Layers.FindId(tLayer.ParentLayerId);
                        if (parent != null) newLayer.ParentLayerId = parent.Id;
                    }
                    doc.Layers.Add(newLayer);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ApplyLinetypes(RhinoDoc doc, File3dm f)
    {
        try
        {
            foreach (var lt in f.AllLinetypes)
            {
                if (lt == null || lt.IsDeleted || string.IsNullOrEmpty(lt.Name)) continue;
                if (doc.Linetypes.Find(lt.Name) >= 0) continue;
                try { doc.Linetypes.Add(lt); } catch { }
            }
        }
        catch { }
    }

    private static void ApplyHatchPatterns(RhinoDoc doc, File3dm f)
    {
        try
        {
            foreach (var hp in f.AllHatchPatterns)
            {
                if (hp == null || hp.IsDeleted || string.IsNullOrEmpty(hp.Name)) continue;
                if (doc.HatchPatterns.FindName(hp.Name) != null) continue;
                try { doc.HatchPatterns.Add(hp); } catch { }
            }
        }
        catch { }
    }

    private static void ApplyDimStyles(RhinoDoc doc, File3dm f)
    {
        try
        {
            foreach (var ds in f.AllDimStyles)
            {
                if (ds == null || ds.IsDeleted || string.IsNullOrEmpty(ds.Name)) continue;
                var existing = doc.DimStyles.FindName(ds.Name);
                if (existing != null)
                    try { doc.DimStyles.Modify(ds, existing.Index, true); } catch { }
                else
                    try { doc.DimStyles.Add(ds, false); } catch { }
            }
        }
        catch { }
    }

    private static void ApplyMaterials(RhinoDoc doc, File3dm f)
    {
        try
        {
            foreach (var mat in f.AllMaterials)
            {
                if (mat == null || mat.IsDeleted || string.IsNullOrEmpty(mat.Name)) continue;
                if (doc.Materials.Find(mat.Name, true) >= 0) continue;
                try { doc.Materials.Add(mat); } catch { }
            }
        }
        catch { }
    }

    private static void ApplyNamedViews(RhinoDoc doc, File3dm f)
    {
        try
        {
            var views = f.AllNamedViews;
            if (views == null) return;
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (v == null || string.IsNullOrEmpty(v.Name)) continue;
                if (doc.NamedViews.FindByName(v.Name) >= 0) continue;
                try { doc.NamedViews.Add(v); } catch { }
            }
        }
        catch { }
    }

    private static void ApplyNamedCPlanes(RhinoDoc doc, File3dm f)
    {
        try
        {
            var cplanes = f.AllNamedConstructionPlanes;
            if (cplanes == null) return;
            for (int i = 0; i < cplanes.Count; i++)
            {
                var cp = cplanes[i];
                if (cp == null || string.IsNullOrEmpty(cp.Name)) continue;
                bool exists = false;
                for (int j = 0; j < doc.NamedConstructionPlanes.Count; j++)
                    if (string.Equals(doc.NamedConstructionPlanes[j]?.Name, cp.Name, StringComparison.OrdinalIgnoreCase))
                    { exists = true; break; }
                if (!exists)
                    try { doc.NamedConstructionPlanes.Add(cp); } catch { }
            }
        }
        catch { }
    }

    // ── Phase 2: Runtime settings (headless doc) ──────────────────────────────

    private static void ApplyRuntimeSettings(RhinoDoc doc, RhinoDoc tpl)
    {
        try { doc.ModelDistanceDisplayPrecision = tpl.ModelDistanceDisplayPrecision; } catch { }
        try { doc.PageDistanceDisplayPrecision  = tpl.PageDistanceDisplayPrecision;  } catch { }

        try
        {
            SetDistanceDisplayMode(doc, GetDistanceDisplayMode(tpl, false), false);
            SetDistanceDisplayMode(doc, GetDistanceDisplayMode(tpl, true),  true);
        }
        catch { }

        try
        {
            var style = tpl.MeshingParameterStyle;
            doc.MeshingParameterStyle = style;
            if (style == MeshingParameterStyle.Custom)
                doc.SetCustomMeshingParameters(tpl.GetCurrentMeshingParameters());
        }
        catch { }

        try { doc.ModelSpaceAnnotationScalingEnabled  = tpl.ModelSpaceAnnotationScalingEnabled;  } catch { }
        try { doc.ModelSpaceTextScale                 = tpl.ModelSpaceTextScale;                 } catch { }
        try { doc.ModelSpaceHatchScalingEnabled       = tpl.ModelSpaceHatchScalingEnabled;       } catch { }
        try { doc.ModelSpaceHatchScale                = tpl.ModelSpaceHatchScale;                } catch { }
        try { doc.LayoutSpaceAnnotationScalingEnabled = tpl.LayoutSpaceAnnotationScalingEnabled; } catch { }
        try { doc.SubDAppearance                      = tpl.SubDAppearance;                      } catch { }
        try { doc.RenderSettings                      = tpl.RenderSettings;                      } catch { }

        try
        {
#pragma warning disable CS0612
            var srcGp = tpl.GroundPlane;
            var dstGp = doc.GroundPlane;
#pragma warning restore CS0612
            dstGp.BeginChange(Rhino.Render.RenderContent.ChangeContexts.Program);
            dstGp.Enabled      = srcGp.Enabled;
            dstGp.Altitude     = srcGp.Altitude;
            dstGp.AutoAltitude = srcGp.AutoAltitude;
            dstGp.ShadowOnly   = srcGp.ShadowOnly;
            dstGp.EndChange();
        }
        catch { }

        try
        {
            var tplViews = tpl.Views.GetStandardRhinoViews();
            ConstructionPlane? srcGrid = null;
            foreach (var tv in tplViews)
            {
                var cp = tv.ActiveViewport.GetConstructionPlane();
                if (srcGrid == null) srcGrid = cp;
                if (tv.ActiveViewport.Name?.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0)
                { srcGrid = cp; break; }
            }

            if (srcGrid != null)
            {
                foreach (var view in doc.Views)
                {
                    try
                    {
                        var vp     = view.ActiveViewport;
                        var cplane = vp.GetConstructionPlane();
                        cplane.GridSpacing        = srcGrid.GridSpacing;
                        cplane.SnapSpacing        = srcGrid.SnapSpacing;
                        cplane.GridLineCount      = srcGrid.GridLineCount;
                        cplane.ThickLineFrequency = srcGrid.ThickLineFrequency;
                        cplane.ShowGrid           = srcGrid.ShowGrid;
                        cplane.ShowAxes           = srcGrid.ShowAxes;
                        vp.SetConstructionPlane(cplane);
                    }
                    catch { }
                }
            }
            else
            {
                try { doc.SetGridDefaults(tpl.GetGridDefaults()); } catch { }
            }
        }
        catch { }
    }

    // ── Phase 3: Artful-specific layer cleanup ────────────────────────────────

    private static int EnsureLayer(RhinoDoc doc, string name)
    {
        int idx = doc.Layers.FindByFullPath(name, -1);
        if (idx >= 0) return idx;
        return doc.Layers.Add(new Layer { Name = name });
    }

    private static List<Layer> CollectNumericLayers(RhinoDoc doc)
    {
        var result = new List<Layer>();
        foreach (var layer in doc.Layers)
        {
            if (layer == null || layer.IsDeleted) continue;
            if (layer.ParentLayerId != Guid.Empty) continue;
            if (layer.Name.Length > 0 && layer.Name.All(char.IsDigit))
                result.Add(layer);
        }
        return result;
    }

    private static int MoveObjectsToLayer(RhinoDoc doc, int srcIdx, int dstIdx)
    {
        int moved = 0;
        var srcLayer = doc.Layers[srcIdx];
        if (srcLayer == null) return 0;
        var objs = doc.Objects.FindByLayer(srcLayer);
        if (objs == null) return 0;
        foreach (var obj in objs)
        {
            if (obj == null) continue;
            var attr = obj.Attributes.Duplicate();
            attr.LayerIndex = dstIdx;
            if (doc.Objects.ModifyAttributes(obj, attr, true))
                moved++;
        }
        return moved;
    }

    private static void PrepareLayerForDelete(RhinoDoc doc, int layerIdx, int safeIdx)
    {
        var layer = doc.Layers[layerIdx];
        if (layer == null || layer.IsDeleted) return;
        if (doc.Layers.CurrentLayerIndex == layerIdx)
            doc.Layers.SetCurrentLayerIndex(safeIdx, true);
        if (layer.IsLocked || !layer.IsVisible)
        {
            layer.IsLocked  = false;
            layer.IsVisible = true;
            doc.Layers.Modify(layer, layerIdx, true);
        }
    }

    private static int SortLayersLikeTemplate(RhinoDoc doc)
    {
        var templateOrder = ReadTemplateLayerOrder();
        if (templateOrder.Count == 0) return 0;

        var current = doc.Layers
            .Where(l => l != null && !l.IsDeleted)
            .OrderBy(l => l.SortIndex)
            .ToList();

        var matched      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrder = new List<Layer>();

        foreach (var tplPath in templateOrder)
        {
            Layer? match = current.FirstOrDefault(l =>
                string.Equals(l.FullPath, tplPath, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                var tplLeaf = CanonicalLeaf(tplPath.Split(new[] { "::" }, StringSplitOptions.None)[^1]);
                match = current.FirstOrDefault(l =>
                    !string.IsNullOrEmpty(l.FullPath) &&
                    string.Equals(
                        CanonicalLeaf(l.FullPath!.Split(new[] { "::" }, StringSplitOptions.None)[^1]),
                        tplLeaf, StringComparison.OrdinalIgnoreCase));
            }

            if (match != null && !string.IsNullOrEmpty(match.FullPath) && matched.Add(match.FullPath!))
                matchedOrder.Add(match);
        }

        var finalIndices = current
            .Where(l => string.IsNullOrEmpty(l.FullPath) || !matched.Contains(l.FullPath!))
            .Concat(matchedOrder)
            .Select(l => l.Index)
            .ToList();

        var originalIndices = current.Select(l => l.Index).ToList();
        if (!finalIndices.SequenceEqual(originalIndices))
        {
            try { doc.Layers.Sort(finalIndices); } catch { }
            doc.Views.Redraw();
        }

        return finalIndices.Count;
    }

    private static List<string> ReadTemplateLayerOrder()
    {
        try
        {
            var f = File3dm.Read(TemplatePath);
            if (f == null) return new List<string>();
            var layers = f.AllLayers.Where(l => l != null && !l.IsDeleted && !string.IsNullOrEmpty(l.FullPath)).ToList();
            bool hasSort = layers.Any(l => l.SortIndex >= 0);
            if (hasSort) layers = layers.OrderBy(l => l.SortIndex).ThenBy(l => l.FullPath).ToList();
            return layers.Select(l => l.FullPath!).ToList();
        }
        catch { return new List<string>(); }
    }

    private static string CanonicalLeaf(string leaf)
    {
        var l = leaf.Trim().ToLowerInvariant();
        return l == "referrence" ? "reference" : l;
    }

    // ── Undo/redo support for doc properties ─────────────────────────────────

    private sealed class DocSettingsSnapshot
    {
        public UnitSystem ModelUnitSystem;
        public double ModelAbsoluteTolerance;
        public double ModelRelativeTolerance;
        public double ModelAngleToleranceDegrees;
        public UnitSystem PageUnitSystem;
        public double PageAbsoluteTolerance;
        public double PageRelativeTolerance;
        public double PageAngleToleranceDegrees;
        public int ModelDistanceDisplayPrecision;
        public int PageDistanceDisplayPrecision;
        public int ModelDistanceDisplayMode;
        public int PageDistanceDisplayMode;
        public MeshingParameterStyle MeshingParameterStyle;
        public MeshingParameters? CustomMeshingParameters;
        public bool ModelSpaceAnnotationScalingEnabled;
        public double ModelSpaceTextScale;
        public bool ModelSpaceHatchScalingEnabled;
        public double ModelSpaceHatchScale;
        public bool LayoutSpaceAnnotationScalingEnabled;
        public SubDComponentLocation SubDAppearance;
        public Rhino.Render.RenderSettings? RenderSettings;
        public bool GroundPlaneEnabled;
        public double GroundPlaneAltitude;
        public bool GroundPlaneAutoAltitude;
        public bool GroundPlaneShadowOnly;
        public string Notes = string.Empty;
        public bool EarthLocationIsSet;
        public double EarthLat;
        public double EarthLon;
        public double EarthElevation;
        public Point3d ModelBasepoint;
        public List<(Guid ViewportId, ConstructionPlane CPlane)> ViewportGrids = new();

        // Table state — fully synced on undo/redo so all Apply* changes are reversible.
        public List<(string Key, string? Value)>          DocStrings    = new();
        public List<Linetype>                             Linetypes     = new();
        public List<HatchPattern>                         HatchPatterns = new();
        public List<DimensionStyle>                       DimStyles     = new();
        public List<Material>                             Materials     = new();
        public List<Rhino.DocObjects.ViewInfo>            NamedViews    = new();
        public List<(string Name, ConstructionPlane Cp)>  NamedCPlanes  = new();
    }

    private static DocSettingsSnapshot TakeDocSnapshot(RhinoDoc doc)
    {
        var s = new DocSettingsSnapshot
        {
            ModelUnitSystem            = doc.ModelUnitSystem,
            ModelAbsoluteTolerance     = doc.ModelAbsoluteTolerance,
            ModelRelativeTolerance     = doc.ModelRelativeTolerance,
            ModelAngleToleranceDegrees = doc.ModelAngleToleranceDegrees,
            PageUnitSystem             = doc.PageUnitSystem,
            PageAbsoluteTolerance      = doc.PageAbsoluteTolerance,
            PageRelativeTolerance      = doc.PageRelativeTolerance,
            PageAngleToleranceDegrees  = doc.PageAngleToleranceDegrees,
            Notes                      = doc.Notes ?? string.Empty,
            ModelBasepoint             = doc.ModelBasepoint,
        };
        try { s.ModelDistanceDisplayPrecision = doc.ModelDistanceDisplayPrecision;    } catch { }
        try { s.PageDistanceDisplayPrecision  = doc.PageDistanceDisplayPrecision;     } catch { }
        try { s.ModelDistanceDisplayMode      = GetDistanceDisplayMode(doc, false);   } catch { }
        try { s.PageDistanceDisplayMode       = GetDistanceDisplayMode(doc, true);    } catch { }
        try
        {
            s.MeshingParameterStyle = doc.MeshingParameterStyle;
            if (s.MeshingParameterStyle == MeshingParameterStyle.Custom)
                s.CustomMeshingParameters = doc.GetCurrentMeshingParameters();
        }
        catch { }
        try { s.ModelSpaceAnnotationScalingEnabled  = doc.ModelSpaceAnnotationScalingEnabled;  } catch { }
        try { s.ModelSpaceTextScale                 = doc.ModelSpaceTextScale;                 } catch { }
        try { s.ModelSpaceHatchScalingEnabled       = doc.ModelSpaceHatchScalingEnabled;       } catch { }
        try { s.ModelSpaceHatchScale                = doc.ModelSpaceHatchScale;                } catch { }
        try { s.LayoutSpaceAnnotationScalingEnabled = doc.LayoutSpaceAnnotationScalingEnabled; } catch { }
        try { s.SubDAppearance                      = doc.SubDAppearance;                      } catch { }
        try { s.RenderSettings                      = doc.RenderSettings;                      } catch { }
        try
        {
#pragma warning disable CS0612
            var gp = doc.GroundPlane;
#pragma warning restore CS0612
            s.GroundPlaneEnabled      = gp.Enabled;
            s.GroundPlaneAltitude     = gp.Altitude;
            s.GroundPlaneAutoAltitude = gp.AutoAltitude;
            s.GroundPlaneShadowOnly   = gp.ShadowOnly;
        }
        catch { }
        try
        {
            var eap = doc.EarthAnchorPoint;
            s.EarthLocationIsSet = eap?.EarthLocationIsSet() ?? false;
            if (s.EarthLocationIsSet && eap != null)
            {
                s.EarthLat       = eap.EarthBasepointLatitude;
                s.EarthLon       = eap.EarthBasepointLongitude;
                s.EarthElevation = eap.EarthBasepointElevation;
            }
        }
        catch { }
        try
        {
            foreach (var view in doc.Views)
                try { s.ViewportGrids.Add((view.ActiveViewport.Id, view.ActiveViewport.GetConstructionPlane())); }
                catch { }
        }
        catch { }

        // Table state
        try
        {
            for (int i = 0; i < doc.Strings.Count; i++)
            {
                var key = doc.Strings.GetKey(i);
                if (!string.IsNullOrEmpty(key))
                    s.DocStrings.Add((key, doc.Strings.GetValue(i)));
            }
        }
        catch { }
        try
        {
            for (int i = 0; i < doc.Linetypes.Count; i++)
            {
                var lt = doc.Linetypes[i];
                if (lt != null && !lt.IsDeleted && !string.IsNullOrEmpty(lt.Name))
                    s.Linetypes.Add(lt);
            }
        }
        catch { }
        try
        {
            for (int i = 0; i < doc.HatchPatterns.Count; i++)
            {
                var hp = doc.HatchPatterns[i];
                if (hp != null && !hp.IsDeleted && !string.IsNullOrEmpty(hp.Name))
                    s.HatchPatterns.Add(hp);
            }
        }
        catch { }
        try
        {
            for (int i = 0; i < doc.DimStyles.Count; i++)
            {
                var ds = doc.DimStyles[i];
                if (ds != null && !ds.IsDeleted && !string.IsNullOrEmpty(ds.Name))
                    s.DimStyles.Add(ds);
            }
        }
        catch { }
        try
        {
            for (int i = 0; i < doc.Materials.Count; i++)
            {
                var mat = doc.Materials[i];
                if (mat != null && !mat.IsDeleted && !string.IsNullOrEmpty(mat.Name))
                    s.Materials.Add(mat);
            }
        }
        catch { }
        try
        {
            for (int i = 0; i < doc.NamedViews.Count; i++)
            {
                var v = doc.NamedViews[i];
                if (v != null && !string.IsNullOrEmpty(v.Name))
                    s.NamedViews.Add(v);
            }
        }
        catch { }
        try
        {
            for (int i = 0; i < doc.NamedConstructionPlanes.Count; i++)
            {
                var cp = doc.NamedConstructionPlanes[i];
                if (!string.IsNullOrEmpty(cp.Name))
                    s.NamedCPlanes.Add((cp.Name, cp));
            }
        }
        catch { }

        return s;
    }

    private static void RestoreDocSnapshot(RhinoDoc doc, DocSettingsSnapshot s)
    {
        try { doc.ModelUnitSystem            = s.ModelUnitSystem;            } catch { }
        try { doc.ModelAbsoluteTolerance     = s.ModelAbsoluteTolerance;     } catch { }
        try { doc.ModelRelativeTolerance     = s.ModelRelativeTolerance;     } catch { }
        try { doc.ModelAngleToleranceDegrees = s.ModelAngleToleranceDegrees; } catch { }
        try { doc.PageUnitSystem             = s.PageUnitSystem;             } catch { }
        try { doc.PageAbsoluteTolerance      = s.PageAbsoluteTolerance;      } catch { }
        try { doc.PageRelativeTolerance      = s.PageRelativeTolerance;      } catch { }
        try { doc.PageAngleToleranceDegrees  = s.PageAngleToleranceDegrees;  } catch { }
        try { doc.ModelDistanceDisplayPrecision = s.ModelDistanceDisplayPrecision; } catch { }
        try { doc.PageDistanceDisplayPrecision  = s.PageDistanceDisplayPrecision;  } catch { }
        try { SetDistanceDisplayMode(doc, s.ModelDistanceDisplayMode, false); } catch { }
        try { SetDistanceDisplayMode(doc, s.PageDistanceDisplayMode,  true);  } catch { }
        try
        {
            doc.MeshingParameterStyle = s.MeshingParameterStyle;
            if (s.MeshingParameterStyle == MeshingParameterStyle.Custom && s.CustomMeshingParameters != null)
                doc.SetCustomMeshingParameters(s.CustomMeshingParameters);
        }
        catch { }
        try { doc.ModelSpaceAnnotationScalingEnabled  = s.ModelSpaceAnnotationScalingEnabled;  } catch { }
        try { doc.ModelSpaceTextScale                 = s.ModelSpaceTextScale;                 } catch { }
        try { doc.ModelSpaceHatchScalingEnabled       = s.ModelSpaceHatchScalingEnabled;       } catch { }
        try { doc.ModelSpaceHatchScale                = s.ModelSpaceHatchScale;                } catch { }
        try { doc.LayoutSpaceAnnotationScalingEnabled = s.LayoutSpaceAnnotationScalingEnabled; } catch { }
        try { doc.SubDAppearance = s.SubDAppearance;                                           } catch { }
        try { if (s.RenderSettings != null) doc.RenderSettings = s.RenderSettings;            } catch { }
        try
        {
#pragma warning disable CS0612
            var gp = doc.GroundPlane;
#pragma warning restore CS0612
            gp.BeginChange(Rhino.Render.RenderContent.ChangeContexts.Program);
            gp.Enabled      = s.GroundPlaneEnabled;
            gp.Altitude     = s.GroundPlaneAltitude;
            gp.AutoAltitude = s.GroundPlaneAutoAltitude;
            gp.ShadowOnly   = s.GroundPlaneShadowOnly;
            gp.EndChange();
        }
        catch { }
        try { doc.Notes          = s.Notes;          } catch { }
        try { doc.ModelBasepoint = s.ModelBasepoint; } catch { }
        try
        {
            if (s.EarthLocationIsSet)
                doc.EarthAnchorPoint = new EarthAnchorPoint
                {
                    EarthBasepointLatitude  = s.EarthLat,
                    EarthBasepointLongitude = s.EarthLon,
                    EarthBasepointElevation = s.EarthElevation,
                };
        }
        catch { }
        try
        {
            foreach (var view in doc.Views)
            {
                try
                {
                    var vp    = view.ActiveViewport;
                    var entry = s.ViewportGrids.FirstOrDefault(x => x.ViewportId == vp.Id);
                    if (entry.ViewportId != Guid.Empty)
                        vp.SetConstructionPlane(entry.CPlane);
                }
                catch { }
            }
        }
        catch { }

        // ── Table sync (handles both undo and redo) ───────────────────────────

        // Document strings
        try
        {
            var snapshotKeys = new HashSet<string>(s.DocStrings.Select(x => x.Key), StringComparer.Ordinal);
            for (int i = doc.Strings.Count - 1; i >= 0; i--)
            {
                var key = doc.Strings.GetKey(i);
                if (!string.IsNullOrEmpty(key) && !snapshotKeys.Contains(key))
                    try { doc.Strings.Delete(key); } catch { }
            }
            foreach (var (key, value) in s.DocStrings)
                try { doc.Strings.SetString(key, value ?? string.Empty); } catch { }
        }
        catch { }

        // Linetypes
        try
        {
            var snapshotNames = new HashSet<string>(s.Linetypes.Select(l => l.Name!), StringComparer.OrdinalIgnoreCase);
            for (int i = doc.Linetypes.Count - 1; i >= 0; i--)
            {
                var lt = doc.Linetypes[i];
                if (lt == null || lt.IsDeleted || string.IsNullOrEmpty(lt.Name)) continue;
                if (!snapshotNames.Contains(lt.Name))
                    try { doc.Linetypes.Delete(lt); } catch { }
            }
            foreach (var lt in s.Linetypes)
                if (doc.Linetypes.Find(lt.Name) < 0)
                    try { doc.Linetypes.Add(lt); } catch { }
        }
        catch { }

        // Hatch patterns
        try
        {
            var snapshotNames = new HashSet<string>(s.HatchPatterns.Select(h => h.Name!), StringComparer.OrdinalIgnoreCase);
            for (int i = doc.HatchPatterns.Count - 1; i >= 0; i--)
            {
                var hp = doc.HatchPatterns[i];
                if (hp == null || hp.IsDeleted || string.IsNullOrEmpty(hp.Name)) continue;
                if (!snapshotNames.Contains(hp.Name))
                    try { doc.HatchPatterns.Delete(hp, true); } catch { }
            }
            foreach (var hp in s.HatchPatterns)
                if (doc.HatchPatterns.FindName(hp.Name) == null)
                    try { doc.HatchPatterns.Add(hp); } catch { }
        }
        catch { }

        // Dim styles — no Delete API; restore modified + add missing
        try
        {
            foreach (var ds in s.DimStyles)
            {
                if (string.IsNullOrEmpty(ds.Name)) continue;
                var existing = doc.DimStyles.FindName(ds.Name);
                if (existing != null)
                    try { doc.DimStyles.Modify(ds, existing.Index, true); } catch { }
                else
                    try { doc.DimStyles.Add(ds, false); } catch { }
            }
        }
        catch { }

        // Materials
        try
        {
            var snapshotNames = new HashSet<string>(s.Materials.Select(m => m.Name!), StringComparer.OrdinalIgnoreCase);
            for (int i = doc.Materials.Count - 1; i >= 0; i--)
            {
                var mat = doc.Materials[i];
                if (mat == null || mat.IsDeleted || string.IsNullOrEmpty(mat.Name)) continue;
                if (!snapshotNames.Contains(mat.Name))
                    try { doc.Materials.Delete(mat); } catch { }
            }
            foreach (var mat in s.Materials)
                if (doc.Materials.Find(mat.Name, true) < 0)
                    try { doc.Materials.Add(mat); } catch { }
        }
        catch { }

        // Named views
        try
        {
            var snapshotNames = new HashSet<string>(s.NamedViews.Select(v => v.Name!), StringComparer.OrdinalIgnoreCase);
            for (int i = doc.NamedViews.Count - 1; i >= 0; i--)
            {
                var v = doc.NamedViews[i];
                if (v == null || string.IsNullOrEmpty(v.Name)) continue;
                if (!snapshotNames.Contains(v.Name))
                    try { doc.NamedViews.Delete(i); } catch { }
            }
            foreach (var v in s.NamedViews)
                if (doc.NamedViews.FindByName(v.Name) < 0)
                    try { doc.NamedViews.Add(v); } catch { }
        }
        catch { }

        // Named cplanes
        try
        {
            var snapshotNames = new HashSet<string>(s.NamedCPlanes.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            for (int i = doc.NamedConstructionPlanes.Count - 1; i >= 0; i--)
            {
                var cp = doc.NamedConstructionPlanes[i];
                if (string.IsNullOrEmpty(cp.Name)) continue;
                if (!snapshotNames.Contains(cp.Name))
                    try { doc.NamedConstructionPlanes.Delete(i); } catch { }
            }
            foreach (var (name, cp) in s.NamedCPlanes)
            {
                bool exists = false;
                for (int j = 0; j < doc.NamedConstructionPlanes.Count; j++)
                    if (string.Equals(doc.NamedConstructionPlanes[j].Name, name, StringComparison.OrdinalIgnoreCase))
                    { exists = true; break; }
                if (!exists)
                    try { doc.NamedConstructionPlanes.Add(cp); } catch { }
            }
        }
        catch { }

        doc.Views.Redraw();
    }

    private static void OnDocSettingsUndoRedo(object? sender, Rhino.Commands.CustomUndoEventArgs e)
    {
        try
        {
            if (e.Tag is DocSettingsSnapshot[] snapshots && snapshots.Length == 2)
                RestoreDocSnapshot(e.Document, e.CreatedByRedo ? snapshots[1] : snapshots[0]);
        }
        catch { }
    }

    // ── Reflection helpers — DistanceDisplayMode ──────────────────────────────

    private static MethodInfo? _getDistanceDisplayMode;
    private static MethodInfo? _setDistanceDisplayMode;

    private static void EnsureDisplayModeReflection()
    {
        if (_getDistanceDisplayMode != null) return;
        try
        {
            var asm        = typeof(RhinoDoc).Assembly;
            var flags      = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var unsafeType = asm.GetTypes().FirstOrDefault(t => t.Name == "UnsafeNativeMethods");
            if (unsafeType == null) return;
            _getDistanceDisplayMode = unsafeType.GetMethod("CRhinoDocProperties_GetDistanceDisplayMode", flags);
            _setDistanceDisplayMode = unsafeType.GetMethod("CRhinoDocProperties_SetDistanceDisplayMode", flags);
        }
        catch { }
    }

    private static int GetDistanceDisplayMode(RhinoDoc doc, bool usePageUnits)
    {
        try
        {
            EnsureDisplayModeReflection();
            if (_getDistanceDisplayMode == null) return 0;
            return _getDistanceDisplayMode.Invoke(null, new object[] { doc.RuntimeSerialNumber, usePageUnits }) is int v ? v : 0;
        }
        catch { return 0; }
    }

    private static void SetDistanceDisplayMode(RhinoDoc doc, int mode, bool usePageUnits)
    {
        try
        {
            EnsureDisplayModeReflection();
            _setDistanceDisplayMode?.Invoke(null, new object[] { doc.RuntimeSerialNumber, mode, usePageUnits });
        }
        catch { }
    }
}
