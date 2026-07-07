import type { NextConfig } from "next";

// Desktop build sets NEXT_EXPORT=1 → static export served from the .NET app's wwwroot.
// Hosted web build leaves it unset → normal Next server, unchanged.
const nextConfig: NextConfig = {
  ...(process.env.NEXT_EXPORT === "1" ? { output: "export" } : {}),
};

export default nextConfig;
