# wasm-fcs

`wasm-fcs` は F# Compiler Services (FCS) を WASM 実行環境へ閉じ込めるための独立リポジトリです。任意の F# コードを同じ API で次の 2 つの環境から扱います。

- Browser WASM: `parse`、`metadata`、`run` を JS / TS から呼び出せるランタイムと Playground
- WASI: Wasmtime から起動できる CLI。ファイル CLI と stdin JSON の両方を提供

## 構成

```text
wasm-fcs
├── nix/wasm-fcs.nix          # FCS 本体、依存 NuGet、WASI パッチを一つの Nix package に固定
├── src/WasmFcs.Core/         # FCS checker + syntax/symbol metadata + sandbox execution
├── src/WasmFcs.Wasi/         # wasi-wasm guest / CLI
├── src/WasmFcs.Browser/      # browser-wasm JS exports
├── browser/src/index.ts      # JS/TS facade
└── browser/index.html        # 実行可能な Playground
```

FCS の WASI 向け変更は `patches/wasm-fcs-async.patch` と `patches/wasm-fcs-hashing.patch` に限定し、`nix/wasm-fcs.nix` が FCS の source revision・NuGet 依存・patch をまとめて所有します。パッチは WASI でコンパイラ処理を逐次化し、スレッド依存の暗号学的ハッシュを移植可能な非暗号学的ハッシュへ置換します。後者は FCS 内部のキャッシュキー / MVID 用途であり、署名用途には使用しません。

## Nix でビルド

```sh
nix build .#wasm-fcs
nix build .#wasi-runtime
nix build .#browser-runtime
nix develop
```

成果物を同時に使う場合は名前を分けておくと便利です。

```sh
nix build .#wasm-fcs -o result-fcs
nix build .#wasi-runtime -o result-wasi
nix build .#browser-runtime -o browser/public/fcs-runtime
```

`wasm-fcs` package の成果物は `lib/FSharp.Compiler.Service.dll` です。WASI と Browser の各 runtime はこれを `WasmFcsPath` として参照し、NuGet から別の FCS を取得しません。

## WASI CLI

```sh
cat > example.fsx <<'EOF'
printfn "hello from F#"
let answer = 40 + 2
printfn "answer = %d" answer
EOF

# Nix outputのlauncherを使う（launcherがruntime directoryでWasmtimeを起動）
result-wasi/run-wasm-fcs run example.fsx
result-wasi/run-wasm-fcs parse example.fsx
result-wasi/run-wasm-fcs metadata example.fsx
```

機械的な呼び出しでは stdout へ JSON を 1 つ渡せます。WASI guest の stdout は JSON 専用で、ユーザーコードの `printfn` は `run` 結果の `output` へ回収されます。

```sh
{ printf 'run\n'; cat example.fsx; } \
  | (cd result-wasi && wasmtime run -S http --dir . dotnet.wasm WasmFcs.Wasi)

# JSON requestも受け付ける
printf '%s' '{"command":"run","source":"printfn \\"hello\\""}' \
  | (cd result-wasi && wasmtime run -S http --dir . dotnet.wasm WasmFcs.Wasi)
```

## Browser API と Playground

Browser runtime を生成して Playground を起動します。

```sh
nix build .#browser-runtime -o browser/public/fcs-runtime
npm install --prefix browser
npm run dev --prefix browser
```

ブラウザは `COOP: same-origin` と `COEP: require-corp` による cross-origin isolation が必要です。Vite 設定はこれらの開発用ヘッダーを設定します。

```ts
import { createBrowserFcs } from "wasm-fcs-browser";

const fcs = await createBrowserFcs({ runtimeUrl: "/fcs-runtime" });
const parsed = await fcs.parse("let answer = 40 + 2");
const symbols = await fcs.metadata("let answer = 40 + 2");
const execution = await fcs.run('printfn "hello"');
```

各操作は JSON ではなく型付きの TS object を返します。`diagnostics` は FCS の `FSxxxx` 診断と、空ソース・禁止ディレクティブ・sandbox 能力拒否などの `FCSWxxxx` 診断を共通形式で返します。

### Facade package の手動導入

Browser facade は npm registry へは公開せず、リリース時に GitHub Release の tarball として生成します。対応する `wasm-fcs-browser-facade-<version>.tgz` をダウンロードしてローカルインストールしてください。

```sh
npm install ./wasm-fcs-browser-facade-0.1.0.tgz
```

WASM runtime は facade package に含まれません。対応する `wasm-fcs-browser-runtime-<version>.tar.gz` を取得し、checksum を確認してアプリの static directory へ展開します。

```sh
sha256sum -c wasm-fcs-browser-runtime-0.1.0.tar.gz.sha256
mkdir -p public/fcs-runtime
tar --extract --gzip \
  --file wasm-fcs-browser-runtime-0.1.0.tar.gz \
  --directory public/fcs-runtime
```

```ts
import { createBrowserFcs } from "wasm-fcs-browser";

const fcs = await createBrowserFcs({ runtimeUrl: "/fcs-runtime" });
```

`runtimeUrl` は展開済み runtime を配信するディレクトリであり、GitHub Release の archive URL ではありません。ページ配信には `Cross-Origin-Opener-Policy: same-origin` と `Cross-Origin-Embedder-Policy: require-corp` が必要です。

## サンドボックス境界

FCS はコンパイラであり、実行時の安全境界ではありません。そのため実行前に FCS の symbol use から次を拒否し、さらにホスト側の WASI / Browser capability に依存します。

- `System.IO`、`System.Net`、`System.Reflection`、`System.Diagnostics`、`System.Environment`
- native interop、assembly loader、expression compiler、F# native/reflection escape
- `#r`、`#load`、`#I`、`#line`

WASI の 実行時に host directory を公開するのは CLI 自身が入力ファイルを読む `--dir .` だけです。ユーザーコードへ書き込みディレクトリや network capability を渡さないでください。実行は直列化し、同一プロセス内で複数ソースを実行する場合はホスト側で必要に応じて runtime を再生成してください。

## FCS の性能比較

Native .NET、WASI / Wasmtime、Browser WASM を 同じ FCS・参照アセンブリ・ワークロードで比較します。ベンチマークは `nix develop` 内で実行してください。

```sh
nix build .#wasi-runtime -o result-wasi
nix build .#browser-runtime -o result-browser
nix build .#wasm-fcs -o result-fcs
dotnet build bench/WasmFcs.BenchHost/WasmFcs.BenchHost.csproj -c Release

WASM_FCS_DLL="$PWD/result-fcs/lib/FSharp.Compiler.Service.dll" \
WASM_FCS_RUNTIME="$PWD/result-wasi" \
node tools/benchmark.mjs > /tmp/fcs-native-wasi.ndjson

WASM_FCS_RUNTIME="$PWD/result-wasi" \
node tools/benchmark-browser.mjs > /tmp/fcs-browser.ndjson

node tools/render-benchmark.mjs /tmp/fcs-native-wasi.ndjson /tmp/fcs-browser.ndjson
```

`BENCH_ITERATIONS` は steady-state の既定回数 (10)、`BENCH_COLD_ITERATIONS` と `BENCH_WARM_ITERATIONS` は新規プロセス／ページを作る測定の既定回数 (3) です。厳密な冷起動比較では各値を 10 以上に設定してください。

最新の自動生成ベンチマークレポート (測定条件・絶対時間・Native 比・FCS 内部フェーズの内訳) は、英語版 [README.md](README.md#fcs-performance-comparison) を参照してください。

## License

MIT

The original code is licensed under MIT. Third-party runtime, compiler, and
build components retain their upstream licenses; see
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
