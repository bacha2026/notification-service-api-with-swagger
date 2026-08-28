import { getVersionInfo } from "@/app/health/version/version-info";

export const dynamic = "force-dynamic";
export const revalidate = 0;

export function GET() {
  const version = getVersionInfo();

  return Response.json(version, {
    headers: {
      "Cache-Control": "no-store, max-age=0",
      "X-Content-Type-Options": "nosniff",
      "X-Release-Color": version.color,
      "X-Release-Sha": version.sha,
    },
  });
}
