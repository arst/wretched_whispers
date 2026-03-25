"use client";

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
        <li key={i} className="text-sm text-doom-bone py-0.5">
          {item}
        </li>
      ))}
    </ul>
  );
}
