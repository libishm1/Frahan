// Lazy .NET WebAssembly loader. Importing this module is cheap (no runtime
// download); the ~0.6 MB gzipped runtime is fetched only when frahanBoot() is
// first called (on the first Nest), so the page - and "Load sample" - are
// instant, which matters most on mobile.
//
// Do NOT call dotnet.run(): Main() is empty and running it exits the runtime,
// after which [JSExport] methods throw. create() + getAssemblyExports leaves
// the runtime resident for on-demand interop.
import { dotnet } from './_framework/dotnet.js';

let bootPromise = null;

globalThis.frahanBoot = () => {
  if (!bootPromise) {
    bootPromise = (async () => {
      const { getAssemblyExports, getConfig } = await dotnet
        .withDiagnosticTracing(false)
        .create();
      const config = getConfig();
      const exports = await getAssemblyExports(config.mainAssemblyName);
      globalThis.frahan = {
        nest: (requestJson) => exports.NestInterop.Nest(requestJson),
        version: () => exports.NestInterop.Version(),
      };
      return globalThis.frahan;
    })();
  }
  return bootPromise;
};

// tell the UI the loader is available (engine not yet downloaded)
window.dispatchEvent(new CustomEvent('frahan-loader-ready'));
