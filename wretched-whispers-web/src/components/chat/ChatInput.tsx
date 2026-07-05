"use client";

import { useState, useRef, useCallback, type KeyboardEvent } from "react";

interface ChatInputProps {
  onSend: (message: string) => void;
  disabled: boolean;
  status?: string | null;
}

export default function ChatInput({ onSend, disabled, status }: ChatInputProps) {
  const [text, setText] = useState("");
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const isEnded = status === "ended";
  const isDisabled = disabled || isEnded;

  const handleSend = useCallback(() => {
    const trimmed = text.trim();
    if (!trimmed || isDisabled) return;
    onSend(trimmed);
    setText("");
    // Reset textarea height
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
    }
  }, [text, isDisabled, onSend]);

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
      <div className="mx-auto w-full md:w-[80vw] max-w-6xl px-4 py-3 flex items-end gap-3">
        <textarea
          ref={textareaRef}
          value={text}
          onChange={(e) => {
            setText(e.target.value);
            handleInput();
          }}
          onKeyDown={handleKeyDown}
          placeholder={isEnded ? "This tale has ended..." : status === "in-progress" ? "What do you do?" : "Speak, wretch..."}
          disabled={isDisabled}
          rows={1}
          className={`flex-1 resize-none bg-doom-black text-doom-bone border border-doom-card rounded px-4 py-3 font-body text-lg leading-relaxed placeholder:text-doom-ash focus:outline-none focus:border-doom-yellow/60 transition-colors ${
            isDisabled ? "opacity-50 cursor-not-allowed" : ""
          } ${isEnded ? "bg-[#1a1a1a] cursor-not-allowed" : ""}`}
        />
        {!isEnded && (
          <button
            onClick={handleSend}
            disabled={isDisabled || !text.trim()}
            className={`font-display text-lg tracking-wider px-5 py-3 rounded transition-colors ${
              isDisabled || !text.trim()
                ? "bg-doom-card text-doom-ash cursor-not-allowed"
                : "bg-doom-yellow text-doom-black hover:bg-doom-yellow/90"
            }`}
          >
            SEND
          </button>
        )}
      </div>
    </div>
  );
}
