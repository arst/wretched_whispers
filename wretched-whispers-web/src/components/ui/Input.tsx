"use client";

import { type InputHTMLAttributes } from "react";

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
}

export default function Input({
  label,
  error,
  className = "",
  id,
  ...props
}: InputProps) {
  const inputId = id || label?.toLowerCase().replace(/\s+/g, "-");

  return (
    <div className="flex flex-col gap-1.5">
      {label && (
        <label
          htmlFor={inputId}
          className="text-doom-ash text-xs tracking-wider uppercase"
        >
          {label}
        </label>
      )}
      <input
        id={inputId}
        className={`bg-doom-dark text-doom-bone placeholder:text-doom-ash/50 border-doom-card focus:ring-doom-yellow/50 focus:border-doom-yellow/50 border px-4 py-3 transition-colors duration-150 focus:ring-1 focus:outline-none ${error ? "border-doom-pink ring-doom-pink/50 ring-1" : ""} ${className} `}
        {...props}
      />
      {error && <p className="text-doom-pink text-xs">{error}</p>}
    </div>
  );
}
