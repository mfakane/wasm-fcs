{
  buildDotnetModule,
  dotnet-sdk_10,
  dotnet-runtime_10,
  fetchFromGitHub,
  lib,
  nugetDeps,
}:

buildDotnetModule {
  pname = "fsharp-compiler-service-wasi";
  version = "43.11.302-wasi";

  src = fetchFromGitHub {
    owner = "dotnet";
    repo = "fsharp";
    rev = "80d0ddf5af8b1cfb3cb87fb603b44579eaf45b58";
    hash = "sha256-4M+YZRtpPv6PDiTTE6vkq9VlRRpFPoTtshZP4nyeRVM=";
  };

  patches = [
    ../patches/wasm-fcs-async.patch
    ../patches/wasm-fcs-hashing.patch
  ];
  patchFlags = [ "-p1" "-l" ];
  postPatch = ''
    rm -f global.json
    rm -f .config/dotnet-tools.json
    sed -i '/optimizationData\.targets/d' Directory.Build.targets
    sed -i '/<Target Name="CopyMIBCWrapper"/,/<\/Target>/d' Directory.Build.targets
  '';
  preConfigure = ''
    export DISABLE_ARCADE=true
    export BUILDING_USING_DOTNET=true
  '';
  projectFile = "src/Compiler/FSharp.Compiler.Service.fsproj";
  inherit nugetDeps;
  dotnet-sdk = dotnet-sdk_10;
  dotnet-runtime = dotnet-runtime_10;
  selfContainedBuild = false;

  buildPhase = ''
    runHook preBuild
    dotnet build src/Compiler/FSharp.Compiler.Service.fsproj -c Release \
      -p:BUILDING_USING_DOTNET=true \
      -p:SKIP_NETCURRENT_FSC_BUILD=true \
      -p:DebugType=none
    runHook postBuild
  '';

  installPhase = ''
    runHook preInstall
    install -Dm644 \
      artifacts/bin/FSharp.Compiler.Service/Release/netstandard2.0/FSharp.Compiler.Service.dll \
      "$out/lib/FSharp.Compiler.Service.dll"
    runHook postInstall
  '';

  meta = {
    description = "FSharp.Compiler.Service with WASI-safe asynchronous compilation boundaries";
    homepage = "https://github.com/dotnet/fsharp";
    license = lib.licenses.mit;
    platforms = lib.platforms.linux;
  };
}

