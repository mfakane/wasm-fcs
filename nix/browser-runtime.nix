{
  buildDotnetModule,
  dotnet-runtime_10,
  dotnet-browser-sdk,
  wasm-fcs,
  lib,
  nugetDeps,
  python3,
  src,
}:

buildDotnetModule {
  pname = "fcs-browser-runtime";
  version = "0.1.0";
  inherit src nugetDeps;

  projectFile = "src/WasmFcs.Browser/WasmFcs.Browser.csproj";
  dotnet-sdk = dotnet-browser-sdk;
  dotnet-runtime = dotnet-runtime_10;
  runtimeId = "browser-wasm";
  selfContainedBuild = false;
  dontDotnetFixup = true;
  nativeBuildInputs = [ python3 ];
  env = {
    DOTNET_CLI_HOME = "/build/dotnet-home";
  };
  dotnetRestoreFlags = [
    "-p:WasmFcsPath=${wasm-fcs}/lib/FSharp.Compiler.Service.dll"
  ];
  preConfigure = ''
    userDotnet="$DOTNET_CLI_HOME/.dotnet"
    mkdir -p \
      "$userDotnet/metadata/workloads/10.0.300/InstalledWorkloads" \
      "$userDotnet/packs" \
      "$userDotnet/sdk-manifests/10.0.100"
    touch "$userDotnet/metadata/workloads/10.0.300/InstalledWorkloads/browser-wasm"
    mkdir -p "$userDotnet/metadata/workloads/InstalledWorkloadSets/v1/10.0.303.1"
    touch "$userDotnet/metadata/workloads/InstalledWorkloadSets/v1/10.0.303.1/10.0.300"
    for pack in \
      Microsoft.NET.Runtime.WebAssembly.Sdk \
      Microsoft.NETCore.App.Runtime.Mono.browser-wasm \
      Microsoft.NETCore.App.Runtime.Mono.multithread.browser-wasm \
      Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.browser-wasm \
      Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.linux-x64 \
      Microsoft.NET.Runtime.Emscripten.3.1.56.Node.linux-x64 \
      Microsoft.NET.Runtime.Emscripten.3.1.56.Cache.linux-x64; do
      case "$pack" in
        Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.linux-x64|Microsoft.NET.Runtime.Emscripten.3.1.56.Node.linux-x64)
          cp -rL "${dotnet-browser-sdk}/share/dotnet/packs/$pack" "$userDotnet/packs/$pack"
          chmod -R u+rx "$userDotnet/packs/$pack"
          ;;
        *)
          ln -s "${dotnet-browser-sdk}/share/dotnet/packs/$pack" "$userDotnet/packs/$pack"
          ;;
      esac
      mkdir -p "$userDotnet/metadata/workloads/InstalledPacks/v1/$pack/10.0.11"
      touch "$userDotnet/metadata/workloads/InstalledPacks/v1/$pack/10.0.11/10.0.300"
    done
    ln -s "${dotnet-browser-sdk}/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.emscripten.current" \
      "$userDotnet/sdk-manifests/10.0.100/microsoft.net.workload.emscripten.current"
    mkdir -p "$userDotnet/metadata/workloads/InstalledManifests/v1/microsoft.net.workload.emscripten.current/10.0.111"
    touch "$userDotnet/metadata/workloads/InstalledManifests/v1/microsoft.net.workload.emscripten.current/10.0.111/10.0.100"
    mkdir -p "$NUGET_PACKAGES/fsharp.core"
    cp -rL "$NUGET_FALLBACK_PACKAGES/fsharp.core/10.1.302" "$NUGET_PACKAGES/fsharp.core/"
    chmod -R u+w "$NUGET_PACKAGES/fsharp.core"
  '';
  buildPhase = ''
    runHook preBuild
    dotnet publish src/WasmFcs.Browser/WasmFcs.Browser.csproj \
      --no-restore -c Release \
      -p:WasmFcsPath=${wasm-fcs}/lib/FSharp.Compiler.Service.dll
    runHook postBuild
  '';
  installPhase = ''
    runHook preInstall
    mkdir -p "$out"
    cp -r src/WasmFcs.Browser/bin/Release/net10.0/publish/wwwroot/. "$out/"
    runHook postInstall
  '';
  meta = {
    description = "F# Compiler Services browser WASM runtime";
    homepage = "https://github.com/mfakane/wasm-fcs";
    license = lib.licenses.mit;
    platforms = lib.platforms.linux;
  };
}
