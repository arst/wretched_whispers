"use client";

import { itemGlyph } from "@/lib/itemGlyph";

interface InventoryListProps {
  items: string[];
}

export default function InventoryList({ items }: InventoryListProps) {
  if (items.length === 0) {
    return <p className="text-doom-ash text-sm italic">Empty</p>;
  }

  // Items arrive one entry per UNIT (so turn deltas track quantity changes);
  // group duplicates back into one row with a ×N badge, preserving first-seen order.
  const grouped = new Map<string, number>();
  for (const item of items) {
    grouped.set(item, (grouped.get(item) ?? 0) + 1);
  }

  return (
    <ul className="max-h-40 overflow-y-auto">
      {[...grouped.entries()].map(([item, count]) => (
        <li key={item} className="flex items-baseline gap-2 py-0.5">
          <span
            aria-hidden="true"
            className="text-doom-yellow w-4 shrink-0 text-center text-sm"
          >
            {itemGlyph(item)}
          </span>
          <span className="text-doom-bone text-sm">{item}</span>
          {count > 1 && <span className="text-doom-ash text-xs">×{count}</span>}
        </li>
      ))}
    </ul>
  );
}
