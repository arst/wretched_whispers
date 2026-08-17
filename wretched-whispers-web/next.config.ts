import type { NextConfig } from "next";
import { parseDeploymentProfile } from "./src/lib/deployment";

const deploymentProfile = parseDeploymentProfile(
  process.env.NEXT_PUBLIC_DEPLOYMENT_PROFILE,
);

const nextConfig: NextConfig = {
  turbopack: { root: __dirname },
  ...(deploymentProfile
    ? { output: "export", distDir: ".next-export", trailingSlash: true }
    : {}),
};

export default nextConfig;
