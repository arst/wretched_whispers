"use client";

import { useEffect, useRef, useState } from "react";
import { useDesktopSettingsStore } from "@/stores/desktopSettingsStore";
import { isStandalone } from "@/lib/deployment";

// Standalone settings: the app needs the user's own OpenAI-compatible key. On first run (no key set)
// this blocks the UI with a mandatory key screen; afterwards it opens as a dismissible modal from the
// header gear so the user can change key / model / base URL. In Server builds it is a pass-through.

export default function DesktopSettingsGate({
  children,
}: {
  children: React.ReactNode;
}) {
  const [ready, setReady] = useState(!isStandalone);
  const [hasKey, setHasKey] = useState(false);
  const [apiKey, setApiKey] = useState("");
  const [model, setModel] = useState("gpt-4o");
  const [baseUrl, setBaseUrl] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const dialog = useRef<HTMLDialogElement>(null);

  const open = useDesktopSettingsStore((s) => s.open);
  const setOpen = useDesktopSettingsStore((s) => s.setOpen);
  const firstRun = !hasKey;
  const showForm = isStandalone && ready && (firstRun || open);

  useEffect(() => {
    if (!isStandalone) return;
    fetch("/api/settings")
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

  useEffect(() => {
    if (showForm && !dialog.current?.open) dialog.current?.showModal();
    if (!showForm && dialog.current?.open) dialog.current.close();
  }, [showForm]);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError("");
    try {
      const res = await fetch("/api/settings", {
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
  if (!isStandalone) return <>{children}</>;

  if (!ready) return null;

  return (
    <>
      {children}
      <dialog
        ref={dialog}
        aria-label={firstRun ? "OpenAI settings required" : "Settings"}
        onCancel={(event) => {
          event.preventDefault();
          if (!firstRun) setOpen(false);
        }}
        className="text-doom-bone backdrop:bg-doom-black/90 m-auto w-full max-w-none bg-transparent px-4"
      >
        <form
          onSubmit={save}
          className="bg-doom-card border-doom-yellow/30 flex w-full max-w-md flex-col gap-5 border p-8"
        >
          <div className="flex items-start justify-between gap-4">
            <h1 className="font-display text-doom-yellow text-2xl tracking-wider">
              {firstRun ? "YOUR KEY" : "SETTINGS"}
            </h1>
            {!firstRun && (
              <button
                type="button"
                onClick={() => setOpen(false)}
                className="text-doom-ash hover:text-doom-pink cursor-pointer text-sm tracking-wider uppercase transition-colors"
              >
                Close
              </button>
            )}
          </div>

          <p className="text-doom-ash text-sm">
            Wretched Whispers runs on your own OpenAI-compatible key. It is
            stored only on this machine and never leaves it except to call the
            API.
          </p>

          <label className="text-doom-ash flex flex-col gap-1 text-xs tracking-wider uppercase">
            API Key
            <input
              type="password"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder={hasKey ? "•••••••• (leave blank to keep)" : "sk-..."}
              autoFocus={firstRun}
              className="bg-doom-black border-doom-card focus:border-doom-yellow/50 text-doom-bone border px-3 py-2 font-mono text-sm outline-none"
            />
          </label>

          <label className="text-doom-ash flex flex-col gap-1 text-xs tracking-wider uppercase">
            Model
            <input
              type="text"
              value={model}
              onChange={(e) => setModel(e.target.value)}
              className="bg-doom-black border-doom-card focus:border-doom-yellow/50 text-doom-bone border px-3 py-2 font-mono text-sm outline-none"
            />
          </label>

          <label className="text-doom-ash flex flex-col gap-1 text-xs tracking-wider uppercase">
            Base URL{" "}
            <span className="text-doom-ash/60 normal-case">(optional)</span>
            <input
              type="text"
              value={baseUrl}
              onChange={(e) => setBaseUrl(e.target.value)}
              placeholder="https://openrouter.ai/api/v1"
              className="bg-doom-black border-doom-card focus:border-doom-yellow/50 text-doom-bone border px-3 py-2 font-mono text-sm outline-none"
            />
            <span className="text-doom-ash/60 text-[11px] normal-case">
              Leave blank for OpenAI. For OpenRouter use the URL above and a
              tool-calling model. For Azure OpenAI use
              https://&lt;resource&gt;.openai.azure.com/openai/v1 and your
              deployment name as the model.
            </span>
          </label>

          {error && <p className="text-doom-pink text-sm">{error}</p>}

          <button
            type="submit"
            disabled={saving || (firstRun && apiKey.trim().length === 0)}
            className="bg-doom-yellow text-doom-black font-display py-2.5 text-sm tracking-wider uppercase transition-all hover:brightness-110 disabled:opacity-40"
          >
            {saving ? "Binding..." : firstRun ? "Begin" : "Save"}
          </button>
        </form>
      </dialog>
    </>
  );
}
