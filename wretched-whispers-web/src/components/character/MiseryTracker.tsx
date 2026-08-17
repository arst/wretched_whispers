"use client";

interface MiseryTrackerProps {
  count: number;
}

export default function MiseryTracker({ count }: MiseryTrackerProps) {
  return (
    <div
      className="flex items-center gap-1"
      role="img"
      aria-label={`Misery tracker: ${count} of 7`}
    >
      {Array.from({ length: 7 }, (_, i) => (
        <span
          key={`${i}-${i < count}`}
          className={`h-2 w-2 rounded-full ${
            i < count ? "bg-[#ff1493]" : "bg-[#8a8a8a]/40"
          }`}
          style={
            i < count ? { animation: "doom-pulse 0.6s ease-in-out" } : undefined
          }
        />
      ))}
    </div>
  );
}
