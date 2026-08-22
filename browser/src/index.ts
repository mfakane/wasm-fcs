import { createBrowserProgram, decode } from "./runtime.js";

export type Diagnostic = {
  severity: "error" | "warning" | string;
  code: string;
  message: string;
  startLine: number;
  startColumn: number;
  endLine: number;
  endColumn: number;
};

export type ParseResult = {
  success: boolean;
  fileName: string;
  treeKind: string;
  declarationKinds: string[];
  diagnostics: Diagnostic[];
};

export type SymbolMetadata = {
  name: string;
  fullName: string;
  kind: string;
  typeText: string;
  startLine: number;
  startColumn: number;
  endLine: number;
  endColumn: number;
  isDefinition: boolean;
};

export type MetadataResult = {
  success: boolean;
  fileName: string;
  symbols: SymbolMetadata[];
  diagnostics: Diagnostic[];
};

export type RunResult = {
  success: boolean;
  fileName: string;
  output: string;
  error: string;
  durationMs: number;
  diagnostics: Diagnostic[];
};

export type BrowserFcs = {
  parse(source: string, fileName?: string): Promise<ParseResult>;
  metadata(source: string, fileName?: string): Promise<MetadataResult>;
  run(source: string, fileName?: string): Promise<RunResult>;
};

const defaultFileName = "/virtual/Playground.fsx";

export async function createBrowserFcs(options: { runtimeUrl?: string } = {}): Promise<BrowserFcs> {
  const program = await createBrowserProgram(options);

  return {
    parse: async (source, fileName = defaultFileName) => decode<ParseResult>(await program.Parse(source, fileName)),
    metadata: async (source, fileName = defaultFileName) => decode<MetadataResult>(await program.Metadata(source, fileName)),
    run: async (source, fileName = defaultFileName) => decode<RunResult>(await program.Run(source, fileName)),
  };
}
