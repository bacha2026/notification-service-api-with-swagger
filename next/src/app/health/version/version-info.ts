export type VersionInfo = {
  service: "next";
  color: string;
  sha: string;
};

type ReleaseEnvironment = {
  readonly RELEASE_COLOR?: string;
  readonly RELEASE_ID?: string;
};

const runtimeLabel = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;

export function getVersionInfo(
  environment: ReleaseEnvironment = {
    RELEASE_COLOR: process.env.RELEASE_COLOR,
    RELEASE_ID: process.env.RELEASE_ID,
  },
): VersionInfo {
  return {
    service: "next",
    color: readRuntimeLabel(environment.RELEASE_COLOR, "local"),
    sha: readRuntimeLabel(environment.RELEASE_ID, "development"),
  };
}

function readRuntimeLabel(value: string | undefined, fallback: string): string {
  const candidate = value?.trim();
  return candidate && runtimeLabel.test(candidate) ? candidate : fallback;
}
