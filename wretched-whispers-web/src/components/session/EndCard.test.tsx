// @vitest-environment jsdom

import { fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";
import EndCard from "./EndCard";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  HTMLDialogElement.prototype.close = function () { this.open = false; };
});

describe("EndCard", () => {
  it("returns to character creation instead of posting an invalid session", () => {
    render(
      <EndCard characterName="Wretch" worldEnded miseryCount={7} currentDay={4} />
    );

    fireEvent.click(screen.getByRole("button", { name: "BEGIN ANEW" }));

    expect(push).toHaveBeenCalledWith("/sessions");
  });
});
