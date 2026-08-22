import { readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { nativeRatio, round } from "./benchmark-stats.mjs";

const args = process.argv.slice(2);
const inputPaths = [];
let readmePath = resolve(import.meta.dirname, "../README.md");
let svgPath = resolve(import.meta.dirname, "../bench/benchmark.svg");
let checkOnly = false;
for (let index = 0; index < args.length; index += 1) {
  if (args[index] === "--check") checkOnly = true;
  else if (args[index] === "--readme") readmePath = resolve(args[++index]);
  else if (args[index] === "--svg") svgPath = resolve(args[++index]);
  else inputPaths.push(resolve(args[index]));
}
if (!inputPaths.length) throw new Error("Usage: node tools/render-benchmark.mjs results.ndjson [...results.ndjson] [--readme README.md] [--svg bench/benchmark.svg]");

const records = inputPaths.flatMap((path) => readFileSync(path, "utf8")
  .split("\n")
  .filter(Boolean)
  .map((line) => JSON.parse(line)));
const keyOf = (record) => `${record.workload}|${record.scenario}|${record.operation}`;
const byKey = new Map(records.map((record) => [keyOf(record) + `|${record.platform}`, record]));
const workloads = [...new Set(records.map((record) => record.workload))];
const referenceCounts = [...new Set(records.map((record) => record.referenceCount).filter((value) => value !== undefined))];
if (referenceCounts.length > 1) throw new Error(`Reference pack count differs: ${referenceCounts.join(", ")}`);

function record(workload, scenario, operation, platform) {
  return byKey.get(`${workload}|${scenario}|${operation}|${platform}`);
}

function formatMs(value) {
  if (!value) return "—";
  const p95 = value.p95Ms === null ? "—" : `${value.p95Ms.toFixed(3)} ms`;
  return `${value.medianMs.toFixed(3)} ms / ${p95} (n=${value.n})`;
}

function formatRatio(value) {
  if (!value) return "—";
  return `${value.toFixed(2)}×`;
}

function rowLabel(workload, scenario, operation) {
  return `${workload} / ${scenario} / ${operation}`;
}

function tables() {
  const rows = [];
  for (const workload of workloads) {
    for (const scenario of ["ready", "cold", "warm", "steady"]) {
      const operationsForScenario = scenario === "ready" ? ["ready"] : ["parse", "metadata", "run"];
      for (const operation of operationsForScenario) rows.push({ workload, scenario, operation });
    }
  }

  const absolute = [
    "| workload / scenario / operation | Native | WASI | Browser |",
    "| --- | ---: | ---: | ---: |",
    ...rows.map(({ workload, scenario, operation }) => `| ${rowLabel(workload, scenario, operation)} | ${formatMs(record(workload, scenario, operation, "native"))} | ${formatMs(record(workload, scenario, operation, "wasi"))} | ${formatMs(record(workload, scenario, operation, "browser"))} |`),
  ].join("\n");

  const ratios = [
    "| workload / scenario / operation | Native | WASI | Browser |",
    "| --- | ---: | ---: | ---: |",
    ...rows.map(({ workload, scenario, operation }) => {
      const native = record(workload, scenario, operation, "native");
      const nativeMedian = native?.medianMs;
      return `| ${rowLabel(workload, scenario, operation)} | 1.00× | ${formatRatio(nativeRatio(record(workload, scenario, operation, "wasi")?.medianMs, nativeMedian))} | ${formatRatio(nativeRatio(record(workload, scenario, operation, "browser")?.medianMs, nativeMedian))} |`;
    }),
  ].join("\n");

  const phaseRows = ["parseAndCheckMs", "symbolExtractionMs", "compileMs", "loadAndExecuteMs"];
  const phases = [
    "| medium / steady inner phase | Native | WASI | Browser |",
    "| --- | ---: | ---: | ---: |",
    ...phaseRows.map((phase) => {
      const values = ["native", "wasi", "browser"].map((platform) => record("medium", "steady", "run", platform)?.inner?.[phase]?.medianMs);
      return `| ${phase} | ${values.map((value) => value === undefined ? "—" : `${value.toFixed(3)} ms`).join(" | ")} |`;
    }),
  ].join("\n");
  return { absolute, ratios, phases };
}

function escapeXml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;" })[character]);
}

function chart() {
  const workload = workloads.includes("medium") ? "medium" : workloads[0];
  const cells = [
    ["ready", "ready"],
    ["cold", "run"],
    ["warm", "run"],
    ["steady", "parse"],
    ["steady", "metadata"],
    ["steady", "run"],
  ];
  const platforms = ["native", "wasi", "browser"];
  const colors = { native: "#2563eb", wasi: "#f59e0b", browser: "#10b981" };
  const width = 1100;
  const panelHeight = 330;
  const chartLeft = 95;
  const chartTop = 45;
  const chartWidth = 940;
  const chartBottom = 275;
  const groupWidth = chartWidth / cells.length;
  const values = cells.flatMap(([scenario, operation]) => platforms.map((platform) => record(workload, scenario, operation, platform)?.medianMs ?? 0));
  const maxValue = Math.max(...values, 1);
  const nativeValues = cells.flatMap(([scenario, operation]) => {
    const native = record(workload, scenario, operation, "native")?.medianMs ?? 1;
    return platforms.map((platform) => nativeRatio(record(workload, scenario, operation, platform)?.medianMs, native) ?? 0);
  });
  const maxRatio = Math.max(...nativeValues, 1);

  const panel = (offset, title, max, valueFor, labelFor) => {
    const grid = [0, 0.25, 0.5, 0.75, 1].map((fraction) => {
      const y = offset + chartBottom - fraction * 230;
      return `<line x1="${chartLeft}" y1="${y}" x2="${chartLeft + chartWidth}" y2="${y}" stroke="#e5e7eb"/><text x="${chartLeft - 10}" y="${y + 4}" text-anchor="end" font-size="11">${escapeXml(labelFor(fraction * max))}</text>`;
    }).join("");
    const bars = cells.map(([scenario, operation], cellIndex) => {
      const groupLeft = chartLeft + cellIndex * groupWidth + 18;
      const barWidth = Math.min(30, (groupWidth - 36) / 3 - 4);
      const barsForCell = platforms.map((platform, platformIndex) => {
        const value = valueFor(scenario, operation, platform);
        const height = value / max * 230;
        const x = groupLeft + platformIndex * (barWidth + 4);
        const y = offset + chartBottom - height;
        return `<rect x="${x}" y="${y}" width="${barWidth}" height="${Math.max(height, 1)}" fill="${colors[platform]}"/><title>${escapeXml(`${platform}: ${value.toFixed(3)}`)}</title>`;
      }).join("");
      return `${barsForCell}<text x="${chartLeft + cellIndex * groupWidth + groupWidth / 2}" y="${offset + chartBottom + 22}" text-anchor="middle" font-size="11">${escapeXml(`${scenario} ${operation}`)}</text>`;
    }).join("");
    return `<text x="${chartLeft}" y="${offset + 20}" font-size="16" font-weight="bold">${escapeXml(title)}</text>${grid}${bars}`;
  };

  const legend = platforms.map((platform, index) => `<rect x="${chartLeft + index * 110}" y="${panelHeight * 2 - 30}" width="12" height="12" fill="${colors[platform]}"/><text x="${chartLeft + 18 + index * 110}" y="${panelHeight * 2 - 20}" font-size="12">${platform}</text>`).join("");
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${panelHeight * 2}" viewBox="0 0 ${width} ${panelHeight * 2}"><rect width="100%" height="100%" fill="white"/>${panel(0, `${workload}: absolute median (ms)`, maxValue, (scenario, operation, platform) => record(workload, scenario, operation, platform)?.medianMs ?? 0, (value) => `${round(value)} ms`)}${panel(panelHeight, `${workload}: relative to Native`, maxRatio, (scenario, operation, platform) => { const native = record(workload, scenario, operation, "native")?.medianMs; return nativeRatio(record(workload, scenario, operation, platform)?.medianMs, native) ?? 0; }, (value) => `${round(value)}×`)}${legend}</svg>`;
}

const { absolute, ratios, phases } = tables();
const readmeBefore = readFileSync(readmePath, "utf8");
const existingDate = readmeBefore.match(/Generated: ([^\n]+)/)?.[1];
const generatedDate = process.env.BENCHMARK_DATE ?? (checkOnly ? existingDate : new Date().toISOString());
const generated = `<!-- BENCHMARK-REPORT-START -->
### Measurement conditions

- Terminology: \`cold\` is the time from a fresh process/runtime to the first operation completing, \`warm\` is an unprocessed source after FCS is ready, and \`steady\` is the same source repeated after one warm-up.
- Table values are the median and p95 (\`median / p95\`) of the elapsed time as observed from the host. p95 is omitted when the sample count is below 10.
- The Native ratio is computed as the Native median for the same workload/scenario/operation equal to 1.00×.
- Both \`parse\` and \`metadata\` include \`ParseAndCheckFileInProject\`. \`run\` includes parsing, compiling, assembly loading, and executing startup code.
- Browser's \`ready\` / \`cold\` include Chromium process startup and page load, but not preview server startup time.
- Reference assembly count: ${referenceCounts[0] ?? "unknown"}. All workloads are single files.

#### Absolute time (ms)

${absolute}

#### Ratio to Native

${ratios}

#### FCS internal phases (medium / steady / run)

${phases}

![FCS benchmark chart](bench/benchmark.svg)

Generated: ${generatedDate ?? "unrecorded"}
<!-- BENCHMARK-REPORT-END -->`;

const marker = /<!-- BENCHMARK-REPORT-START -->[\s\S]*?<!-- BENCHMARK-REPORT-END -->/;
if (!marker.test(readmeBefore)) throw new Error("README benchmark markers are missing.");
const updatedReadme = readmeBefore.replace(marker, generated);
const updatedSvg = chart();
if (checkOnly) {
  const currentSvg = readFileSync(svgPath, "utf8");
  if (updatedReadme !== readmeBefore || updatedSvg !== currentSvg) {
    throw new Error("Generated benchmark report is stale.");
  }
} else {
  writeFileSync(readmePath, updatedReadme);
  writeFileSync(svgPath, updatedSvg);
}
