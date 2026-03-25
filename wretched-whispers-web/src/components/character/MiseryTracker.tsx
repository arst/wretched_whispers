"use client";

import { useEffect, useRef, useState } from "react";

interface MiseryTrackerProps {
  count: number;
}

export default function MiseryTracker({ count }: MiseryTrackerProps) {
  const prevCountRef = useRef(count);
  const [pulseIndex, setPulseIndex] = useState<number | null>(null);

  useEffect(() => {
    if (count > prevCountRef.current) {
      setPulseIndex(count - 1);
      const timer = setTimeout(() => setPulseIndex(null), 1000);
      return () => clearTimeout(timer);
    }
    prevCountRef.current = count;
  }, [count]);

  return (
    <div
      className="flex items-center gap-1"
      role="img"
      aria-label={`Misery tracker: ${count} of 7`}
    >
      {Array.from({ length: 7 }, (_, i) => (
        <span
          key={i}
          className={`w-2 h-2 rounded-full ${
            i < count ? "bg-[#ff1493]" : "bg-[#8a8a8a]/40"
          }`}
          style={
            i === pulseIndex
              ? { animation: "doom-pulse 0.6s ease-in-out" }
              : undefined
          }
        />
      ))}
    </div>
  );
}
