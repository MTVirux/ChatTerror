import { render } from "preact";
import { openAccounts } from "./core/accounts";
import { App } from "./ui/App";
import { applyTheme, loadTheme } from "./ui/theme";
import "./ui/styles.css";

applyTheme(loadTheme());

const root = document.getElementById("app")!;

openAccounts()
  .then((manager) => render(<App manager={manager} />, root))
  .catch(() => {
    const message = document.createElement("p");
    message.className = "boot-error";
    message.textContent = "ChatTerror couldn't start. Reload the page to try again.";
    root.replaceChildren(message);
  });
