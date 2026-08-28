import { RenderingPage, SnapshotPanel } from "@/app/rendering/_components/rendering-demo";
import { createRenderingSnapshot } from "@/app/rendering/_lib/rendering-data";

export const dynamic = "force-static";
export const revalidate = 5;

export default function IncrementalStaticRenderingPage() {
  const snapshot = createRenderingSnapshot("ISR");

  return (
    <RenderingPage
      mode="ISR"
      title="Incremental Static Regeneration"
      summary="This static response remains stable for five seconds. The first request after expiry starts regeneration, and later requests receive the refreshed snapshot."
    >
      <SnapshotPanel snapshot={snapshot} />
    </RenderingPage>
  );
}

