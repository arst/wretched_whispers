"use client";

interface PlayerMessageProps {
  content: string;
}

export default function PlayerMessage({ content }: PlayerMessageProps) {
  return (
    <div className="bg-doom-dark mb-4 ml-auto max-w-[80%] rounded px-5 py-4">
      <p className="text-doom-yellow font-body mb-2 text-sm font-semibold tracking-widest uppercase">
        You
      </p>
      <div className="text-doom-bone text-xl leading-relaxed whitespace-pre-wrap">
        {content}
      </div>
    </div>
  );
}
