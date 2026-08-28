import { RenderingPage, SnapshotPanel } from "@/app/rendering/_components/rendering-demo";
import { createRenderingSnapshot } from "@/app/rendering/_lib/rendering-data";

export const dynamic = "error";

export default function StaticRenderingPage() {
  const snapshot = createRenderingSnapshot("SSG");

  return (
    <RenderingPage
      mode="SSG"
      title="Static Site Generation"
      summary="Next.js creates this HTML during the production build. Every visitor receives the same snapshot until a new release is built."
    >
      <SnapshotPanel snapshot={snapshot} />
    </RenderingPage>
  );
}

