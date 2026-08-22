# wasm-fcs-browser

This package is the JavaScript/TypeScript facade for the FCS Browser WASM
runtime. It does not include the WASM runtime itself.

## Install

Install the facade tarball from the matching GitHub Release:

```sh
npm install ./wasm-fcs-browser-facade-<version>.tgz
```

Download the matching `wasm-fcs-browser-runtime-<version>.tar.gz`, verify its
checksum, and extract it into the application's static directory:

```text
public/fcs-runtime/_framework/dotnet.js
```

```sh
sha256sum -c wasm-fcs-browser-runtime-<version>.tar.gz.sha256
mkdir -p public/fcs-runtime
tar --extract --gzip \
  --file wasm-fcs-browser-runtime-<version>.tar.gz \
  --directory public/fcs-runtime
```

Then create the facade with the served runtime directory:

```ts
import { createBrowserFcs } from "wasm-fcs-browser";

const fcs = await createBrowserFcs({ runtimeUrl: "/fcs-runtime" });
```

The application must serve the page with:

```text
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Embedder-Policy: require-corp
```

Use matching facade and runtime versions. `runtimeUrl` is a URL to an
extracted and served directory; it is not the GitHub Release archive URL.
