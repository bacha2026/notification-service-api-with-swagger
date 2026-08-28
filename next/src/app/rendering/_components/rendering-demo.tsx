import Link from "next/link";
import type { ReactNode } from "react";
import type { RenderingMode, RenderingSnapshot } from "@/app/rendering/_lib/rendering-data";
import styles from "@/app/rendering/rendering.module.css";

const modeLinks: { mode: RenderingMode; href: string }[] = [
  { mode: "SSG", href: "/rendering/ssg" },
  { mode: "SSR", href: "/rendering/ssr" },
  { mode: "ISR", href: "/rendering/isr" },
  { mode: "CSR", href: "/rendering/csr" },
];

function Timestamp({ value }: { value: string }) {
  return (
    <time dateTime={value} className={styles.mono}>
      {value}
    </time>
  );
}

export function RenderingNavigation({ activeMode }: { activeMode?: RenderingMode }) {
  return (
    <nav className={styles.modeNavigation} aria-label="Rendering demonstrations">
      <Link className={!activeMode ? styles.activeModeLink : styles.modeLink} href="/rendering">
        Overview
      </Link>
      {modeLinks.map(({ mode, href }) => (
        <Link
          key={mode}
          className={activeMode === mode ? styles.activeModeLink : styles.modeLink}
          href={href}
          aria-current={activeMode === mode ? "page" : undefined}
        >
          {mode}
        </Link>
      ))}
    </nav>
  );
}

export function RenderingPage({
  mode,
  title,
  summary,
  children,
}: {
  mode: RenderingMode;
  title: string;
  summary: string;
  children: ReactNode;
}) {
  return (
    <article className={styles.page}>
      <RenderingNavigation activeMode={mode} />
      <header className={styles.demoHeader}>
        <div>
          <p className={styles.eyebrow}>Rendering mode</p>
          <h1>{title}</h1>
          <p className={styles.summary}>{summary}</p>
        </div>
        <span className={styles.modeBadge} data-testid="mode">
          {mode}
        </span>
      </header>
      {children}
    </article>
  );
}

export function SnapshotPanel({ snapshot }: { snapshot: RenderingSnapshot }) {
  return (
    <section className={styles.snapshotPanel} aria-labelledby="snapshot-title" data-testid="snapshot-panel">
      <div className={styles.panelHeading}>
        <div>
          <p className={styles.eyebrow}>Live evidence</p>
          <h2 id="snapshot-title">Rendering snapshot</h2>
        </div>
        <span className={styles.environment}>{snapshot.executionEnvironment}</span>
      </div>

      <dl className={styles.snapshotGrid}>
        <div className={styles.primaryDatum}>
          <dt>Data fetched at (UTC)</dt>
          <dd data-testid="fetch-timestamp">
            <Timestamp value={snapshot.fetchedAtUtc} />
          </dd>
        </div>
        <div className={styles.primaryDatum}>
          <dt>Page rendered at (UTC)</dt>
          <dd data-testid="render-timestamp">
            <Timestamp value={snapshot.renderedAtUtc} />
          </dd>
        </div>
        <div>
          <dt>Request / snapshot ID</dt>
          <dd className={styles.mono} data-testid="request-id">
            {snapshot.requestId}
          </dd>
        </div>
        <div>
          <dt>Release ID</dt>
          <dd className={styles.mono} data-testid="release-id">
            {snapshot.releaseId}
          </dd>
        </div>
        <div>
          <dt>Cache policy</dt>
          <dd>{snapshot.cachePolicy}</dd>
        </div>
        <div>
          <dt>Refresh behavior</dt>
          <dd>{snapshot.refreshBehavior}</dd>
        </div>
      </dl>

      <p className={styles.source}>{snapshot.source}</p>
    </section>
  );
}

