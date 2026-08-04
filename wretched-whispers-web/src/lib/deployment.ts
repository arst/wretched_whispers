export const deploymentProfiles = [
  "Server",
  "StandaloneContainer",
  "Desktop",
] as const;

export type DeploymentProfile = (typeof deploymentProfiles)[number];

export function parseDeploymentProfile(
  value: string | undefined
): DeploymentProfile | null {
  if (value === undefined || value === "") return null;
  if (deploymentProfiles.includes(value as DeploymentProfile))
    return value as DeploymentProfile;
  throw new Error(`Invalid NEXT_PUBLIC_DEPLOYMENT_PROFILE: ${value}`);
}

export function isStandaloneProfile(profile: DeploymentProfile | null) {
  return profile === "StandaloneContainer" || profile === "Desktop";
}

export const deploymentProfile = parseDeploymentProfile(
  process.env.NEXT_PUBLIC_DEPLOYMENT_PROFILE
);
export const isStandalone = isStandaloneProfile(deploymentProfile);
