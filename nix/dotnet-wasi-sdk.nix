{
  dotnet-runtime_10,
  dotnet-sdk_10,
  fetchurl,
  lib,
  llvmPackages,
  patchelf,
  runCommand,
  stdenv,
  unzip,
}:

let
  version = "10.0.11";
  fetchPack = name: hash:
    let
      lowerName = lib.toLower name;
      archive = fetchurl {
        url = "https://api.nuget.org/v3-flatcontainer/${lowerName}/${version}/${lowerName}.${version}.nupkg";
        inherit hash;
      };
    in
    runCommand "${name}-${version}" { nativeBuildInputs = [ unzip patchelf ]; } ''
      mkdir -p "$out"
      unzip -q ${archive} -d "$out"
      if [ -f "$out/tools/mono-aot-cross" ]; then
        chmod +x "$out/tools/mono-aot-cross"
        patchelf \
          --set-interpreter ${stdenv.cc.bintools.dynamicLinker} \
          --set-rpath ${lib.makeLibraryPath [ stdenv.cc.cc.lib stdenv.cc.libc llvmPackages.libcxx ]} \
          "$out/tools/mono-aot-cross"
      fi
    '';
  packs = {
    "Microsoft.NET.Runtime.WebAssembly.Wasi.Sdk" = fetchPack "Microsoft.NET.Runtime.WebAssembly.Wasi.Sdk" "sha256-XMDu+acGIfiTbBO/E9CUMumiBvKuFYZXX0azPsuo6Ro=";
    "Microsoft.NETCore.App.Runtime.Mono.wasi-wasm" = fetchPack "Microsoft.NETCore.App.Runtime.Mono.wasi-wasm" "sha256-BFkBRwASG00pi7fKzvJ+x1jDFs+XMyM/itxAaDDoxnU=";
    "Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.wasi-wasm" = fetchPack "Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.wasi-wasm" "sha256-xeKTZoxSM3q+aIdrtewuzzqOWZ7cLnNgs235hvqo/q0=";
    "Microsoft.NET.Runtime.MonoAOTCompiler.Task" = fetchPack "Microsoft.NET.Runtime.MonoAOTCompiler.Task" "sha256-bggsA5UblEZ2RKWpzxLJNoLh589e+BDAa+6YUi0pe14=";
    "Microsoft.NET.Runtime.MonoTargets.Sdk" = fetchPack "Microsoft.NET.Runtime.MonoTargets.Sdk" "sha256-VO7KRq9onglEDY9tfasI8PoxtiGWbgMYJ3Ov7p2IY18=";
  };
  monoManifestArchive = fetchurl {
    url = "https://api.nuget.org/v3-flatcontainer/microsoft.net.workload.mono.toolchain.current.manifest-10.0.100/10.0.111/microsoft.net.workload.mono.toolchain.current.manifest-10.0.100.10.0.111.nupkg";
    hash = "sha256-KFxbgOJMcfR9TvWK58DdyIgiTMDXZv2Zpv7DlzAJ6fg=";
  };
  monoManifest = runCommand "dotnet-wasi-manifest-10.0.111" { nativeBuildInputs = [ unzip ]; } ''
    mkdir -p "$out"
    unzip -q ${monoManifestArchive} -d "$out"
  '';
  emscriptenManifestArchive = fetchurl {
    url = "https://api.nuget.org/v3-flatcontainer/microsoft.net.workload.emscripten.current.manifest-10.0.100/10.0.111/microsoft.net.workload.emscripten.current.manifest-10.0.100.10.0.111.nupkg";
    hash = "sha256-y/DYhAizP4oYORHHbHcJjpkFtYq5SoPN9nJX3QcBnr4=";
  };
  emscriptenManifest = runCommand "dotnet-emscripten-manifest-10.0.111" { nativeBuildInputs = [ unzip ]; } ''
    mkdir -p "$out"
    unzip -q ${emscriptenManifestArchive} -d "$out"
  '';
in
runCommand "dotnet-sdk-10-wasi" {
  meta = dotnet-sdk_10.meta;
  passthru = {
    icu = dotnet-sdk_10.icu;
    runtime = dotnet-runtime_10;
    packages = dotnet-sdk_10.packages or [ ];
  };
} ''
  mkdir -p "$out/share/dotnet" "$out/bin"
  cp -rs ${dotnet-sdk_10}/share/dotnet/* "$out/share/dotnet/"
  rm "$out/share/dotnet/dotnet"
  cp ${dotnet-sdk_10}/share/dotnet/dotnet "$out/share/dotnet/dotnet"
  chmod -R u+w "$out/share/dotnet/sdk"
  rm -rf "$out/share/dotnet/sdk"
  cp -rL ${dotnet-sdk_10}/share/dotnet/sdk "$out/share/dotnet/sdk"
  cp -rs ${dotnet-sdk_10}/nix-support "$out/"
  chmod u+w "$out/share/dotnet/packs" "$out/share/dotnet/sdk-manifests"
  for name in ${lib.concatStringsSep " " (lib.mapAttrsToList (name: _: lib.escapeShellArg name) packs)}; do
    mkdir -p "$out/share/dotnet/packs/$name"
  done
  ${lib.concatStringsSep "\n" (lib.mapAttrsToList (name: path: ''
    ln -s ${path} "$out/share/dotnet/packs/${name}/${version}"
  '') packs)}
  manifestRoot="$out/share/dotnet/sdk-manifests/10.0.100"
  chmod u+w "$manifestRoot"
  installManifest() {
    chmod -R u+w "$manifestRoot/$1"
    rm -rf "$manifestRoot/$1"
    mkdir -p "$manifestRoot/$1/10.0.111"
    cp -r "$2"/data/* "$manifestRoot/$1/10.0.111/"
  }
  installManifest microsoft.net.workload.mono.toolchain.current ${monoManifest}
  installManifest microsoft.net.workload.emscripten.current ${emscriptenManifest}
  ln -s "$out/share/dotnet/dotnet" "$out/bin/dotnet"
''

