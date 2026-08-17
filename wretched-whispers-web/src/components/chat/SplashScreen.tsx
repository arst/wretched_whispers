"use client";

import { useEffect } from "react";
import Dots from "@/components/ui/Dots";

interface SplashScreenProps {
  onTransition: () => void;
  show: boolean;
}

export default function SplashScreen({
  onTransition,
  show,
}: SplashScreenProps) {
  useEffect(() => {
    if (!show) {
      const timer = setTimeout(() => {
        onTransition();
      }, 400);
      return () => clearTimeout(timer);
    }
  }, [show, onTransition]);

  return (
    <div
      className={`bg-doom-black fixed inset-0 z-30 flex flex-col items-center justify-center transition-opacity duration-400 ${
        show ? "opacity-100" : "opacity-0"
      }`}
    >
      <h1
        className="font-display text-doom-yellow text-center text-5xl tracking-wider md:text-7xl"
        style={{ animation: "doom-breathe 3s ease-in-out infinite" }}
      >
        WRETCHED WHISPERS
      </h1>
      <p
        className="text-doom-ash mt-6 text-sm tracking-widest uppercase md:text-base"
        style={{ animation: "doom-breathe 3s ease-in-out 0.5s infinite" }}
      >
        The darkness stirs...
      </p>
      <div className="mt-10">
        <Dots size="w-1.5 h-1.5" />
      </div>
    </div>
  );
}
