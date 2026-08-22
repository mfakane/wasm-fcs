export function summarize(values) {
  if (!values.length) throw new Error("Cannot summarize an empty sample set.");
  const sorted = [...values].sort((left, right) => left - right);
  const mean = values.reduce((sum, value) => sum + value, 0) / values.length;
  const median = sorted.length % 2 === 1
    ? sorted[(sorted.length - 1) / 2]
    : (sorted[sorted.length / 2 - 1] + sorted[sorted.length / 2]) / 2;
  const percentile = (fraction) => sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * fraction) - 1)];
  return {
    n: values.length,
    meanMs: round(mean),
    medianMs: round(median),
    p95Ms: values.length >= 10 ? round(percentile(0.95)) : null,
  };
}

export function summarizeInner(results) {
  const fields = ["totalMs", "parseAndCheckMs", "symbolExtractionMs", "compileMs", "loadAndExecuteMs"];
  const inner = {};
  for (const field of fields) {
    const values = results
      .map((result) => result?.timing?.[field])
      .filter((value) => typeof value === "number");
    if (values.length) inner[field] = summarize(values);
  }
  return inner;
}

export function round(value) {
  return Number(value.toFixed(3));
}

export function nativeRatio(value, nativeValue) {
  if (typeof value !== "number" || typeof nativeValue !== "number" || nativeValue <= 0) return null;
  return round(value / nativeValue);
}
