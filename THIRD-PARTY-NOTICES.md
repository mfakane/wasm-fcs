# Third-party notices

The MIT license in `LICENSE` applies to the original code in this repository.
It does not change the licenses of third-party components used to build or
run the FCS WASM and WASI artifacts. Those components remain under their
respective upstream licenses.

This file records the direct components currently used by the repository. The
exact dependency closure can change when the pinned FCS, .NET, Nixpkgs, NuGet,
or npm inputs change; the corresponding lock files and upstream license files
are authoritative for those versions.

## Runtime and compiler components

| Component | Use | License | Upstream |
| --- | --- | --- | --- |
| F# Compiler Services | FCS compiler and language-service implementation | MIT | <https://github.com/dotnet/fsharp> |
| FSharp.Core | F# standard library | MIT | <https://github.com/dotnet/fsharp> |
| .NET runtime and reference assemblies | Native, WASI, and Browser WASM runtime/reference APIs | MIT | <https://github.com/dotnet/runtime> |

The FCS source revision and patches used for the WASI build are pinned in
`nix/wasm-fcs.nix` and `patches/`. The generated runtime may also contain
transitive .NET/Mono WebAssembly runtime assets; retain the upstream notices
when redistributing those generated artifacts.

## Browser development dependencies

The browser package uses these direct development dependencies:

| Package | Use | License |
| --- | --- | --- |
| Playwright | Browser benchmark automation | Apache-2.0 |
| Vite | Browser development/build server | MIT |
| TypeScript | Type checking and transpilation | Apache-2.0 |
| `@types/node` | Node.js type definitions | MIT |

Their exact versions and transitive dependency license metadata are recorded
in `browser/package-lock.json`. The browser package publishes only `src/`, so
Playwright, Vite, and TypeScript are development dependencies rather than
runtime dependencies of the facade.

## Build and benchmark tools

The Nix development environment also uses .NET SDK/workloads, Wasmtime,
Playwright browser binaries, and related Nixpkgs packages. These are tooling
inputs, not part of the original project code. Their licenses are provided by
the pinned Nixpkgs revisions in `flake.lock`; when distributing a fully
self-contained toolchain or runtime image, generate and ship notices for that
closure as well.
