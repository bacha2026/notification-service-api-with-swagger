export const renderingModes = ["SSG", "SSR", "ISR", "CSR"] as const;

export type RenderingMode = (typeof renderingModes)[number];

export type RenderingSnapshot = {
  mode: RenderingMode;
  fetchedAtUtc: string;
  renderedAtUtc: string;
  requestId: string;
  releaseId: string;
  executionEnvironment: string;
  cachePolicy: string;
  refreshBehavior: string;
  source: string;
};

type SnapshotOverrides = Partial<
  Pick<
    RenderingSnapshot,
    | "fetchedAtUtc"
    | "renderedAtUtc"
    | "requestId"
    | "releaseId"
    | "executionEnvironment"
  >
>;

const modePolicies: Record<
  RenderingMode,
  Pick<RenderingSnapshot, "cachePolicy" | "refreshBehavior">
> = {
  SSG: {
    cachePolicy: "Pre-rendered once during next build",
    refreshBehavior: "Timestamps change only after a new application build",
  },
  SSR: {
    cachePolicy: "Dynamic server render; response is not shared or cached",
    refreshBehavior: "Every full request receives a new server snapshot",
  },
  ISR: {
    cachePolicy: "Pre-rendered, then revalidated at most every 5 seconds",
    refreshBehavior: "Stable inside the window; stale-while-revalidate after expiry",
  },
  CSR: {
    cachePolicy: "Browser fetch with cache: no-store from a no-store Route Handler",
    refreshBehavior: "Fetches after hydration and whenever Retry is selected",
  },
};

function getReleaseId() {
  return (
    process.env.RELEASE_ID ??
    process.env.GITHUB_SHA ??
    process.env.NEXT_PUBLIC_RELEASE_ID ??
    "local-development"
  ).slice(0, 40);
}

export function createRenderingSnapshot(
  mode: RenderingMode,
  overrides: SnapshotOverrides = {},
): RenderingSnapshot {
  const fetchedAtUtc = overrides.fetchedAtUtc ?? new Date().toISOString();

  return {
    mode,
    fetchedAtUtc,
    renderedAtUtc: overrides.renderedAtUtc ?? new Date().toISOString(),
    requestId: overrides.requestId ?? crypto.randomUUID(),
    releaseId: overrides.releaseId ?? getReleaseId(),
    executionEnvironment: overrides.executionEnvironment ?? "Next.js Node.js server",
    cachePolicy: modePolicies[mode].cachePolicy,
    refreshBehavior: modePolicies[mode].refreshBehavior,
    source: "Built-in rendering lab data; no network or external service",
  };
}

