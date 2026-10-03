import { Component, type ComponentChildren } from "preact";
import { PLACE_KEY, SERVER_KEY, SUB_KEY } from "./place";

function resetView() {
  try {
    for (const key of [PLACE_KEY, SERVER_KEY, SUB_KEY]) localStorage.removeItem(key);
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
        <button class="btn" onClick={resetView}>Reset view and reload</button>
      </div>
    );
  }
}
