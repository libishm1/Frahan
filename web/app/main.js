// Boots the .NET WebAssembly runtime, wires the exported nester onto
// globalThis.frahan, then hands control to app.js (the UI).
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

await dotnet.run();

// signal readiness to the UI
window.dispatchEvent(new CustomEvent('frahan-ready'));
