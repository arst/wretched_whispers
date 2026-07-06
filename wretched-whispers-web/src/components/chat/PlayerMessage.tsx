"use client";

interface PlayerMessageProps {
  content: string;
}

export default function PlayerMessage({ content }: PlayerMessageProps) {
  return (
    <div className="ml-auto max-w-[80%] bg-doom-dark rounded px-5 py-4 mb-4">
      <p className="text-doom-yellow text-sm uppercase tracking-widest mb-2 font-body font-semibold">
        You
      </p>
      <div className="text-doom-bone text-xl leading-relaxed whitespace-pre-wrap">
        {content}
      </div>
    </div>
  );
}
