{
  dotnet-wasi-sdk,
  fetchurl,
  lib,
  llvmPackages,
  patchelf,
  runCommand,
  stdenv,
  unzip,
  zlib,
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
          "$out/tools/mono-aot-cross"
      fi
      if [ "${name}" = "Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.linux-x64" ] || \
         [ "${name}" = "Microsoft.NET.Runtime.Emscripten.3.1.56.Node.linux-x64" ]; then
        while IFS= read -r executable; do
          if patchelf --print-interpreter "$executable" >/dev/null 2>&1; then
            patchelf \
              --set-interpreter ${stdenv.cc.bintools.dynamicLinker} \
              --set-rpath "${lib.makeLibraryPath [ stdenv.cc.cc.lib stdenv.cc.libc llvmPackages.libcxx zlib ]}:$out/tools/lib" \
              "$executable"
          fi
        done < <(find "$out/tools" -type f)
        chmod -R u+rx "$out"
      fi
    '';
  packs = {
    "Microsoft.NET.Runtime.WebAssembly.Sdk" = fetchPack "Microsoft.NET.Runtime.WebAssembly.Sdk" "sha256-7+F6RQQUo5me+EW01Esn4UwRGICdhZf6nOMGN/hzq2s=";
    "Microsoft.NETCore.App.Runtime.Mono.browser-wasm" = fetchPack "Microsoft.NETCore.App.Runtime.Mono.browser-wasm" "sha256-kjG23ZWSon16AYVx1JmrhL3R02NxXSm/NzEdydle2ys=";
    "Microsoft.NETCore.App.Runtime.Mono.multithread.browser-wasm" = fetchPack "Microsoft.NETCore.App.Runtime.Mono.multithread.browser-wasm" "sha256-VPvnx4j8Rc5jorfvlr3vPJdUzjQsH2qqT/F/6hdrXoU=";
    "Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.browser-wasm" = fetchPack "Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.browser-wasm" "sha256-EG8OWRNsKoYX8U6/H4X86wJcQBkp80PmcdHbTl8NTYo=";
    "Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.linux-x64" = fetchPack "Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.linux-x64" "sha256-ETjr1hdE+HanmU1FjQsntlZHNAHDJ6Pf8s2aAw3HPqM=";
    "Microsoft.NET.Runtime.Emscripten.3.1.56.Node.linux-x64" = fetchPack "Microsoft.NET.Runtime.Emscripten.3.1.56.Node.linux-x64" "sha256-ocL/9/dza2/UsPJdRMFOHQ4+IiEFbXSOAvWbG3CguE8=";
    "Microsoft.NET.Runtime.Emscripten.3.1.56.Cache.linux-x64" = fetchPack "Microsoft.NET.Runtime.Emscripten.3.1.56.Cache.linux-x64" "sha256-4dIjXvUtI9j+JXhAVX6DONyBfTssZLdIVF70JgznsCw=";
  };
  webAssemblyLibraryPack = fetchurl {
    url = "https://api.nuget.org/v3-flatcontainer/microsoft.net.sdk.webassembly.pack/${version}/microsoft.net.sdk.webassembly.pack.${version}.nupkg";
    hash = "sha256-fLZoDVr/z+GH2lzQDJ6l58nD0lESi3e2zYq5KVOodnY=";
  };
  manifestArchive = fetchurl {
    url = "https://api.nuget.org/v3-flatcontainer/microsoft.net.workload.emscripten.current.manifest-10.0.100/10.0.111/microsoft.net.workload.emscripten.current.manifest-10.0.100.10.0.111.nupkg";
    hash = "sha256-y/DYhAizP4oYORHHbHcJjpkFtYq5SoPN9nJX3QcBnr4=";
  };
  manifest = runCommand "dotnet-emscripten-manifest-10.0.111" { nativeBuildInputs = [ unzip ]; } ''
    mkdir -p "$out"
    unzip -q ${manifestArchive} -d "$out"
  '';
in
runCommand "dotnet-sdk-10-browser" {
  passthru = dotnet-wasi-sdk.passthru;
} ''
  mkdir -p "$out/share/dotnet" "$out/bin"
  cp -rs ${dotnet-wasi-sdk}/share/dotnet/* "$out/share/dotnet/"
  cp -rs ${dotnet-wasi-sdk}/nix-support "$out/"
  rm "$out/share/dotnet/dotnet"
  cp ${dotnet-wasi-sdk}/share/dotnet/dotnet "$out/share/dotnet/dotnet"
  chmod -R u+w "$out/share/dotnet/sdk"
  rm -rf "$out/share/dotnet/sdk"
  cp -rL ${dotnet-wasi-sdk}/share/dotnet/sdk "$out/share/dotnet/sdk"
  chmod -R u+w "$out/share/dotnet/packs" "$out/share/dotnet/sdk-manifests"
  chmod u+w "$out/share/dotnet/library-packs"
  rm -rf "$out/share/dotnet/library-packs"
  cp -rL ${dotnet-wasi-sdk}/share/dotnet/library-packs "$out/share/dotnet/library-packs"
  chmod u+w "$out/share/dotnet/library-packs"
  install -Dm644 ${webAssemblyLibraryPack} \
    "$out/share/dotnet/library-packs/Microsoft.NET.Sdk.WebAssembly.Pack.${version}.nupkg"
  for name in ${lib.concatStringsSep " " (lib.mapAttrsToList (name: _: lib.escapeShellArg name) packs)}; do
    mkdir -p "$out/share/dotnet/packs/$name"
  done
  ${lib.concatStringsSep "\n" (lib.mapAttrsToList (name: path: ''
    ln -s ${path} "$out/share/dotnet/packs/${name}/${version}"
  '') packs)}
  manifestRoot="$out/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.emscripten.current"
  rm -rf "$manifestRoot"
  mkdir -p "$manifestRoot/10.0.111"
  cp -r ${manifest}/data/* "$manifestRoot/10.0.111/"
  ln -s "$out/share/dotnet/dotnet" "$out/bin/dotnet"
''
