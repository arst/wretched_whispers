import { describe, expect, it } from "vitest";
import { isStandaloneProfile, parseDeploymentProfile } from "./deployment";

describe("deployment profile", () => {
  it.each([
    ["Server", false],
    ["StandaloneContainer", true],
    ["Desktop", true],
  ] as const)("maps %s", (name, standalone) => {
    const profile = parseDeploymentProfile(name);
    expect(isStandaloneProfile(profile)).toBe(standalone);
  });

  it("allows an unset profile for local Next development", () => {
    expect(parseDeploymentProfile(undefined)).toBeNull();
  });

  it("rejects an invalid profile", () => {
    expect(() => parseDeploymentProfile("Other")).toThrow(
      "Invalid NEXT_PUBLIC_DEPLOYMENT_PROFILE"
    );
  });
});
