"use client";

import { useEffect } from "react";
import Dots from "@/components/ui/Dots";

interface SplashScreenProps {
  onTransition: () => void;
  show: boolean;
}

export default function SplashScreen({ onTransition, show }: SplashScreenProps) {
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
      className={`fixed inset-0 z-30 flex flex-col items-center justify-center bg-doom-black transition-opacity duration-400 ${
        show ? "opacity-100" : "opacity-0"
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
      <div className="mt-10">
        <Dots size="w-1.5 h-1.5" />
      </div>
    </div>
  );
}
