"use client";

import { useEffect, useState } from "react";

interface SplashScreenProps {
  onTransition: () => void;
  show: boolean;
}

export default function SplashScreen({ onTransition, show }: SplashScreenProps) {
  const [fading, setFading] = useState(false);

  useEffect(() => {
    if (!show) {
      setFading(true);
      const timer = setTimeout(() => {
        onTransition();
      }, 400);
      return () => clearTimeout(timer);
    }
  }, [show, onTransition]);

  return (
    <div
      className={`fixed inset-0 z-30 flex flex-col items-center justify-center bg-doom-black transition-opacity duration-400 ${
        fading ? "opacity-0" : "opacity-100"
      }`}
    >
      <h1
        className="font-display text-doom-yellow text-5xl md:text-7xl tracking-wider text-center"
        style={{ animation: "doom-breathe 3s ease-in-out infinite" }}
      >
        WRETCHED WHISPERS
      </h1>
      <p
        className="text-doom-ash text-sm md:text-base mt-6 tracking-widest uppercase"
        style={{ animation: "doom-breathe 3s ease-in-out 0.5s infinite" }}
      >
        The darkness stirs...
      </p>
      <div className="flex items-center gap-2 mt-10">
        <span
          className="inline-block w-1.5 h-1.5 rounded-full bg-doom-yellow"
          style={{ animation: "doom-pulse 1.4s ease-in-out infinite" }}
        />
        <span
          className="inline-block w-1.5 h-1.5 rounded-full bg-doom-yellow"
          style={{ animation: "doom-pulse 1.4s ease-in-out 0.2s infinite" }}
        />
        <span
          className="inline-block w-1.5 h-1.5 rounded-full bg-doom-yellow"
          style={{ animation: "doom-pulse 1.4s ease-in-out 0.4s infinite" }}
        />
      </div>
    </div>
  );
}
