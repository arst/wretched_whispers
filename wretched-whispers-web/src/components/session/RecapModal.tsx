"use client";

import { useEffect, useRef } from "react";

interface RecapModalProps {
  open: boolean;
  loading: boolean;
  text: string | null;
  location: string | null;
  day: number;
  hour: number;
  onClose: () => void;
}

export default function RecapModal({
  open,
  loading,
  text,
  location,
  day,
  hour,
  onClose,
}: RecapModalProps) {
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    if (open && !dialog.current?.open) dialog.current?.showModal();
    if (!open && dialog.current?.open) dialog.current.close();
  }, [open]);

  return (
    <dialog
      ref={dialog}
      className="border-doom-yellow/50 bg-doom-card text-doom-bone backdrop:bg-doom-black/85 m-auto max-h-[90vh] w-[calc(100%_-_2rem)] max-w-2xl overflow-y-auto border p-6 shadow-2xl md:p-8"
      aria-labelledby="recap-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <h2
        id="recap-title"
        className="font-display text-doom-yellow text-3xl tracking-wider md:text-4xl"
      >
        Previously, in this dying world...
      </h2>

      {loading ? (
        <p className="text-doom-ash mt-8 animate-pulse text-center text-sm tracking-widest uppercase">
          Gathering the whispers...
        </p>
      ) : (
        <div className="mt-6 leading-relaxed whitespace-pre-line">{text}</div>
      )}

      <div className="border-doom-ash/20 mt-8 flex items-end justify-between gap-4 border-t pt-4">
        <p className="text-doom-ash text-xs tracking-wider uppercase">
          {location ? `${location} · ` : ""}
          Day {day}, Hour {hour}
        </p>
        <button
          type="button"
          autoFocus
          onClick={onClose}
          className="border-doom-yellow text-doom-yellow hover:bg-doom-yellow hover:text-doom-black border px-4 py-2 text-xs tracking-widest uppercase transition-colors"
        >
          Continue
        </button>
      </div>
    </dialog>
  );
}
