"use client";

import { useState, useRef, useCallback, type KeyboardEvent } from "react";

interface ChatInputProps {
  onSend: (message: string) => void;
  disabled: boolean;
}

export default function ChatInput({ onSend, disabled }: ChatInputProps) {
  const [text, setText] = useState("");
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const handleSend = useCallback(() => {
    const trimmed = text.trim();
    if (!trimmed || disabled) return;
    onSend(trimmed);
    setText("");
    // Reset textarea height
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
    }
  }, [text, disabled, onSend]);

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend]
  );

  // Auto-resize textarea
  const handleInput = useCallback(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = "auto";
    // Clamp to ~4 lines (approximately 96px)
    el.style.height = `${Math.min(el.scrollHeight, 96)}px`;
  }, []);

  return (
    <div className="fixed bottom-0 left-0 right-0 bg-doom-dark border-t border-doom-card z-40">
      <div className="max-w-2xl mx-auto px-4 py-3 flex items-end gap-3">
        <textarea
          ref={textareaRef}
          value={text}
          onChange={(e) => {
            setText(e.target.value);
            handleInput();
          }}
          onKeyDown={handleKeyDown}
          placeholder="Speak, wretch..."
          disabled={disabled}
          rows={1}
          className={`flex-1 resize-none bg-doom-black text-doom-bone border border-doom-card rounded px-3 py-2 font-body text-sm leading-relaxed placeholder:text-doom-ash focus:outline-none focus:border-doom-yellow/60 transition-colors ${
            disabled ? "opacity-50 cursor-not-allowed" : ""
          }`}
        />
        <button
          onClick={handleSend}
          disabled={disabled || !text.trim()}
          className={`font-display text-sm tracking-wider px-4 py-2 rounded transition-colors ${
            disabled || !text.trim()
              ? "bg-doom-card text-doom-ash cursor-not-allowed"
              : "bg-doom-yellow text-doom-black hover:bg-doom-yellow/90"
          }`}
        >
          SEND
        </button>
      </div>
    </div>
  );
}
