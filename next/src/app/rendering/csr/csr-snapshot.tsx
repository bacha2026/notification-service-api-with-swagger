"use client";

import { RotateCw } from "lucide-react";
import { useEffect, useState } from "react";
import { SnapshotPanel } from "@/app/rendering/_components/rendering-demo";
import type { RenderingSnapshot } from "@/app/rendering/_lib/rendering-data";
import styles from "@/app/rendering/rendering.module.css";

type LoadState =
  | { status: "loading" }
  | { status: "success"; snapshot: RenderingSnapshot }
  | { status: "error"; message: string };

export function CsrSnapshot() {
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<LoadState>({ status: "loading" });

  useEffect(() => {
    const controller = new AbortController();

    async function loadSnapshot() {
      setState({ status: "loading" });

      try {
        const endpoint = new URL("/api/rendering-snapshot", window.location.origin);
        const response = await fetch(endpoint, {
          cache: "no-store",
          headers: { Accept: "application/json" },
          signal: controller.signal,
        });

        if (!response.ok) {
          throw new Error(`The rendering snapshot request failed (${response.status}).`);
        }

        const snapshot = (await response.json()) as RenderingSnapshot;
        setState({
          status: "success",
          snapshot: {
            ...snapshot,
            renderedAtUtc: new Date().toISOString(),
            executionEnvironment: "Browser after React hydration",
          },
        });
      } catch (error) {
        if (controller.signal.aborted) return;

        setState({
          status: "error",
          message: error instanceof Error ? error.message : "The rendering snapshot could not be loaded.",
        });
      }
    }

    void loadSnapshot();
    return () => controller.abort();
  }, [attempt]);

  if (state.status === "loading") {
    return (
      <section className={styles.loadingPanel} aria-live="polite" data-testid="csr-loading">
        <div>
          <div className={styles.loadingIndicator} aria-hidden="true" />
          <h2>Waiting for the browser fetch</h2>
          <p>The static shell is visible. Fresh data is requested only after this component hydrates.</p>
        </div>
      </section>
    );
  }

  if (state.status === "error") {
    return (
      <section className={styles.errorPanel} role="alert">
        <div>
          <h2>Snapshot unavailable</h2>
          <p>{state.message}</p>
          <button className={styles.retryButton} onClick={() => setAttempt((value) => value + 1)}>
            <RotateCw aria-hidden="true" />
            Retry
          </button>
        </div>
      </section>
    );
  }

  return <SnapshotPanel snapshot={state.snapshot} />;
}

