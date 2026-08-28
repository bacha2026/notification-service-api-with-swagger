import { NextResponse } from "next/server";
import { createRenderingSnapshot } from "@/app/rendering/_lib/rendering-data";

export const dynamic = "force-dynamic";
export const revalidate = 0;

export function GET() {
  const snapshot = createRenderingSnapshot("CSR", {
    executionEnvironment: "Next.js Route Handler",
  });

  return NextResponse.json(snapshot, {
    headers: {
      "Cache-Control": "no-store, max-age=0",
      Pragma: "no-cache",
      "X-Content-Type-Options": "nosniff",
    },
  });
}

