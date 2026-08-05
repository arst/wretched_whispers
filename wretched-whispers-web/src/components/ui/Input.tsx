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
          className="text-doom-ash text-xs uppercase tracking-wider"
        >
          {label}
        </label>
      )}
      <input
        id={inputId}
        className={`
          bg-doom-dark text-doom-bone placeholder:text-doom-ash/50
          border border-doom-card px-4 py-3
          focus:outline-none focus:ring-1 focus:ring-doom-yellow/50 focus:border-doom-yellow/50
          transition-colors duration-150
          ${error ? "border-doom-pink ring-1 ring-doom-pink/50" : ""}
          ${className}
        `}
        {...props}
      />
      {error && <p className="text-doom-pink text-xs">{error}</p>}
    </div>
  );
}
