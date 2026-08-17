"use client";

import { useState, useRef, useCallback, type KeyboardEvent } from "react";

interface ChatInputProps {
  onSend: (message: string) => void;
  disabled: boolean;
  status?: string | null;
}

export default function ChatInput({
  onSend,
  disabled,
  status,
}: ChatInputProps) {
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
    [handleSend],
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
    <div className="bg-doom-dark border-doom-card fixed right-0 bottom-0 left-0 z-40 border-t">
      <div className="mx-auto flex w-full max-w-6xl items-end gap-3 px-4 py-3 md:w-[80vw]">
        <textarea
          ref={textareaRef}
          value={text}
          onChange={(e) => {
            setText(e.target.value);
            handleInput();
          }}
          onKeyDown={handleKeyDown}
          placeholder={
            isEnded
              ? "This tale has ended..."
              : status === "in-progress"
                ? "What do you do?"
                : "Speak, wretch..."
          }
          disabled={isDisabled}
          rows={1}
          className={`bg-doom-black text-doom-bone border-doom-card font-body placeholder:text-doom-ash focus:border-doom-yellow/60 flex-1 resize-none rounded border px-4 py-3 text-lg leading-relaxed transition-colors focus:outline-none ${
            isDisabled ? "cursor-not-allowed opacity-50" : ""
          } ${isEnded ? "cursor-not-allowed bg-[#1a1a1a]" : ""}`}
        />
        {!isEnded && (
          <button
            onClick={handleSend}
            disabled={isDisabled || !text.trim()}
            className={`font-display rounded px-5 py-3 text-lg tracking-wider transition-colors ${
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
