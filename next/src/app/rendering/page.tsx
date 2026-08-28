import Link from "next/link";
import { RenderingNavigation } from "@/app/rendering/_components/rendering-demo";
import styles from "@/app/rendering/rendering.module.css";

export const dynamic = "error";

const demonstrations = [
  {
    mode: "SSG",
    name: "Static Site Generation",
    href: "/rendering/ssg",
    detail: "Captured during the production build and served unchanged until the next release.",
  },
  {
    mode: "SSR",
    name: "Server-Side Rendering",
    href: "/rendering/ssr",
    detail: "Captured on the Node.js server independently for every incoming request.",
  },
  {
    mode: "ISR",
    name: "Incremental Static Regeneration",
    href: "/rendering/isr",
    detail: "Served from a static snapshot, then refreshed after the five-second revalidation window.",
  },
  {
    mode: "CSR",
    name: "Client-Side Rendering",
    href: "/rendering/csr",
    detail: "Starts with a browser shell and requests fresh no-store data after React hydrates.",
  },
] as const;

export default function RenderingOverviewPage() {
  return (
    <article className={styles.page}>
      <RenderingNavigation />
      <header className={styles.overviewHeader}>
        <p className={styles.eyebrow}>Next.js rendering lab</p>
        <h1>Four paths, four cache behaviors</h1>
        <p>
          Each route exposes fetch and render timestamps, a request or snapshot identifier, and the active
          release. Reload the pages to observe where and when their output changes.
        </p>
      </header>

      <section className={styles.modeGrid} aria-label="Rendering modes">
        {demonstrations.map((demo) => (
          <Link className={styles.modeCard} href={demo.href} key={demo.mode}>
            <div>
              <p className={styles.eyebrow}>{demo.mode}</p>
              <h2>{demo.name}</h2>
            </div>
            <p>{demo.detail}</p>
            <span className={styles.cardAction}>Open live route</span>
          </Link>
        ))}
      </section>
    </article>
  );
}

