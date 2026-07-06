// Boots the .NET WebAssembly runtime and wires the exported nester onto
// globalThis.frahan, then hands control to app.js (the UI).
//
// IMPORTANT: do NOT call dotnet.run() / runMainAndExit here. Main() is empty
// and running it exits the runtime, after which the [JSExport] methods throw
// ".NET runtime already exited". Creating the runtime and fetching the
// assembly exports is enough to keep it resident for on-demand interop calls.
import { dotnet } from './_framework/dotnet.js';

const { getAssemblyExports, getConfig } = await dotnet
  .withDiagnosticTracing(false)
  .create();

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

globalThis.frahan = {
  nest: (requestJson) => exports.NestInterop.Nest(requestJson),
  version: () => exports.NestInterop.Version(),
};

// signal readiness to the UI (the runtime stays alive; no run() call)
window.dispatchEvent(new CustomEvent('frahan-ready'));
