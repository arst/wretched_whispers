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
      className="m-auto max-h-[90vh] w-[calc(100%_-_2rem)] max-w-2xl overflow-y-auto border border-doom-yellow/50 bg-doom-card p-6 text-doom-bone shadow-2xl backdrop:bg-doom-black/85 md:p-8"
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
        className="font-display text-3xl tracking-wider text-doom-yellow md:text-4xl"
      >
        Previously, in this dying world...
      </h2>

      {loading ? (
        <p className="mt-8 animate-pulse text-center text-sm uppercase tracking-widest text-doom-ash">
          Gathering the whispers...
        </p>
      ) : (
        <div className="mt-6 whitespace-pre-line leading-relaxed">{text}</div>
      )}

      <div className="mt-8 flex items-end justify-between gap-4 border-t border-doom-ash/20 pt-4">
        <p className="text-xs uppercase tracking-wider text-doom-ash">
          {location ? `${location} · ` : ""}
          Day {day}, Hour {hour}
        </p>
        <button
          type="button"
          autoFocus
          onClick={onClose}
          className="border border-doom-yellow px-4 py-2 text-xs uppercase tracking-widest text-doom-yellow transition-colors hover:bg-doom-yellow hover:text-doom-black"
        >
          Continue
        </button>
      </div>
    </dialog>
  );
}
