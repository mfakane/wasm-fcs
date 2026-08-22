{
  buildDotnetModule,
  dotnet-runtime_10,
  dotnet-wasi-sdk,
  wasm-fcs,
  lib,
  nugetDeps,
  src,
  wasi-sdk-25,
}:

buildDotnetModule {
  pname = "wasm-fcs-runtime";
  version = "0.1.0";
  inherit src nugetDeps;

  projectFile = "src/WasmFcs.Wasi/WasmFcs.Wasi.csproj";
  dotnet-sdk = dotnet-wasi-sdk;
  dotnet-runtime = dotnet-runtime_10;
  runtimeId = "wasi-wasm";
  selfContainedBuild = false;
  dontDotnetFixup = true;

  dotnetRestoreFlags = [
    "-p:WasmFcsPath=${wasm-fcs}/lib/FSharp.Compiler.Service.dll"
  ];
  env = {
    DOTNET_CLI_HOME = "/build/dotnet-home";
    WASI_SDK_PATH = "${wasi-sdk-25}";
  };
  preConfigure = ''
    userDotnet="$DOTNET_CLI_HOME/.dotnet"
    mkdir -p \
      "$userDotnet/metadata/workloads/10.0.300/InstalledWorkloads" \
      "$userDotnet/packs" \
      "$userDotnet/sdk-manifests/10.0.100"
    touch "$userDotnet/metadata/workloads/10.0.300/InstalledWorkloads/wasi-experimental"
    mkdir -p "$userDotnet/metadata/workloads/InstalledWorkloadSets/v1/10.0.303.1"
    touch "$userDotnet/metadata/workloads/InstalledWorkloadSets/v1/10.0.303.1/10.0.300"
    for pack in \
      Microsoft.NET.Runtime.WebAssembly.Wasi.Sdk \
      Microsoft.NETCore.App.Runtime.Mono.wasi-wasm \
      Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.wasi-wasm \
      Microsoft.NET.Runtime.MonoAOTCompiler.Task \
      Microsoft.NET.Runtime.MonoTargets.Sdk; do
      ln -s "${dotnet-wasi-sdk}/share/dotnet/packs/$pack" "$userDotnet/packs/$pack"
      mkdir -p "$userDotnet/metadata/workloads/InstalledPacks/v1/$pack/10.0.11"
      touch "$userDotnet/metadata/workloads/InstalledPacks/v1/$pack/10.0.11/10.0.300"
    done
    for manifest in \
      microsoft.net.workload.mono.toolchain.current \
      microsoft.net.workload.emscripten.current; do
      ln -s "${dotnet-wasi-sdk}/share/dotnet/sdk-manifests/10.0.100/$manifest" \
        "$userDotnet/sdk-manifests/10.0.100/$manifest"
      mkdir -p "$userDotnet/metadata/workloads/InstalledManifests/v1/$manifest/10.0.111"
      touch "$userDotnet/metadata/workloads/InstalledManifests/v1/$manifest/10.0.111/10.0.100"
    done
    mkdir -p "$NUGET_PACKAGES/fsharp.core"
    cp -rL "$NUGET_FALLBACK_PACKAGES/fsharp.core/10.1.302" "$NUGET_PACKAGES/fsharp.core/"
    chmod -R u+w "$NUGET_PACKAGES/fsharp.core"
  '';

  buildPhase = ''
    runHook preBuild
    dotnet publish src/WasmFcs.Wasi/WasmFcs.Wasi.csproj \
      --no-restore -c Release \
      -p:WasmFcsPath=${wasm-fcs}/lib/FSharp.Compiler.Service.dll
    runHook postBuild
  '';

  installPhase = ''
    runHook preInstall
    mkdir -p "$out"
    cp -r src/WasmFcs.Wasi/bin/Release/net10.0/wasi-wasm/AppBundle/. "$out/"
    install -Dm644 "${wasm-fcs}/lib/FSharp.Compiler.Service.dll" "$out/FSharp.Compiler.Service.dll"
    cat > "$out/run-wasm-fcs" <<'EOF'
#!/bin/sh
runtime_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
command_name=''${1:-}
source_file=''${2:--}
case "$command_name" in
  run|parse|metadata) ;;
  *) echo "usage: run-wasm-fcs <run|parse|metadata> [script.fsx|-]" >&2; exit 2 ;;
esac
(
  printf '%s\n' "$command_name"
  if [ "$source_file" = "-" ]; then
    cat
  else
    cat "$source_file"
  fi
) | (
  cd "$runtime_dir" || exit 3
  exec wasmtime run -S http --dir . dotnet.wasm WasmFcs.Wasi
)
EOF
    chmod +x "$out/run-wasm-fcs"
    runHook postInstall
  '';

  meta = {
    description = "F# Compiler Services WASI runtime and CLI";
    homepage = "https://github.com/mfakane/wasm-fcs";
    license = lib.licenses.mit;
    platforms = lib.platforms.linux;
  };
}
