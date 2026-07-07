"use client";

import { useEffect, useState } from "react";

// Desktop-only first-run gate: the app can't run without the user's own OpenAI key, so block the
// session UI behind a key-entry screen until GET /settings reports one is set. In the hosted web
// build (no NEXT_PUBLIC_DESKTOP) this is a pass-through — /settings doesn't exist there.
const isDesktop = process.env.NEXT_PUBLIC_DESKTOP === "1";

export default function DesktopSettingsGate({
  children,
}: {
  children: React.ReactNode;
}) {
  const [ready, setReady] = useState(!isDesktop);
  const [needsKey, setNeedsKey] = useState(false);
  const [apiKey, setApiKey] = useState("");
  const [model, setModel] = useState("gpt-4o");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!isDesktop) return;
    fetch("/settings")
      .then((r) => r.json())
      .then((d: { hasKey: boolean; model?: string }) => {
        setNeedsKey(!d.hasKey);
        if (d.model) setModel(d.model);
        setReady(true);
      })
      .catch(() => {
        setNeedsKey(true);
        setReady(true);
      });
  }, []);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError("");
    try {
      const res = await fetch("/settings", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ apiKey: apiKey.trim(), model: model.trim() }),
      });
      const d: { hasKey: boolean } = await res.json();
      if (!d.hasKey) throw new Error("rejected");
      setNeedsKey(false);
    } catch {
      setError("Could not save your key. Check it and try again.");
    } finally {
      setSaving(false);
    }
  }

  if (!ready) return null;
  if (!needsKey) return <>{children}</>;

  return (
    <div className="min-h-screen bg-doom-black flex items-center justify-center px-4">
      <form
        onSubmit={save}
        className="w-full max-w-md bg-doom-card border border-doom-yellow/30 p-8 flex flex-col gap-5"
      >
        <div>
          <h1 className="font-display text-doom-yellow text-2xl tracking-wider">
            YOUR KEY
          </h1>
          <p className="text-doom-ash text-sm mt-2">
            Wretched Whispers runs on your own OpenAI key. It is stored only on
            this machine and never leaves it except to call OpenAI.
          </p>
        </div>

        <label className="flex flex-col gap-1 text-xs uppercase tracking-wider text-doom-ash">
          OpenAI API Key
          <input
            type="password"
            value={apiKey}
            onChange={(e) => setApiKey(e.target.value)}
            placeholder="sk-..."
            autoFocus
            className="bg-doom-black border border-doom-card focus:border-doom-yellow/50 outline-none text-doom-bone text-sm px-3 py-2 font-mono"
          />
        </label>

        <label className="flex flex-col gap-1 text-xs uppercase tracking-wider text-doom-ash">
          Model
          <input
            type="text"
            value={model}
            onChange={(e) => setModel(e.target.value)}
            className="bg-doom-black border border-doom-card focus:border-doom-yellow/50 outline-none text-doom-bone text-sm px-3 py-2 font-mono"
          />
        </label>

        {error && <p className="text-doom-pink text-sm">{error}</p>}

        <button
          type="submit"
          disabled={saving || apiKey.trim().length === 0}
          className="bg-doom-yellow text-doom-black text-sm uppercase tracking-wider py-2.5 font-display disabled:opacity-40 hover:brightness-110 transition-all"
        >
          {saving ? "Binding..." : "Begin"}
        </button>
      </form>
    </div>
  );
}
