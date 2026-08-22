{
  fetchurl,
  patchelf,
  runCommand,
  stdenv,
}:

let
  archive = fetchurl {
    url = "https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-25/wasi-sdk-25.0-x86_64-linux.tar.gz";
    hash = "sha256-UmQN3hNZm/EnqVSZ5h1tZAJWEZRW0a+Il6tnJbzz2Jw=";
  };
in
runCommand "wasi-sdk-25.0" { nativeBuildInputs = [ patchelf ]; } ''
  mkdir -p "$out"
  tar -xzf ${archive} --strip-components=1 -C "$out"
  find "$out/bin" -type f -exec sh -c '
    patchelf --print-interpreter "$1" >/dev/null 2>&1 \
      && patchelf --set-interpreter ${stdenv.cc.bintools.dynamicLinker} "$1" \
      || true
  ' sh {} \;
''

