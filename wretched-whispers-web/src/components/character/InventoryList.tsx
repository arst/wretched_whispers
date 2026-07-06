"use client";

import { itemGlyph } from "@/lib/itemGlyph";

interface InventoryListProps {
  items: string[];
}

export default function InventoryList({ items }: InventoryListProps) {
  if (items.length === 0) {
    return <p className="text-sm text-doom-ash italic">Empty</p>;
  }

  return (
    <ul className="max-h-40 overflow-y-auto">
      {items.map((item, i) => (
        <li key={i} className="flex items-baseline gap-2 py-0.5">
          <span
            aria-hidden="true"
            className="w-4 shrink-0 text-center text-sm text-doom-yellow"
          >
            {itemGlyph(item)}
          </span>
          <span className="text-sm text-doom-bone">{item}</span>
        </li>
      ))}
    </ul>
  );
}
