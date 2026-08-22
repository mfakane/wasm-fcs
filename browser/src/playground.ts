import { createBrowserFcs, type BrowserFcs } from "./index";
import "./style.css";

const source = document.querySelector<HTMLTextAreaElement>("#source")!;
const output = document.querySelector<HTMLElement>("#output")!;
const status = document.querySelector<HTMLElement>("#status")!;
let runtime: Promise<BrowserFcs> | undefined;

function getRuntime(): Promise<BrowserFcs> {
  runtime ??= createBrowserFcs().then((value) => {
    status.textContent = "runtime ready";
    return value;
  }).catch((error) => {
    runtime = undefined;
    status.textContent = "runtime failed";
    throw error;
  });
  status.textContent = "starting runtime…";
  return runtime;
}

for (const button of document.querySelectorAll<HTMLButtonElement>("button[data-command]")) {
  button.addEventListener("click", async () => {
    button.disabled = true;
    output.textContent = "working…";
    try {
      const fcs = await getRuntime();
      const command = button.dataset.command;
      const result = command === "parse"
        ? await fcs.parse(source.value)
        : command === "metadata"
          ? await fcs.metadata(source.value)
          : await fcs.run(source.value);
      output.textContent = JSON.stringify(result, null, 2);
    } catch (error) {
      output.textContent = error instanceof Error ? error.stack ?? error.message : String(error);
    } finally {
      button.disabled = false;
    }
  });
}

document.querySelector<HTMLButtonElement>("#clear")!.addEventListener("click", () => {
  output.textContent = "";
});

