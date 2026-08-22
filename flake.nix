{
  description = "Portable F# Compiler Services runtime for Browser WASM and WASI";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs {
          inherit system;
          config.allowUnsupportedSystem = true;
        };
        inherit (pkgs) lib;
        src = lib.cleanSourceWith {
          src = ./.;
          filter = path: type:
            let base = lib.baseNameOf path;
            in lib.cleanSourceFilter path type &&
              !(type == "directory" && lib.elem base [ "result" "bin" "obj" "node_modules" ".git" ".direnv" ]);
        };
        dotnet-wasi-sdk = pkgs.callPackage ./nix/dotnet-wasi-sdk.nix { };
        dotnet-browser-sdk = pkgs.callPackage ./nix/dotnet-browser-sdk.nix { inherit dotnet-wasi-sdk; };
        wasi-sdk-25 = pkgs.callPackage ./nix/wasi-sdk-25.nix { };
        wasm-fcs = pkgs.callPackage ./nix/wasm-fcs.nix {
          nugetDeps = ./nix/wasm-fcs-nuget-deps.json;
        };
        wasi-runtime = pkgs.callPackage ./nix/wasi-runtime.nix {
          inherit src wasm-fcs dotnet-wasi-sdk wasi-sdk-25;
          nugetDeps = ./nix/runtime-nuget-deps.json;
        };
        browser-runtime = pkgs.callPackage ./nix/browser-runtime.nix {
          inherit src wasm-fcs dotnet-browser-sdk;
          nugetDeps = ./nix/runtime-nuget-deps.json;
        };
      in {
        packages = {
          inherit wasm-fcs wasi-runtime browser-runtime;
          default = wasm-fcs;
        };

        devShells.default =
          let
            browsersDir = pkgs.playwright-driver.browsers;
            chromiumShellDir = lib.head (lib.filter (lib.hasPrefix "chromium_headless_shell-")
              (builtins.attrNames (builtins.readDir browsersDir)));
            chromiumExe = "${browsersDir}/${chromiumShellDir}/chrome-headless-shell-linux64/chrome-headless-shell";
          in
          pkgs.mkShell {
            packages = with pkgs; [
              dotnet-sdk_10
              wasmtime
              nodejs
              pnpm
              jq
              playwright-driver.browsers
            ];
            shellHook = ''
              export WASM_FCS_DLL="${wasm-fcs}/lib/FSharp.Compiler.Service.dll"
              export WASM_FCS_RUNTIME="${wasi-runtime}"
              export WASM_FCS_BROWSER_RUNTIME="${browser-runtime}"
              export PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
              export PLAYWRIGHT_BROWSERS_PATH="${browsersDir}"
              export PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH="${chromiumExe}"
            '';
        };
      });
}
