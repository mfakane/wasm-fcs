const small = `module Example
open System
let answer = 40 + 2
printfn "answer = %d" answer
`;

function generatedModule(name, count) {
  const declarations = Array.from({ length: count }, (_, index) => {
    const value = index + 1;
    return `let value${String(value).padStart(4, "0")} = ${value}\n`;
  }).join("");
  return `module ${name}\nopen System\n${declarations}let answer = value0001 + value${String(count).padStart(4, "0")}\nprintfn "answer = %d" answer\n`;
}

export const workloads = [
  { name: "small", source: small, fileName: "/virtual/Small.fsx" },
  { name: "medium", source: generatedModule("Medium", 100), fileName: "/virtual/Medium.fsx" },
  { name: "large", source: generatedModule("Large", 1000), fileName: "/virtual/Large.fsx" },
];

export function workloadInfo(workload) {
  const source = new TextEncoder().encode(workload.source);
  return {
    sourceChars: workload.source.length,
    sourceBytes: source.byteLength,
    sourceLines: workload.source.split("\n").length - 1,
    fileCount: 1,
  };
}
