"use client";

import { useEffect, useState } from "react";
import { useDesktopSettingsStore } from "@/stores/desktopSettingsStore";

// Desktop-only settings: the app needs the user's own OpenAI-compatible key. On first run (no key set)
// this blocks the UI with a mandatory key screen; afterwards it opens as a dismissible modal from the
// header gear so the user can change key / model / base URL. In the hosted web build (no
// NEXT_PUBLIC_DESKTOP) it's a pass-through — /settings doesn't exist there.
const isDesktop = process.env.NEXT_PUBLIC_DESKTOP === "1";

export default function DesktopSettingsGate({
  children,
}: {
  children: React.ReactNode;
}) {
  const [ready, setReady] = useState(!isDesktop);
  const [hasKey, setHasKey] = useState(false);
  const [apiKey, setApiKey] = useState("");
  const [model, setModel] = useState("gpt-4o");
  const [baseUrl, setBaseUrl] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const open = useDesktopSettingsStore((s) => s.open);
  const setOpen = useDesktopSettingsStore((s) => s.setOpen);

  useEffect(() => {
    if (!isDesktop) return;
    fetch("/settings")
      .then((r) => r.json())
      .then((d: { hasKey: boolean; model?: string; baseUrl?: string }) => {
        setHasKey(d.hasKey);
        if (d.model) setModel(d.model);
        if (d.baseUrl) setBaseUrl(d.baseUrl);
        setReady(true);
      })
      .catch(() => {
        setHasKey(false);
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
        body: JSON.stringify({
          apiKey: apiKey.trim(),
          model: model.trim(),
          baseUrl: baseUrl.trim(),
        }),
      });
      const d: { hasKey: boolean } = await res.json();
      if (!d.hasKey) throw new Error("rejected");
      setHasKey(true);
      setApiKey(""); // don't retain the key in component state after saving
      setOpen(false);
    } catch {
      setError("Could not save. Check your key and try again.");
    } finally {
      setSaving(false);
    }
  }

  // Web build never shows the key screen — the hosted API has its own key. Structural, so no
  // state-polarity refactor can regress it again.
  if (!isDesktop) return <>{children}</>;

  if (!ready) return null;

  // First run (no key) is mandatory and non-dismissible; the header gear opens it again later.
  const firstRun = !hasKey;
  const showForm = firstRun || open;

  return (
    <>
      {children}
      {showForm && (
        <div className="fixed inset-0 z-50 bg-doom-black/90 flex items-center justify-center px-4">
          <form
            onSubmit={save}
            className="w-full max-w-md bg-doom-card border border-doom-yellow/30 p-8 flex flex-col gap-5"
          >
            <div className="flex items-start justify-between gap-4">
              <h1 className="font-display text-doom-yellow text-2xl tracking-wider">
                {firstRun ? "YOUR KEY" : "SETTINGS"}
              </h1>
              {!firstRun && (
                <button
                  type="button"
                  onClick={() => setOpen(false)}
                  className="text-doom-ash hover:text-doom-pink transition-colors text-sm uppercase tracking-wider cursor-pointer"
                >
                  Close
                </button>
              )}
            </div>

            <p className="text-doom-ash text-sm">
              Wretched Whispers runs on your own OpenAI-compatible key. It is
              stored only on this machine and never leaves it except to call the API.
            </p>

            <label className="flex flex-col gap-1 text-xs uppercase tracking-wider text-doom-ash">
              API Key
              <input
                type="password"
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                placeholder={hasKey ? "•••••••• (leave blank to keep)" : "sk-..."}
                autoFocus={firstRun}
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

            <label className="flex flex-col gap-1 text-xs uppercase tracking-wider text-doom-ash">
              Base URL <span className="normal-case text-doom-ash/60">(optional)</span>
              <input
                type="text"
                value={baseUrl}
                onChange={(e) => setBaseUrl(e.target.value)}
                placeholder="https://openrouter.ai/api/v1"
                className="bg-doom-black border border-doom-card focus:border-doom-yellow/50 outline-none text-doom-bone text-sm px-3 py-2 font-mono"
              />
              <span className="normal-case text-doom-ash/60 text-[11px]">
                Leave blank for OpenAI. For OpenRouter use the URL above and a
                tool-calling model.
              </span>
            </label>

            {error && <p className="text-doom-pink text-sm">{error}</p>}

            <button
              type="submit"
              disabled={saving || (firstRun && apiKey.trim().length === 0)}
              className="bg-doom-yellow text-doom-black text-sm uppercase tracking-wider py-2.5 font-display disabled:opacity-40 hover:brightness-110 transition-all"
            >
              {saving ? "Binding..." : firstRun ? "Begin" : "Save"}
            </button>
          </form>
        </div>
      )}
    </>
  );
}
