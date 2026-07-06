// Boots the .NET WebAssembly runtime on page load and wires the exported nester
// onto globalThis.frahan, so by the time you click Nest the engine is ready and
// nesting is instant. (Eager load: loading up front beats loading on first click.)
//
// Do NOT call dotnet.run(): Main() is empty and running it exits the runtime,
// after which [JSExport] methods throw. create() + getAssemblyExports leaves
// the runtime resident for on-demand interop.
import { dotnet } from './_framework/dotnet.js';

try {
  const { getAssemblyExports, getConfig } = await dotnet
    .withDiagnosticTracing(false)
    .create();

  const config = getConfig();
  const exports = await getAssemblyExports(config.mainAssemblyName);

  globalThis.frahan = {
    nest: (requestJson) => exports.NestInterop.Nest(requestJson),
    version: () => exports.NestInterop.Version(),
  };

  // engine resident and ready (no run() call); tell the UI to enable Nest
  window.dispatchEvent(new CustomEvent('frahan-ready'));
} catch (e) {
  // surface a load failure instead of hanging on "loading engine…"
  window.dispatchEvent(new CustomEvent('frahan-error', { detail: String(e && e.message || e) }));
}
