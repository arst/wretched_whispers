"use client";

interface PlayerMessageProps {
  content: string;
}

export default function PlayerMessage({ content }: PlayerMessageProps) {
  return (
    <div className="ml-auto max-w-[80%] bg-doom-dark rounded px-4 py-3 mb-3">
      <p className="text-doom-ash text-xs uppercase tracking-widest mb-2 font-body">
        You
      </p>
      <div className="text-doom-bone leading-relaxed whitespace-pre-wrap">
        {content}
      </div>
    </div>
  );
}
