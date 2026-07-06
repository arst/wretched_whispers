"use client";

import { useRef, useEffect, useCallback } from "react";

const NEAR_BOTTOM_THRESHOLD = 100;

export function useAutoScroll(deps: unknown[]) {
  const containerRef = useRef<HTMLDivElement>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const userScrolledUp = useRef(false);
  const isPrependRef = useRef(false);

  // Track whether user has manually scrolled up
  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    function handleScroll() {
      if (!container) return;
      const { scrollTop, scrollHeight, clientHeight } = container;
      const distanceFromBottom = scrollHeight - scrollTop - clientHeight;
      userScrolledUp.current = distanceFromBottom > NEAR_BOTTOM_THRESHOLD;
    }

    container.addEventListener("scroll", handleScroll, { passive: true });
    return () => container.removeEventListener("scroll", handleScroll);
  }, []);

  // Auto-scroll when dependencies change (new messages, streaming text)
  useEffect(() => {
    if (userScrolledUp.current) return;
    if (isPrependRef.current) {
      isPrependRef.current = false; // Reset after one skip
      return;
    }

    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  const scrollToBottom = useCallback(() => {
    userScrolledUp.current = false;
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, []);

  return { containerRef, bottomRef, scrollToBottom, isPrependRef };
}
