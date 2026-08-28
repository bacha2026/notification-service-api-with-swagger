import { CsrSnapshot } from "@/app/rendering/csr/csr-snapshot";
import { RenderingPage } from "@/app/rendering/_components/rendering-demo";

export const dynamic = "error";

export default function ClientRenderingPage() {
  return (
    <RenderingPage
      mode="CSR"
      title="Client-Side Rendering"
      summary="The route ships a stable shell first. After hydration, the browser requests a new snapshot and replaces the loading state without caching the response."
    >
      <CsrSnapshot />
    </RenderingPage>
  );
}

