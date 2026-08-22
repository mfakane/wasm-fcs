import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { resolve, join } from "node:path";

const root = resolve(import.meta.dirname, "..");
const browserRoot = resolve(root, "browser");
const outputArgument = process.argv.indexOf("--output");
if (outputArgument < 0 || !process.argv[outputArgument + 1]) {
  throw new Error("Usage: node tools/package-browser-facade.mjs --output <directory>");
}

const output = resolve(process.argv[outputArgument + 1]);
const packageMetadata = JSON.parse(readFileSync(join(browserRoot, "package.json"), "utf8"));
const dist = resolve(browserRoot, "dist-lib");
const packageReadme = resolve(browserRoot, "PACKAGE-README.md");
if (!packageMetadata.version) throw new Error("browser/package.json must define a version.");
if (!existsSync(dist)) throw new Error("Facade output is missing; run npm run build:lib first.");

rmSync(output, { recursive: true, force: true });
mkdirSync(output, { recursive: true });
cpSync(dist, join(output, "dist"), { recursive: true });
cpSync(packageReadme, join(output, "README.md"));
cpSync(resolve(root, "LICENSE"), join(output, "LICENSE"));
cpSync(resolve(root, "THIRD-PARTY-NOTICES.md"), join(output, "THIRD-PARTY-NOTICES.md"));

const stagedPackage = {
  name: packageMetadata.name,
  version: packageMetadata.version,
  private: true,
  type: "module",
  main: "./dist/index.js",
  types: "./dist/index.d.ts",
  exports: {
    ".": {
      types: "./dist/index.d.ts",
      import: "./dist/index.js",
      default: "./dist/index.js",
    },
  },
  files: ["dist", "THIRD-PARTY-NOTICES.md"],
  sideEffects: false,
  license: "MIT",
};
writeFileSync(join(output, "package.json"), `${JSON.stringify(stagedPackage, null, 2)}\n`);
