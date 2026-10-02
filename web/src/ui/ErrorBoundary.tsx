import { Component, type ComponentChildren } from "preact";
import { LAST_KEY, NAV_KEY } from "./App";

function resetNav() {
  try {
    localStorage.removeItem(NAV_KEY);
    localStorage.removeItem(LAST_KEY);
  } catch {
    // Blocked storage; reloading is all that is left to try.
  }
  location.replace(location.pathname);
}

export class ErrorBoundary extends Component<{ children: ComponentChildren }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    if (!this.state.failed) return this.props.children;
    return (
      <div class="boot-error">
        <p>Something went wrong showing ChatTerror.</p>
        <button class="btn" onClick={resetNav}>Reset view and reload</button>
      </div>
    );
  }
}
