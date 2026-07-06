using System.Runtime.InteropServices.JavaScript;

// .NET-on-WASM entry point. The runtime starts, then JS calls the exported
// Nest function on demand. No loop needed; keep the module alive.
return;

public partial class NestInterop
{
    // Exposed to JS as globalThis.frahan.Nest(requestJson) -> responseJson.
    // The whole 2D nest runs here, in the browser, on the managed lane.
    [JSExport]
    internal static string Nest(string requestJson)
        => Frahan.Nest2D.NestApi.Nest(requestJson);

    // Version/health ping so the page can confirm the engine loaded.
    [JSExport]
    internal static string Version() => "Frahan.Nest2D wasm 0.1";
}
