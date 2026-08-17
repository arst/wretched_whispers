"use client";

/** Three pulsing dots — the app's one "something is happening" signal. */
export default function Dots({ size = "w-2 h-2" }: { size?: string }) {
  return (
    <div className="flex items-center gap-2">
      {[0, 0.2, 0.4].map((delay) => (
        <span
          key={delay}
          className={`inline-block ${size} bg-doom-yellow rounded-full`}
          style={{
            animation: `doom-pulse 1.4s ease-in-out ${delay}s infinite`,
          }}
        />
      ))}
    </div>
  );
}
