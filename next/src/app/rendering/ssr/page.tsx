import { connection } from "next/server";
import { RenderingPage, SnapshotPanel } from "@/app/rendering/_components/rendering-demo";
import { createRenderingSnapshot } from "@/app/rendering/_lib/rendering-data";

export default async function ServerRenderingPage() {
  await connection();
  const snapshot = createRenderingSnapshot("SSR");

  return (
    <RenderingPage
      mode="SSR"
      title="Server-Side Rendering"
      summary="The request reaches the Node.js runtime before this snapshot is produced, so each full request receives fresh server output."
    >
      <SnapshotPanel snapshot={snapshot} />
    </RenderingPage>
  );
}

